# Ebolito

Resurrection of the original **Ebolito Skills & Services** marketplace.

## UI themes

The application has one codebase and selectable visual themes rather than separate feature branches:

- **Modern** — a current interpretation of the approved Ebolito marketplace concept.
- **Legacy / Wayback** — preserves the visual character and wording of the original ESSJ-era site, including its original header artwork.

Theme switching is deliberately presentation-only. Domain behavior, routes, data and engagement workflows remain shared.

## Product direction

Ebolito helps customers discover skilled professionals, inspect profiles and work portfolios, build trust through reviews, and send a one-click engagement request. Engagement delivery is designed to support web, WhatsApp and SMS so professionals can participate even with intermittent Internet access.

## Structure

The repository uses the flat project layout used by the newer Aegis projects:

- `Ebolito.Web/`
- `Ebolito.Domain/`
- `Ebolito.Application/`
- `Ebolito.Infrastructure/`
- `Ebolito.Tests/`

The first resurrection commit focuses on the shared web shell and theme system. Common.*, Aegis.Configuration and Aegis.Diagnostics integrations should remain infrastructure concerns and must not be duplicated by theme.
