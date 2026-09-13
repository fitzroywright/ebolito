#if COMMON_MESSAGING
using Common.Messaging;
using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Infrastructure;

public sealed class CommonMessagingEngagementNotifier : IEngagementNotifier
{
    private const string SlackChannelMetadataKey = "slack.channel";
    private readonly IExternalDeliveryQueue queue;
    private readonly HashSet<EngagementChannel> supportedExternalChannels;
    private readonly IEngagementActionLinkBuilder? actionLinks;

    public CommonMessagingEngagementNotifier(
        IExternalDeliveryQueue queue,
        IEnumerable<EngagementChannel> supportedExternalChannels,
        IEngagementActionLinkBuilder? actionLinks = null)
    {
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.supportedExternalChannels = supportedExternalChannels?.ToHashSet()
            ?? throw new ArgumentNullException(nameof(supportedExternalChannels));
        this.actionLinks = actionLinks;
    }

    public async Task<EngagementChannel?> DeliverAsync(
        Professional professional,
        CustomerIdentity customer,
        Engagement engagement,
        ProfessionalNotificationPolicy policy,
        IReadOnlyCollection<EngagementDeliveryAttempt> previousAttempts,
        CancellationToken cancellationToken = default)
    {
        var attempted = previousAttempts.Where(x => x.Succeeded).Select(x => x.Channel).ToHashSet();

        foreach (var channel in policy.BuildRoute())
        {
            if (attempted.Contains(channel) || !policy.HasEndpoint(channel)) continue;

            if (channel == EngagementChannel.Web)
                return EngagementChannel.Web;

            if (!supportedExternalChannels.Contains(channel)) continue;
            var endpoint = policy.Endpoints.FirstOrDefault(x => x.Enabled && x.Channel == channel && !string.IsNullOrWhiteSpace(x.Address));
            if (endpoint is null) continue;

            var commonChannel = MapChannel(channel);
            if (commonChannel == MessageChannel.None) continue;

            var metadata = BuildMetadata(engagement, professional, endpoint, channel);
            var recipient = BuildProfessionalRecipient(professional, endpoint, channel, commonChannel);
            var request = new MessageRequest
            {
                RecipientIds = [recipient.UserId],
                Title = "New Ebolito Engagement",
                Body = BuildProfessionalBody(customer, engagement),
                Severity = MessageSeverity.Information,
                Channels = commonChannel,
                Source = "Ebolito",
                CorrelationId = engagement.Id.ToString("D"),
                Metadata = metadata
            };

            await queue.EnqueueAsync(new ExternalDeliveryWorkItem(
                Guid.NewGuid(),
                [recipient],
                commonChannel,
                request,
                engagement.Id.ToString("D"),
                "Ebolito Engagement",
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

            return channel;
        }

        return null;
    }

    public async Task NotifyCustomerAsync(
        CustomerIdentity customer,
        Professional professional,
        Engagement engagement,
        CancellationToken cancellationToken = default)
    {
        if (engagement.RequestedChannel == EngagementChannel.Web) return;

        EngagementChannel channel = engagement.RequestedChannel;
        if (!supportedExternalChannels.Contains(channel))
        {
            channel = !string.IsNullOrWhiteSpace(customer.Email) && supportedExternalChannels.Contains(EngagementChannel.Email)
                ? EngagementChannel.Email
                : EngagementChannel.Sms;
        }

        if (!supportedExternalChannels.Contains(channel)) return;
        var commonChannel = MapChannel(channel);
        if (commonChannel == MessageChannel.None) return;

        var recipient = new RecipientSnapshot(
            customer.Id.ToString("D"),
            customer.DisplayName,
            channel == EngagementChannel.Email ? customer.Email : null,
            null,
            channel == EngagementChannel.Sms || channel == EngagementChannel.WhatsApp ? customer.VerifiedMobileNumber : null,
            commonChannel);

        var decision = engagement.Status == EngagementStatus.Accepted ? "accepted" : "declined";
        var body = $"{professional.DisplayName} has {decision} your Ebolito engagement request.";
        if (engagement.Status == EngagementStatus.Accepted && !string.IsNullOrWhiteSpace(professional.PhoneNumber))
            body += $" Contact: {professional.PhoneNumber}.";

        var request = new MessageRequest
        {
            RecipientIds = [recipient.UserId],
            Title = $"Ebolito request {decision}",
            Body = body,
            Severity = MessageSeverity.Information,
            Channels = commonChannel,
            Source = "Ebolito",
            CorrelationId = engagement.Id.ToString("D"),
            Metadata = new Dictionary<string, string>
            {
                ["ebolito.engagementId"] = engagement.Id.ToString("D"),
                ["ebolito.event"] = engagement.Status == EngagementStatus.Accepted ? "accepted" : "declined"
            }
        };

        await queue.EnqueueAsync(new ExternalDeliveryWorkItem(
            Guid.NewGuid(),
            [recipient],
            commonChannel,
            request,
            engagement.Id.ToString("D"),
            "Ebolito Customer Notification",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private static RecipientSnapshot BuildProfessionalRecipient(
        Professional professional,
        NotificationEndpoint endpoint,
        EngagementChannel channel,
        MessageChannel commonChannel)
    {
        var address = endpoint.Address.Trim();
        string? email = null;
        string? slackUserId = null;
        string? mobile = null;

        switch (channel)
        {
            case EngagementChannel.Email:
                email = address;
                break;
            case EngagementChannel.Slack when LooksLikeSlackUserId(address):
                slackUserId = address;
                break;
            case EngagementChannel.Sms:
            case EngagementChannel.WhatsApp:
                mobile = address;
                break;
        }

        return new RecipientSnapshot(
            professional.Id.ToString("D"),
            professional.DisplayName,
            email,
            slackUserId,
            mobile,
            commonChannel);
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(
        Engagement engagement,
        Professional professional,
        NotificationEndpoint endpoint,
        EngagementChannel channel)
    {
        var metadata = new Dictionary<string, string>
        {
            ["ebolito.engagementId"] = engagement.Id.ToString("D"),
            ["ebolito.professionalId"] = professional.Id.ToString("D"),
            ["ebolito.customerPreferredContact"] = engagement.RequestedChannel.ToString()
        };

        if (channel == EngagementChannel.Slack && !LooksLikeSlackUserId(endpoint.Address))
            metadata[SlackChannelMetadataKey] = endpoint.Address.Trim();

        return metadata;
    }

    private string BuildProfessionalBody(CustomerIdentity customer, Engagement engagement)
    {
        var body = $"{customer.DisplayName} wants to discuss a service.\n\n" +
                   $"Request: {engagement.RequestText}\n" +
                   $"Location: {engagement.Location}\n" +
                   $"Customer prefers: {engagement.RequestedChannel}";

        var view = actionLinks?.BuildViewLink(engagement.Id);
        var accept = actionLinks?.BuildResponseLink(engagement.Id, EngagementResponse.Accept);
        var decline = actionLinks?.BuildResponseLink(engagement.Id, EngagementResponse.Decline);

        if (!string.IsNullOrWhiteSpace(view)) body += $"\n\nView: {view}";
        if (!string.IsNullOrWhiteSpace(accept)) body += $"\nAccept: {accept}";
        if (!string.IsNullOrWhiteSpace(decline)) body += $"\nDecline: {decline}";
        if (string.IsNullOrWhiteSpace(view) && string.IsNullOrWhiteSpace(accept) && string.IsNullOrWhiteSpace(decline))
            body += $"\nEngagement: {engagement.Id:D}";

        return body;
    }

    private static bool LooksLikeSlackUserId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.TrimStart().StartsWith('U');

    private static MessageChannel MapChannel(EngagementChannel channel) => channel switch
    {
        EngagementChannel.Slack => MessageChannel.Slack,
        EngagementChannel.Teams => MessageChannel.MsTeams,
        EngagementChannel.Email => MessageChannel.Smtp,
        EngagementChannel.Sms => MessageChannel.Sms,
        EngagementChannel.WhatsApp => MessageChannel.WhatsApp,
        _ => MessageChannel.None
    };
}

public sealed class CommonMessagingMobileVerificationSender(IExternalDeliveryQueue queue) : IMobileVerificationSender
{
    public async Task SendCodeAsync(string mobileNumber, string code, CancellationToken cancellationToken = default)
    {
        var recipient = new RecipientSnapshot(mobileNumber, "Ebolito User", null, null, mobileNumber, MessageChannel.Sms);
        var request = new MessageRequest
        {
            RecipientIds = [recipient.UserId],
            Title = "Ebolito verification code",
            Body = $"Your Ebolito verification code is {code}. It expires in 10 minutes.",
            Severity = MessageSeverity.Information,
            Channels = MessageChannel.Sms,
            Source = "Ebolito",
            CorrelationId = Guid.NewGuid().ToString("D"),
            Metadata = new Dictionary<string, string> { ["ebolito.event"] = "mobile-verification" }
        };

        await queue.EnqueueAsync(new ExternalDeliveryWorkItem(
            Guid.NewGuid(),
            [recipient],
            MessageChannel.Sms,
            request,
            null,
            "Ebolito Mobile Verification",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }
}
#endif
