using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ebolito.Application;
using Ebolito.Infrastructure;

namespace Ebolito.Web;

public enum EngineeringDiagnosticLevel
{
    Level5Scan = 5,
    Level4Analysis = 4,
    Level3Verification = 3,
    Level2Repair = 2,
    Level1CriticalIntervention = 1
}

public enum EngineeringDiagnosticStatus
{
    Passed = 1,
    Warning = 2,
    Failed = 3,
    InterventionRequired = 4
}

public sealed record EngineeringDiagnosticCheckResult(string CheckId, string Name, EngineeringDiagnosticStatus Status, string Summary, string? Evidence = null);
public sealed record EngineeringDiagnosticRunRequest(EngineeringDiagnosticLevel Level, string? Reason);
public sealed record EngineeringDiagnosticResolutionRequest(string Resolution);
public sealed record EngineeringDiagnosticRun(
    Guid RunId,
    EngineeringDiagnosticLevel Level,
    string Application,
    string Environment,
    string RequestedBy,
    string? Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    EngineeringDiagnosticStatus Status,
    IReadOnlyList<EngineeringDiagnosticCheckResult> Checks,
    DateTimeOffset? ResolvedAt = null,
    string? ResolvedBy = null,
    string? Resolution = null);

public sealed class EbolitoEngineeringDiagnostics(
    IMarketplaceStore marketplaceStore,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    private readonly ConcurrentDictionary<Guid, EngineeringDiagnosticRun> _runs = new();

    public async Task<EngineeringDiagnosticRun> RunAsync(EngineeringDiagnosticRunRequest request, string requestedBy, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var level = (int)request.Level;
        var checks = new List<EngineeringDiagnosticCheckResult>
        {
            new("app.process", "Application process", EngineeringDiagnosticStatus.Passed, "Ebolito process is running and accepted a diagnostics request."),
            new("store.mode", "Marketplace store", EngineeringDiagnosticStatus.Passed, marketplaceStore is PostgresMarketplaceStore ? "PostgreSQL marketplace store is configured." : "In-memory development marketplace store is active.", marketplaceStore.GetType().Name)
        };

        if (level <= 4)
        {
            if (marketplaceStore is PostgresMarketplaceStore postgres)
            {
                try
                {
                    var canConnect = await postgres.CanConnectAsync(cancellationToken);
                    checks.Add(new("database.connectivity", "PostgreSQL connectivity", canConnect ? EngineeringDiagnosticStatus.Passed : EngineeringDiagnosticStatus.Failed, canConnect ? "Database connection succeeded." : "Database connection failed."));
                }
                catch (Exception ex)
                {
                    checks.Add(new("database.connectivity", "PostgreSQL connectivity", EngineeringDiagnosticStatus.Failed, "Database connection threw an exception.", ex.GetType().Name));
                }
            }
            else
            {
                checks.Add(new("database.connectivity", "PostgreSQL connectivity", EngineeringDiagnosticStatus.Warning, "PostgreSQL is not configured; development memory store is active."));
            }
        }

        if (level <= 3)
        {
            try
            {
                var skills = await marketplaceStore.GetSkillsAsync(cancellationToken);
                var professionals = await marketplaceStore.GetProfessionalsAsync(cancellationToken);
                checks.Add(new("marketplace.read", "Marketplace read path", EngineeringDiagnosticStatus.Passed, $"Read {skills.Count} skills and {professionals.Count} professionals."));
            }
            catch (Exception ex)
            {
                checks.Add(new("marketplace.read", "Marketplace read path", EngineeringDiagnosticStatus.Failed, "Marketplace read path failed.", ex.GetType().Name));
            }
        }

        if (level <= 2)
        {
            var configurationUrl = configuration["Aegis:Configuration:Url"];
            checks.Add(new("configuration.registration", "Aegis.Configuration registration", string.IsNullOrWhiteSpace(configurationUrl) ? EngineeringDiagnosticStatus.Warning : EngineeringDiagnosticStatus.Passed, string.IsNullOrWhiteSpace(configurationUrl) ? "Aegis.Configuration URL is not configured; self-registration is disabled." : "Aegis.Configuration URL is configured for best-effort self-registration."));

            var verificationReady = environment.IsDevelopment() || !string.IsNullOrWhiteSpace(configuration["Messaging:Provider"]);
            checks.Add(new("messaging.verification", "Mobile verification delivery", verificationReady ? EngineeringDiagnosticStatus.Passed : EngineeringDiagnosticStatus.InterventionRequired, verificationReady ? "Mobile verification has an available delivery mode." : "Production mobile verification is intentionally disabled until Common.Messaging/SMS is configured."));
        }

        if (level <= 1)
        {
            var diagnosticsKeyPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EBOLITO_DIAGNOSTICS_KEY"));
            checks.Add(new("security.diagnostics-key", "Diagnostics machine credential", diagnosticsKeyPresent ? EngineeringDiagnosticStatus.Passed : EngineeringDiagnosticStatus.InterventionRequired, diagnosticsKeyPresent ? "EBOLITO_DIAGNOSTICS_KEY is configured." : "EBOLITO_DIAGNOSTICS_KEY is missing."));
        }

        var status = Aggregate(checks);
        var run = new EngineeringDiagnosticRun(Guid.NewGuid(), request.Level, "Ebolito", environment.EnvironmentName, requestedBy, request.Reason, started, DateTimeOffset.UtcNow, status, checks);
        _runs[run.RunId] = run;
        return run;
    }

    public IReadOnlyList<EngineeringDiagnosticRun> GetRecent(int take = 50) => _runs.Values.OrderByDescending(x => x.StartedAt).Take(Math.Clamp(take, 1, 200)).ToArray();
    public EngineeringDiagnosticRun? Get(Guid id) => _runs.TryGetValue(id, out var value) ? value : null;

    public EngineeringDiagnosticRun? Resolve(Guid id, string resolvedBy, string resolution)
    {
        if (!_runs.TryGetValue(id, out var run)) return null;
        var updated = run with { ResolvedAt = DateTimeOffset.UtcNow, ResolvedBy = resolvedBy, Resolution = resolution };
        _runs[id] = updated;
        return updated;
    }

    public static bool IsAuthorized(HttpRequest request)
    {
        var configured = Environment.GetEnvironmentVariable("EBOLITO_DIAGNOSTICS_KEY");
        if (string.IsNullOrWhiteSpace(configured)) return false;
        if (!request.Headers.TryGetValue("X-Aegis-Diagnostics-Key", out var supplied)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(configured);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static EngineeringDiagnosticStatus Aggregate(IEnumerable<EngineeringDiagnosticCheckResult> checks)
    {
        var statuses = checks.Select(x => x.Status).ToArray();
        if (statuses.Contains(EngineeringDiagnosticStatus.InterventionRequired)) return EngineeringDiagnosticStatus.InterventionRequired;
        if (statuses.Contains(EngineeringDiagnosticStatus.Failed)) return EngineeringDiagnosticStatus.Failed;
        if (statuses.Contains(EngineeringDiagnosticStatus.Warning)) return EngineeringDiagnosticStatus.Warning;
        return EngineeringDiagnosticStatus.Passed;
    }
}
