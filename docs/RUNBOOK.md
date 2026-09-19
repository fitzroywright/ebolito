# Ebolito Operational Runbook

## Purpose
Provides professional/customer engagement workflows and multi-channel notifications.

## Dependencies
PostgreSQL marketplace state, Common.Messaging, Common.Secrets, registration/configuration services and configured channel providers.

## Health Verification
Inspect application diagnostics, engagement/conversation flows, messaging queue state and decision diagnostics.

## Normal State
Marketplace data store is reachable, registration is valid, engagement flows are fresh and provider queues are draining.

## Decision Semantics
Accepted, Declined, authorization denied, unsupported or other valid negative outcomes are business/security decisions. They are not automatically infrastructure failures.

## Common Warnings
Retrying external notifications, provider unconfigured, stale telemetry or slow dependency stages.

## Common Failures
Database unavailable, durable message queue unavailable/dead-letter growth, repeated provider failure or invalid secret-provider health.

## Recovery
Preserve engagement state and durable queues. Restore dependencies, then prove recovery with fresh diagnostic and correlated flow evidence.

## Backup / Restore
Back up the marketplace database and any durable messaging store. Validate correlation/idempotency after restore.

## Deployment / Rollback
Preserve registration identity and durable messaging state.

## Purge / Re-registration
Use explicit administrative processes. Do not purge identity as ordinary troubleshooting.

## Escalation
Escalate queue exhaustion, authentication/authorization anomalies, database restore issues or evidence of duplicate engagement delivery.

## Log Locations
Application/service logs and Operations correlation traces. Never log verification codes, session tokens, provider credentials, registration credentials or secret values.
