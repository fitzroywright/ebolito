using System.Text.Json;
using Ebolito.Domain;
using NpgsqlTypes;

namespace Ebolito.Infrastructure;

public sealed partial class PostgresMarketplaceStore
{
    public async Task SaveProfessionalAsync(Professional professional, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(professional);
        const string sql = """
            insert into professionals(
                id,slug,display_name,business_name,headline,about,phone_number,whatsapp_number,
                skill_ids,service_areas,is_screened,is_active)
            values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10::jsonb,$11,$12)
            on conflict(id) do update set
                slug=excluded.slug,
                display_name=excluded.display_name,
                business_name=excluded.business_name,
                headline=excluded.headline,
                about=excluded.about,
                phone_number=excluded.phone_number,
                whatsapp_number=excluded.whatsapp_number,
                skill_ids=excluded.skill_ids,
                service_areas=excluded.service_areas,
                is_screened=excluded.is_screened,
                is_active=excluded.is_active
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(professional.Id);
        cmd.Parameters.AddWithValue(professional.Slug);
        cmd.Parameters.AddWithValue(professional.DisplayName);
        AddNullableText(cmd, "business_name", professional.BusinessName);
        cmd.Parameters.AddWithValue(professional.Headline);
        cmd.Parameters.AddWithValue(professional.About);
        AddNullableText(cmd, "phone_number", professional.PhoneNumber);
        AddNullableText(cmd, "whatsapp_number", professional.WhatsAppNumber);
        cmd.Parameters.AddWithValue(professional.SkillIds.ToArray());
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(professional.ServiceAreas, JsonOptions));
        cmd.Parameters.AddWithValue(professional.IsScreened);
        cmd.Parameters.AddWithValue(professional.IsActive);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SavePortfolioProjectAsync(PortfolioProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        const string sql = """
            insert into portfolio_projects(
                id,professional_id,title,description,location,completed_on,skill_ids,photos,is_featured)
            values($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9)
            on conflict(id) do update set
                title=excluded.title,
                description=excluded.description,
                location=excluded.location,
                completed_on=excluded.completed_on,
                skill_ids=excluded.skill_ids,
                photos=excluded.photos,
                is_featured=excluded.is_featured
            where portfolio_projects.professional_id=excluded.professional_id
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(project.Id);
        cmd.Parameters.AddWithValue(project.ProfessionalId);
        cmd.Parameters.AddWithValue(project.Title);
        cmd.Parameters.AddWithValue(project.Description);
        cmd.Parameters.AddWithValue(project.Location);
        var completed = cmd.Parameters.Add("completed_on", NpgsqlDbType.Date);
        completed.Value = (object?)project.CompletedOn ?? DBNull.Value;
        cmd.Parameters.AddWithValue(project.SkillIds.ToArray());
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(project.Photos, JsonOptions));
        cmd.Parameters.AddWithValue(project.IsFeatured);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeletePortfolioProjectAsync(Guid professionalId, Guid projectId, CancellationToken cancellationToken = default)
    {
        const string sql = "delete from portfolio_projects where id=$1 and professional_id=$2";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(projectId);
        cmd.Parameters.AddWithValue(professionalId);
        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void AddNullableText(Npgsql.NpgsqlCommand cmd, string name, string? value)
    {
        var parameter = cmd.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }
}
