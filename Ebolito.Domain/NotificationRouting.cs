namespace Ebolito.Domain;

public sealed record NotificationEndpoint(
    EngagementChannel Channel,
    string Address,
    string? Label = null,
    bool Enabled = true);

public sealed record ProfessionalNotificationPolicy(
    Guid ProfessionalId,
    EngagementChannel PrimaryChannel,
    EngagementChannel? BusinessChannel,
    EngagementChannel FallbackChannel,
    TimeSpan EscalationAfter,
    IReadOnlyCollection<EngagementChannel> EscalationOrder,
    IReadOnlyCollection<NotificationEndpoint> Endpoints)
{
    public IReadOnlyList<EngagementChannel> BuildRoute()
    {
        var route = new List<EngagementChannel> { EngagementChannel.Web, PrimaryChannel };
        if (BusinessChannel is not null) route.Add(BusinessChannel.Value);
        route.AddRange(EscalationOrder);
        route.Add(FallbackChannel);

        return route.Distinct().ToArray();
    }

    public bool HasEndpoint(EngagementChannel channel) =>
        channel == EngagementChannel.Web || Endpoints.Any(x => x.Enabled && x.Channel == channel && !string.IsNullOrWhiteSpace(x.Address));
}

public sealed record EngagementDeliveryAttempt(
    Guid Id,
    Guid EngagementId,
    EngagementChannel Channel,
    DateTimeOffset AttemptedAt,
    bool Succeeded,
    string? Detail = null);
