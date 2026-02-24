using Microsoft.AspNetCore.Components.Authorization;
using InfoGen.Components;
using InfoGen.Data;
using InfoGen.Services;
using Microsoft.AspNetCore.Identity;
using InfoGen.Components.Account;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

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
    client.DefaultRequestHeaders.Add("User-Agent", "InfoGen/1.0 (https://github.com/yourusername/InfoGen; contact@example.com)");
});
builder.Services.AddHttpClient<IGeminiService, GeminiService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3); // Image generation can be slow
});

// Storage services
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IArticleStorageService, ArticleStorageService>();

// Stripe
if (!string.IsNullOrEmpty(config["Stripe:SecretKey"]))
{
    builder.Services.AddScoped<IStripeService, StripeService>();
}

builder.Services.AddScoped<IUsageService, UsageService>();

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

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

var stripeGroup = app.MapGroup("/stripe").RequireAuthorization();

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

app.Run();
