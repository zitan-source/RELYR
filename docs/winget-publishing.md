# WinGet publishing

RELYR uses Microsoft WingetCreate to generate and submit version updates for the
`ZITAN.RELYR` package. The automation starts only after a non-prerelease GitHub
Release is published.

## One-time repository setup

Do this only after the initial `ZITAN.RELYR` submission has been merged into
`microsoft/winget-pkgs`.

1. Create a GitHub personal access token (classic) for the `zitan-source`
   account.
2. Grant only the `public_repo` scope. The optional `delete_repo` scope is not
   required by this workflow.
3. In the RELYR repository, open **Settings > Secrets and variables > Actions**.
4. Add a repository secret named `WINGET_CREATE_GITHUB_TOKEN`.
5. Paste the token as the secret value. Never put it in source files, workflow
   inputs, issues, comments, or logs.

Fine-grained personal access tokens are not currently supported by WingetCreate.
The built-in `GITHUB_TOKEN` cannot submit a pull request to the external
`microsoft/winget-pkgs` repository, so a separate secret is necessary.

## Automatic operation

Publishing a release such as `v0.1.390` causes the workflow to:

1. Locate `RELYR-Setup-0.1.390.exe` in that GitHub Release.
2. Download the pinned Microsoft WingetCreate executable.
3. Verify WingetCreate's SHA-256 before executing it.
4. Generate a new `ZITAN.RELYR` manifest using the release URL.
5. Submit the update pull request to `microsoft/winget-pkgs`.
6. Retain the generated YAML files as a GitHub Actions artifact for 14 days.

Microsoft still validates and merges each update. Automatic submission does not
bypass their review.

## Manual preview or retry

Open **Actions > Submit WinGet update > Run workflow**.

- Enter a version without or with a leading `v`.
- Leave **submit** disabled to generate manifests without opening a pull request.
- Enable **submit** only to retry an update that has not already been submitted.

Do not submit the same version twice. Check existing pull requests first.

For a local preview after the initial package is available in WinGet:

```powershell
./tools/Publish-WinGetUpdate.ps1 -Version 0.1.390
```

Local preview never submits unless `-Submit` is explicitly supplied. Automated
submission reads the token only from the `WINGET_CREATE_GITHUB_TOKEN`
environment variable, as recommended by Microsoft.
