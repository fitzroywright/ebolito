using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ebolito.Domain;

namespace Ebolito.Application;

public sealed record MobileVerificationStart(string DisplayName, string MobileNumber, string? Email = null);
public sealed record MobileVerificationChallenge(Guid Id, DateTimeOffset ExpiresAt);
public sealed record MobileVerificationComplete(Guid ChallengeId, string Code);
public sealed record PendingMobileVerification(Guid Id, string DisplayName, string MobileNumber, string? Email, string CodeHash, DateTimeOffset ExpiresAt, int AttemptsRemaining = 5);

public interface IVerificationChallengeStore
{
    Task SaveAsync(PendingMobileVerification challenge, CancellationToken cancellationToken = default);
    Task<PendingMobileVerification?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IMobileVerificationSender
{
    Task SendCodeAsync(string mobileNumber, string code, CancellationToken cancellationToken = default);
}

public interface ICustomerIdentityService
{
    Task<MobileVerificationChallenge> StartAsync(MobileVerificationStart request, CancellationToken cancellationToken = default);
    Task<CustomerIdentity> CompleteAsync(MobileVerificationComplete request, CancellationToken cancellationToken = default);
}

public sealed class CustomerIdentityService(
    IMarketplaceStore marketplaceStore,
    IVerificationChallengeStore challengeStore,
    IMobileVerificationSender sender) : ICustomerIdentityService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);
    private const int MaximumAttempts = 5;
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastSentAt = new(StringComparer.Ordinal);

    public async Task<MobileVerificationChallenge> StartAsync(MobileVerificationStart request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) throw new ArgumentException("Your name is required.");
        var mobile = NormalizeMobile(request.MobileNumber);
        var now = DateTimeOffset.UtcNow;
        if (lastSentAt.TryGetValue(mobile, out var previous) && now - previous < ResendCooldown)
            throw new InvalidOperationException("Please wait before requesting another verification code.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new PendingMobileVerification(
            Guid.NewGuid(),
            request.DisplayName.Trim(),
            mobile,
            string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            HashCode(code),
            now.Add(ChallengeLifetime),
            MaximumAttempts);

        await challengeStore.SaveAsync(challenge, cancellationToken);
        try
        {
            await sender.SendCodeAsync(mobile, code, cancellationToken);
            lastSentAt[mobile] = now;
        }
        catch
        {
            await challengeStore.RemoveAsync(challenge.Id, cancellationToken);
            throw;
        }
        return new MobileVerificationChallenge(challenge.Id, challenge.ExpiresAt);
    }

    public async Task<CustomerIdentity> CompleteAsync(MobileVerificationComplete request, CancellationToken cancellationToken = default)
    {
        var challenge = await challengeStore.GetAsync(request.ChallengeId, cancellationToken)
            ?? throw new InvalidOperationException("Verification challenge was not found or has expired.");

        if (challenge.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await challengeStore.RemoveAsync(challenge.Id, cancellationToken);
            throw new InvalidOperationException("Verification challenge has expired.");
        }

        if (challenge.AttemptsRemaining <= 0)
        {
            await challengeStore.RemoveAsync(challenge.Id, cancellationToken);
            throw new InvalidOperationException("Too many incorrect verification attempts. Request a new code.");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(challenge.CodeHash),
                Convert.FromHexString(HashCode(request.Code))))
        {
            var remaining = challenge.AttemptsRemaining - 1;
            if (remaining <= 0)
                await challengeStore.RemoveAsync(challenge.Id, cancellationToken);
            else
                await challengeStore.SaveAsync(challenge with { AttemptsRemaining = remaining }, cancellationToken);
            throw new InvalidOperationException(remaining <= 0
                ? "Too many incorrect verification attempts. Request a new code."
                : $"Verification code is incorrect. {remaining} attempt{(remaining == 1 ? "" : "s")} remaining.");
        }

        var customer = await marketplaceStore.GetCustomerByMobileAsync(challenge.MobileNumber, cancellationToken)
            ?? new CustomerIdentity(Guid.NewGuid(), challenge.DisplayName, challenge.MobileNumber, challenge.Email);

        if (customer.DisplayName != challenge.DisplayName || customer.Email != challenge.Email)
            customer = customer with { DisplayName = challenge.DisplayName, Email = challenge.Email };

        await marketplaceStore.SaveCustomerAsync(customer, cancellationToken);
        await challengeStore.RemoveAsync(challenge.Id, cancellationToken);
        return customer;
    }

    private static string NormalizeMobile(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A mobile number is required.");
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 10 && digits.StartsWith("876")) digits = "1" + digits;
        if (digits.Length < 10 || digits.Length > 15) throw new ArgumentException("Enter a valid mobile number including area/country code.");
        return "+" + digits;
    }

    private static string HashCode(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes);
    }
}
