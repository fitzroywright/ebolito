using System.Net.Http.Json;

namespace Ebolito.Web;

public sealed class ConfigurationRegistrationHostedService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<ConfigurationRegistrationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = configuration["Aegis:Configuration:Url"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return;

        var registrationKey = Environment.GetEnvironmentVariable("AEGIS_CONFIGURATION_REGISTRATION_KEY");
        if (string.IsNullOrWhiteSpace(registrationKey))
        {
            logger.LogWarning("Aegis.Configuration URL is configured but AEGIS_CONFIGURATION_REGISTRATION_KEY is missing; skipping contract registration.");
            return;
        }

        var contractPath = Path.Combine(environment.ContentRootPath, "Configuration", "ebolito.configuration-contract.json");
        if (!File.Exists(contractPath))
        {
            logger.LogWarning("Ebolito configuration contract was not found at {ContractPath}; startup will continue.", contractPath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(contractPath, stoppingToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/contracts/register"));
            request.Headers.Add("X-Configuration-Registration-Key", registrationKey);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var client = httpClientFactory.CreateClient(nameof(ConfigurationRegistrationHostedService));
            using var response = await client.SendAsync(request, stoppingToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Aegis.Configuration registration returned HTTP {StatusCode}; Ebolito startup will continue.", (int)response.StatusCode);
                return;
            }

            logger.LogInformation("Ebolito configuration contract registered with Aegis.Configuration.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to register Ebolito with Aegis.Configuration; startup will continue.");
        }
    }
}
