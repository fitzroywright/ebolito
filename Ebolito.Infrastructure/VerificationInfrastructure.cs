using System.Collections.Concurrent;
using Ebolito.Application;

namespace Ebolito.Infrastructure;

public sealed class InMemoryVerificationChallengeStore : IVerificationChallengeStore
{
    private readonly ConcurrentDictionary<Guid, PendingMobileVerification> _challenges = new();

    public Task SaveAsync(PendingMobileVerification challenge, CancellationToken cancellationToken = default)
    {
        _challenges[challenge.Id] = challenge;
        RemoveExpired();
        return Task.CompletedTask;
    }

    public Task<PendingMobileVerification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        RemoveExpired();
        return Task.FromResult(_challenges.TryGetValue(id, out var value) ? value : null);
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _challenges.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _challenges.Where(x => x.Value.ExpiresAt <= now))
            _challenges.TryRemove(item.Key, out _);
    }
}

public sealed class DevelopmentMobileVerificationSender : IMobileVerificationSender
{
    public Task SendCodeAsync(string mobileNumber, string code, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Ebolito development verification] {mobileNumber}: {code}");
        return Task.CompletedTask;
    }
}
