using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public static class CustomerEngagementEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEngagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/customer/engagements", async (
            HttpRequest request,
            CustomerSessionTokenService sessions,
            IMarketplaceStore store,
            CancellationToken ct) =>
        {
            if (!sessions.TryValidate(request, out var customerId)) return Results.Unauthorized();
            var customer = await store.GetCustomerAsync(customerId, ct);
            if (customer is null) return Results.Unauthorized();

            var engagements = await store.GetEngagementsForCustomerAsync(customerId, ct);
            var items = new List<object>(engagements.Count);
            foreach (var engagement in engagements)
            {
                var professional = await store.GetProfessionalAsync(engagement.ProfessionalId, ct);
                var review = await store.GetReviewByEngagementAsync(engagement.Id, ct);
                items.Add(new
                {
                    engagement,
                    professional = professional is null ? null : new
                    {
                        professional.Id,
                        professional.Slug,
                        professional.DisplayName,
                        professional.BusinessName,
                        professional.PhoneNumber,
                        professional.WhatsAppNumber
                    },
                    reviewSubmitted = review is not null,
                    reviewUrl = engagement.Status == EngagementStatus.Completed && review is null
                        ? $"/review.html?engagement={engagement.Id:D}"
                        : null
                });
            }

            return Results.Ok(new
            {
                customer = new { customer.Id, customer.DisplayName, customer.VerifiedMobileNumber, customer.Email },
                engagements = items
            });
        });

        return endpoints;
    }
}
