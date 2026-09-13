using System.Text.Json;
using Ebolito.Application;
using Ebolito.Domain;
using Npgsql;

namespace Ebolito.Infrastructure;

public sealed class PostgresMarketplaceStore : IMarketplaceStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresMarketplaceStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<IReadOnlyCollection<Skill>> GetSkillsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "select id, name, synonyms from skills order by name";
        var results = new List<Skill>();
        await using var cmd = _dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new Skill(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<string[]>(2)));
        return results;
    }

    public async Task<IReadOnlyCollection<Professional>> GetProfessionalsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "select id, slug, display_name, business_name, headline, about, phone_number, whatsapp_number, skill_ids, service_areas::text, is_screened, is_active from professionals";
        var results = new List<Professional>();
        await using var cmd = _dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadProfessional(reader));
        return results;
    }

    public async Task<Professional?> GetProfessionalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "select id, slug, display_name, business_name, headline, about, phone_number, whatsapp_number, skill_ids, service_areas::text, is_screened, is_active from professionals where id = $1";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfessional(reader) : null;
    }

    public async Task<Professional?> GetProfessionalBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        const string sql = "select id, slug, display_name, business_name, headline, about, phone_number, whatsapp_number, skill_ids, service_areas::text, is_screened, is_active from professionals where lower(slug) = lower($1)";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(slug);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfessional(reader) : null;
    }

    public async Task<IReadOnlyCollection<PortfolioProject>> GetProjectsAsync(Guid professionalId, CancellationToken cancellationToken = default)
    {
        const string sql = "select id, professional_id, title, description, location, completed_on, skill_ids, photos::text, is_featured from portfolio_projects where professional_id = $1 order by is_featured desc, completed_on desc nulls last";
        var results = new List<PortfolioProject>();
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(professionalId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PortfolioProject(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetFieldValue<Guid[]>(6),
                Deserialize<PortfolioPhoto[]>(reader.GetString(7)),
                reader.GetBoolean(8)));
        }
        return results;
    }

    public async Task<IReadOnlyCollection<Review>> GetReviewsAsync(Guid professionalId, CancellationToken cancellationToken = default)
    {
        const string sql = "select id, professional_id, engagement_id, customer_display_name, rating, comment, created_at, verified_engagement from reviews where professional_id = $1 order by created_at desc";
        var results = new List<Review>();
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(professionalId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new Review(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetBoolean(7)));
        }
        return results;
    }

    public async Task<CustomerIdentity?> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "select id, display_name, verified_mobile_number, email from customers where id = $1";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new CustomerIdentity(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task SaveEngagementAsync(Engagement engagement, CancellationToken cancellationToken = default)
    {
        const string sql = """
            insert into engagements(id, professional_id, customer_id, skill_id, request_text, location, requested_channel, delivered_channel, status, created_at, updated_at)
            values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            on conflict(id) do update set
              delivered_channel = excluded.delivered_channel,
              status = excluded.status,
              updated_at = excluded.updated_at
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(engagement.Id);
        cmd.Parameters.AddWithValue(engagement.ProfessionalId);
        cmd.Parameters.AddWithValue(engagement.CustomerId);
        cmd.Parameters.AddWithValue((object?)engagement.SkillId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(engagement.RequestText);
        cmd.Parameters.AddWithValue(engagement.Location);
        cmd.Parameters.AddWithValue((int)engagement.RequestedChannel);
        cmd.Parameters.AddWithValue((object?)(engagement.DeliveredChannel is null ? null : (int)engagement.DeliveredChannel.Value) ?? DBNull.Value);
        cmd.Parameters.AddWithValue((int)engagement.Status);
        cmd.Parameters.AddWithValue(engagement.CreatedAt);
        cmd.Parameters.AddWithValue(engagement.UpdatedAt);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Engagement?> GetEngagementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "select id, professional_id, customer_id, skill_id, request_text, location, requested_channel, delivered_channel, status, created_at, updated_at from engagements where id = $1";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return Engagement.Restore(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            (EngagementChannel)reader.GetInt32(6),
            reader.IsDBNull(7) ? null : (EngagementChannel)reader.GetInt32(7),
            (EngagementStatus)reader.GetInt32(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetFieldValue<DateTimeOffset>(10));
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _dataSource.CreateCommand("select 1");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static Professional ReadProfessional(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetFieldValue<Guid[]>(8),
        Deserialize<ServiceArea[]>(reader.GetString(9)),
        reader.GetBoolean(10),
        reader.GetBoolean(11));

    private static T[] Deserialize<T>(string json) => JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? [];

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
