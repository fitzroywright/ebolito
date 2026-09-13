using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public static class ProfessionalInboxEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/professionals/{id:guid}/engagements", async (
            Guid id,
            HttpRequest request,
            IMarketplaceStore store,
            CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            var professional = await store.GetProfessionalAsync(id, ct);
            if (professional is null) return Results.NotFound();

            var engagements = await store.GetEngagementsForProfessionalAsync(id, ct);
            var items = new List<object>(engagements.Count);
            foreach (var engagement in engagements)
            {
                var customer = await store.GetCustomerAsync(engagement.CustomerId, ct);
                var attempts = await store.GetDeliveryAttemptsAsync(engagement.Id, ct);
                items.Add(new
                {
                    engagement,
                    customer = customer is null ? null : new
                    {
                        customer.Id,
                        customer.DisplayName,
                        customer.VerifiedMobileNumber,
                        customer.Email
                    },
                    deliveries = attempts.OrderByDescending(x => x.AttemptedAt).ToArray()
                });
            }

            return Results.Ok(new
            {
                professional = new { professional.Id, professional.DisplayName, professional.BusinessName },
                engagements = items
            });
        });

        return endpoints;
    }
}
