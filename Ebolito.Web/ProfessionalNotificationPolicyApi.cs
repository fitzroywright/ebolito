using System.Security.Cryptography;
using System.Text;
using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed record NotificationEndpointInput(EngagementChannel Channel, string Address, string? Label = null, bool Enabled = true);
public sealed record ProfessionalNotificationPolicyUpdate(EngagementChannel PrimaryChannel, EngagementChannel? BusinessChannel, EngagementChannel FallbackChannel, int EscalationAfterMinutes, IReadOnlyCollection<EngagementChannel> EscalationOrder, IReadOnlyCollection<NotificationEndpointInput> Endpoints)
{
    public ProfessionalNotificationPolicy ToDomain(Guid professionalId)
    {
        Validate();
        return new ProfessionalNotificationPolicy(professionalId, PrimaryChannel, BusinessChannel, FallbackChannel, TimeSpan.FromMinutes(EscalationAfterMinutes), EscalationOrder.Distinct().ToArray(), Endpoints.Select(x => new NotificationEndpoint(x.Channel, x.Address.Trim(), x.Label?.Trim(), x.Enabled)).ToArray());
    }
    private void Validate()
    {
        if (EscalationAfterMinutes is < 1 or > 1440) throw new ArgumentException("EscalationAfterMinutes must be between 1 and 1440.");
        if (PrimaryChannel == EngagementChannel.Web || FallbackChannel == EngagementChannel.Web || BusinessChannel == EngagementChannel.Web) throw new ArgumentException("Web/in-app is always the canonical first Ebolito notification and must not be configured as an external primary, business, or fallback channel.");
        if (EscalationOrder.Any(x => x == EngagementChannel.Web)) throw new ArgumentException("Web/in-app must not appear in EscalationOrder; Ebolito adds it automatically.");
        if (Endpoints.Count == 0) throw new ArgumentException("At least one notification endpoint is required.");
        foreach (var endpoint in Endpoints)
        {
            if (endpoint.Channel == EngagementChannel.Web) throw new ArgumentException("Web/in-app does not require a notification endpoint.");
            if (string.IsNullOrWhiteSpace(endpoint.Address)) throw new ArgumentException($"An address is required for {endpoint.Channel}.");
        }
        var duplicates = Endpoints.Where(x => x.Enabled).GroupBy(x => (x.Channel, Address: x.Address.Trim()), StringTupleComparer.Instance).Any(x => x.Count() > 1);
        if (duplicates) throw new ArgumentException("Duplicate enabled notification endpoints are not allowed.");
        RequireEndpoint(PrimaryChannel, "primary"); RequireEndpoint(FallbackChannel, "fallback"); if (BusinessChannel is not null) RequireEndpoint(BusinessChannel.Value, "business");
    }
    private void RequireEndpoint(EngagementChannel channel, string role) { if (!Endpoints.Any(x => x.Enabled && x.Channel == channel && !string.IsNullOrWhiteSpace(x.Address))) throw new ArgumentException($"The {role} channel {channel} must have an enabled endpoint."); }
    private sealed class StringTupleComparer : IEqualityComparer<(EngagementChannel Channel, string Address)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((EngagementChannel Channel, string Address) x, (EngagementChannel Channel, string Address) y) => x.Channel == y.Channel && string.Equals(x.Address, y.Address, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((EngagementChannel Channel, string Address) obj) => HashCode.Combine(obj.Channel, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Address));
    }
}

public static class ProfileAdministration
{
    public const string HeaderName = "X-Ebolito-Profile-Admin-Key";
    public static bool IsAuthorized(HttpRequest request)
    {
        var expected = Environment.GetEnvironmentVariable("EBOLITO_PROFILE_ADMIN_KEY");
        if (string.IsNullOrWhiteSpace(expected) || !request.Headers.TryGetValue(HeaderName, out var supplied)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected); var suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}

public static class ProfessionalNotificationPolicyEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalNotificationPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapProfessionalSiteManagementEndpoints();

        object Capabilities(IConfiguration configuration)
        {
#if COMMON_MESSAGING
            const bool commonMessagingCompiled = true;
#else
            const bool commonMessagingCompiled = false;
#endif
            var configured = new Dictionary<string, bool>
            {
                [nameof(EngagementChannel.Slack)] = configuration.GetValue("Messaging:Slack:Enabled", false),
                [nameof(EngagementChannel.Teams)] = configuration.GetValue("Messaging:Teams:Enabled", false),
                [nameof(EngagementChannel.Email)] = configuration.GetValue("Messaging:Email:Enabled", false),
                [nameof(EngagementChannel.Sms)] = configuration.GetValue("Messaging:Sms:Enabled", false),
                [nameof(EngagementChannel.WhatsApp)] = false,
                [nameof(EngagementChannel.Push)] = false,
                [nameof(EngagementChannel.Webhook)] = false,
                [nameof(EngagementChannel.Messenger)] = false,
                [nameof(EngagementChannel.Instagram)] = false
            };
            return new { commonMessagingCompiled, canonicalInAppChannel = EngagementChannel.Web, configured, note = "WhatsApp/Push/Webhook/Messenger/Instagram are modeled by Ebolito policy but require Common.Messaging providers before production delivery is advertised." };
        }

        endpoints.MapGet("/api/admin/messaging/capabilities", (HttpRequest request, IConfiguration configuration) => ProfileAdministration.IsAuthorized(request) ? Results.Ok(Capabilities(configuration)) : Results.Unauthorized());
        endpoints.MapGet("/api/admin/professionals/{id:guid}/messaging/capabilities", (Guid id, HttpRequest request, ProfessionalSessionTokenService sessions, IConfiguration configuration) => ProfessionalAccess.IsAuthorized(request, id, sessions) ? Results.Ok(Capabilities(configuration)) : Results.Unauthorized());

        endpoints.MapGet("/api/admin/professionals/{id:guid}/notification-policy", async (Guid id, HttpRequest request, ProfessionalSessionTokenService sessions, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfessionalAccess.IsAuthorized(request, id, sessions)) return Results.Unauthorized();
            if (await store.GetProfessionalAsync(id, ct) is null) return Results.NotFound();
            return Results.Ok(await store.GetNotificationPolicyAsync(id, ct));
        });

        endpoints.MapPut("/api/admin/professionals/{id:guid}/notification-policy", async (Guid id, HttpRequest request, ProfessionalSessionTokenService sessions, ProfessionalNotificationPolicyUpdate update, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfessionalAccess.IsAuthorized(request, id, sessions)) return Results.Unauthorized();
            if (await store.GetProfessionalAsync(id, ct) is null) return Results.NotFound();
            try { var policy = update.ToDomain(id); await store.SaveNotificationPolicyAsync(policy, ct); return Results.Ok(policy); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        return endpoints;
    }
}
