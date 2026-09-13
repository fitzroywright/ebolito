using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed record ProfessionalScreeningDecision(bool? IsScreened = null, bool? IsActive = null);

public static class ProfessionalScreeningEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalScreeningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/screening/professionals", async (HttpRequest request, string? status, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            var professionals = await store.GetProfessionalsAsync(ct);
            var query = professionals.AsEnumerable();
            query = (status ?? "pending").Trim().ToLowerInvariant() switch
            {
                "pending" => query.Where(x => x.IsActive && !x.IsScreened),
                "screened" => query.Where(x => x.IsActive && x.IsScreened),
                "inactive" => query.Where(x => !x.IsActive),
                "all" => query,
                _ => query.Where(x => x.IsActive && !x.IsScreened)
            };
            var result = query.OrderBy(x => x.DisplayName).Select(x => new
            {
                x.Id,
                x.Slug,
                x.DisplayName,
                x.BusinessName,
                x.Headline,
                x.About,
                x.PhoneNumber,
                x.WhatsAppNumber,
                x.SkillIds,
                x.ServiceAreas,
                x.IsScreened,
                x.IsActive
            }).ToArray();
            return Results.Ok(result);
        });

        endpoints.MapPost("/api/admin/screening/professionals/{id:guid}", async (Guid id, HttpRequest request, ProfessionalScreeningDecision decision, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            var current = await store.GetProfessionalAsync(id, ct);
            if (current is null) return Results.NotFound();
            var updated = current with
            {
                IsScreened = decision.IsScreened ?? current.IsScreened,
                IsActive = decision.IsActive ?? current.IsActive
            };
            await store.SaveProfessionalAsync(updated, ct);
            return Results.Ok(updated);
        });

        return endpoints;
    }
}
