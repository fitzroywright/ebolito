using Ebolito.Application.Engagements;
using Ebolito.Domain.Engagements;

namespace Ebolito.Infrastructure.Messaging;

// Keep Ebolito independent of Slack, WhatsApp, SMS, email and in-app transports.
// The host adapts this envelope to Common.Messaging; channel implementations remain there.
public sealed record EngagementNotification(
    string EventName,
    Guid EngagementId,
    Guid ProfessionalId,
    string Title,
    string Body,
    string ViewAction,
    string? AcceptAction = null,
    string? DeclineAction = null);

public interface ICommonMessagingPublisher
{
    Task PublishAsync(EngagementNotification notification, CancellationToken cancellationToken = default);
}

public sealed class CommonMessagingEngagementNotifier(ICommonMessagingPublisher publisher) : IEngagementNotifier
{
    public Task NotifyRequestedAsync(Engagement engagement, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(new EngagementNotification(
            "ebolito.engagement.requested",
            engagement.Id,
            engagement.ProfessionalId,
            "New Ebolito Engagement",
            BuildRequestBody(engagement),
            $"/engagements/{engagement.Id}",
            $"/engagements/{engagement.Id}/accept",
            $"/engagements/{engagement.Id}/decline"), cancellationToken);

    public Task NotifyStatusChangedAsync(Engagement engagement, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(new EngagementNotification(
            "ebolito.engagement.status-changed",
            engagement.Id,
            engagement.ProfessionalId,
            $"Engagement {engagement.Status}",
            $"Your Ebolito engagement for {engagement.Service} is now {engagement.Status}.",
            $"/engagements/{engagement.Id}"), cancellationToken);

    private static string BuildRequestBody(Engagement engagement)
    {
        var body = $"A customer wants to discuss {engagement.Service}.";
        if (!string.IsNullOrWhiteSpace(engagement.Message)) body += $"\n\nRequested: {engagement.Message}";
        if (!string.IsNullOrWhiteSpace(engagement.PreferredContactChannel)) body += $"\n\nPreferred contact: {engagement.PreferredContactChannel}";
        return body;
    }
}