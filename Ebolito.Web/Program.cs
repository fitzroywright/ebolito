using System.Net;
using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;
using Ebolito.Web;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

var postgresConnection = builder.Configuration.GetConnectionString("Ebolito");
if (!string.IsNullOrWhiteSpace(postgresConnection))
    builder.Services.AddSingleton<IMarketplaceStore>(_ => new PostgresMarketplaceStore(postgresConnection));
else
    builder.Services.AddSingleton<IMarketplaceStore, InMemoryMarketplaceStore>();

builder.Services.AddSingleton<SecureEngagementActionLinks>();
builder.Services.AddSingleton<IEngagementActionLinkBuilder>(provider => provider.GetRequiredService<SecureEngagementActionLinks>());

#if COMMON_MESSAGING
builder.Services.AddEbolitoCommonMessaging(builder.Configuration, postgresConnection);
#else
builder.Services.AddSingleton<IEngagementNotifier, FallbackEngagementNotifier>();
#endif

builder.Services.AddSingleton<IMarketplaceService, MarketplaceService>();
builder.Services.AddSingleton<IVerificationChallengeStore, InMemoryVerificationChallengeStore>();
builder.Services.TryAddSingleton<IMobileVerificationSender>(_ => builder.Environment.IsDevelopment()
    ? new DevelopmentMobileVerificationSender()
    : new DisabledMobileVerificationSender());
builder.Services.AddSingleton<ICustomerIdentityService, CustomerIdentityService>();
builder.Services.AddSingleton<EbolitoEngineeringDiagnostics>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<ConfigurationRegistrationHostedService>();
builder.Services.AddHostedService<EngagementEscalationHostedService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapProfessionalNotificationPolicyEndpoints();
app.MapVerifiedReviewEndpoints();

async Task<IResult> ReadyResult(IMarketplaceStore store, CancellationToken ct)
{
    if (store is PostgresMarketplaceStore postgres)
    {
        try
        {
            return await postgres.CanConnectAsync(ct)
                ? Results.Ok(new { status = "ready", store = "postgresql", application = "Ebolito" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    return Results.Ok(new { status = "ready", store = "memory", application = "Ebolito" });
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live", application = "Ebolito" }));
app.MapGet("/health/ready", ReadyResult);
app.MapGet("/health", ReadyResult);

app.MapGet("/api/skills", async (IMarketplaceStore store, CancellationToken ct) => Results.Ok(await store.GetSkillsAsync(ct)));

app.MapGet("/api/professionals", async (string? service, string? location, IMarketplaceService marketplace, CancellationToken ct) =>
    Results.Ok(await marketplace.SearchAsync(service, location, ct)));

app.MapGet("/api/professionals/{slug}", async (string slug, IMarketplaceService marketplace, CancellationToken ct) =>
{
    var profile = await marketplace.GetProfileAsync(slug, ct);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
});

app.MapPost("/api/identity/mobile/start", async (MobileVerificationStart request, ICustomerIdentityService identity, CancellationToken ct) =>
{
    try { return Results.Ok(await identity.StartAsync(request, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapPost("/api/identity/mobile/complete", async (MobileVerificationComplete request, ICustomerIdentityService identity, CancellationToken ct) =>
{
    try { return Results.Ok(await identity.CompleteAsync(request, ct)); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/engagements", async (EngagementRequest request, IMarketplaceService marketplace, CancellationToken ct) =>
{
    try
    {
        var engagement = await marketplace.RequestEngagementAsync(request, ct);
        return Results.Created($"/api/engagements/{engagement.Id}", engagement);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/engagements/{id:guid}/response", async (Guid id, EngagementResponse response, IMarketplaceService marketplace, CancellationToken ct) =>
{
    try { return Results.Ok(await marketplace.RespondToEngagementAsync(id, response, ct)); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/engagements/{id:guid}", async (Guid id, IMarketplaceStore store, CancellationToken ct) =>
{
    var engagement = await store.GetEngagementAsync(id, ct);
    return engagement is null ? Results.NotFound() : Results.Ok(engagement);
});

app.MapGet("/api/engagements/{id:guid}/deliveries", async (Guid id, IMarketplaceStore store, CancellationToken ct) =>
    Results.Ok(await store.GetDeliveryAttemptsAsync(id, ct)));

app.MapGet("/engagements/{id:guid}/view", async (Guid id, long expires, string sig, SecureEngagementActionLinks links, IMarketplaceStore store, CancellationToken ct) =>
{
    if (!links.Verify(id, "view", expires, sig)) return Results.Unauthorized();
    var engagement = await store.GetEngagementAsync(id, ct);
    if (engagement is null) return Results.NotFound();
    var professional = await store.GetProfessionalAsync(engagement.ProfessionalId, ct);
    var customer = await store.GetCustomerAsync(engagement.CustomerId, ct);

    var html = $"""
        <!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><title>Ebolito Engagement</title></head>
        <body style="font-family:Arial,sans-serif;max-width:720px;margin:40px auto;padding:0 20px">
        <h1>Ebolito Engagement</h1>
        <p><b>Status:</b> {WebUtility.HtmlEncode(engagement.Status.ToString())}</p>
        <p><b>Professional:</b> {WebUtility.HtmlEncode(professional?.DisplayName ?? "Professional")}</p>
        <p><b>Customer:</b> {WebUtility.HtmlEncode(customer?.DisplayName ?? "Customer")}</p>
        <p><b>Location:</b> {WebUtility.HtmlEncode(engagement.Location)}</p>
        <h2>Request</h2><p>{WebUtility.HtmlEncode(engagement.RequestText)}</p>
        </body></html>
        """;
    return Results.Content(html, "text/html");
});

app.MapGet("/engagements/{id:guid}/respond", async (Guid id, string decision, long expires, string sig, SecureEngagementActionLinks links, IMarketplaceStore store, CancellationToken ct) =>
{
    var normalized = decision.Equals("accept", StringComparison.OrdinalIgnoreCase) ? "accept" : decision.Equals("decline", StringComparison.OrdinalIgnoreCase) ? "decline" : null;
    if (normalized is null || !links.Verify(id, normalized, expires, sig)) return Results.Unauthorized();

    var engagement = await store.GetEngagementAsync(id, ct);
    if (engagement is null) return Results.NotFound();
    if (engagement.Status != EngagementStatus.Delivered)
        return Results.Content($"<h1>Ebolito</h1><p>This request is already {WebUtility.HtmlEncode(engagement.Status.ToString())}.</p>", "text/html");

    var verb = normalized == "accept" ? "Accept" : "Decline";
    var html = $"""
        <!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><title>{verb} Ebolito Request</title></head>
        <body style="font-family:Arial,sans-serif;max-width:720px;margin:40px auto;padding:0 20px">
        <h1>{verb} this Ebolito request?</h1>
        <p>{WebUtility.HtmlEncode(engagement.RequestText)}</p>
        <form method="post">
          <input type="hidden" name="decision" value="{normalized}">
          <input type="hidden" name="expires" value="{expires}">
          <input type="hidden" name="sig" value="{WebUtility.HtmlEncode(sig)}">
          <button type="submit" style="padding:12px 22px">Confirm {verb}</button>
        </form>
        </body></html>
        """;
    return Results.Content(html, "text/html");
});

app.MapPost("/engagements/{id:guid}/respond", async (Guid id, HttpRequest request, SecureEngagementActionLinks links, IMarketplaceService marketplace, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var decision = form["decision"].ToString().ToLowerInvariant();
    if (!long.TryParse(form["expires"], out var expires)) return Results.BadRequest("Invalid action link.");
    var sig = form["sig"].ToString();
    if ((decision != "accept" && decision != "decline") || !links.Verify(id, decision, expires, sig)) return Results.Unauthorized();

    try
    {
        var response = decision == "accept" ? EngagementResponse.Accept : EngagementResponse.Decline;
        var engagement = await marketplace.RespondToEngagementAsync(id, response, ct);
        var message = engagement.Status == EngagementStatus.Accepted
            ? "You accepted the request. Ebolito has notified the customer."
            : "You declined the request. Ebolito has notified the customer.";
        return Results.Content($"<h1>Ebolito</h1><p>{WebUtility.HtmlEncode(message)}</p>", "text/html");
    }
    catch (InvalidOperationException ex)
    {
        return Results.Content($"<h1>Ebolito</h1><p>{WebUtility.HtmlEncode(ex.Message)}</p>", "text/html", statusCode: StatusCodes.Status409Conflict);
    }
});

app.MapPost("/api/engineering/diagnostics/run", async (HttpRequest httpRequest, EngineeringDiagnosticRunRequest request, EbolitoEngineeringDiagnostics diagnostics, CancellationToken ct) =>
{
    if (!EbolitoEngineeringDiagnostics.IsAuthorized(httpRequest)) return Results.Unauthorized();
    return Results.Ok(await diagnostics.RunAsync(request, "Aegis.Diagnostics", ct));
});

app.MapGet("/api/engineering/diagnostics/runs", (HttpRequest request, EbolitoEngineeringDiagnostics diagnostics) =>
{
    if (!EbolitoEngineeringDiagnostics.IsAuthorized(request)) return Results.Unauthorized();
    return Results.Ok(diagnostics.GetRecent());
});

app.MapGet("/api/engineering/diagnostics/runs/{runId:guid}", (Guid runId, HttpRequest request, EbolitoEngineeringDiagnostics diagnostics) =>
{
    if (!EbolitoEngineeringDiagnostics.IsAuthorized(request)) return Results.Unauthorized();
    var run = diagnostics.Get(runId);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapPost("/api/engineering/diagnostics/runs/{runId:guid}/resolve", (Guid runId, HttpRequest request, EngineeringDiagnosticResolutionRequest resolution, EbolitoEngineeringDiagnostics diagnostics) =>
{
    if (!EbolitoEngineeringDiagnostics.IsAuthorized(request)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(resolution.Resolution)) return Results.BadRequest(new { error = "Resolution is required." });
    var run = diagnostics.Resolve(runId, "Aegis.Diagnostics", resolution.Resolution.Trim());
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapFallbackToFile("index.html");
app.Run();
