using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed record ProfessionalEngagementInboxItem(
    Guid Id,
    string CustomerName,
    string RequestText,
    string Location,
    EngagementStatus Status,
    EngagementChannel RequestedChannel,
    EngagementChannel? DeliveredChannel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class ProfessionalEngagementInboxEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalEngagementInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/professionals/{professionalId:guid}/engagements", async (
            Guid professionalId,
            HttpRequest request,
            IMarketplaceStore store,
            CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            if (await store.GetProfessionalAsync(professionalId, ct) is null) return Results.NotFound();

            var engagements = await store.GetEngagementsForProfessionalAsync(professionalId, ct);
            var items = new List<ProfessionalEngagementInboxItem>(engagements.Count);
            foreach (var engagement in engagements)
            {
                var customer = await store.GetCustomerAsync(engagement.CustomerId, ct);
                items.Add(new ProfessionalEngagementInboxItem(
                    engagement.Id,
                    customer?.DisplayName ?? "Customer",
                    engagement.RequestText,
                    engagement.Location,
                    engagement.Status,
                    engagement.RequestedChannel,
                    engagement.DeliveredChannel,
                    engagement.CreatedAt,
                    engagement.UpdatedAt));
            }

            return Results.Ok(items);
        });

        return endpoints;
    }
}
