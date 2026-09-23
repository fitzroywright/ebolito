using System.Text.Json.Nodes;
using Common.Diagnostics;
using Common.Registration;
using Common.Secrets;

namespace Ebolito.Web;

public sealed class ConfigurationRegistrationHostedService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ISecretProvider secrets,
    ILogger<ConfigurationRegistrationHostedService> logger) : BackgroundService
{
    private const string ApplicationId = "Ebolito";
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string configurationUrl = AegisControlPlaneEndpoints.ResolvePublic(
            configuration,
            AegisControlPlaneService.Configuration,
            logger,
            configurationKey: "Aegis:Configuration:Url");
        string operationsUrl = AegisControlPlaneEndpoints.ResolvePublic(
            configuration,
            AegisControlPlaneService.Operations,
            logger,
            configurationKey: "Aegis:Operations:Url");

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryRegisterAsync(configurationUrl, operationsUrl, stoppingToken);
            try { await Task.Delay(RetryInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<bool> TryRegisterAsync(
        string configurationUrl,
        string operationsUrl,
        CancellationToken cancellationToken)
    {
        string contractPath = Path.Combine(environment.ContentRootPath, "Configuration", "ebolito.configuration-contract.json");
        if (!File.Exists(contractPath))
        {
            logger.LogWarning("Ebolito configuration contract was not found at {ContractPath}; Ebolito is not registered and will retry.", contractPath);
            return false;
        }

        try
        {
            JsonObject? contract = JsonNode.Parse(await File.ReadAllTextAsync(contractPath, cancellationToken))?.AsObject();
            if (contract is null)
            {
                logger.LogWarning("Ebolito configuration contract at {ContractPath} is not a JSON object; Ebolito is not registered and will retry.", contractPath);
                return false;
            }

            contract["module"] = "Ebolito.Web";
            contract["runbookReference"] = "docs/RUNBOOK.md";
            contract["siteId"] = NullIfBlank(configuration["Site:Id"]);
            contract["instanceId"] = NullIfBlank(configuration["Service:Identity"] ?? Environment.MachineName);
            contract["runtimeEnvironment"] = new JsonObject
            {
                ["machineName"] = Environment.MachineName,
                ["operatingSystem"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                ["processArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                ["frameworkDescription"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["hostingEnvironment"] = environment.EnvironmentName
            };
            contract["presentation"] = new JsonObject
            {
                ["iconUrl"] = configuration["Aegis:Presentation:IconUrl"],
                ["shortName"] = "Ebolito",
                ["accent"] = configuration["Aegis:Presentation:Accent"] ?? "gold"
            };
            string? diagnosticSecretName =
                NullIfBlank(configuration["Aegis:Diagnostics:RequestCredentialSecretName"]);
            contract["diagnostics"] = new JsonObject
            {
                ["baseUrl"] = NullIfBlank(configuration["Ebolito:PublicBaseUrl"]),
                ["healthPath"] = "/health",
                ["diagnosticsRunPath"] = "/api/engineering/diagnostics/diagnostic-level/run",
                ["diagnosticsRunsPath"] = "/api/engineering/diagnostics/diagnostic-level/runs",
                ["supportsRemoteDiagnostics"] = true,
                ["supportsOperationalTelemetry"] = true,
                ["authenticationScheme"] = "DiagnosticLevel-HMAC-SHA256",
                ["secretName"] = diagnosticSecretName,
                ["supportedLevels"] = new JsonArray(5, 4, 3, 2, 1)
            };

            bool databaseConfigured = await HasSecretAsync("ConnectionStrings:Ebolito", cancellationToken);
            bool customerSigningConfigured = await HasSecretAsync("ebolito/session/customer-signing-key", cancellationToken);
            bool professionalSigningConfigured = await HasSecretAsync("ebolito/session/professional-signing-key", cancellationToken);
            bool publicBaseUrlConfigured = HasValue(configuration["Ebolito:PublicBaseUrl"]);
            bool portfolioStorageConfigured = HasValue(configuration["Storage:Root"]);
            bool messagingConfigured = await IsMessagingConfiguredAsync(cancellationToken);
            bool diagnosticsConfigured = HasValue(configuration["Aegis:Diagnostics:Url"]);

            if (contract["requirements"] is JsonArray requirements)
            {
                foreach (JsonNode? node in requirements)
                {
                    if (node is not JsonObject requirement) continue;
                    string id = requirement["id"]?.GetValue<string>() ?? string.Empty;
                    (bool configured, string? message) = id switch
                    {
                        "database" => (databaseConfigured, "EB-CONFIG-DB-001: PostgreSQL connection string must resolve through Common.Secrets."),
                        "public-base-url" => (publicBaseUrlConfigured, "EB-CONFIG-URL-001: Ebolito:PublicBaseUrl must be configured."),
                        "customer-session-signing" => (customerSigningConfigured, "EB-CONFIG-SEC-001: Customer session signing key must resolve through Common.Secrets."),
                        "professional-session-signing" => (professionalSigningConfigured, "EB-CONFIG-SEC-002: Professional session signing key must resolve through Common.Secrets."),
                        "portfolio-storage" => (portfolioStorageConfigured, "EB-CONFIG-STORAGE-001: Storage:Root must be configured."),
                        "messaging" => (messagingConfigured, "EB-CONFIG-MSG-001: Common.Messaging must have durable delivery and at least one fully commissioned delivery channel."),
                        "diagnostics" => (diagnosticsConfigured, "EB-CONFIG-DIAG-001: Aegis:Diagnostics:Url must be configured."),
                        _ => (false, "No runtime commissioning evaluator is defined for this requirement.")
                    };

                    requirement["isConfigured"] = configured;
                    requirement["configurationState"] = configured ? "Configured" : "Missing";
                    requirement["effectiveValueAvailable"] = configured;
                    requirement["verificationStatus"] = configured ? "PASS" : message;
                    foreach (string key in new[] { "safeDisplayValue", "defaultValue", "value", "currentValue", "resolvedValue", "effectiveValue", "example" })
                        requirement.Remove(key);
                }
            }

            string instanceId =
                contract["instanceId"]?.GetValue<string>()
                ?? Environment.MachineName;
            contract["instanceId"] = instanceId;

            string identityFile =
                configuration["Aegis:Registration:IdentityFile"]
                ?? (OperatingSystem.IsWindows()
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Aegis",
                        "Ebolito",
                        "registration-identity.json")
                    : "/var/lib/aegis/ebolito/registration-identity.json");

            HttpClient client = httpClientFactory.CreateClient(nameof(ConfigurationRegistrationHostedService));
            HttpClient lifecycleClient = httpClientFactory.CreateClient(
                $"{nameof(ConfigurationRegistrationHostedService)}.Lifecycle");
            ILifecycleEventSink lifecycle = CreateLifecycleSink(
                operationsUrl,
                lifecycleClient,
                identityFile,
                instanceId);

            var registration = new RegistrationLifecycleClient(
                client,
                new RegistrationLifecycleOptions(
                    new Uri(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute),
                    ApplicationId,
                    instanceId,
                    identityFile,
                    TimeSpan.FromSeconds(Math.Clamp(
                        configuration.GetValue("Aegis:Registration:RequestTimeoutSeconds", 30),
                        5,
                        120)),
                    RunbookReference: "docs/RUNBOOK.md"),
                logger: logger,
                lifecycleEventSink: lifecycle);

            RegistrationLifecycleStatus result =
                await registration.StepAsync(
                    new JsonObject
                    {
                        ["displayName"] = "Ebolito",
                        ["module"] = "Ebolito.Web",
                        ["presentation"] = new JsonObject
                        {
                            ["iconUrl"] = configuration["Aegis:Presentation:IconUrl"],
                            ["shortName"] = "Ebolito",
                            ["accent"] = configuration["Aegis:Presentation:Accent"] ?? "gold"
                        },
                        ["siteId"] = contract["siteId"]?.DeepClone(),
                        ["publicUrl"] = configuration["Ebolito:PublicBaseUrl"],
                        ["hostname"] = Environment.MachineName,
                        ["runtimeEnvironment"] = environment.EnvironmentName
                    },
                    cancellationToken);

            if (!result.IsRegistered)
            {
                logger.LogInformation(
                    "Ebolito registration state is {RegistrationState}: {RegistrationError}; Ebolito remains operational.",
                    result.State,
                    result.Error ?? "No additional detail.");
                return false;
            }

            System.Net.HttpStatusCode contractStatus =
                await registration.PublishConfigurationContractAsync(contract, cancellationToken);

            if ((int)contractStatus is >= 200 and < 300)
            {
                logger.LogInformation(
                    "Ebolito registration is valid and its runtime commissioning contract was published.");
                return true;
            }

            logger.LogWarning(
                "Ebolito is registered, but Configuration contract publication returned HTTP {StatusCode}.",
                (int)contractStatus);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to register Ebolito with Aegis.Configuration; Ebolito remains operational and will retry.");
            return false;
        }
    }

    private static ILifecycleEventSink CreateLifecycleSink(
        string operationsUrl,
        HttpClient client,
        string identityFile,
        string instanceId)
    {
        if (!Uri.TryCreate(
                operationsUrl.TrimEnd('/') + "/api/operations/activity/observe",
                UriKind.Absolute,
                out Uri? endpoint))
            return new NullLifecycleEventSink();

        var store = new FileRegistrationIdentityStore(identityFile);
        return new HttpLifecycleEventSink(
            client,
            new HttpLifecycleEventSinkOptions(endpoint, TimeSpan.FromSeconds(10)),
            async (request, lifecycleEvent, cancellationToken) =>
            {
                RegistrationIdentityDocument identity =
                    await store.LoadOrCreateAsync(
                        ApplicationId,
                        instanceId,
                        cancellationToken);

                if (string.IsNullOrWhiteSpace(identity.Credential))
                    return;

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        identity.Credential);
                request.Headers.TryAddWithoutValidation(
                    "X-Aegis-Application-Id",
                    identity.ApplicationId);
                request.Headers.TryAddWithoutValidation(
                    "X-Aegis-Instance-Id",
                    identity.InstanceId);
                request.Headers.TryAddWithoutValidation(
                    "X-Aegis-Installation-Id",
                    identity.InstallationId);
            });
    }

    private async Task<bool> IsMessagingConfiguredAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Messaging:DurableQueue", true)) return false;

        bool anyEnabled = false;
        bool allEnabledChannelsConfigured = true;

        if (configuration.GetValue("Messaging:Slack:Enabled", false))
        {
            anyEnabled = true;
            string secretName = configuration["Messaging:Slack:BotTokenSecretName"] ?? "messaging/slack/bot-token";
            allEnabledChannelsConfigured &= await HasSecretAsync(secretName, cancellationToken);
        }

        if (configuration.GetValue("Messaging:Teams:Enabled", false))
        {
            anyEnabled = true;
            string secretName = configuration["Messaging:Teams:WebhookSecretName"] ?? "messaging/teams/webhook-url";
            allEnabledChannelsConfigured &= await HasSecretAsync(secretName, cancellationToken);
        }

        if (configuration.GetValue("Messaging:MicrosoftGraph:Teams:Enabled", false))
        {
            anyEnabled = true;
            string tokenName = configuration["Messaging:MicrosoftGraph:Teams:DelegatedAccessTokenSecretName"] ?? "messaging/msgraph/teams/delegated-access-token";
            allEnabledChannelsConfigured &= HasValue(configuration["Messaging:MicrosoftGraph:Teams:SenderUpn"])
                && await HasSecretAsync(tokenName, cancellationToken);
        }

        if (configuration.GetValue("Messaging:Email:Enabled", false))
        {
            anyEnabled = true;
            allEnabledChannelsConfigured &= HasValue(configuration["Messaging:Email:Host"])
                && HasValue(configuration["Messaging:Email:FromAddress"]);
        }

        if (configuration.GetValue("Messaging:MicrosoftGraph:Email:Enabled", false))
        {
            anyEnabled = true;
            string secretName = configuration["Messaging:MicrosoftGraph:ClientSecretName"] ?? "messaging/msgraph/client-secret";
            allEnabledChannelsConfigured &= HasValue(configuration["Messaging:MicrosoftGraph:TenantId"])
                && HasValue(configuration["Messaging:MicrosoftGraph:ClientId"])
                && HasValue(configuration["Messaging:MicrosoftGraph:Email:SenderUpn"])
                && await HasSecretAsync(secretName, cancellationToken);
        }

        if (configuration.GetValue("Messaging:Sms:Enabled", false))
        {
            anyEnabled = true;
            string endpointName = configuration["Messaging:Sms:EndpointSecretName"] ?? "messaging/sms/endpoint";
            string tokenName = configuration["Messaging:Sms:ApiTokenSecretName"] ?? "messaging/sms/api-token";
            allEnabledChannelsConfigured &= await HasSecretAsync(endpointName, cancellationToken)
                && await HasSecretAsync(tokenName, cancellationToken);
        }

        if (configuration.GetValue("Messaging:WhatsApp:Enabled", false))
        {
            anyEnabled = true;
            string endpointName = configuration["Messaging:WhatsApp:EndpointSecretName"] ?? "messaging/whatsapp/endpoint";
            string tokenName = configuration["Messaging:WhatsApp:ApiTokenSecretName"] ?? "messaging/whatsapp/api-token";
            allEnabledChannelsConfigured &= await HasSecretAsync(endpointName, cancellationToken)
                && await HasSecretAsync(tokenName, cancellationToken);
        }

        return anyEnabled && allEnabledChannelsConfigured;
    }

    private async Task<bool> HasSecretAsync(string name, CancellationToken cancellationToken)
    {
        try { return HasValue(await secrets.GetAsync(name, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unable to confirm whether Common.Secrets can resolve {SecretName}.", name);
            return false;
        }
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
    private static string? NullIfBlank(string? value) => HasValue(value) ? value!.Trim() : null;
}
