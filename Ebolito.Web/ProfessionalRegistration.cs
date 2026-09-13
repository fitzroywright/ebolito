using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed record ProfessionalRegistrationStart(string DisplayName, string MobileNumber, string? BusinessName = null);
public sealed record ProfessionalRegistrationChallenge(Guid ChallengeId, DateTimeOffset ExpiresAt, string DestinationHint);
public sealed record ProfessionalRegistrationComplete(Guid ChallengeId, string Code);
public sealed record ProfessionalRegistrationResult(Guid ProfessionalId, string Slug, bool IsScreened, string SessionToken, DateTimeOffset SessionExpiresAt);

public sealed class ProfessionalRegistrationService(
    IMarketplaceStore store,
    IMobileVerificationSender sender,
    ProfessionalSessionTokenService sessions)
{
    private sealed record Pending(Guid Id, string DisplayName, string MobileNumber, string? BusinessName, string CodeHash, DateTimeOffset ExpiresAt, int AttemptsRemaining);
    private readonly ConcurrentDictionary<Guid, Pending> challenges = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastSent = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(30);

    public async Task<ProfessionalRegistrationChallenge> StartAsync(ProfessionalRegistrationStart request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) throw new ArgumentException("Your name is required.");
        var mobile = NormalizeMobile(request.MobileNumber);
        var professionals = await store.GetProfessionalsAsync(ct);
        if (professionals.Any(x => string.Equals(NormalizeExisting(x.PhoneNumber), mobile, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A professional account already uses this mobile number. Use Professional Sign In instead.");

        var now = DateTimeOffset.UtcNow;
        if (lastSent.TryGetValue(mobile, out var sentAt) && now - sentAt < ResendDelay)
            throw new InvalidOperationException("Please wait before requesting another verification code.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var pending = new Pending(Guid.NewGuid(), request.DisplayName.Trim(), mobile, string.IsNullOrWhiteSpace(request.BusinessName) ? null : request.BusinessName.Trim(), Hash(code), now.Add(ChallengeLifetime), 5);
        challenges[pending.Id] = pending;
        lastSent[mobile] = now;
        try
        {
            await sender.SendCodeAsync(mobile, code, ct);
        }
        catch
        {
            challenges.TryRemove(pending.Id, out _);
            throw;
        }
        return new ProfessionalRegistrationChallenge(pending.Id, pending.ExpiresAt, Mask(mobile));
    }

    public async Task<ProfessionalRegistrationResult> CompleteAsync(ProfessionalRegistrationComplete request, CancellationToken ct)
    {
        if (!challenges.TryGetValue(request.ChallengeId, out var pending))
            throw new InvalidOperationException("Registration challenge was not found or has expired.");
        if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            challenges.TryRemove(request.ChallengeId, out _);
            throw new InvalidOperationException("Registration challenge has expired.");
        }

        var expected = Convert.FromHexString(pending.CodeHash);
        var actual = Convert.FromHexString(Hash(request.Code));
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            var remaining = pending.AttemptsRemaining - 1;
            if (remaining <= 0) challenges.TryRemove(request.ChallengeId, out _);
            else challenges[request.ChallengeId] = pending with { AttemptsRemaining = remaining };
            throw new InvalidOperationException(remaining <= 0 ? "Too many incorrect verification attempts. Request a new code." : "Verification code is incorrect.");
        }

        var professionals = await store.GetProfessionalsAsync(ct);
        if (professionals.Any(x => string.Equals(NormalizeExisting(x.PhoneNumber), pending.MobileNumber, StringComparison.OrdinalIgnoreCase)))
        {
            challenges.TryRemove(request.ChallengeId, out _);
            throw new InvalidOperationException("A professional account already uses this mobile number.");
        }

        var id = Guid.NewGuid();
        var slug = BuildUniqueSlug(pending.BusinessName ?? pending.DisplayName, professionals);
        var professional = new Professional(
            id,
            slug,
            pending.DisplayName,
            pending.BusinessName,
            "New professional profile",
            "Profile pending completion and screening.",
            pending.MobileNumber,
            pending.MobileNumber,
            [],
            [],
            false,
            true);
        await store.SaveProfessionalAsync(professional, ct);
        challenges.TryRemove(request.ChallengeId, out _);
        var session = sessions.Issue(id);
        return new ProfessionalRegistrationResult(id, slug, false, session.Token, session.ExpiresAt);
    }

    private static string BuildUniqueSlug(string value, IReadOnlyCollection<Professional> professionals)
    {
        var baseSlug = new string(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (baseSlug.Contains("--", StringComparison.Ordinal)) baseSlug = baseSlug.Replace("--", "-", StringComparison.Ordinal);
        baseSlug = baseSlug.Trim('-');
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "professional";
        var slug = baseSlug;
        var suffix = 2;
        var existing = professionals.Select(x => x.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (existing.Contains(slug)) slug = $"{baseSlug}-{suffix++}";
        return slug;
    }

    private static string NormalizeMobile(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A mobile number is required.");
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 10 && digits.StartsWith("876")) digits = "1" + digits;
        if (digits.Length < 10 || digits.Length > 15) throw new ArgumentException("Enter a valid mobile number including area/country code.");
        return "+" + digits;
    }

    private static string? NormalizeExisting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 10 && digits.StartsWith("876")) digits = "1" + digits;
        return digits.Length is >= 10 and <= 15 ? "+" + digits : value.Trim();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    private static string Mask(string mobile)
    {
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? "****" : $"***-***-{digits[^4..]}";
    }
}

public static class ProfessionalRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/professional-registration/start", async (ProfessionalRegistrationStart request, ProfessionalRegistrationService registration, CancellationToken ct) =>
        {
            try { return Results.Ok(await registration.StartAsync(request, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        endpoints.MapPost("/api/professional-registration/complete", async (ProfessionalRegistrationComplete request, ProfessionalRegistrationService registration, CancellationToken ct) =>
        {
            try { return Results.Ok(await registration.CompleteAsync(request, ct)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        return endpoints;
    }
}
