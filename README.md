# Ebolito

Resurrection of the original **Ebolito Skills & Services** marketplace: a Jamaican professional discovery, portfolio, reputation and engagement platform.

## Product model

Ebolito lets customers search by service/location, inspect a professional's mini-site and work portfolio, verify their mobile identity once, and send an engagement request. Ebolito remains the system of record for the engagement, its status, customer/professional identity, timestamps and delivery audit trail.

The customer does **not** choose how Ebolito reaches the professional. They may only state how they prefer the professional to contact them. The professional or business owns a notification policy that defines primary, business, fallback and escalation channels.

A typical business route can therefore be:

`Ebolito in-app -> Slack #new-leads -> Email -> WhatsApp -> SMS`

Accepting or declining the engagement stops escalation automatically.

## UI themes

One application codebase exposes two presentation-only themes:

- **Modern** - current Ebolito marketplace presentation.
- **Legacy / Wayback** - preserves the visual character and wording of the original ESSJ/Ebolito site.

Themes never fork business logic, data, routes or engagement workflows.

> The recovered original legacy header remains a binary asset that must be copied to `Ebolito.Web/wwwroot/legacy/defaultheader.png` from the old source tree.

## Flat workspace

The Ebolito repository is flat:

- `Ebolito.Domain/`
- `Ebolito.Application/`
- `Ebolito.Infrastructure/`
- `Ebolito.Web/`
- `Ebolito.Tests/`

Target framework: **.NET 10**.

When the normal workspace also contains:

```text
D:\Projects\Common\Common.Messaging\
```

Ebolito automatically compiles the real Common.Messaging adapter through conditional project references. A standalone Ebolito clone still builds and uses the deterministic fallback adapter.

## Development run

With no PostgreSQL connection string, Ebolito uses its in-memory development store:

```powershell
dotnet run --project Ebolito.Web
```

Without Common.Messaging/SMS, development mobile verification writes the OTP to the server console. Production never logs OTP values through the fallback sender.

## PostgreSQL

Production persistence uses **Npgsql 10.0.3**.

```powershell
psql "$env:ConnectionStrings__Ebolito" -f Ebolito.Infrastructure/Postgres/schema.sql
```

Configure:

```text
ConnectionStrings__Ebolito=Host=...;Database=ebolito;Username=...;Password=...
```

PostgreSQL stores professionals, skills, portfolios, reviews, verified customers, engagements, professional notification policies/endpoints and engagement delivery-attempt history.

## Mobile identity

Before an engagement is sent, the customer must have a verified mobile identity. Verification:

- generates a cryptographically random six-digit OTP;
- stores only a SHA-256 hash;
- expires after ten minutes;
- uses fixed-time hash comparison;
- reuses an existing verified identity for the same normalized mobile number.

When Common.Messaging SMS is enabled, OTP delivery is queued through Common.Messaging. Otherwise production verification fails closed.

## Common.Messaging

**Ebolito owns routing policy; Common.Messaging owns transport.**

The production Ebolito adapter writes `ExternalDeliveryWorkItem` records to Common.Messaging using the Ebolito engagement ID as the correlation ID. That preserves one coherent Ebolito history even when delivery occurs through another system.

Currently supported Common.Messaging providers used by Ebolito are:

- Slack;
- Microsoft Teams;
- SMTP email;
- SMS.

Slack supports both individual DMs and business-channel routing through generic `MessageRequest.Metadata["slack.channel"]`. The corresponding Common.Messaging change is isolated in PR #3 in that repository.

A provider-neutral WhatsApp channel is being added separately in Common.Messaging PR #4. Until that shared provider is merged into the local Common workspace, Ebolito does not advertise WhatsApp as a production-capable transport even though WhatsApp remains a valid Ebolito routing-policy channel.

### Channel configuration

No provider credentials are stored in `appsettings.json`. The file contains enablement, non-secret settings and secret names only.

Example enablement keys:

```text
Messaging__Slack__Enabled=true
Messaging__Teams__Enabled=true
Messaging__Email__Enabled=true
Messaging__Sms__Enabled=true
```

The environment fallback secret resolver converts configured secret names to uppercase environment names, replacing punctuation with `_`. Examples:

```text
MESSAGING_SLACK_BOT_TOKEN=...
MESSAGING_TEAMS_WEBHOOK_URL=...
MESSAGING_SMTP_USERNAME=...
MESSAGING_SMTP_PASSWORD=...
MESSAGING_SMS_ENDPOINT=...
MESSAGING_SMS_API_TOKEN=...
```

Where Common.Secrets/OpenBao is registered for Common.Messaging, it should remain the preferred credential source.

## Professional/business notification policy

Notification policy is persisted independently from the professional profile. Changing Slack/Teams/SMS/etc. routing does not change profile/business data.

The protected operational API currently provides:

```text
GET /api/admin/messaging/capabilities
GET /api/admin/professionals/{id}/notification-policy
PUT /api/admin/professionals/{id}/notification-policy
```

Access requires:

```text
X-Ebolito-Profile-Admin-Key: <key>
EBOLITO_PROFILE_ADMIN_KEY=<key>
```

This is a commissioning/administration boundary until full professional self-service authentication is added.

Policy validation ensures that Ebolito in-app remains the canonical first notification and that external primary/business/fallback channels have valid enabled endpoints. Escalation intervals can be configured from 1 minute to 24 hours.

## Signed engagement actions

External notifications can include **View**, **Accept** and **Decline** links. These links:

- are HMAC-SHA256 signed;
- expire (48 hours by default);
- are verified using fixed-time comparison;
- show a confirmation page before Accept/Decline changes Ebolito state;
- never allow a GET request to mutate an engagement.

Configure:

```text
Ebolito__PublicBaseUrl=https://ebolito.example.com
EBOLITO_ENGAGEMENT_ACTION_KEY=<strong random signing key>
```

After the professional confirms Accept/Decline, Ebolito updates the engagement and asks Common.Messaging to notify the customer using the customer's preferred supported return channel.

## Escalation

`EngagementEscalationHostedService` scans unacknowledged `Delivered` engagements once per minute. It applies each professional's configured `EscalationAfter` interval, sends only the next untried channel, records each successful delivery attempt, and stops automatically after the engagement leaves `Delivered`.

## Aegis.Configuration

Ebolito packages:

`Ebolito.Web/Configuration/ebolito.configuration-contract.json`

Set:

```text
Aegis__Configuration__Url=https://<configuration-host>
AEGIS_CONFIGURATION_REGISTRATION_KEY=<per-application registration secret>
```

Ebolito best-effort publishes the contract to `POST /api/contracts/register` using `X-Configuration-Registration-Key`. Publication failure never blocks startup.

The contract currently declares PostgreSQL, the public base URL, Common.Messaging and Common.Diagnostics. Environment-only machine credentials are verified by Ebolito diagnostics until they are explicitly migrated to Common.Secrets-backed runtime resolution.

## Aegis.Diagnostics protocol

Ebolito exposes:

```text
GET  /health
GET  /health/live
GET  /health/ready
POST /api/engineering/diagnostics/run
GET  /api/engineering/diagnostics/runs
GET  /api/engineering/diagnostics/runs/{runId}
POST /api/engineering/diagnostics/runs/{runId}/resolve
```

Machine diagnostics require:

```text
X-Aegis-Diagnostics-Key: <key>
EBOLITO_DIAGNOSTICS_KEY=<key>
```

Diagnostics report PostgreSQL connectivity, marketplace read health, whether Common.Messaging was compiled into the flat workspace, enabled transport channels, SMS verification readiness, signed-action readiness and missing machine credentials.

## CI and validation

`.github/workflows/ci.yml` restores/builds the .NET 10 web host and runs `Ebolito.Tests` on pushes and pull requests.

Ebolito CI has completed successfully on the resurrection branch. Common.Messaging PR #3 (Slack business-channel delivery) has also completed its CI successfully. Newer commits should remain subject to the same CI before merge.

## Still intentionally incomplete

- Full professional self-service authentication/profile editor.
- Portfolio image upload/storage UI.
- Verified reviews generated automatically from completed engagements.
- Common.Messaging WhatsApp PR #4 must land before Ebolito enables that provider in production.
- Push, webhook, Messenger and Instagram remain policy concepts until corresponding Common.Messaging providers exist.
