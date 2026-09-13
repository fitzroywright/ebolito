namespace Ebolito.Domain;

public enum EngagementChannel
{
    WhatsApp = 0,
    Sms = 1,
    Web = 2,
    Email = 3,
    Slack = 4,
    Teams = 5,
    Push = 6,
    Webhook = 7,
    Messenger = 8,
    Instagram = 9
}

public enum EngagementStatus { Requested, Delivered, Accepted, Declined, Contacted, Hired, Completed, Reviewed }

public sealed record Skill(Guid Id, string Name, IReadOnlyCollection<string> Synonyms);

public sealed record ServiceArea(string Parish, string? Community = null)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Community) ? Parish : $"{Community}, {Parish}";
}

public sealed record PortfolioPhoto(Guid Id, string Url, string? Caption, int SortOrder = 0);

public sealed record PortfolioProject(
    Guid Id,
    Guid ProfessionalId,
    string Title,
    string Description,
    string Location,
    DateOnly? CompletedOn,
    IReadOnlyCollection<Guid> SkillIds,
    IReadOnlyCollection<PortfolioPhoto> Photos,
    bool IsFeatured = false);

public sealed record Review(
    Guid Id,
    Guid ProfessionalId,
    Guid? EngagementId,
    string CustomerDisplayName,
    int Rating,
    string Comment,
    DateTimeOffset CreatedAt,
    bool VerifiedEngagement);

public sealed record Professional(
    Guid Id,
    string Slug,
    string DisplayName,
    string? BusinessName,
    string Headline,
    string About,
    string? PhoneNumber,
    string? WhatsAppNumber,
    IReadOnlyCollection<Guid> SkillIds,
    IReadOnlyCollection<ServiceArea> ServiceAreas,
    bool IsScreened = false,
    bool IsActive = true);

public sealed record CustomerIdentity(
    Guid Id,
    string DisplayName,
    string VerifiedMobileNumber,
    string? Email = null);

public sealed class Engagement
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ProfessionalId { get; init; }
    public required Guid CustomerId { get; init; }
    public Guid? SkillId { get; init; }
    public required string RequestText { get; init; }
    public required string Location { get; init; }
    public EngagementChannel RequestedChannel { get; init; }
    public EngagementChannel? DeliveredChannel { get; private set; }
    public EngagementStatus Status { get; private set; } = EngagementStatus.Requested;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static Engagement Restore(
        Guid id,
        Guid professionalId,
        Guid customerId,
        Guid? skillId,
        string requestText,
        string location,
        EngagementChannel requestedChannel,
        EngagementChannel? deliveredChannel,
        EngagementStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) => new()
    {
        Id = id,
        ProfessionalId = professionalId,
        CustomerId = customerId,
        SkillId = skillId,
        RequestText = requestText,
        Location = location,
        RequestedChannel = requestedChannel,
        DeliveredChannel = deliveredChannel,
        Status = status,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    };

    public void RecordDelivery(EngagementChannel channel)
    {
        if (Status == EngagementStatus.Requested)
        {
            Status = EngagementStatus.Delivered;
        }
        else if (Status != EngagementStatus.Delivered)
        {
            throw new InvalidOperationException($"Cannot record delivery while engagement is {Status}.");
        }

        DeliveredChannel = channel;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkDelivered(EngagementChannel channel) => RecordDelivery(channel);
    public void Accept() => Transition(EngagementStatus.Accepted);
    public void Decline() => Transition(EngagementStatus.Declined);
    public void MarkContacted() => Transition(EngagementStatus.Contacted);
    public void MarkHired() => Transition(EngagementStatus.Hired);
    public void Complete() => Transition(EngagementStatus.Completed);
    public void MarkReviewed() => Transition(EngagementStatus.Reviewed);

    private void Transition(EngagementStatus next)
    {
        if (!CanTransition(Status, next))
            throw new InvalidOperationException($"Cannot transition engagement from {Status} to {next}.");

        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool CanTransition(EngagementStatus current, EngagementStatus next) => (current, next) switch
    {
        (EngagementStatus.Delivered, EngagementStatus.Accepted or EngagementStatus.Declined) => true,
        (EngagementStatus.Accepted, EngagementStatus.Contacted or EngagementStatus.Hired) => true,
        (EngagementStatus.Contacted, EngagementStatus.Hired) => true,
        (EngagementStatus.Hired, EngagementStatus.Completed) => true,
        (EngagementStatus.Completed, EngagementStatus.Reviewed) => true,
        _ => false
    };
}
