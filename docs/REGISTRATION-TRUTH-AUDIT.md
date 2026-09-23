# Registration truth audit

Scope: `common-registration-migration`.

- Ebolito registers its durable identity with Aegis.Configuration and publishes registration lifecycle telemetry to Aegis.Operations.
- The application does not register directly with Aegis.Diagnostics; remote DiagnosticLevel capability remains disabled until implemented.
- Registration success is logged only after `ConfigurationRegistrar.RegisterContractAsync` returns `Succeeded = true` from a successful HTTP response.
- After a real successful registration, Ebolito continues the lifecycle by periodically re-authenticating its durable identity and republishing the sanitized Configuration contract.
- Missing registration key, missing contract, invalid contract, rejected HTTP response, timeout, network failure, and unexpected exception do not report registration success.
- The static configuration contract contains requirement metadata only. It contains no configuration values, default values, examples, resolved values, or display values.
- The standalone Common.Registration library recursively removes value-bearing fields and normalizes requirement-kind wire metadata at the transport boundary as defense in depth.
- Registration lifecycle events are authenticated to Aegis.Operations with the same durable registration credential, matching the Aegis.Hello reference pattern.
- Ebolito remains operational while Configuration is unavailable and retries registration; this is offline tolerance, not a fabricated registered state.
- `Ebolito.slnx` and CI consume `fitzroywright/Common.Registration` directly at `Common/Common.Registration`.
- No Operations, Configuration, or Diagnostics product code is changed by this migration.

Validation: the permanent Common.Registration repository is green. Ebolito previously completed a green downstream run against the standalone library; the one-time-registration correction is being validated by the current migration CI run.