using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Infrastructure;

public sealed partial class PostgresMarketplaceStore
{
    public async Task<IReadOnlyCollection<Engagement>> GetEngagementsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        const string sql = "select id,professional_id,customer_id,skill_id,request_text,location,requested_channel,delivered_channel,status,created_at,updated_at from engagements where customer_id=$1 order by updated_at desc";
        var results = new List<Engagement>();
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(customerId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadEngagement(reader));
        return results;
    }
}
