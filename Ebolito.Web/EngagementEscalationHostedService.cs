using Ebolito.Application;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed class EngagementEscalationHostedService(
    IServiceProvider services,
    ILogger<EngagementEscalationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinimumEscalationAge = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IMarketplaceStore>();
                var marketplace = scope.ServiceProvider.GetRequiredService<IMarketplaceService>();
                var now = DateTimeOffset.UtcNow;

                // Policy validation allows escalation as early as one minute. Query at that floor,
                // then apply each professional's actual interval before sending the next channel.
                var candidates = await store.GetUnacknowledgedEngagementsAsync(now.Subtract(MinimumEscalationAge), stoppingToken);
                foreach (var engagement in candidates)
                {
                    var policy = await store.GetNotificationPolicyAsync(engagement.ProfessionalId, stoppingToken);
                    if (engagement.UpdatedAt > now.Subtract(policy.EscalationAfter)) continue;

                    var escalated = await marketplace.EscalateEngagementAsync(engagement.Id, stoppingToken);
                    if (escalated)
                        logger.LogInformation("Escalated Ebolito engagement {EngagementId} to its next configured notification channel.", engagement.Id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ebolito engagement escalation scan failed; the next scan will retry.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }
}
