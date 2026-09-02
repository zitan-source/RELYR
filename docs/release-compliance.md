# Release compliance checklist

Complete this checklist before publishing a public RELYR release. It is an
engineering control, not a substitute for jurisdiction-specific legal advice.

- Describe availability as “worldwide where permitted by applicable law”; do
  not promise unrestricted availability in every country or region.
- Run `verify-third-party-notices.ps1` after NuGet restore. Every resolved
  package must be covered, Apache license copies must ship with the payload,
  and exact source revisions must remain documented for MPL components.
- Review `docs/privacy.html` whenever a network endpoint, telemetry behavior,
  hosted asset, browser storage key, download provider, or update mechanism
  changes.
- Confirm that release artifacts contain `LICENSE.txt`,
  `THIRD-PARTY-NOTICES.md`, and `licenses/HidSharp-LICENSE.txt`.
- Confirm the application still sends no keyboard or mouse input content,
  mappings, macros, settings, local file names, or unique device identifier
  during its GitHub update check.
- Record an export-control classification review for the released feature set
  and re-review it if encryption, security tooling, controlled hardware, or a
  restricted end-use is added.
- Keep mandatory consumer rights, intentional or gross misconduct, death or
  personal injury, and other non-excludable liability outside the contractual
  liability cap.
- Publish the Terms and privacy notice that match the installer, version their
  acceptance, and do not repeat consent for an unchanged Terms version during
  an ordinary update.
