using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;
using Xunit;

namespace Ebolito.Tests;

public sealed class ProfessionalInboxTests
{
    [Fact]
    public async Task Inbox_ReturnsOnlyEngagementsForRequestedProfessional_NewestFirst()
    {
        var store = new InMemoryMarketplaceStore();
        var professionals = (await store.GetProfessionalsAsync()).ToArray();
        var customer = (await store.GetCustomerByMobileAsync("+18765550999"))!;

        var first = new Engagement
        {
            ProfessionalId = professionals[0].Id,
            CustomerId = customer.Id,
            RequestText = "First request",
            Location = "Kingston"
        };
        first.MarkDelivered(EngagementChannel.Web);
        await store.SaveEngagementAsync(first);

        await Task.Delay(5);

        var second = new Engagement
        {
            ProfessionalId = professionals[0].Id,
            CustomerId = customer.Id,
            RequestText = "Second request",
            Location = "St. Andrew"
        };
        second.MarkDelivered(EngagementChannel.Web);
        await store.SaveEngagementAsync(second);

        var other = new Engagement
        {
            ProfessionalId = professionals[1].Id,
            CustomerId = customer.Id,
            RequestText = "Other professional",
            Location = "Spanish Town"
        };
        other.MarkDelivered(EngagementChannel.Web);
        await store.SaveEngagementAsync(other);

        var inbox = (await store.GetEngagementsForProfessionalAsync(professionals[0].Id)).ToArray();

        Assert.Equal(2, inbox.Length);
        Assert.All(inbox, x => Assert.Equal(professionals[0].Id, x.ProfessionalId));
        Assert.Equal(second.Id, inbox[0].Id);
        Assert.Equal(first.Id, inbox[1].Id);
    }
}
