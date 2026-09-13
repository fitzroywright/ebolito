namespace Ebolito.Domain.Engagements;

public enum EngagementStatus
{
    Requested = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3,
    Completed = 4
}

public sealed class Engagement
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CustomerId { get; init; }
    public required Guid ProfessionalId { get; init; }
    public required string Service { get; init; }
    public string? Message { get; init; }
    public string? PreferredContactChannel { get; init; }
    public EngagementStatus Status { get; private set; } = EngagementStatus.Requested;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; private set; }

    public void Accept(DateTimeOffset now)
    {
        if (Status != EngagementStatus.Requested) throw new InvalidOperationException("Only requested engagements can be accepted.");
        Status = EngagementStatus.Accepted;
        RespondedAt = now;
    }

    public void Decline(DateTimeOffset now)
    {
        if (Status != EngagementStatus.Requested) throw new InvalidOperationException("Only requested engagements can be declined.");
        Status = EngagementStatus.Declined;
        RespondedAt = now;
    }
}