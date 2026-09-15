# Registration truth audit

Scope: `common-registration-migration`.

- Ebolito registers only with Aegis.Configuration.
- The application does not register with Aegis.Diagnostics.
- Registration success is logged only after `ConfigurationRegistrar.RegisterContractAsync` returns `Succeeded = true` from a successful HTTP response.
- Missing registration key, missing contract, invalid contract, rejected HTTP response, timeout, network failure, and unexpected exception do not report registration success.
- The static configuration contract contains requirement metadata only. It contains no configuration values, default values, examples, resolved values, or display values.
- Common.Registration applies the metadata-only policy again at the transport boundary as defense in depth.
- Ebolito remains operational while Configuration is unavailable and retries registration; this is offline tolerance, not a fabricated registered state.
- No Operations, Configuration, or Diagnostics product code is changed by this migration.

This audit is intentionally committed on the migration branch so CI is rerun against the current Common.Registration staging branch before the migration can be considered proven.
