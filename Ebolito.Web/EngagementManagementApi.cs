using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public enum EngagementProgressAction
{
    Contacted = 0,
    Hired = 1,
    Completed = 2
}

public sealed record EngagementProgressUpdate(EngagementProgressAction Action);

public static class EngagementManagementEndpoints
{
    public static IEndpointRouteBuilder MapEngagementManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/engagements/{id:guid}/progress", async (
            Guid id,
            HttpRequest request,
            EngagementProgressUpdate update,
            IMarketplaceStore store,
            CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();

            var engagement = await store.GetEngagementAsync(id, ct);
            if (engagement is null) return Results.NotFound();

            try
            {
                switch (update.Action)
                {
                    case EngagementProgressAction.Contacted:
                        engagement.MarkContacted();
                        break;
                    case EngagementProgressAction.Hired:
                        engagement.MarkHired();
                        break;
                    case EngagementProgressAction.Completed:
                        engagement.Complete();
                        break;
                    default:
                        return Results.BadRequest(new { error = "Unsupported engagement progress action." });
                }

                await store.SaveEngagementAsync(engagement, ct);
                return Results.Ok(new
                {
                    engagement,
                    reviewUrl = engagement.Status == EngagementStatus.Completed
                        ? $"/review.html?engagement={engagement.Id:D}"
                        : null
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        return endpoints;
    }
}
