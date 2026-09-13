using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var postgresConnection = builder.Configuration.GetConnectionString("Ebolito");
if (!string.IsNullOrWhiteSpace(postgresConnection))
{
    builder.Services.AddSingleton<IMarketplaceStore>(_ => new PostgresMarketplaceStore(postgresConnection));
}
else
{
    builder.Services.AddSingleton<IMarketplaceStore, InMemoryMarketplaceStore>();
}

builder.Services.AddSingleton<IEngagementNotifier, FallbackEngagementNotifier>();
builder.Services.AddSingleton<IMarketplaceService, MarketplaceService>();
builder.Services.AddSingleton<IVerificationChallengeStore, InMemoryVerificationChallengeStore>();
builder.Services.AddSingleton<IMobileVerificationSender>(builder.Environment.IsDevelopment()
    ? new DevelopmentMobileVerificationSender()
    : new DisabledMobileVerificationSender());
builder.Services.AddSingleton<ICustomerIdentityService, CustomerIdentityService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (IMarketplaceStore store, CancellationToken ct) =>
{
    if (store is PostgresMarketplaceStore postgres)
    {
        try
        {
            return await postgres.CanConnectAsync(ct)
                ? Results.Ok(new { status = "ready", store = "postgresql" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    return Results.Ok(new { status = "ready", store = "memory" });
});

app.MapGet("/api/skills", async (IMarketplaceStore store, CancellationToken ct) =>
    Results.Ok(await store.GetSkillsAsync(ct)));

app.MapGet("/api/professionals", async (string? service, string? location, IMarketplaceService marketplace, CancellationToken ct) =>
    Results.Ok(await marketplace.SearchAsync(service, location, ct)));

app.MapGet("/api/professionals/{slug}", async (string slug, IMarketplaceService marketplace, CancellationToken ct) =>
{
    var profile = await marketplace.GetProfileAsync(slug, ct);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
});

app.MapPost("/api/identity/mobile/start", async (MobileVerificationStart request, ICustomerIdentityService identity, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await identity.StartAsync(request, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/identity/mobile/complete", async (MobileVerificationComplete request, ICustomerIdentityService identity, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await identity.CompleteAsync(request, ct));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/engagements", async (EngagementRequest request, IMarketplaceService marketplace, CancellationToken ct) =>
{
    try
    {
        var engagement = await marketplace.RequestEngagementAsync(request, ct);
        return Results.Created($"/api/engagements/{engagement.Id}", engagement);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/engagements/{id:guid}/response", async (Guid id, EngagementResponse response, IMarketplaceService marketplace, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await marketplace.RespondToEngagementAsync(id, response, ct));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/engagements/{id:guid}", async (Guid id, IMarketplaceStore store, CancellationToken ct) =>
{
    var engagement = await store.GetEngagementAsync(id, ct);
    return engagement is null ? Results.NotFound() : Results.Ok(engagement);
});

app.MapFallbackToFile("index.html");
app.Run();
