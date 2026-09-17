using System.Security.Cryptography;
using System.Text;
using Common.Secrets;

namespace Ebolito.Web;

public sealed record CustomerSession(Guid CustomerId, string Token, DateTimeOffset ExpiresAt);

public sealed class CustomerSessionTokenService
{
    public const string HeaderName = "X-Ebolito-Customer-Session";
    public const string SigningKeySecretName = "ebolito/session/customer-signing-key";

    private readonly byte[]? signingKey;
    private readonly TimeSpan lifetime;

    public CustomerSessionTokenService(IConfiguration configuration, ISecretProvider secrets)
    {
        var secret = ResolveSecret(secrets, SigningKeySecretName);
        signingKey = string.IsNullOrWhiteSpace(secret) ? null : Encoding.UTF8.GetBytes(secret);
        lifetime = TimeSpan.FromHours(Math.Clamp(configuration.GetValue("Ebolito:CustomerSessionHours", 24 * 90), 1, 24 * 365));
    }

    public bool IsConfigured => signingKey is not null;

    public CustomerSession Issue(Guid customerId)
    {
        if (signingKey is null)
            throw new InvalidOperationException($"Customer session signing is not configured. Common.Secrets did not resolve '{SigningKeySecretName}'.");

        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var expires = expiresAt.ToUnixTimeSeconds();
        var signature = Sign(customerId, expires);
        return new CustomerSession(customerId, $"{customerId:D}.{expires}.{signature}", expiresAt);
    }

    public bool TryValidate(string? token, out Guid customerId)
    {
        customerId = Guid.Empty;
        if (signingKey is null || string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var parsedId) || !long.TryParse(parts[1], out var expires)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return false;

        var expected = Sign(parsedId, expires);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(parts[2]);
        if (expectedBytes.Length != suppliedBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes)) return false;

        customerId = parsedId;
        return true;
    }

    public bool TryValidate(HttpRequest request, out Guid customerId)
    {
        var token = request.Headers.TryGetValue(HeaderName, out var value) ? value.ToString() : null;
        return TryValidate(token, out customerId);
    }

    private string Sign(Guid customerId, long expires)
    {
        using var hmac = new HMACSHA256(signingKey!);
        var payload = Encoding.UTF8.GetBytes($"{customerId:D}|{expires}");
        return Base64Url(hmac.ComputeHash(payload));
    }

    private static string? ResolveSecret(ISecretProvider secrets, string name)
    {
        try
        {
            return secrets.GetAsync(name).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Common.Secrets could not resolve required Ebolito secret '{name}'.", exception);
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
