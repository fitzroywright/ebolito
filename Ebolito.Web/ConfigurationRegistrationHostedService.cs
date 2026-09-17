using System.Text.Json.Nodes;
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
        string? baseUrl = configuration["Aegis:Configuration:Url"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogInformation("Aegis.Configuration registration is disabled because no Configuration URL is configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryRegisterAsync(baseUrl, stoppingToken)) return;
            try { await Task.Delay(RetryInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<bool> TryRegisterAsync(string baseUrl, CancellationToken cancellationToken)
    {
        string secretName = RegistrationCredentialResolver.SecretNameFor(ApplicationId);
        string? registrationKey;
        try
        {
            registrationKey = await secrets.GetAsync(secretName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Common.Secrets could not resolve Ebolito's Aegis.Configuration registration credential; Ebolito remains operational and will retry.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(registrationKey))
        {
            logger.LogWarning("Common.Secrets did not resolve required secret {SecretName}; Ebolito is not registered and will retry.", secretName);
            return false;
        }

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

            contract["siteId"] = NullIfBlank(configuration["Site:Id"]);
            contract["instanceId"] = NullIfBlank(configuration["Service:Identity"] ?? Environment.MachineName);

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

            HttpClient client = httpClientFactory.CreateClient(nameof(ConfigurationRegistrationHostedService));
            ConfigurationRegistrar registrar = new(client, new RegistrationOptions(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), registrationKey, TimeSpan.FromSeconds(10)));
            RegistrationResult result = await registrar.RegisterContractAsync(contract, cancellationToken);
            if (!result.Succeeded)
            {
                logger.LogWarning("Aegis.Configuration registration failed for Ebolito: {RegistrationError}; Ebolito remains operational and will retry.", result.Error ?? "Unknown registration failure.");
                return false;
            }

            logger.LogInformation("Ebolito runtime commissioning state registered with Aegis.Configuration with HTTP {StatusCode} using Common.Secrets.", (int?)result.StatusCode);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to register Ebolito with Aegis.Configuration; Ebolito remains operational and will retry.");
            return false;
        }
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
