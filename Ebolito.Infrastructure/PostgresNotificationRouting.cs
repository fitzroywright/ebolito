using Ebolito.Domain;
using Npgsql;

namespace Ebolito.Infrastructure;

public sealed partial class PostgresMarketplaceStore
{
    public async Task<IReadOnlyCollection<Engagement>> GetUnacknowledgedEngagementsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        const string sql = "select id,professional_id,customer_id,skill_id,request_text,location,requested_channel,delivered_channel,status,created_at,updated_at from engagements where status=$1 and updated_at <= $2 order by updated_at";
        var results = new List<Engagement>();
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue((int)EngagementStatus.Delivered);
        cmd.Parameters.AddWithValue(olderThan);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadEngagement(reader));
        return results;
    }

    public async Task<ProfessionalNotificationPolicy> GetNotificationPolicyAsync(Guid professionalId, CancellationToken cancellationToken = default)
    {
        const string sql = "select primary_channel,business_channel,fallback_channel,escalation_after_seconds,escalation_order,endpoints::text from professional_notification_policies where professional_id=$1";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(professionalId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ProfessionalNotificationPolicy(
                professionalId,
                (EngagementChannel)reader.GetInt32(0),
                reader.IsDBNull(1) ? null : (EngagementChannel)reader.GetInt32(1),
                (EngagementChannel)reader.GetInt32(2),
                TimeSpan.FromSeconds(reader.GetInt32(3)),
                reader.GetFieldValue<int[]>(4).Select(x => (EngagementChannel)x).ToArray(),
                Deserialize<NotificationEndpoint>(reader.GetString(5)));
        }

        var professional = await GetProfessionalAsync(professionalId, cancellationToken);
        var endpoints = new List<NotificationEndpoint>();
        if (!string.IsNullOrWhiteSpace(professional?.WhatsAppNumber)) endpoints.Add(new NotificationEndpoint(EngagementChannel.WhatsApp, professional.WhatsAppNumber!, "WhatsApp"));
        if (!string.IsNullOrWhiteSpace(professional?.PhoneNumber)) endpoints.Add(new NotificationEndpoint(EngagementChannel.Sms, professional.PhoneNumber!, "SMS"));
        return new ProfessionalNotificationPolicy(professionalId, EngagementChannel.WhatsApp, null, EngagementChannel.Sms, TimeSpan.FromMinutes(10), [EngagementChannel.WhatsApp, EngagementChannel.Sms], endpoints);
    }

    public async Task SaveDeliveryAttemptAsync(EngagementDeliveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        const string sql = "insert into engagement_delivery_attempts(id,engagement_id,channel,attempted_at,succeeded,detail) values($1,$2,$3,$4,$5,$6)";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(attempt.Id);
        cmd.Parameters.AddWithValue(attempt.EngagementId);
        cmd.Parameters.AddWithValue((int)attempt.Channel);
        cmd.Parameters.AddWithValue(attempt.AttemptedAt);
        cmd.Parameters.AddWithValue(attempt.Succeeded);
        cmd.Parameters.AddWithValue((object?)attempt.Detail ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<EngagementDeliveryAttempt>> GetDeliveryAttemptsAsync(Guid engagementId, CancellationToken cancellationToken = default)
    {
        const string sql = "select id,engagement_id,channel,attempted_at,succeeded,detail from engagement_delivery_attempts where engagement_id=$1 order by attempted_at";
        var results = new List<EngagementDeliveryAttempt>();
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(engagementId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new EngagementDeliveryAttempt(
                reader.GetGuid(0),
                reader.GetGuid(1),
                (EngagementChannel)reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return results;
    }
}
