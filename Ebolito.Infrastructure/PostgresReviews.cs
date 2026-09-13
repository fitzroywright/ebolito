using Ebolito.Domain;

namespace Ebolito.Infrastructure;

public sealed partial class PostgresMarketplaceStore
{
    public async Task<Review?> GetReviewByEngagementAsync(Guid engagementId, CancellationToken cancellationToken = default)
    {
        const string sql = "select id,professional_id,engagement_id,customer_display_name,rating,comment,created_at,verified_engagement from reviews where engagement_id=$1";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(engagementId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Review(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetBoolean(7));
    }

    public async Task SaveReviewAsync(Review review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        const string sql = """
            insert into reviews(id,professional_id,engagement_id,customer_display_name,rating,comment,created_at,verified_engagement)
            values($1,$2,$3,$4,$5,$6,$7,$8)
            """;
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(review.Id);
        cmd.Parameters.AddWithValue(review.ProfessionalId);
        var engagementId = cmd.Parameters.Add("engagement_id", NpgsqlTypes.NpgsqlDbType.Uuid);
        engagementId.Value = (object?)review.EngagementId ?? DBNull.Value;
        cmd.Parameters.AddWithValue(review.CustomerDisplayName);
        cmd.Parameters.AddWithValue(review.Rating);
        cmd.Parameters.AddWithValue(review.Comment);
        cmd.Parameters.AddWithValue(review.CreatedAt);
        cmd.Parameters.AddWithValue(review.VerifiedEngagement);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
