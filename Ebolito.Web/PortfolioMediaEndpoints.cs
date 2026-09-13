#if COMMON_STORAGE
using Common.Storage;
using Ebolito.Application;
#endif

namespace Ebolito.Web;

public static class PortfolioMediaEndpoints
{
    private const long MaximumImageBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapPortfolioMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
#if COMMON_STORAGE
        endpoints.MapPost("/api/admin/professionals/{id:guid}/portfolio-media", async (Guid id, HttpRequest request, ProfessionalSessionTokenService sessions, IConfiguration configuration, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfessionalAccess.IsAuthorized(request, id, sessions)) return Results.Unauthorized();
            if (await store.GetProfessionalAsync(id, ct) is null) return Results.NotFound();
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data is required." });
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "An image file is required." });
            if (file.Length > MaximumImageBytes) return Results.BadRequest(new { error = "Portfolio images may not exceed 10 MB." });
            var normalizedType = NormalizeImageContentType(file.ContentType); if (normalizedType is null) return Results.BadRequest(new { error = "Only JPEG, PNG and WebP portfolio images are accepted." });
            await using var upload = file.OpenReadStream(); if (!await HasExpectedImageSignatureAsync(upload, normalizedType, ct)) return Results.BadRequest(new { error = "The uploaded file content does not match its image type." }); upload.Position = 0;
            var storage = new LocalFileStorage(ResolveStorageRoot(configuration));
            var extension = normalizedType switch { "image/jpeg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp", _ => ".bin" };
            var mediaId = Guid.NewGuid(); var storageKey = $"ebolito/professionals/{id:D}/portfolio/{mediaId:N}{extension}";
            var stored = await storage.StoreAsync(new StorageWriteRequest(storageKey, upload, normalizedType, Path.GetFileName(file.FileName), id.ToString("D"), new Dictionary<string, string> { ["application"] = "Ebolito", ["professionalId"] = id.ToString("D"), ["purpose"] = "portfolio-image" }, MaximumImageBytes), ct);
            return Results.Ok(new { storageKey = stored.StorageKey, url = $"/media/{stored.StorageKey}", stored.Length, stored.ContentType, stored.Sha256, stored.Version });
        });

        endpoints.MapGet("/media/{**storageKey}", async (string storageKey, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!storageKey.StartsWith("ebolito/professionals/", StringComparison.Ordinal) || storageKey.Contains("..", StringComparison.Ordinal)) return Results.NotFound();
            var storage = new LocalFileStorage(ResolveStorageRoot(configuration)); var metadata = await storage.GetMetadataAsync(storageKey, ct);
            if (metadata is null || metadata.Status != StorageStatus.Stored || !metadata.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            var stream = await storage.OpenReadAsync(storageKey, ct); return Results.Stream(stream, metadata.ContentType, enableRangeProcessing: true);
        });
#else
        endpoints.MapGet("/api/admin/storage/capability", (HttpRequest request) => ProfileAdministration.IsAuthorized(request) ? Results.Ok(new { commonStorageCompiled = false, portfolioUploadAvailable = false }) : Results.Unauthorized());
#endif
        return endpoints;
    }

#if COMMON_STORAGE
    private static string ResolveStorageRoot(IConfiguration configuration) { var configured = configuration["Storage:Root"]; return string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "data", "storage") : Path.GetFullPath(configured); }
    private static string? NormalizeImageContentType(string? contentType) => contentType?.Trim().ToLowerInvariant() switch { "image/jpeg" or "image/jpg" => "image/jpeg", "image/png" => "image/png", "image/webp" => "image/webp", _ => null };
    private static async Task<bool> HasExpectedImageSignatureAsync(Stream stream, string contentType, CancellationToken ct)
    {
        var header = new byte[12]; var read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct); if (read < 3) return false;
        return contentType switch { "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF, "image/png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "image/webp" => read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8), _ => false };
    }
#endif
}
