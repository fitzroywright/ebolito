# Ebolito Slack integration

Slack is an optional engagement channel. It is not Ebolito's system of record and Ebolito must remain fully usable when Slack is unavailable or not configured.

## Flow

1. Customer selects **Request Engagement**.
2. Ebolito creates and persists the engagement first.
3. Ebolito routes notification through the Common.Messaging boundary.
4. Common.Messaging delivers to the professional's configured policy channels, including in-app, Slack, WhatsApp, SMS and/or email when providers are available.
5. External View/Accept/Decline actions return to Ebolito through signed links and confirmation endpoints.
6. Ebolito changes engagement state and preserves the authoritative audit history.

## Slack presentation

A Slack notification should identify the engagement, summarize the requested service/location and customer return-contact preference, and expose View / Accept / Decline actions where signed action links are enabled.

The Slack adapter may render richer Block Kit content, but domain data and state transitions remain in Ebolito.

## Rules

- Never store authoritative engagement state in Slack.
- Never require Slack workspace membership to use Ebolito.
- Slack credentials, signing secrets and tokens belong in Common.Secrets/OpenBao, not Ebolito source.
- Verify Slack signatures/timestamps for any future interactive callback implementation.
- Do not trust user-supplied professional/customer IDs in callback payloads.
- Resolve Slack users/channels from the professional notification policy.
- Preserve engagement correlation IDs and delivery-attempt audit history.
- Retry transient transport failures in Common.Messaging; do not roll back a persisted engagement because notification delivery failed.
- Keep in-app/Ebolito state as the durable history; external channels are delivery surfaces.

## Ebolito Slack app

An optional Ebolito Slack app is appropriate. Keep its scopes minimal: outbound engagement notifications and interactive actions only where required. Avoid broad workspace-reading permissions. Installation should be optional per organization/professional.

## Additional channels

The same routing architecture supports WhatsApp, SMS, email, Teams and in-app delivery. Future transport providers belong in Common.Messaging rather than Ebolito.Domain or Ebolito.Application.
