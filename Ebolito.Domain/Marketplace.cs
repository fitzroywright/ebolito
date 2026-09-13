namespace Ebolito.Domain;

public enum EngagementChannel { WhatsApp, Sms, Web }
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
    bool VerifiedEngagement)
{
    public int Rating { get; init; } = Rating is >= 1 and <= 5 ? Rating : throw new ArgumentOutOfRangeException(nameof(Rating));
}

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

    public void MarkDelivered(EngagementChannel channel) => Transition(EngagementStatus.Delivered, channel);
    public void Accept() => Transition(EngagementStatus.Accepted);
    public void Decline() => Transition(EngagementStatus.Declined);
    public void MarkContacted() => Transition(EngagementStatus.Contacted);
    public void MarkHired() => Transition(EngagementStatus.Hired);
    public void Complete() => Transition(EngagementStatus.Completed);
    public void MarkReviewed() => Transition(EngagementStatus.Reviewed);

    private void Transition(EngagementStatus next, EngagementChannel? channel = null)
    {
        if (!CanTransition(Status, next))
            throw new InvalidOperationException($"Cannot transition engagement from {Status} to {next}.");

        Status = next;
        DeliveredChannel = channel ?? DeliveredChannel;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool CanTransition(EngagementStatus current, EngagementStatus next) => (current, next) switch
    {
        (EngagementStatus.Requested, EngagementStatus.Delivered) => true,
        (EngagementStatus.Delivered, EngagementStatus.Accepted or EngagementStatus.Declined) => true,
        (EngagementStatus.Accepted, EngagementStatus.Contacted or EngagementStatus.Hired) => true,
        (EngagementStatus.Contacted, EngagementStatus.Hired) => true,
        (EngagementStatus.Hired, EngagementStatus.Completed) => true,
        (EngagementStatus.Completed, EngagementStatus.Reviewed) => true,
        _ => false
    };
}
