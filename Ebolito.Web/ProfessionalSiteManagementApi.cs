using System.Text.RegularExpressions;
using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed record ServiceAreaInput(string Parish, string? Community = null);
public sealed record PortfolioPhotoInput(string Url, string? Caption = null, int SortOrder = 0);

public sealed record ProfessionalSiteUpdate(
    string Slug,
    string DisplayName,
    string? BusinessName,
    string Headline,
    string About,
    string? PhoneNumber,
    string? WhatsAppNumber,
    IReadOnlyCollection<Guid> SkillIds,
    IReadOnlyCollection<ServiceAreaInput> ServiceAreas,
    bool IsActive = true);

public sealed record PortfolioProjectUpdate(
    Guid? Id,
    string Title,
    string Description,
    string Location,
    DateOnly? CompletedOn,
    IReadOnlyCollection<Guid> SkillIds,
    IReadOnlyCollection<PortfolioPhotoInput> Photos,
    bool IsFeatured = false);

public static partial class ProfessionalSiteManagementEndpoints
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    public static IEndpointRouteBuilder MapProfessionalSiteManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPortfolioMediaEndpoints();

        endpoints.MapGet("/api/admin/professionals/{id:guid}/site", async (Guid id, HttpRequest request, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            var professional = await store.GetProfessionalAsync(id, ct);
            if (professional is null) return Results.NotFound();
            var projects = await store.GetProjectsAsync(id, ct);
            var policy = await store.GetNotificationPolicyAsync(id, ct);
            return Results.Ok(new { professional, projects, notificationPolicy = policy });
        });

        endpoints.MapPut("/api/admin/professionals/{id:guid}/site", async (Guid id, HttpRequest request, ProfessionalSiteUpdate update, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            var current = await store.GetProfessionalAsync(id, ct);
            if (current is null) return Results.NotFound();

            try
            {
                var professional = await BuildProfessionalAsync(id, current, update, store, ct);
                await store.SaveProfessionalAsync(professional, ct);
                return Results.Ok(professional);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        endpoints.MapPost("/api/admin/professionals/{id:guid}/portfolio", async (Guid id, HttpRequest request, PortfolioProjectUpdate update, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            if (await store.GetProfessionalAsync(id, ct) is null) return Results.NotFound();

            try
            {
                ValidateProject(update);
                await ValidateSkillIdsAsync(update.SkillIds, store, ct);
                var projectId = update.Id is null or { } value when value == Guid.Empty ? Guid.NewGuid() : update.Id.Value;
                var project = new PortfolioProject(
                    projectId,
                    id,
                    update.Title.Trim(),
                    update.Description.Trim(),
                    update.Location.Trim(),
                    update.CompletedOn,
                    update.SkillIds.Distinct().ToArray(),
                    update.Photos
                        .OrderBy(x => x.SortOrder)
                        .Select(x => new PortfolioPhoto(Guid.NewGuid(), x.Url.Trim(), x.Caption?.Trim(), x.SortOrder))
                        .ToArray(),
                    update.IsFeatured);
                await store.SavePortfolioProjectAsync(project, ct);
                return Results.Ok(project);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        endpoints.MapDelete("/api/admin/professionals/{id:guid}/portfolio/{projectId:guid}", async (Guid id, Guid projectId, HttpRequest request, IMarketplaceStore store, CancellationToken ct) =>
        {
            if (!ProfileAdministration.IsAuthorized(request)) return Results.Unauthorized();
            return await store.DeletePortfolioProjectAsync(id, projectId, ct) ? Results.NoContent() : Results.NotFound();
        });

        return endpoints;
    }

    private static async Task<Professional> BuildProfessionalAsync(Guid id, Professional current, ProfessionalSiteUpdate update, IMarketplaceStore store, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(update.Slug) || !SlugRegex().IsMatch(update.Slug.Trim()))
            throw new ArgumentException("Slug must contain lowercase letters/numbers separated by single hyphens.");
        if (string.IsNullOrWhiteSpace(update.DisplayName)) throw new ArgumentException("Display name is required.");
        if (string.IsNullOrWhiteSpace(update.Headline)) throw new ArgumentException("Headline is required.");
        if (string.IsNullOrWhiteSpace(update.About)) throw new ArgumentException("About text is required.");
        if (update.SkillIds is null || update.SkillIds.Count == 0) throw new ArgumentException("At least one skill is required.");
        if (update.ServiceAreas is null || update.ServiceAreas.Count == 0) throw new ArgumentException("At least one service area is required.");
        if (update.ServiceAreas.Any(x => x is null || string.IsNullOrWhiteSpace(x.Parish))) throw new ArgumentException("Every service area requires a parish.");

        var slug = update.Slug.Trim();
        var existingSlug = await store.GetProfessionalBySlugAsync(slug, ct);
        if (existingSlug is not null && existingSlug.Id != id) throw new ArgumentException("That professional URL slug is already in use.");
        await ValidateSkillIdsAsync(update.SkillIds, store, ct);

        return new Professional(
            id,
            slug,
            update.DisplayName.Trim(),
            NullIfWhiteSpace(update.BusinessName),
            update.Headline.Trim(),
            update.About.Trim(),
            NullIfWhiteSpace(update.PhoneNumber),
            NullIfWhiteSpace(update.WhatsAppNumber),
            update.SkillIds.Distinct().ToArray(),
            update.ServiceAreas.Select(x => new ServiceArea(x.Parish.Trim(), NullIfWhiteSpace(x.Community))).ToArray(),
            current.IsScreened,
            update.IsActive);
    }

    private static async Task ValidateSkillIdsAsync(IReadOnlyCollection<Guid> requested, IMarketplaceStore store, CancellationToken ct)
    {
        if (requested is null || requested.Count == 0) throw new ArgumentException("At least one skill is required.");
        var valid = (await store.GetSkillsAsync(ct)).Select(x => x.Id).ToHashSet();
        var unknown = requested.Where(x => !valid.Contains(x)).Distinct().ToArray();
        if (unknown.Length > 0) throw new ArgumentException($"Unknown skill IDs: {string.Join(", ", unknown)}");
    }

    private static void ValidateProject(PortfolioProjectUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.Title)) throw new ArgumentException("Project title is required.");
        if (string.IsNullOrWhiteSpace(update.Description)) throw new ArgumentException("Project description is required.");
        if (string.IsNullOrWhiteSpace(update.Location)) throw new ArgumentException("Project location is required.");
        if (update.SkillIds is null || update.SkillIds.Count == 0) throw new ArgumentException("At least one skill is required.");
        if (update.Photos is null) throw new ArgumentException("Photos collection is required.");
        if (update.Photos.Count > 30) throw new ArgumentException("A portfolio project may contain at most 30 photos.");
        foreach (var photo in update.Photos)
        {
            if (photo is null || string.IsNullOrWhiteSpace(photo.Url)) throw new ArgumentException("Portfolio photo URL is required.");
            if (!Uri.TryCreate(photo.Url, UriKind.RelativeOrAbsolute, out _)) throw new ArgumentException($"Invalid portfolio photo URL: {photo.Url}");
        }
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
