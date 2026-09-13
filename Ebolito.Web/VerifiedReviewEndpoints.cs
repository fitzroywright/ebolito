using Ebolito.Application;

namespace Ebolito.Web;

public static class VerifiedReviewEndpoints
{
    public static IEndpointRouteBuilder MapVerifiedReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/reviews", async (VerifiedReviewRequest request, IMarketplaceService marketplace, CancellationToken ct) =>
        {
            try
            {
                var review = await marketplace.SubmitVerifiedReviewAsync(request, ct);
                return Results.Created($"/api/professionals/{review.ProfessionalId}/reviews/{review.Id}", review);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        return endpoints;
    }
}
