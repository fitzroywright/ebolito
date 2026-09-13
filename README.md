# Ebolito

Resurrection of the original **Ebolito Skills & Services** marketplace: a Jamaican professional discovery, portfolio, reputation and engagement platform.

## What the product does

Customers can search screened professionals by service/location, inspect profiles and portfolios, verify a mobile identity, request an engagement, track that engagement, and submit a verified review after completion.

Professionals can register by mobile OTP, sign in with a scoped professional session, manage their profile and portfolio, configure notification routing, receive engagements in the Ebolito inbox, accept/decline work, and move engagements through Contacted -> Hired -> Completed.

New professional registrations are created as `IsActive=true, IsScreened=false`. They may sign in and finish setup immediately, but they are excluded from public search/profile lookup and cannot receive marketplace engagements until an administrator approves screening.

Ebolito remains the system of record. External channels such as Slack, Teams, email, SMS and WhatsApp are delivery surfaces only.

## Core engagement flow

`Customer -> Ebolito -> Professional`

Typical delivery route:

`Ebolito in-app -> Slack #new-leads -> Email -> WhatsApp -> SMS`

Accepting or declining stops escalation. Engagement state remains in Ebolito regardless of which channel delivered the notification.

## Professional onboarding and screening

Public registration:

- `/professional-register.html`
- verifies the applicant mobile number by six-digit OTP;
- prevents duplicate professional accounts on the same mobile number;
- throttles immediate code resend;
- limits incorrect verification attempts;
- creates a private pending professional profile;
- issues a professional-scoped signed session;
- forwards the applicant into profile setup.

Professional self-service:

- `/professional-login.html`
- `/manage.html`
- `/inbox.html`
- `/messaging.html`

Administrative screening:

- `/screening.html`
- `GET /api/admin/screening/professionals`
- `POST /api/admin/screening/professionals/{id}`

Screening actions require the machine administration credential and cannot be performed with a normal professional session.

## Customer identity and engagement history

Customer mobile verification uses random six-digit OTP values, SHA-256 hashes, fixed-time comparison, ten-minute expiry, resend throttling and a five-attempt limit.

After verification, Ebolito issues a signed customer session. Customer-facing engagement creation, engagement history and verified-review submission are bound to that session rather than trusting a client-supplied customer ID.

Customer history is available at `/my-engagements.html`.

## Themes

One application exposes two presentation-only themes:

- **Modern**
- **Legacy / Wayback**

Themes share all business logic and routes.

The recovered original binary header asset is still not in the repository. To complete the Wayback presentation, copy the original asset to:

`Ebolito.Web/wwwroot/legacy/defaultheader.png`

## Workspace and runtime

Repository layout:

- `Ebolito.Domain/`
- `Ebolito.Application/`
- `Ebolito.Infrastructure/`
- `Ebolito.Web/`
- `Ebolito.Tests/`

Target framework: **.NET 10**.

Standalone development:

```powershell
dotnet run --project Ebolito.Web
```

With no PostgreSQL connection string Ebolito uses the in-memory development store.

When the flat workspace also contains the Common.* repositories, Ebolito conditionally compiles production adapters for Common.Messaging, Common.Storage, Common.Secrets and diagnostics integration.

## PostgreSQL

Production persistence uses PostgreSQL/Npgsql.

```powershell
psql "$env:ConnectionStrings__Ebolito" -f Ebolito.Infrastructure/Postgres/schema.sql
```

Configure:

```text
ConnectionStrings__Ebolito=Host=...;Database=ebolito;Username=...;Password=...
```

PostgreSQL stores professionals, skills, portfolios, reviews, customers, engagements, notification policies and delivery-attempt history.

## Professional and customer sessions

Production requires separate signing credentials:

```text
EBOLITO_CUSTOMER_SESSION_KEY=<strong random value>
EBOLITO_PROFESSIONAL_SESSION_KEY=<strong random value>
```

Development generates process-local signing keys when these values are absent. Production fails closed.

The machine commissioning/admin credential remains available for administration-only operations:

```text
EBOLITO_PROFILE_ADMIN_KEY=<strong random value>
X-Ebolito-Profile-Admin-Key: <same value>
```

Normal professional profile, inbox and messaging workflows can use `X-Ebolito-Professional-Session` instead.

## Common.Messaging

**Ebolito owns routing policy; Common.Messaging owns transport.**

Production-capable Ebolito transports available through the current Common.Messaging workspace include:

- Slack
- Microsoft Teams
- SMTP email
- SMS
- WhatsApp through the provider-neutral gateway adapter

Slack business-channel delivery is supported without making Slack the system of record.

The provider-neutral WhatsApp implementation from Common.Messaging PR #4 has been merged into Common.Messaging `main`. Ebolito can register it when `Messaging:WhatsApp:Enabled=true`. Deployment still needs the configured WhatsApp gateway endpoint and optional API token through Common.Secrets/OpenBao or the environment fallback resolver.

Default WhatsApp secret names:

```text
messaging/whatsapp/endpoint
messaging/whatsapp/api-token
```

Push, webhook, Messenger and Instagram remain policy concepts until Common.Messaging provides concrete transports.

## Portfolio and reviews

Professional profile management supports:

- profile editing;
- portfolio project CRUD;
- Common.Storage-backed image upload;
- featured projects;
- service/area selection.

Verified reviews require a completed engagement, the original verified customer, and one review per engagement. Successful review submission moves the engagement to `Reviewed`.

## Signed engagement actions

External notifications can carry View, Accept and Decline links. Links are HMAC-SHA256 signed, expire, use fixed-time verification, show confirmation before mutation, and never mutate state through GET.

Configure:

```text
Ebolito__PublicBaseUrl=https://ebolito.example.com
EBOLITO_ENGAGEMENT_ACTION_KEY=<strong random value>
```

## Escalation

`EngagementEscalationHostedService` scans unacknowledged delivered engagements, applies each professional's configured escalation interval, sends only the next untried channel, records successful attempts, and stops after the engagement leaves `Delivered`.

## Aegis.Configuration and Diagnostics

Configuration contract:

`Ebolito.Web/Configuration/ebolito.configuration-contract.json`

Configure registration:

```text
Aegis__Configuration__Url=https://<configuration-host>
AEGIS_CONFIGURATION_REGISTRATION_KEY=<per-application registration secret>
```

Diagnostics endpoints:

```text
GET  /health
GET  /health/live
GET  /health/ready
POST /api/engineering/diagnostics/run
GET  /api/engineering/diagnostics/runs
GET  /api/engineering/diagnostics/runs/{runId}
POST /api/engineering/diagnostics/runs/{runId}/resolve
```

Diagnostics require:

```text
EBOLITO_DIAGNOSTICS_KEY=<strong random value>
X-Aegis-Diagnostics-Key: <same value>
```

Diagnostics cover PostgreSQL, marketplace reads, Common.* integration, portfolio storage, messaging channels including WhatsApp capability, mobile verification readiness, engagement-action signing, customer-session signing, professional-session signing and machine credentials.

## CI

`.github/workflows/ci.yml` builds the standalone web host, runs Ebolito tests, and separately validates the flat Common.* workspace integration.

## Remaining work

The major Ebolito product loops are now implemented. Remaining work is primarily deployment/integration rather than missing marketplace workflow:

- configure production PostgreSQL, durable Common.Storage and OpenBao/Common.Secrets;
- configure real SMS delivery for customer/professional OTP;
- configure Slack/Teams/email/SMS/WhatsApp credentials and execute end-to-end delivery tests;
- configure the WhatsApp gateway or BSP bridge behind the provider-neutral Common.Messaging adapter before enabling it;
- register the production instance with Aegis.Configuration and Aegis.Diagnostics;
- restore the original legacy header binary asset if the exact Wayback appearance is required;
- add future Common.Messaging providers for push/webhook/Messenger/Instagram only when there is a real product need.
