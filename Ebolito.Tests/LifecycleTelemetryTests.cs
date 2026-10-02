using Common.Diagnostics;
using Ebolito.Web;

namespace Ebolito.Tests;

public sealed class LifecycleTelemetryTests
{
    [Fact]
    public void ConversationEvent_UsesEngagementAsCorrelationAndBusinessId()
    {
        Guid engagementId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        DateTimeOffset at = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var item = new EbolitoFlowTelemetryItem(engagementId, "Handler", at, "sensitive detail must not be copied", false);

        LifecycleEvent evt = EbolitoOperationsFlowPublisher.CreateLifecycleEvent(item, "ebolito-01");

        Assert.Equal("Ebolito", evt.ApplicationId);
        Assert.Equal("Conversation", evt.Flow);
        Assert.Equal("Handler", evt.Stage);
        Assert.Equal(engagementId.ToString("D"), evt.CorrelationId);
        Assert.Equal(engagementId.ToString("D"), evt.RelatedBusinessId);
        Assert.Equal(LifecycleEventOutcome.Succeeded, evt.Outcome);
        Assert.Null(evt.Detail);
    }

    [Fact]
    public void FailedConversationEvent_IsMarkedFailedWithoutDetailLeak()
    {
        var item = new EbolitoFlowTelemetryItem(Guid.NewGuid(), "Dependencies", DateTimeOffset.UtcNow, "database password=do-not-log", true);

        LifecycleEvent evt = EbolitoOperationsFlowPublisher.CreateLifecycleEvent(item, "ebolito-01");

        Assert.Equal(LifecycleEventOutcome.Failed, evt.Outcome);
        Assert.Null(evt.Detail);
        Assert.Null(evt.Properties);
    }
}
