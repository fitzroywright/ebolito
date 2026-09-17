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
        var baseUrl = configuration["Aegis:Configuration:Url"];
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

        var contractPath = Path.Combine(environment.ContentRootPath, "Configuration", "ebolito.configuration-contract.json");
        if (!File.Exists(contractPath))
        {
            logger.LogWarning("Ebolito configuration contract was not found at {ContractPath}; Ebolito is not registered and will retry.", contractPath);
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(contractPath, cancellationToken);
            var contract = JsonNode.Parse(json)?.AsObject();
            if (contract is null)
            {
                logger.LogWarning("Ebolito configuration contract at {ContractPath} is not a JSON object; Ebolito is not registered and will retry.", contractPath);
                return false;
            }

            var client = httpClientFactory.CreateClient(nameof(ConfigurationRegistrationHostedService));
            var registrar = new ConfigurationRegistrar(client, new RegistrationOptions(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), registrationKey, TimeSpan.FromSeconds(10)));
            var result = await registrar.RegisterContractAsync(contract, cancellationToken);
            if (!result.Succeeded)
            {
                logger.LogWarning("Aegis.Configuration registration failed for Ebolito: {RegistrationError}; Ebolito remains operational and will retry.", result.Error ?? "Unknown registration failure.");
                return false;
            }

            logger.LogInformation("Ebolito configuration contract registered once with Aegis.Configuration with HTTP {StatusCode} using Common.Secrets.", (int?)result.StatusCode);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to register Ebolito with Aegis.Configuration; Ebolito remains operational and will retry.");
            return false;
        }
    }
}
