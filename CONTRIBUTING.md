# Contributing to RELYR

[English](CONTRIBUTING.md) | [日本語](CONTRIBUTING.ja.md)

Thank you for helping RELYR. Bug reports, documentation improvements, translations, focused code changes, and reproducible test cases are welcome.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before making a change

1. Search existing issues and pull requests.
2. Open an issue before a large feature, installer change, or behavioral redesign.
3. Keep each pull request focused on one problem.
4. Never include personal settings, macros, credentials, build output, or installer artifacts in a pull request.

## Development setup

- Windows 10 or 11 x64
- .NET 10 SDK
- Inno Setup 6 only when working on installers

Build the application from the repository root:

```powershell
dotnet build .\RELYR\RELYR.csproj -c Release -warnaserror
```

Read the [architecture overview](docs/architecture.md) before changing component responsibilities. Before modifying input, Deck layout, profiles, startup, shutdown, or installers, read the [stability contract](docs/stability-contract.md) and follow its validation requirements.

Do not run input-engine tests in an actively used Windows session. Those tests can inject real input and must be run only in a dedicated, unused session.

## Pull-request checklist

- Explain the user-visible problem and the chosen solution.
- Add or update tests when behavior changes.
- Update English user-facing text first and keep localized text aligned.
- Run the relevant checks documented in the README and stability contract.
- Confirm that no unrelated files or generated output are included.
- Preserve existing license and third-party notices.

Submitting a contribution does not guarantee that it will be merged. Reviews and maintenance are performed on a best-effort basis.
