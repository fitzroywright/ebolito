using System.Security.Cryptography;
using System.Text;
using Ebolito.Application;

namespace Ebolito.Web;

public sealed class SecureEngagementActionLinks : IEngagementActionLinkBuilder
{
    private readonly string? publicBaseUrl;
    private readonly byte[]? signingKey;
    private readonly TimeSpan lifetime;

    public SecureEngagementActionLinks(IConfiguration configuration, string? signingSecret)
    {
        publicBaseUrl = configuration["Ebolito:PublicBaseUrl"]?.TrimEnd('/');
        signingKey = string.IsNullOrWhiteSpace(signingSecret) ? null : Encoding.UTF8.GetBytes(signingSecret);
        lifetime = TimeSpan.FromHours(Math.Clamp(configuration.GetValue("Ebolito:EngagementActionLinkHours", 48), 1, 168));
    }

    public string? BuildViewLink(Guid engagementId) => Build(engagementId, "view", $"/engagements/{engagementId:D}/view");

    public string? BuildResponseLink(Guid engagementId, EngagementResponse response)
    {
        var decision = response == EngagementResponse.Accept ? "accept" : "decline";
        return Build(engagementId, decision, $"/engagements/{engagementId:D}/respond?decision={decision}");
    }

    public bool Verify(Guid engagementId, string action, long expires, string signature)
    {
        if (signingKey is null || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(signature)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return false;

        var expected = Sign(engagementId, action, expires);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(signature);
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private string? Build(Guid engagementId, string action, string path)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl) || signingKey is null) return null;
        var expires = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        var signature = Sign(engagementId, action, expires);
        var separator = path.Contains('?') ? '&' : '?';
        return $"{publicBaseUrl}{path}{separator}expires={expires}&sig={Uri.EscapeDataString(signature)}";
    }

    private string Sign(Guid engagementId, string action, long expires)
    {
        using var hmac = new HMACSHA256(signingKey!);
        var payload = Encoding.UTF8.GetBytes($"{engagementId:D}|{action}|{expires}");
        return Base64Url(hmac.ComputeHash(payload));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
