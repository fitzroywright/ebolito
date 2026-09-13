using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;

namespace Ebolito.Tests;

public sealed class ProfessionalScreeningTests
{
    [Fact]
    public async Task Pending_professional_is_hidden_from_public_search_and_profile()
    {
        var store = new InMemoryMarketplaceStore();
        var pending = new Professional(
            Guid.NewGuid(),
            "pending-plumber",
            "Pending Plumber",
            null,
            "Pending profile",
            "Awaiting screening.",
            "+18765551234",
            "+18765551234",
            [Guid.Parse("11111111-1111-1111-1111-111111111111")],
            [new ServiceArea("Kingston")],
            false,
            true);
        await store.SaveProfessionalAsync(pending);
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());

        var results = await service.SearchAsync("Plumbing", "Kingston");
        var profile = await service.GetProfileAsync(pending.Slug);

        Assert.DoesNotContain(results, x => x.Id == pending.Id);
        Assert.Null(profile);
    }

    [Fact]
    public async Task Pending_professional_cannot_receive_marketplace_engagement()
    {
        var store = new InMemoryMarketplaceStore();
        var pending = new Professional(
            Guid.NewGuid(),
            "pending-electrician",
            "Pending Electrician",
            null,
            "Pending profile",
            "Awaiting screening.",
            "+18765551235",
            "+18765551235",
            [Guid.Parse("33333333-3333-3333-3333-333333333333")],
            [new ServiceArea("Kingston")],
            false,
            true);
        await store.SaveProfessionalAsync(pending);
        var customer = new CustomerIdentity(Guid.NewGuid(), "Customer", "+18765554321");
        await store.SaveCustomerAsync(customer);
        var service = new MarketplaceService(store, new FallbackEngagementNotifier());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestEngagementAsync(new EngagementRequest(pending.Id, customer.Id, null, "Need help", "Kingston")));

        Assert.Contains("not available", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
