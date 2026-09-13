using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;

namespace Ebolito.Tests;

public sealed class MarketplaceTests
{
    [Fact]
    public void Engagement_FollowsExpectedLifecycle()
    {
        var engagement = new Engagement
        {
            ProfessionalId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RequestText = "Repair a leaking pipe",
            Location = "Kingston",
            RequestedChannel = EngagementChannel.WhatsApp
        };

        engagement.MarkDelivered(EngagementChannel.WhatsApp);
        engagement.Accept();
        engagement.MarkContacted();
        engagement.MarkHired();
        engagement.Complete();
        engagement.MarkReviewed();

        Assert.Equal(EngagementStatus.Reviewed, engagement.Status);
        Assert.Equal(EngagementChannel.WhatsApp, engagement.DeliveredChannel);
    }

    [Fact]
    public void Engagement_RejectsInvalidTransition()
    {
        var engagement = new Engagement
        {
            ProfessionalId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RequestText = "Need an electrician",
            Location = "Portmore"
        };

        Assert.Throws<InvalidOperationException>(() => engagement.MarkHired());
    }

    [Fact]
    public async Task Search_UsesSkillSynonymsAndLocation()
    {
        var store = new InMemoryMarketplaceStore();
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());

        var results = await service.SearchAsync("carpenter", "Spanish Town");

        var professional = Assert.Single(results);
        Assert.Equal("Marcus Brown", professional.DisplayName);
    }

    [Fact]
    public async Task Engagement_RequiresKnownVerifiedCustomer()
    {
        var store = new InMemoryMarketplaceStore();
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());
        var professional = (await store.GetProfessionalsAsync()).First();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestEngagementAsync(new EngagementRequest(
            professional.Id,
            Guid.NewGuid(),
            null,
            "Need help",
            "Kingston",
            EngagementChannel.Sms)));
    }
}
