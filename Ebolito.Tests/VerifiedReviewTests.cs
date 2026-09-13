using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;
using Xunit;

namespace Ebolito.Tests;

public sealed class VerifiedReviewTests
{
    [Fact]
    public async Task CompletedEngagement_CanCreateOneVerifiedReview()
    {
        var store = new InMemoryMarketplaceStore();
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());
        var professional = (await store.GetProfessionalsAsync()).First();
        var customer = (await store.GetCustomerByMobileAsync("+18765550999"))!;

        var engagement = new Engagement
        {
            ProfessionalId = professional.Id,
            CustomerId = customer.Id,
            RequestText = "Complete a test job",
            Location = "Kingston"
        };
        engagement.MarkDelivered(EngagementChannel.Web);
        engagement.Accept();
        engagement.MarkHired();
        engagement.Complete();
        await store.SaveEngagementAsync(engagement);

        var review = await service.SubmitVerifiedReviewAsync(new VerifiedReviewRequest(
            engagement.Id,
            customer.Id,
            5,
            "Excellent work."));

        Assert.True(review.VerifiedEngagement);
        Assert.Equal(engagement.Id, review.EngagementId);
        Assert.Equal(EngagementStatus.Reviewed, engagement.Status);
        Assert.NotNull(await store.GetReviewByEngagementAsync(engagement.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitVerifiedReviewAsync(new VerifiedReviewRequest(
            engagement.Id,
            customer.Id,
            4,
            "Second review should fail.")));
    }

    [Fact]
    public async Task Review_IsRejectedBeforeCompletionOrForAnotherCustomer()
    {
        var store = new InMemoryMarketplaceStore();
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());
        var professional = (await store.GetProfessionalsAsync()).First();
        var customer = (await store.GetCustomerByMobileAsync("+18765550999"))!;
        var engagement = new Engagement
        {
            ProfessionalId = professional.Id,
            CustomerId = customer.Id,
            RequestText = "Incomplete test job",
            Location = "Kingston"
        };
        engagement.MarkDelivered(EngagementChannel.Web);
        await store.SaveEngagementAsync(engagement);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitVerifiedReviewAsync(new VerifiedReviewRequest(
            engagement.Id,
            customer.Id,
            5,
            "Too early.")));

        engagement.Accept();
        engagement.MarkHired();
        engagement.Complete();
        await store.SaveEngagementAsync(engagement);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitVerifiedReviewAsync(new VerifiedReviewRequest(
            engagement.Id,
            Guid.NewGuid(),
            5,
            "Wrong customer.")));
    }
}
