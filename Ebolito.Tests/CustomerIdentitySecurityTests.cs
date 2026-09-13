using Ebolito.Application;
using Ebolito.Infrastructure;

namespace Ebolito.Tests;

public sealed class CustomerIdentitySecurityTests
{
    [Fact]
    public async Task Customer_verification_limits_incorrect_attempts()
    {
        var store = new InMemoryMarketplaceStore();
        var challenges = new InMemoryVerificationChallengeStore();
        var sender = new CapturingSender();
        var service = new CustomerIdentityService(store, challenges, sender);

        var challenge = await service.StartAsync(new MobileVerificationStart("Security Test", "876-555-0199"));

        for (var attempt = 4; attempt >= 1; attempt--)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CompleteAsync(new MobileVerificationComplete(challenge.Id, "999999")));
            Assert.Contains($"{attempt} attempt", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        var final = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteAsync(new MobileVerificationComplete(challenge.Id, "999999")));
        Assert.Contains("Too many", final.Message, StringComparison.OrdinalIgnoreCase);

        var removed = await challenges.GetAsync(challenge.Id);
        Assert.Null(removed);
    }

    [Fact]
    public async Task Customer_verification_throttles_immediate_resend()
    {
        var store = new InMemoryMarketplaceStore();
        var challenges = new InMemoryVerificationChallengeStore();
        var sender = new CapturingSender();
        var service = new CustomerIdentityService(store, challenges, sender);
        var request = new MobileVerificationStart("Security Test", "876-555-0188");

        await service.StartAsync(request);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(request));

        Assert.Contains("wait", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Customer_verification_accepts_captured_code()
    {
        var store = new InMemoryMarketplaceStore();
        var challenges = new InMemoryVerificationChallengeStore();
        var sender = new CapturingSender();
        var service = new CustomerIdentityService(store, challenges, sender);

        var challenge = await service.StartAsync(new MobileVerificationStart("Verified Customer", "876-555-0177", "verified@example.com"));
        var code = Assert.Single(sender.Messages).Code;
        var customer = await service.CompleteAsync(new MobileVerificationComplete(challenge.Id, code));

        Assert.Equal("Verified Customer", customer.DisplayName);
        Assert.Equal("+18765550177", customer.VerifiedMobileNumber);
        Assert.Equal("verified@example.com", customer.Email);
        Assert.Null(await challenges.GetAsync(challenge.Id));
    }

    private sealed class CapturingSender : IMobileVerificationSender
    {
        public List<(string Mobile, string Code)> Messages { get; } = [];
        public Task SendCodeAsync(string mobileNumber, string code, CancellationToken cancellationToken = default)
        {
            Messages.Add((mobileNumber, code));
            return Task.CompletedTask;
        }
    }
}
