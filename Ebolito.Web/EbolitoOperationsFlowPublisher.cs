using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Common.Registration;
using Ebolito.Domain;

namespace Ebolito.Web;

public sealed record EbolitoFlowTelemetryItem(
    Guid EngagementId,
    string Stage,
    DateTimeOffset ObservedAtUtc,
    string? Detail,
    bool Failed);

public sealed class EbolitoOperationsFlowPublisher(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<EbolitoOperationsFlowPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Ebolito";
    private readonly Channel<EbolitoFlowTelemetryItem> queue =
        Channel.CreateBounded<EbolitoFlowTelemetryItem>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(Guid engagementId, string stage, string? detail = null, bool failed = false) =>
        queue.Writer.TryWrite(new(engagementId, stage, DateTimeOffset.UtcNow, detail, failed));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (EbolitoFlowTelemetryItem item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await PublishAsync(item, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ebolito Operations flow publication failed; engagement remains committed.");
            }
        }
    }

    private async Task PublishAsync(EbolitoFlowTelemetryItem item, CancellationToken ct)
    {
        string? operationsUrl = configuration["Aegis:Operations:Url"];
        if (string.IsNullOrWhiteSpace(operationsUrl)) return;

        string instanceId = configuration["Service:Identity"]?.Trim() ?? Environment.MachineName;
        string identityFile = configuration["Aegis:Registration:IdentityFile"]
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aegis", "Ebolito", "registration-identity.json")
                : "/var/lib/aegis/ebolito/registration-identity.json");

        var store = new FileRegistrationIdentityStore(identityFile);
        RegistrationIdentityDocument identity =
            await store.LoadOrCreateAsync(ApplicationId, instanceId, ct);
        if (string.IsNullOrWhiteSpace(identity.Credential)) return;

        string[] stageNames = ["Inbound", "Identity", "Authorization", "Intent", "Handler", "Dependencies", "Response"];
        int currentIndex = Array.FindIndex(stageNames, x => x.Equals(item.Stage, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0) currentIndex = 4;

        bool terminal = item.Stage.Equals("Response", StringComparison.OrdinalIgnoreCase);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var stages = stageNames.Select((name, index) => new
        {
            name,
            state = index < currentIndex
                ? "Completed"
                : index == currentIndex
                    ? item.Failed ? "Failed" : terminal ? "Completed" : "Active"
                    : "Pending",
            startedAtUtc = index <= currentIndex ? item.ObservedAtUtc : (DateTimeOffset?)null,
            completedAtUtc = index < currentIndex || (index == currentIndex && terminal) ? now : (DateTimeOffset?)null,
            warningAfterSeconds = index < 4 ? 10 : 30,
            criticalAfterSeconds = index < 4 ? 60 : 300,
            error = index == currentIndex && item.Failed ? item.Detail : null,
            parentStage = index == 0 ? null : stageNames[index - 1],
            branch = (string?)null
        }).ToArray();

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            operationsUrl.TrimEnd('/') + "/api/operations/flows/observe")
        {
            Content = JsonContent.Create(new
            {
                correlationId = item.EngagementId.ToString("D"),
                applicationId = ApplicationId,
                flowType = "Conversation",
                instanceId,
                startedAtUtc = item.ObservedAtUtc,
                observedAtUtc = now,
                currentStage = item.Stage,
                relatedBusinessId = item.EngagementId.ToString("D"),
                runbook = "Ebolito / Conversation",
                stages
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
        request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", item.EngagementId.ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            logger.LogDebug("Ebolito Operations flow publication returned HTTP {StatusCode}.", (int)response.StatusCode);
    }
}
