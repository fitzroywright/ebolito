using Ebolito.Domain.Engagements;

namespace Ebolito.Tests.Engagements;

public sealed class EngagementTests
{
    [Fact]
    public void Accept_moves_requested_engagement_to_accepted()
    {
        var engagement = NewEngagement();
        var now = DateTimeOffset.UtcNow;

        engagement.Accept(now);

        Assert.Equal(EngagementStatus.Accepted, engagement.Status);
        Assert.Equal(now, engagement.RespondedAt);
    }

    [Fact]
    public void Decline_moves_requested_engagement_to_declined()
    {
        var engagement = NewEngagement();
        engagement.Decline(DateTimeOffset.UtcNow);
        Assert.Equal(EngagementStatus.Declined, engagement.Status);
    }

    [Fact]
    public void Engagement_cannot_be_responded_to_twice()
    {
        var engagement = NewEngagement();
        engagement.Accept(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => engagement.Decline(DateTimeOffset.UtcNow));
    }

    private static Engagement NewEngagement() => new()
    {
        CustomerId = Guid.NewGuid(),
        ProfessionalId = Guid.NewGuid(),
        Service = "Photography"
    };
}