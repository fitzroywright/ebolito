using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Ebolito.Web;

public sealed class ConfigurationRegistrationHostedService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<ConfigurationRegistrationHostedService> logger) : BackgroundService
{
    private const string ApplicationId = "Ebolito";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? baseUrl = configuration["Aegis:Configuration:Url"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        string? registrationKey = Environment.GetEnvironmentVariable("AEGIS_CONFIGURATION_REGISTRATION_KEY");
        if (string.IsNullOrWhiteSpace(registrationKey))
        {
            logger.LogWarning("Aegis.Configuration URL is configured but AEGIS_CONFIGURATION_REGISTRATION_KEY is missing; skipping operational registration.");
            return;
        }
        try
        {
            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            string instanceId = Environment.MachineName;
            bool postgres = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Ebolito"));
            object[] requirements =
            [
                Config("configuration-url", "Aegis:Configuration:Url", "System Configuration URL", false, "Aegis.Configuration endpoint.", "http://localhost:5101"),
                Config("diagnostics-url", "Aegis:Diagnostics:Url", "System Diagnostics URL", false, "Aegis.Diagnostics endpoint.", "http://localhost:5102"),
                Secret("database", "ConnectionStrings:Ebolito", "Ebolito Database Connection", false, "Optional PostgreSQL connection; when absent Ebolito uses its in-memory store.", postgres, "ConnectionStrings:Ebolito"),
                Config("public-url", "Ebolito:PublicBaseUrl", "Public Base URL", false, "Public base URL used when generating Ebolito links."),
                Config("action-hours", "Ebolito:EngagementActionLinkHours", "Engagement Action Link Hours", false, "Validity period for signed engagement action links.", "48"),
                Config("storage-root", "Storage:Root", "Storage Root", false, "Optional external storage root used when Common.Storage is present."),
                Config("durable-queue", "Messaging:DurableQueue", "Durable Messaging Queue", false, "Enables durable messaging when Common.Messaging is available.", "true"),
                Config("slack-enabled", "Messaging:Slack:Enabled", "Slack Enabled", false, "Enables Slack notification delivery.", "false"),
                Config("slack-secret-name", "Messaging:Slack:BotTokenSecretName", "Slack Bot Token Secret Name", false, "Logical secret name for Slack authentication.", "messaging/slack/bot-token"),
                Config("teams-enabled", "Messaging:Teams:Enabled", "Teams Enabled", false, "Enables Teams webhook delivery.", "false"),
                Config("teams-secret-name", "Messaging:Teams:WebhookSecretName", "Teams Webhook Secret Name", false, "Logical secret name for Teams webhook.", "messaging/teams/webhook-url"),
                Config("email-enabled", "Messaging:Email:Enabled", "Email Enabled", false, "Enables SMTP notification delivery.", "false"),
                Config("email-host", "Messaging:Email:Host", "SMTP Host", false, "SMTP host.", null, "Messaging:Email:Enabled=true"),
                Config("email-port", "Messaging:Email:Port", "SMTP Port", false, "SMTP port.", "587", "Messaging:Email:Enabled=true"),
                Config("email-ssl", "Messaging:Email:EnableSsl", "SMTP SSL", false, "Enable SMTP TLS/SSL.", "true", "Messaging:Email:Enabled=true"),
                Config("email-from", "Messaging:Email:FromAddress", "SMTP From Address", false, "SMTP sender address.", null, "Messaging:Email:Enabled=true"),
                Config("email-user-secret", "Messaging:Email:UserNameSecretName", "SMTP Username Secret Name", false, "Logical secret name for SMTP username.", "messaging/smtp/username"),
                Config("email-password-secret", "Messaging:Email:PasswordSecretName", "SMTP Password Secret Name", false, "Logical secret name for SMTP password.", "messaging/smtp/password"),
                Config("sms-enabled", "Messaging:Sms:Enabled", "SMS Enabled", false, "Enables SMS notification delivery.", "false"),
                Config("sms-endpoint-secret", "Messaging:Sms:EndpointSecretName", "SMS Endpoint Secret Name", false, "Logical secret name for SMS endpoint.", "messaging/sms/endpoint"),
                Config("sms-token-secret", "Messaging:Sms:ApiTokenSecretName", "SMS API Token Secret Name", false, "Logical secret name for SMS API token.", "messaging/sms/api-token"),
                Config("whatsapp-enabled", "Messaging:WhatsApp:Enabled", "WhatsApp Enabled", false, "Enables WhatsApp notification delivery.", "false"),
                Config("whatsapp-endpoint-secret", "Messaging:WhatsApp:EndpointSecretName", "WhatsApp Endpoint Secret Name", false, "Logical secret name for WhatsApp endpoint.", "messaging/whatsapp/endpoint"),
                Config("whatsapp-token-secret", "Messaging:WhatsApp:ApiTokenSecretName", "WhatsApp API Token Secret Name", false, "Logical secret name for WhatsApp API token.", "messaging/whatsapp/api-token"),
                Secret("registration-key", "AEGIS_CONFIGURATION_REGISTRATION_KEY", "Configuration Registration Credential", false, "Environment-provided registration credential; value is never published.", true, "Environment:AEGIS_CONFIGURATION_REGISTRATION_KEY"),
                Secret("secret-zero", "Secret0", "Secret Zero", false, "Secret-provider bootstrap material, if configured by the deployed Common libraries. Value is never published.", SecretZeroDetected(), "OpenBao/bootstrap")
            ];

            List<object> dependencies =
            [
                Dependency("configuration", "Aegis.Configuration", false, "Feature", "Receives Ebolito operational contracts.", "Ebolito continues; centralized visibility is unavailable.", "HTTP/HTTPS JSON", "Aegis.Configuration", baseUrl, await ReachableHttpAsync(baseUrl, stoppingToken)),
                Dependency("diagnostics", "Aegis.Diagnostics", false, "Feature", "Invokes Ebolito engineering diagnostics remotely.", "Ebolito remains functional; centralized diagnostics orchestration is unavailable.", "HTTP/HTTPS diagnostics API", "Aegis.Diagnostics", configuration["Aegis:Diagnostics:Url"], await ReachableHttpAsync(configuration["Aegis:Diagnostics:Url"], stoppingToken))
            ];
            if (postgres)
                dependencies.Add(Dependency("database", "Ebolito Database", true, "Startup", "Persists marketplace, engagement, review, notification-routing and professional-management state.", "Configured PostgreSQL-backed Ebolito cannot provide its persistent marketplace store.", "Npgsql", "PostgreSQL", null, null));
            else
                dependencies.Add(Dependency("memory-store", "In-Memory Marketplace Store", true, "Startup", "Provides non-persistent marketplace state when no PostgreSQL connection is configured.", "The process cannot provide marketplace state.", "In-process interface", "In-memory store", null, true));
            AddMessagingDependencies(dependencies);

            object registration = new
            {
                configuration = new { applicationId = ApplicationId, displayName = "Ebolito", version, siteId = configuration["Site:Id"], instanceId, description = "Marketplace, customer engagement and professional workflow application.", requirements },
                dependencies = new { applicationId = ApplicationId, version, siteId = configuration["Site:Id"], instanceId, dependencies },
                environment = new { applicationId = ApplicationId, version, siteId = configuration["Site:Id"], instanceId, environment = await DetectEnvironmentAsync(baseUrl, stoppingToken) }
            };

            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/registrations/register"));
            request.Headers.Add("X-Aegis-Registration-Key", registrationKey);
            request.Content = new StringContent(JsonSerializer.Serialize(registration), Encoding.UTF8, "application/json");
            HttpClient client = httpClientFactory.CreateClient(nameof(ConfigurationRegistrationHostedService));
            using HttpResponseMessage response = await client.SendAsync(request, stoppingToken);
            if (!response.IsSuccessStatusCode) logger.LogWarning("Aegis.Configuration operational registration returned HTTP {StatusCode}; Ebolito startup will continue.", (int)response.StatusCode);
            else logger.LogInformation("Ebolito configuration, dependency and detected-environment contracts registered with Aegis.Configuration.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "Unable to register Ebolito operational contracts; startup will continue."); }
    }

    private void AddMessagingDependencies(List<object> dependencies)
    {
        if (configuration.GetValue("Messaging:Slack:Enabled", false)) dependencies.Add(Dependency("slack", "Slack", false, "Feature", "Professional/customer notification channel.", "Other channels remain available; Slack delivery is unavailable.", "Slack API", "Slack", null, null));
        if (configuration.GetValue("Messaging:Teams:Enabled", false)) dependencies.Add(Dependency("teams", "Microsoft Teams", false, "Feature", "Professional/customer notification channel.", "Other channels remain available; Teams delivery is unavailable.", "Webhook HTTPS", "Microsoft Teams", null, null));
        if (configuration.GetValue("Messaging:Email:Enabled", false)) dependencies.Add(Dependency("smtp", "SMTP Server", false, "Feature", "Professional/customer email notifications.", "Other channels remain available; email delivery is unavailable.", "SMTP", "SMTP server", HostPort("Messaging:Email:Host", "Messaging:Email:Port", 587), null));
        if (configuration.GetValue("Messaging:Sms:Enabled", false)) dependencies.Add(Dependency("sms", "SMS Provider", false, "Feature", "Professional/customer SMS notifications.", "Other channels remain available; SMS delivery is unavailable.", "Configured messaging adapter", "SMS provider", null, null));
        if (configuration.GetValue("Messaging:WhatsApp:Enabled", false)) dependencies.Add(Dependency("whatsapp", "WhatsApp Provider", false, "Feature", "Professional/customer WhatsApp notifications.", "Other channels remain available; WhatsApp delivery is unavailable.", "Configured messaging adapter", "WhatsApp provider", null, null));
    }

    private object Config(string id,string key,string name,bool required,string purpose,string? defaultValue=null,string? condition=null){string envKey=key.Replace(":","__",StringComparison.Ordinal);string? env=Environment.GetEnvironmentVariable(envKey);string? configured=configuration[key];string? value=First(env,configured,defaultValue);return new{id,displayName=name,kind="Configuration",required,purpose,configurationKey=key,isConfigured=!string.IsNullOrWhiteSpace(value),safeDisplayValue=value,sensitive=false,conditionalOn=condition,allowedSources=new[]{"Environment Variable",$"appsettings.{environment.EnvironmentName}.json","appsettings.json","Code Default"},resolutionOrder=new[]{"Environment Variable",$"appsettings.{environment.EnvironmentName}.json","appsettings.json","Code Default"},effectiveSource=!string.IsNullOrWhiteSpace(env)?$"Environment Variable ({envKey})":!string.IsNullOrWhiteSpace(configured)?"Configuration Provider":defaultValue is not null?"Code Default":"Unresolved",hasDefault=defaultValue is not null,defaultValue,redacted=false};}
    private static object Secret(string id,string key,string name,bool required,string purpose,bool configured,string path)=>new{id,displayName=name,kind="Secret",required,purpose,configurationKey=key,isConfigured=configured,safeDisplayValue=(string?)null,sensitive=true,allowedSources=new[]{"Environment Variable","OpenBao/Common.Secrets","Secret0/bootstrap"},resolutionOrder=new[]{"secure runtime source","no plaintext/display fallback"},effectiveSource=configured?"Secure runtime source":"Unresolved",hasDefault=false,defaultValue=(string?)null,secretPath=path,redacted=true};
    private static object Dependency(string id,string name,bool required,string criticality,string purpose,string degraded,string @interface,string? engine,string? endpoint,bool? reachable)=>new{id,displayName=name,required,criticality,purpose,degradedBehavior=degraded,@interface,engine,endpoint,reachable,evidence="Explicitly declared from Ebolito code; PostgreSQL is declared from the Postgres store implementation, not inferred from a connection string."};

    private async Task<object> DetectEnvironmentAsync(string configurationUrl,CancellationToken ct)
    {
        Process p=Process.GetCurrentProcess();GCMemoryInfo memory=GC.GetGCMemoryInfo();List<object>paths=[];List<object>disks=[];HashSet<string>roots=new(StringComparer.OrdinalIgnoreCase);
        foreach(string raw in new[]{environment.ContentRootPath,configuration["Storage:Root"]}.Where(x=>!string.IsNullOrWhiteSpace(x))){string path=Path.GetFullPath(raw!);bool exists=Directory.Exists(path)||File.Exists(path);string dir=Directory.Exists(path)?path:Path.GetDirectoryName(path)??path;bool? writable=Directory.Exists(dir)?await IsWritableAsync(dir,ct):null;paths.Add(new{path,exists,writable});try{string root=Path.GetPathRoot(path)??"";if(root.Length>0&&roots.Add(root)){DriveInfo d=new(root);if(d.IsReady)disks.Add(new{path=root,freeBytes=d.AvailableFreeSpace,totalBytes=d.TotalSize});}}catch{}}
        List<object>endpoints=[new{name="Aegis.Configuration",endpoint=configurationUrl,reachable=await ReachableHttpAsync(configurationUrl,ct)}];if(configuration["Aegis:Diagnostics:Url"]is{Length:>0}durl)endpoints.Add(new{name="Aegis.Diagnostics",endpoint=durl,reachable=await ReachableHttpAsync(durl,ct)});
        return new{operatingSystem=RuntimeInformation.OSDescription,operatingSystemVersion=Environment.OSVersion.VersionString,cpuArchitecture=RuntimeInformation.ProcessArchitecture.ToString(),processorCount=Environment.ProcessorCount,availableMemoryBytes=memory.TotalAvailableMemoryBytes>0?memory.TotalAvailableMemoryBytes:null,processWorkingSetBytes=p.WorkingSet64,runtimeVersion=RuntimeInformation.FrameworkDescription,networkAvailable=NetworkInterface.GetIsNetworkAvailable(),disks,paths,endpoints,hardware=Array.Empty<object>(),detectedAtUtc=DateTimeOffset.UtcNow};
    }
    private string? HostPort(string hostKey,string portKey,int defaultPort){string? host=configuration[hostKey];return string.IsNullOrWhiteSpace(host)?null:$"{host}:{configuration.GetValue(portKey,defaultPort)}";}
    private static bool SecretZeroDetected()=>new[]{"OPENBAO_SECRET_ID","OPENBAO_TOKEN","VAULT_TOKEN"}.Any(n=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(n)));
    private static async Task<bool?> ReachableHttpAsync(string? endpoint,CancellationToken ct){if(!Uri.TryCreate(endpoint,UriKind.Absolute,out Uri? uri)||(uri.Scheme!="http"&&uri.Scheme!="https"))return null;try{using HttpClient c=new(){Timeout=TimeSpan.FromSeconds(3)};using HttpResponseMessage _=await c.SendAsync(new HttpRequestMessage(HttpMethod.Head,uri),ct);return true;}catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}catch{return false;}}
    private static async Task<bool> IsWritableAsync(string path,CancellationToken ct){string probe=Path.Combine(path,$".aegis-write-{Guid.NewGuid():N}");try{await File.WriteAllTextAsync(probe,"",ct);File.Delete(probe);return true;}catch{try{if(File.Exists(probe))File.Delete(probe);}catch{}return false;}}
    private static string? First(params string?[] values)=>values.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
}
