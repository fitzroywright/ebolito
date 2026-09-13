using Ebolito.Domain;
using Ebolito.Infrastructure;
using Xunit;

namespace Ebolito.Tests;

public sealed class CustomerEngagementTests
{
    [Fact]
    public async Task CustomerHistory_ReturnsOnlyThatCustomersEngagements()
    {
        var store = new InMemoryMarketplaceStore();
        var professional = (await store.GetProfessionalsAsync()).First();
        var first = (await store.GetCustomerByMobileAsync("+18765550999"))!;
        var second = new CustomerIdentity(Guid.NewGuid(), "Second Customer", "+18765550888", "second@example.com");
        await store.SaveCustomerAsync(second);

        await store.SaveEngagementAsync(new Engagement
        {
            ProfessionalId = professional.Id,
            CustomerId = first.Id,
            RequestText = "First customer request",
            Location = "Kingston"
        });
        await store.SaveEngagementAsync(new Engagement
        {
            ProfessionalId = professional.Id,
            CustomerId = second.Id,
            RequestText = "Second customer request",
            Location = "St. Andrew"
        });

        var history = await store.GetEngagementsForCustomerAsync(first.Id);

        Assert.Single(history);
        Assert.Equal(first.Id, history.Single().CustomerId);
        Assert.Equal("First customer request", history.Single().RequestText);
    }
}
