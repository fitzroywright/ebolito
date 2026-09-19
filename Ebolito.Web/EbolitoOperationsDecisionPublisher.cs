using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Common.Registration;

namespace Ebolito.Web;

public sealed record EbolitoDecisionTelemetryItem(
    Guid CorrelationId,
    string Decision,
    string Outcome,
    bool IsValidBusinessOutcome,
    string State,
    string Severity,
    string? Detail,
    DateTimeOffset ObservedAtUtc);

public sealed class EbolitoOperationsDecisionPublisher(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<EbolitoOperationsDecisionPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Ebolito";
    private readonly Channel<EbolitoDecisionTelemetryItem> queue =
        Channel.CreateBounded<EbolitoDecisionTelemetryItem>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(
        Guid correlationId,
        string decision,
        string outcome,
        bool isValidBusinessOutcome,
        string state = "Healthy",
        string severity = "Information",
        string? detail = null)
        => queue.Writer.TryWrite(new(
            correlationId,
            decision,
            outcome,
            isValidBusinessOutcome,
            state,
            severity,
            detail,
            DateTimeOffset.UtcNow));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (EbolitoDecisionTelemetryItem item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await PublishAsync(item, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ebolito decision telemetry publication failed; business processing remains operational.");
            }
        }
    }

    private async Task PublishAsync(EbolitoDecisionTelemetryItem item, CancellationToken ct)
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

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            operationsUrl.TrimEnd('/') + "/api/operations/diagnostics/observe")
        {
            Content = JsonContent.Create(new
            {
                diagnosticId = $"{ApplicationId}:decision:{item.CorrelationId:D}:{item.Decision}",
                applicationId = ApplicationId,
                component = "DecisionRouter",
                category = "Decision",
                state = item.State,
                summary = $"{item.Decision}: {item.Outcome}",
                observedAtUtc = item.ObservedAtUtc,
                detail = item.Detail,
                correlationId = item.CorrelationId.ToString("D"),
                runbook = "docs/RUNBOOK.md",
                data = new
                {
                    decision = item.Decision,
                    outcome = item.Outcome,
                    validBusinessOutcome = item.IsValidBusinessOutcome,
                    severity = item.Severity
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
        request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", item.CorrelationId.ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            logger.LogDebug("Ebolito decision telemetry returned HTTP {StatusCode}.", (int)response.StatusCode);
    }
}
