using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMarketplaceStore, InMemoryMarketplaceStore>();
builder.Services.AddSingleton<IEngagementNotifier, FallbackEngagementNotifier>();
builder.Services.AddSingleton<IMarketplaceService, MarketplaceService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/skills", async (IMarketplaceStore store, CancellationToken ct) =>
    Results.Ok(await store.GetSkillsAsync(ct)));

app.MapGet("/api/professionals", async (string? service, string? location, IMarketplaceService marketplace, CancellationToken ct) =>
    Results.Ok(await marketplace.SearchAsync(service, location, ct)));

app.MapGet("/api/professionals/{slug}", async (string slug, IMarketplaceService marketplace, CancellationToken ct) =>
{
    var profile = await marketplace.GetProfileAsync(slug, ct);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
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

app.MapGet("/api/engagements/{id:guid}", async (Guid id, IMarketplaceStore store, CancellationToken ct) =>
{
    var engagement = await store.GetEngagementAsync(id, ct);
    return engagement is null ? Results.NotFound() : Results.Ok(engagement);
});

app.MapFallbackToFile("index.html");
app.Run();
