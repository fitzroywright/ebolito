using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ebolito.Application;

namespace Ebolito.Web;

public sealed record ProfessionalSignInStart(Guid ProfessionalId);
public sealed record ProfessionalSignInChallenge(Guid ChallengeId, DateTimeOffset ExpiresAt, string DestinationHint);
public sealed record ProfessionalSignInComplete(Guid ChallengeId, string Code);
public sealed record ProfessionalSession(Guid ProfessionalId, string Token, DateTimeOffset ExpiresAt);

public sealed class ProfessionalSessionTokenService
{
    public const string HeaderName = "X-Ebolito-Professional-Session";
    private readonly byte[] signingKey;
    private readonly TimeSpan lifetime;

    public ProfessionalSessionTokenService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = Environment.GetEnvironmentVariable("EBOLITO_PROFESSIONAL_SESSION_KEY");
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("EBOLITO_PROFESSIONAL_SESSION_KEY must be configured outside Development.");
            configured = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }
        signingKey = Encoding.UTF8.GetBytes(configured);
        lifetime = TimeSpan.FromHours(Math.Clamp(configuration.GetValue("Ebolito:ProfessionalSessionHours", 24 * 30), 1, 24 * 90));
    }

    public ProfessionalSession Issue(Guid professionalId)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var expires = expiresAt.ToUnixTimeSeconds();
        var signature = Sign(professionalId, expires);
        return new ProfessionalSession(professionalId, $"{professionalId:D}.{expires}.{signature}", expiresAt);
    }

    public bool TryValidate(HttpRequest request, Guid expectedProfessionalId)
    {
        var token = request.Headers.TryGetValue(HeaderName, out var supplied) ? supplied.ToString() : null;
        return TryValidate(token, out var professionalId) && professionalId == expectedProfessionalId;
    }

    public bool TryValidate(string? token, out Guid professionalId)
    {
        professionalId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var parsedId) || !long.TryParse(parts[1], out var expires)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(parsedId, expires));
        var actual = Encoding.ASCII.GetBytes(parts[2]);
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual)) return false;
        professionalId = parsedId;
        return true;
    }

    private string Sign(Guid professionalId, long expires)
    {
        using var hmac = new HMACSHA256(signingKey);
        return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes($"professional|{professionalId:D}|{expires}")));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class ProfessionalSignInService(
    IMarketplaceStore store,
    IMobileVerificationSender sender,
    ProfessionalSessionTokenService sessions)
{
    private sealed record Pending(Guid Id, Guid ProfessionalId, string CodeHash, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<Guid, Pending> challenges = new();
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);

    public async Task<ProfessionalSignInChallenge> StartAsync(Guid professionalId, CancellationToken ct)
    {
        var professional = await store.GetProfessionalAsync(professionalId, ct) ?? throw new InvalidOperationException("Professional not found.");
        if (!professional.IsActive) throw new InvalidOperationException("Professional account is inactive.");
        if (string.IsNullOrWhiteSpace(professional.PhoneNumber)) throw new InvalidOperationException("This professional does not have a mobile number configured for sign-in.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var pending = new Pending(Guid.NewGuid(), professional.Id, Hash(code), DateTimeOffset.UtcNow.Add(ChallengeLifetime));
        challenges[pending.Id] = pending;
        await sender.SendCodeAsync(professional.PhoneNumber, code, ct);
        return new ProfessionalSignInChallenge(pending.Id, pending.ExpiresAt, Mask(professional.PhoneNumber));
    }

    public Task<ProfessionalSession> CompleteAsync(Guid challengeId, string code)
    {
        if (!challenges.TryGetValue(challengeId, out var pending)) throw new InvalidOperationException("Sign-in challenge was not found or has expired.");
        if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            challenges.TryRemove(challengeId, out _);
            throw new InvalidOperationException("Sign-in challenge has expired.");
        }
        var expected = Convert.FromHexString(pending.CodeHash);
        var actual = Convert.FromHexString(Hash(code));
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidOperationException("Verification code is incorrect.");
        challenges.TryRemove(challengeId, out _);
        return Task.FromResult(sessions.Issue(pending.ProfessionalId));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    private static string Mask(string mobile)
    {
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? "****" : $"***-***-{digits[^4..]}";
    }
}

public static class ProfessionalAccess
{
    public static bool IsAuthorized(HttpRequest request, Guid professionalId, ProfessionalSessionTokenService sessions) =>
        ProfileAdministration.IsAuthorized(request) || sessions.TryValidate(request, professionalId);

    public static IEndpointRouteBuilder MapProfessionalAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/professional-auth/start", async (ProfessionalSignInStart request, ProfessionalSignInService signIn, CancellationToken ct) =>
        {
            try { return Results.Ok(await signIn.StartAsync(request.ProfessionalId, ct)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        endpoints.MapPost("/api/professional-auth/complete", async (ProfessionalSignInComplete request, ProfessionalSignInService signIn) =>
        {
            try { return Results.Ok(await signIn.CompleteAsync(request.ChallengeId, request.Code)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        return endpoints;
    }
}
