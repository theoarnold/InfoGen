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

            // For instant methods (cards) checkout.session.completed already carries PaymentStatus=="paid".
            // For delayed methods it arrives "unpaid" and the real confirmation comes later via
            // checkout.session.async_payment_succeeded - so we handle both event types and, in every
            // case, only credit once Stripe reports the money as actually paid.
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

            // Stripe needs a 2xx to stop retrying; unhandled event types are a deliberate no-op.
            return Results.Ok();
        }).AllowAnonymous();

        return endpoints;
    }

    // Just a stable category name for the webhook logger.
    private sealed class StripeWebhookLogCategory { }
}
