using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using InfoGen.Components;
using InfoGen.Data;
using InfoGen.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using InfoGen.Components.Account;
using InfoGen.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();

// Rate limiting. /api/generation/research deliberately reserves no credit, so without a cap an
// authenticated user can loop it and run an unmetered Gemini call (plus a 15-minute cache entry)
// on every iteration. Partitioned per user so one caller can't starve everyone else.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Research, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                // Generation itself is gated by credits; this only needs to stop a tight loop, not
                // to be the real quota. Ten a minute is far more than the UI can produce by hand.
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, PersistingRevalidatingAuthenticationStateProvider>();

var config = builder.Configuration;

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});

// External login providers (see https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/)
if (!string.IsNullOrEmpty(config["Authentication:Google:ClientId"]))
{
    authBuilder.AddGoogle(googleOptions =>
    {
        googleOptions.ClientId = config["Authentication:Google:ClientId"]!;
        googleOptions.ClientSecret = config["Authentication:Google:ClientSecret"]!;
    });
}
if (!string.IsNullOrEmpty(config["Authentication:Microsoft:ClientId"]))
{
    authBuilder.AddMicrosoftAccount(microsoftOptions =>
    {
        microsoftOptions.ClientId = config["Authentication:Microsoft:ClientId"]!;
        microsoftOptions.ClientSecret = config["Authentication:Microsoft:ClientSecret"]!;
    });
}

authBuilder.AddIdentityCookies();

// Keep users signed in for 14 days; extend on each request (sliding)
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

// Database
builder.Services.AddDbContext<InfoGenDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"),
        sqlOptions => sqlOptions.EnableRetryOnFailure()));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
})
    .AddEntityFrameworkStores<InfoGenDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Register HttpClient and services
builder.Services.AddHttpClient<IWikipediaService, WikipediaService>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Ficipedia/1.0 (https://www.ficipedia.com)");
});
builder.Services.AddHttpClient<IGeminiService, GeminiService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3); // Image generation can be slow
});

// Storage services
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<ISubscriptionStateService, SubscriptionStateService>();
builder.Services.AddScoped<IArticleStorageService, ArticleStorageService>();

// Stripe
if (!string.IsNullOrEmpty(config["Stripe:SecretKey"]))
{
    builder.Services.AddScoped<IStripeService, StripeService>();
}

builder.Services.AddScoped<IUsageService, UsageService>();

// sitemap.xml/robots.txt only. The default key includes scheme+host, so the Request.Host fallback in
// ResolveBaseUrl can't have one caller's host served back to another.
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(SeoEndpoints.CachePolicy, policy => policy.Expire(TimeSpan.FromHours(6)));
});

var app = builder.Build();

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InfoGenDbContext>();
    db.Database.Migrate();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Database migration failed on startup — save feature won't work until the database is reachable.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

// After the automatic authentication middleware, so the limiter can partition by user id rather
// than falling back to IP (which NATs many users onto one bucket).
app.UseRateLimiter();

app.UseOutputCache();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(InfoGen.Client._Imports).Assembly);

app.MapAdditionalIdentityEndpoints();

app.MapArticleEndpoints();
app.MapWikipediaEndpoints();
app.MapGenerationEndpoints();
app.MapUsageEndpoints();
app.MapSeoEndpoints();
app.MapStripeWebhookEndpoints();
app.MapAdminEndpoints();

// Antiforgery is required explicitly at the group level. The middleware only validates endpoints
// carrying antiforgery metadata, which minimal APIs infer from [FromForm] - so create-checkout and
// create-portal, which take no form parameters, were being posted to without their token ever being
// checked even though the forms have always sent one.
var stripeGroup = app.MapGroup("/stripe")
    .RequireAuthorization()
    .WithMetadata(new RequireAntiforgeryTokenAttribute());

stripeGroup.MapPost("/create-checkout", async (
    HttpContext context,
    UserManager<ApplicationUser> userManager,
    IStripeService stripeService) =>
{
    var user = await userManager.GetUserAsync(context.User);
    if (user is null) return Results.Redirect("/Account/Login");

    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    var checkoutUrl = await stripeService.CreateCheckoutSessionAsync(
        user,
        successUrl: $"{baseUrl}/Account/Profile?status=subscribed",
        cancelUrl: $"{baseUrl}/Account/Profile?status=cancelled");

    return Results.Redirect(checkoutUrl);
});

var accountGroup = app.MapGroup("/Account")
    .RequireAuthorization()
    .WithMetadata(new RequireAntiforgeryTokenAttribute());

accountGroup.MapPost("/UpdateDisplayName", async (
    HttpContext context,
    UserManager<ApplicationUser> userManager,
    [FromForm] string? displayName) =>
{
    var user = await userManager.GetUserAsync(context.User);
    if (user is null) return Results.Redirect("/Account/Login");

    var value = displayName?.Trim();
    if (string.IsNullOrEmpty(value))
    {
        user.DisplayName = null;
        await userManager.UpdateAsync(user);
        return Results.Redirect("/Account/Profile?status=username_cleared");
    }

    var existing = await userManager.Users
        .FirstOrDefaultAsync(u => u.Id != user.Id && u.DisplayName != null && u.DisplayName.ToLower() == value.ToLower());
    if (existing is not null)
        return Results.Redirect("/Account/Profile?status=username_taken");

    user.DisplayName = value;
    await userManager.UpdateAsync(user);
    return Results.Redirect("/Account/Profile?status=username_updated");
});

stripeGroup.MapPost("/create-portal", async (
    HttpContext context,
    UserManager<ApplicationUser> userManager,
    IStripeService stripeService) =>
{
    var user = await userManager.GetUserAsync(context.User);
    if (user is null) return Results.Redirect("/Account/Login");

    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    var portalUrl = await stripeService.CreatePortalSessionAsync(
        user,
        returnUrl: $"{baseUrl}/Account/Profile");

    return Results.Redirect(portalUrl);
});

stripeGroup.MapPost("/create-credit-checkout", async (
    HttpContext context,
    UserManager<ApplicationUser> userManager,
    IStripeService stripeService,
    [FromForm] int packSize) =>
{
    var user = await userManager.GetUserAsync(context.User);
    if (user is null) return Results.Redirect("/Account/Login");

    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    string checkoutUrl;
    try
    {
        checkoutUrl = await stripeService.CreateCreditPackCheckoutSessionAsync(
            user,
            packSize,
            successUrl: $"{baseUrl}/Account/Profile?status=credits_purchased",
            cancelUrl: $"{baseUrl}/Account/Profile?status=cancelled");
    }
    catch (InvalidOperationException)
    {
        return Results.Redirect("/Account/Profile?status=invalid_pack");
    }

    return Results.Redirect(checkoutUrl);
});

app.Run();
