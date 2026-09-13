using System.Security.Cryptography;
using System.Text;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed record NotificationEndpointInput(
    EngagementChannel Channel,
    string Address,
    string? Label = null,
    bool Enabled = true);

public sealed record ProfessionalNotificationPolicyUpdate(
    EngagementChannel PrimaryChannel,
    EngagementChannel? BusinessChannel,
    EngagementChannel FallbackChannel,
    int EscalationAfterMinutes,
    IReadOnlyCollection<EngagementChannel> EscalationOrder,
    IReadOnlyCollection<NotificationEndpointInput> Endpoints)
{
    public ProfessionalNotificationPolicy ToDomain(Guid professionalId)
    {
        Validate();
        return new ProfessionalNotificationPolicy(
            professionalId,
            PrimaryChannel,
            BusinessChannel,
            FallbackChannel,
            TimeSpan.FromMinutes(EscalationAfterMinutes),
            EscalationOrder.Distinct().ToArray(),
            Endpoints
                .Select(x => new NotificationEndpoint(x.Channel, x.Address.Trim(), x.Label?.Trim(), x.Enabled))
                .ToArray());
    }

    private void Validate()
    {
        if (EscalationAfterMinutes is < 1 or > 1440)
            throw new ArgumentException("EscalationAfterMinutes must be between 1 and 1440.");

        if (PrimaryChannel == EngagementChannel.Web || FallbackChannel == EngagementChannel.Web || BusinessChannel == EngagementChannel.Web)
            throw new ArgumentException("Web/in-app is always the canonical first Ebolito notification and must not be configured as an external primary, business, or fallback channel.");

        if (EscalationOrder.Any(x => x == EngagementChannel.Web))
            throw new ArgumentException("Web/in-app must not appear in EscalationOrder; Ebolito adds it automatically.");

        if (Endpoints.Count == 0)
            throw new ArgumentException("At least one notification endpoint is required.");

        foreach (var endpoint in Endpoints)
        {
            if (endpoint.Channel == EngagementChannel.Web)
                throw new ArgumentException("Web/in-app does not require a notification endpoint.");
            if (string.IsNullOrWhiteSpace(endpoint.Address))
                throw new ArgumentException($"An address is required for {endpoint.Channel}.");
        }

        var duplicates = Endpoints
            .Where(x => x.Enabled)
            .GroupBy(x => (x.Channel, Address: x.Address.Trim()), StringTupleComparer.Instance)
            .Any(x => x.Count() > 1);
        if (duplicates) throw new ArgumentException("Duplicate enabled notification endpoints are not allowed.");

        RequireEndpoint(PrimaryChannel, "primary");
        RequireEndpoint(FallbackChannel, "fallback");
        if (BusinessChannel is not null) RequireEndpoint(BusinessChannel.Value, "business");
    }

    private void RequireEndpoint(EngagementChannel channel, string role)
    {
        if (!Endpoints.Any(x => x.Enabled && x.Channel == channel && !string.IsNullOrWhiteSpace(x.Address)))
            throw new ArgumentException($"The {role} channel {channel} must have an enabled endpoint.");
    }

    private sealed class StringTupleComparer : IEqualityComparer<(EngagementChannel Channel, string Address)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((EngagementChannel Channel, string Address) x, (EngagementChannel Channel, string Address) y) =>
            x.Channel == y.Channel && string.Equals(x.Address, y.Address, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((EngagementChannel Channel, string Address) obj) => HashCode.Combine(obj.Channel, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Address));
    }
}

public static class ProfileAdministration
{
    public const string HeaderName = "X-Ebolito-Profile-Admin-Key";

    public static bool IsAuthorized(HttpRequest request)
    {
        var expected = Environment.GetEnvironmentVariable("EBOLITO_PROFILE_ADMIN_KEY");
        if (string.IsNullOrWhiteSpace(expected)) return false;
        if (!request.Headers.TryGetValue(HeaderName, out var supplied)) return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
