#if COMMON_MESSAGING
using Common.Messaging;
using Common.Messaging.Channels.MicrosoftGraph;
using Common.Messaging.Channels.Slack;
using Common.Messaging.Channels.Sms;
using Common.Messaging.Channels.Smtp;
using Common.Messaging.Channels.Teams;
using Common.Messaging.Channels.WhatsApp;
using Common.Messaging.Hosting;
using Ebolito.Application;
using Ebolito.Domain;
using Ebolito.Infrastructure;

namespace Ebolito.Web;

public static class CommonMessagingBootstrap
{
    public static void AddEbolitoCommonMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string? postgresConnection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(postgresConnection) && configuration.GetValue("Messaging:DurableQueue", true))
            services.AddCommonMessagingPostgreSqlDelivery(postgresConnection, "ebolito");
        else
            services.AddCommonMessagingQueuedDelivery(durable: false);

        // Keep applications provider-neutral: Common.Messaging resolves channel secrets
        // through Common.Secrets, which then selects the configured provider.
        services.AddCommonMessagingSecrets();

        var supported = new HashSet<EngagementChannel>();

        if (configuration.GetValue("Messaging:Slack:Enabled", false))
        {
            services.AddSlackMessagingChannel(new SlackMessageOptions
            {
                BotTokenSecretName = configuration["Messaging:Slack:BotTokenSecretName"] ?? "messaging/slack/bot-token"
            });
            supported.Add(EngagementChannel.Slack);
        }

        if (configuration.GetValue("Messaging:Teams:Enabled", false))
        {
            services.AddTeamsMessagingChannel(new TeamsMessageOptions
            {
                WebhookSecretName = configuration["Messaging:Teams:WebhookSecretName"] ?? "messaging/teams/webhook-url"
            });
            supported.Add(EngagementChannel.Teams);
        }

        if (configuration.GetValue("Messaging:MicrosoftGraph:Teams:Enabled", false))
        {
            services.AddMicrosoftGraphTeamsMessagingChannel(new MicrosoftGraphTeamsOptions
            {
                SenderUpn = configuration["Messaging:MicrosoftGraph:Teams:SenderUpn"] ?? string.Empty,
                DelegatedAccessTokenSecretName = configuration["Messaging:MicrosoftGraph:Teams:DelegatedAccessTokenSecretName"]
                    ?? "messaging/msgraph/teams/delegated-access-token"
            });
            supported.Add(EngagementChannel.Teams);
        }

        if (configuration.GetValue("Messaging:Email:Enabled", false))
        {
            services.AddSmtpMessagingChannel(new SmtpMessageOptions
            {
                Host = configuration["Messaging:Email:Host"] ?? string.Empty,
                Port = configuration.GetValue("Messaging:Email:Port", 587),
                EnableSsl = configuration.GetValue("Messaging:Email:EnableSsl", true),
                FromAddress = configuration["Messaging:Email:FromAddress"] ?? string.Empty,
                UserNameSecretName = configuration["Messaging:Email:UserNameSecretName"] ?? "messaging/smtp/username",
                PasswordSecretName = configuration["Messaging:Email:PasswordSecretName"] ?? "messaging/smtp/password"
            });
            supported.Add(EngagementChannel.Email);
        }

        if (configuration.GetValue("Messaging:MicrosoftGraph:Email:Enabled", false))
        {
            services.AddMicrosoftGraphEmailMessagingChannel(new MicrosoftGraphEmailOptions
            {
                TenantId = configuration["Messaging:MicrosoftGraph:TenantId"] ?? string.Empty,
                ClientId = configuration["Messaging:MicrosoftGraph:ClientId"] ?? string.Empty,
                SenderUpn = configuration["Messaging:MicrosoftGraph:Email:SenderUpn"] ?? string.Empty,
                ClientSecretName = configuration["Messaging:MicrosoftGraph:ClientSecretName"]
                    ?? "messaging/msgraph/client-secret"
            });
            supported.Add(EngagementChannel.Email);
        }

        if (configuration.GetValue("Messaging:Sms:Enabled", false))
        {
            services.AddSmsMessagingChannel(new SmsMessageOptions
            {
                EndpointSecretName = configuration["Messaging:Sms:EndpointSecretName"] ?? "messaging/sms/endpoint",
                ApiTokenSecretName = configuration["Messaging:Sms:ApiTokenSecretName"] ?? "messaging/sms/api-token"
            });
            supported.Add(EngagementChannel.Sms);
            services.AddSingleton<IMobileVerificationSender, CommonMessagingMobileVerificationSender>();
        }

        if (configuration.GetValue("Messaging:WhatsApp:Enabled", false))
        {
            services.AddWhatsAppMessagingChannel(new WhatsAppMessageOptions
            {
                EndpointSecretName = configuration["Messaging:WhatsApp:EndpointSecretName"] ?? "messaging/whatsapp/endpoint",
                ApiTokenSecretName = configuration["Messaging:WhatsApp:ApiTokenSecretName"] ?? "messaging/whatsapp/api-token"
            });
            supported.Add(EngagementChannel.WhatsApp);
        }

        services.AddSingleton<IReadOnlySet<EngagementChannel>>(supported);
        services.AddSingleton<IEngagementNotifier>(provider => new CommonMessagingEngagementNotifier(
            provider.GetRequiredService<IExternalDeliveryQueue>(),
            provider.GetRequiredService<IReadOnlySet<EngagementChannel>>(),
            provider.GetService<IEngagementActionLinkBuilder>()));
    }
}
#endif
