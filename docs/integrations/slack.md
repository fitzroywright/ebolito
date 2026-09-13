# Ebolito Slack integration

Slack is an optional engagement channel. It is not Ebolito's system of record and Ebolito must remain fully usable when Slack is unavailable or not configured.

## Flow

1. Customer selects **Request Engagement**.
2. Ebolito creates and persists the engagement first.
3. Ebolito emits `ebolito.engagement.requested` through the Common.Messaging boundary.
4. Common.Messaging routes the event to the professional's enabled channels: in-app, Slack, WhatsApp, SMS and/or email.
5. Channel buttons/links return to authenticated Ebolito endpoints for View, Accept and Decline.
6. Ebolito changes engagement state, audits it, then emits `ebolito.engagement.status-changed`.

## Slack presentation

Suggested Block Kit content:

**New Ebolito Engagement**

A customer wants to discuss Photography Services.

Requested: Wedding photography

Preferred contact: WhatsApp

[View Request] [Accept] [Decline]

The Slack adapter may render richer blocks, but domain data and state transitions must remain in Ebolito.

## Rules

- Never store authoritative engagement state in Slack.
- Never require a Slack workspace membership to use Ebolito.
- Slack credentials, signing secrets and tokens belong in Common.Secrets/OpenBao, not Ebolito configuration or source.
- Verify Slack signatures and timestamps before accepting interactive callbacks.
- Callback actions carry an opaque engagement/action token; do not trust user-supplied professional/customer IDs.
- Resolve Slack users/channels from a professional notification preference/profile mapping.
- Log delivery attempts and correlation IDs through Common.Diagnostics.
- Retry transient delivery failures via Common.Messaging; do not roll back a persisted engagement because notification delivery failed.
- Prefer in-app as the durable notification history. External channels are delivery surfaces.

## Ebolito app

An Ebolito Slack app is appropriate. Minimum scopes should be determined by the Common.Messaging Slack adapter, but the design should prefer outbound notifications plus interactive actions and avoid broad workspace-reading permissions. Installation should be optional per organization/professional.

## Additional channels

The same event contract intentionally supports WhatsApp, SMS, email and in-app delivery. Future channels should be added to Common.Messaging rather than to Ebolito.Domain or Ebolito.Application.
