# Ebolito

Resurrection of the original **Ebolito Skills & Services** marketplace: a Jamaican professional discovery, portfolio, reputation and engagement platform.

## Product model

Ebolito lets customers:

1. search by service and location;
2. compare professionals and screened providers;
3. inspect each professional's mini-site, skills, service areas, portfolio projects and reviews;
4. verify their mobile identity once;
5. send an engagement request;
6. receive a return notification when the professional accepts or declines.

Professionals do not need to remain online. The engagement boundary is designed for **WhatsApp -> SMS -> Web** fallback so someone with only a mobile phone can still participate.

## UI themes

One application codebase exposes selectable presentation-only themes:

- **Modern** - current Ebolito marketplace presentation.
- **Legacy / Wayback** - preserves the visual character and wording of the original ESSJ/Ebolito site.

Themes never fork domain behavior, data, routes or engagement workflows.

> The original legacy header image was recovered from the old ESSJ source. The current GitHub connector can only write text files, so the CSS reference is preserved but the binary asset still needs to be copied into `Ebolito.Web/wwwroot/legacy/defaultheader.png` from the recovered source tree.

## Flat repository structure

- `Ebolito.Domain/` - marketplace entities and engagement lifecycle.
- `Ebolito.Application/` - search, profiles, engagement orchestration and mobile identity verification.
- `Ebolito.Infrastructure/` - in-memory demo store, PostgreSQL store, verification adapters and delivery boundaries.
- `Ebolito.Web/` - web host, API, themes, Aegis.Configuration registration and engineering diagnostics protocol.
- `Ebolito.Tests/` - lifecycle, search and identity verification tests.

Target framework: **.NET 10**.

## Development run

With no database connection string, Ebolito starts with the in-memory development store:

```powershell
dotnet run --project Ebolito.Web
```

The development mobile verification sender writes the one-time code to the server console. This behavior is development-only.

## PostgreSQL

The production persistence implementation uses **Npgsql 10.0.3**, aligned with the current Common.Messaging stack.

Create the schema before first production startup:

```powershell
psql "$env:ConnectionStrings__Ebolito" -f Ebolito.Infrastructure/Postgres/schema.sql
```

Configure the application using the standard ASP.NET Core environment-variable form:

```text
ConnectionStrings__Ebolito=Host=...;Database=ebolito;Username=...;Password=...
```

When `ConnectionStrings:Ebolito` is present, the PostgreSQL store is selected automatically. Otherwise the in-memory store is used.

## Mobile identity

Before Ebolito releases an engagement to a professional, the customer must have a verified mobile identity.

The verification flow:

- generates a cryptographically random six-digit code;
- stores only its SHA-256 hash;
- expires the challenge after ten minutes;
- compares verification hashes in fixed time;
- reuses the same Ebolito customer identity for an already verified mobile number.

Production verification deliberately **fails closed** until a real Common.Messaging/SMS adapter is configured. Codes are never written to production logs by the fallback implementation.

## Aegis.Configuration

Ebolito owns and publishes its configuration contract from:

`Ebolito.Web/Configuration/ebolito.configuration-contract.json`

Set:

```text
Aegis__Configuration__Url=https://<configuration-host>
AEGIS_CONFIGURATION_REGISTRATION_KEY=<per-application registration secret>
```

At startup Ebolito best-effort publishes the contract to:

`POST /api/contracts/register`

using `X-Configuration-Registration-Key`. Registration failure is logged but never prevents Ebolito from starting or operating.

The contract declares PostgreSQL, Common.Messaging and Common.Diagnostics requirements. Secret values are not stored in the contract or `appsettings.json`.

## Aegis.Diagnostics protocol

Ebolito exposes the uniform application protocol expected by Aegis.Diagnostics:

```text
GET  /health
GET  /health/live
GET  /health/ready
POST /api/engineering/diagnostics/run
GET  /api/engineering/diagnostics/runs
GET  /api/engineering/diagnostics/runs/{runId}
POST /api/engineering/diagnostics/runs/{runId}/resolve
```

Machine diagnostics use:

```text
X-Aegis-Diagnostics-Key: <key>
```

with the server-side key supplied only through:

```text
EBOLITO_DIAGNOSTICS_KEY=<key>
```

The diagnostics wire format mirrors Common.Diagnostics. The current implementation is local to Ebolito until the shared Common.Diagnostics project/package is pulled into the same workspace; it should ultimately be replaced by the shared contract rather than maintained as a fork.

## Common.Messaging boundary

Ebolito does not depend directly on Meta, Twilio or another delivery provider. `IEngagementNotifier` owns the application boundary for:

- sending an engagement to the professional;
- WhatsApp/SMS/Web fallback selection;
- notifying the customer when the professional responds.

The current fallback adapter models routing but does not perform production external delivery. The intended production implementation is an adapter over Common.Messaging.

## Current validation status

The branch includes automated tests and has undergone a static compile-risk pass, but **a real `dotnet restore`, `dotnet build` and `dotnet test` run has not yet been executed in this session** because the execution container cannot resolve GitHub. Keep the resurrection PR in draft until that build is run in a normal clone/workspace or CI.
