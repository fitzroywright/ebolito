# Registration truth audit

Scope: `common-registration-migration`.

- Ebolito registers only with Aegis.Configuration.
- The application does not register with Aegis.Diagnostics.
- Registration success is logged only after `ConfigurationRegistrar.RegisterContractAsync` returns `Succeeded = true` from a successful HTTP response.
- After a real successful registration, the startup retry loop stops. Ebolito does not continue re-registering on a timer.
- Missing registration key, missing contract, invalid contract, rejected HTTP response, timeout, network failure, and unexpected exception do not report registration success.
- The static configuration contract contains requirement metadata only. It contains no configuration values, default values, examples, resolved values, or display values.
- The standalone Common.Registration library recursively removes value-bearing fields and normalizes requirement-kind wire metadata at the transport boundary as defense in depth.
- Ebolito remains operational while Configuration is unavailable and retries registration; this is offline tolerance, not a fabricated registered state.
- `Ebolito.slnx` and CI consume `fitzroywright/Common.Registration` directly at `Common/Common.Registration`.
- No Operations, Configuration, or Diagnostics product code is changed by this migration.

Validation: the permanent Common.Registration repository is green. Ebolito previously completed a green downstream run against the standalone library; the one-time-registration correction is being validated by the current migration CI run.