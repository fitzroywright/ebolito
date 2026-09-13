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
        var engagement = new Engagement { ProfessionalId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), RequestText = "Repair a leaking pipe", Location = "Kingston", RequestedChannel = EngagementChannel.WhatsApp };
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
        var engagement = new Engagement { ProfessionalId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), RequestText = "Need an electrician", Location = "Portmore" };
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
    public async Task ProfessionalEdit_ImmediatelyChangesPublicSearch()
    {
        var store = new InMemoryMarketplaceStore();
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());
        var marcus = (await store.GetProfessionalsAsync()).Single(x => x.DisplayName == "Marcus Brown");
        var plumbing = (await store.GetSkillsAsync()).Single(x => x.Name == "Plumbing");

        await store.SaveProfessionalAsync(marcus with
        {
            DisplayName = "Marcus Brown Services",
            Headline = "Plumbing and emergency repairs",
            SkillIds = [plumbing.Id],
            ServiceAreas = [new ServiceArea("Kingston")]
        });

        var results = await service.SearchAsync("plumber", "Kingston");
        Assert.Contains(results, x => x.Id == marcus.Id && x.DisplayName == "Marcus Brown Services");
    }

    [Fact]
    public async Task PortfolioProject_CanBeAddedAndRemoved()
    {
        var store = new InMemoryMarketplaceStore();
        var professional = (await store.GetProfessionalsAsync()).First();
        var skill = (await store.GetSkillsAsync()).First();
        var project = new PortfolioProject(
            Guid.NewGuid(),
            professional.Id,
            "Before and After Bathroom",
            "Full fixture replacement and pipe repair.",
            "Kingston",
            DateOnly.FromDateTime(DateTime.UtcNow),
            [skill.Id],
            [new PortfolioPhoto(Guid.NewGuid(), "/portfolio/bathroom-after.jpg", "After", 1)],
            true);

        await store.SavePortfolioProjectAsync(project);
        Assert.Contains(await store.GetProjectsAsync(professional.Id), x => x.Id == project.Id);

        Assert.True(await store.DeletePortfolioProjectAsync(professional.Id, project.Id));
        Assert.DoesNotContain(await store.GetProjectsAsync(professional.Id), x => x.Id == project.Id);
    }

    [Fact]
    public async Task Engagement_RequiresKnownVerifiedCustomer()
    {
        var store = new InMemoryMarketplaceStore();
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());
        var professional = (await store.GetProfessionalsAsync()).First();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestEngagementAsync(new EngagementRequest(professional.Id, Guid.NewGuid(), null, "Need help", "Kingston", EngagementChannel.Sms)));
    }

    [Fact]
    public async Task BusinessPolicy_RoutesInAppThenSlack()
    {
        var store = new InMemoryMarketplaceStore();
        var notifier = new FallbackEngagementNotifier();
        var marcus = (await store.GetProfessionalsAsync()).Single(x => x.DisplayName == "Marcus Brown");
        var customer = (await store.GetCustomerByMobileAsync("+18765550999"))!;
        var policy = await store.GetNotificationPolicyAsync(marcus.Id);
        var engagement = new Engagement { ProfessionalId = marcus.Id, CustomerId = customer.Id, RequestText = "Need cabinets", Location = "Spanish Town" };

        var first = await notifier.DeliverAsync(marcus, customer, engagement, policy, [], default);
        Assert.Equal(EngagementChannel.Web, first!.Value);

        var attempts = new[] { new EngagementDeliveryAttempt(Guid.NewGuid(), engagement.Id, EngagementChannel.Web, DateTimeOffset.UtcNow, true) };
        var second = await notifier.DeliverAsync(marcus, customer, engagement, policy, attempts, default);
        Assert.Equal(EngagementChannel.Slack, second!.Value);
    }

    [Fact]
    public async Task Marketplace_EscalatesOnlyToUntriedChannels()
    {
        var store = new InMemoryMarketplaceStore();
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());
        var marcus = (await store.GetProfessionalsAsync()).Single(x => x.DisplayName == "Marcus Brown");
        var customer = (await store.GetCustomerByMobileAsync("+18765550999"))!;

        var engagement = await service.RequestEngagementAsync(new EngagementRequest(marcus.Id, customer.Id, null, "Need cabinets", "Spanish Town"));
        Assert.Equal(EngagementChannel.Web, engagement.DeliveredChannel);

        Assert.True(await service.EscalateEngagementAsync(engagement.Id));
        Assert.Equal(EngagementChannel.Slack, engagement.DeliveredChannel);

        var attempts = await store.GetDeliveryAttemptsAsync(engagement.Id);
        Assert.Equal(new[] { EngagementChannel.Web, EngagementChannel.Slack }, attempts.Select(x => x.Channel).ToArray());
    }

    [Fact]
    public async Task NotificationPolicy_CanBeChangedWithoutChangingProfessional()
    {
        var store = new InMemoryMarketplaceStore();
        var marcus = (await store.GetProfessionalsAsync()).Single(x => x.DisplayName == "Marcus Brown");
        var before = await store.GetProfessionalAsync(marcus.Id);

        var policy = new ProfessionalNotificationPolicy(
            marcus.Id,
            EngagementChannel.Teams,
            EngagementChannel.Slack,
            EngagementChannel.Sms,
            TimeSpan.FromMinutes(3),
            [EngagementChannel.Teams, EngagementChannel.Slack, EngagementChannel.Sms],
            [
                new NotificationEndpoint(EngagementChannel.Teams, "default", "Operations Teams channel"),
                new NotificationEndpoint(EngagementChannel.Slack, "#new-leads", "Sales leads"),
                new NotificationEndpoint(EngagementChannel.Sms, "+18765550102", "Fallback mobile")
            ]);

        await store.SaveNotificationPolicyAsync(policy);
        var saved = await store.GetNotificationPolicyAsync(marcus.Id);
        var after = await store.GetProfessionalAsync(marcus.Id);

        Assert.Equal(EngagementChannel.Teams, saved.PrimaryChannel);
        Assert.Equal(TimeSpan.FromMinutes(3), saved.EscalationAfter);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task FallbackNotifier_ReturnsNullWhenAllRoutesAreExhausted()
    {
        var store = new InMemoryMarketplaceStore();
        var notifier = new FallbackEngagementNotifier();
        var professional = (await store.GetProfessionalsAsync()).First();
        var customer = (await store.GetCustomerByMobileAsync("+18765550999"))!;
        var engagement = new Engagement { ProfessionalId = professional.Id, CustomerId = customer.Id, RequestText = "Test", Location = "Kingston" };
        var policy = new ProfessionalNotificationPolicy(
            professional.Id,
            EngagementChannel.Sms,
            null,
            EngagementChannel.Sms,
            TimeSpan.FromMinutes(1),
            [EngagementChannel.Sms],
            [new NotificationEndpoint(EngagementChannel.Sms, "+18765550000")]);
        var attempts = new[]
        {
            new EngagementDeliveryAttempt(Guid.NewGuid(), engagement.Id, EngagementChannel.Web, DateTimeOffset.UtcNow, true),
            new EngagementDeliveryAttempt(Guid.NewGuid(), engagement.Id, EngagementChannel.Sms, DateTimeOffset.UtcNow, true)
        };

        var next = await notifier.DeliverAsync(professional, customer, engagement, policy, attempts);
        Assert.Null(next);
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
        var wrongCode = sender.LastCode == "000000" ? "000001" : "000000";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(new MobileVerificationComplete(challenge.Id, wrongCode)));
    }

    private sealed class CapturingVerificationSender : IMobileVerificationSender
    {
        public string? LastCode { get; private set; }
        public Task SendCodeAsync(string mobileNumber, string code, CancellationToken cancellationToken = default) { LastCode = code; return Task.CompletedTask; }
    }
}
