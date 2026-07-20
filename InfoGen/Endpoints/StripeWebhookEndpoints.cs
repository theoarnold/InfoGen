using InfoGen.Services;
using Stripe;
using Stripe.Checkout;

namespace InfoGen.Endpoints;

public static class StripeWebhookEndpoints
{
    public static IEndpointRouteBuilder MapStripeWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/stripe/webhook", async (
            HttpContext context,
            IConfiguration configuration,
            IUsageService usage,
            ISubscriptionStateService subscriptionState,
            ILogger<StripeWebhookLogCategory> logger) =>
        {
            var webhookSecret = configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret))
            {
                logger.LogWarning("Stripe webhook received but Stripe:WebhookSecret is not configured.");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            // Read the RAW body - signature verification requires the exact bytes Stripe sent.
            string json;
            using (var reader = new StreamReader(context.Request.Body))
                json = await reader.ReadToEndAsync();

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    context.Request.Headers["Stripe-Signature"],
                    webhookSecret);
            }
            catch (StripeException ex)
            {
                logger.LogWarning(ex, "Stripe webhook signature verification failed.");
                return Results.BadRequest();
            }

            // Cards arrive "paid" on checkout.session.completed; delayed methods arrive "unpaid" and
            // confirm later via async_payment_succeeded. Both are handled, and neither credits until
            // Stripe reports the money as actually paid.
            if ((stripeEvent.Type == "checkout.session.completed" || stripeEvent.Type == "checkout.session.async_payment_succeeded") &&
                stripeEvent.Data.Object is Session session &&
                session.Mode == "payment")
            {
                if (session.PaymentStatus != "paid")
                {
                    logger.LogInformation("Stripe session {SessionId} not yet paid (status {Status}) - deferring credit.", session.Id, session.PaymentStatus);
                }
                else if (session.Metadata is not null &&
                    session.Metadata.TryGetValue("UserId", out var userId) &&
                    session.Metadata.TryGetValue("Credits", out var creditsRaw) &&
                    int.TryParse(creditsRaw, out var credits) &&
                    !string.IsNullOrEmpty(userId))
                {
                    var credited = await usage.AddPurchasedCreditsAsync(userId, credits, session.Id);
                    if (credited)
                        logger.LogInformation("Credited {Credits} generations to user {UserId} (session {SessionId}).", credits, userId, session.Id);
                    else
                        logger.LogInformation("Stripe session {SessionId} already processed - no credit applied.", session.Id);
                }
                else
                {
                    logger.LogWarning("Stripe payment session {SessionId} missing expected UserId/Credits metadata.", session.Id);
                }
            }

            // Mirrored into ApplicationUser.IsSubscribed so read paths never have to ask Stripe.
            if (stripeEvent.Data.Object is Subscription subscription &&
                stripeEvent.Type.StartsWith("customer.subscription.", StringComparison.Ordinal))
            {
                // "deleted" can still carry a non-cancelled status, so treat the event type as decisive.
                var isActive = stripeEvent.Type != "customer.subscription.deleted" &&
                               subscription.Status is "active" or "trialing";

                await subscriptionState.SetByStripeCustomerIdAsync(subscription.CustomerId, isActive);
                logger.LogInformation("Subscription event {EventType} (status {Status}) for customer {CustomerId} -> IsSubscribed={IsActive}.",
                    stripeEvent.Type, subscription.Status, subscription.CustomerId, isActive);
            }
            // First signal that someone subscribed - customer.subscription.created may arrive later.
            else if (stripeEvent.Type == "checkout.session.completed" &&
                     stripeEvent.Data.Object is Session subSession &&
                     subSession.Mode == "subscription" &&
                     !string.IsNullOrEmpty(subSession.CustomerId))
            {
                await subscriptionState.SetByStripeCustomerIdAsync(subSession.CustomerId, true);
                logger.LogInformation("Subscription checkout completed for customer {CustomerId}.", subSession.CustomerId);
            }

            // Stripe needs a 2xx to stop retrying; unhandled event types are a deliberate no-op.
            return Results.Ok();
        }).AllowAnonymous();

        return endpoints;
    }

    private sealed class StripeWebhookLogCategory { }
}
