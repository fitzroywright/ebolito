using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;
using Xunit;

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

    [Fact]
    public async Task MobileVerification_CreatesVerifiedCustomer()
    {
        var store = new InMemoryMarketplaceStore();
        var challenges = new InMemoryVerificationChallengeStore();
        var sender = new CapturingVerificationSender();
        var service = new CustomerIdentityService(store, challenges, sender);

        var challenge = await service.StartAsync(new MobileVerificationStart("Fitz Test", "876-555-1212", "fitz@example.com"));
        var customer = await service.CompleteAsync(new MobileVerificationComplete(challenge.Id, sender.LastCode!));

        Assert.Equal("Fitz Test", customer.DisplayName);
        Assert.Equal("+18765551212", customer.VerifiedMobileNumber);
        Assert.Equal(customer, await store.GetCustomerAsync(customer.Id));
    }

    [Fact]
    public async Task MobileVerification_RejectsWrongCode()
    {
        var store = new InMemoryMarketplaceStore();
        var challenges = new InMemoryVerificationChallengeStore();
        var sender = new CapturingVerificationSender();
        var service = new CustomerIdentityService(store, challenges, sender);

        var challenge = await service.StartAsync(new MobileVerificationStart("Test User", "8765553434"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(new MobileVerificationComplete(challenge.Id, "000000")));
    }

    private sealed class CapturingVerificationSender : IMobileVerificationSender
    {
        public string? LastCode { get; private set; }

        public Task SendCodeAsync(string mobileNumber, string code, CancellationToken cancellationToken = default)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}
