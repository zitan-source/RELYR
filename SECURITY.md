# Security policy

[English](SECURITY.md) | [日本語](SECURITY.ja.md)

## Supported versions

The latest public release is the only version considered for security fixes. Before reporting a problem, confirm the version on the [latest release page](https://github.com/zitan-source/RELYR/releases/latest).

## Report a vulnerability privately

Do not open a public issue for a suspected vulnerability or include private macros, credentials, or personal data in a report.

Use [GitHub's private vulnerability reporting form](https://github.com/zitan-source/RELYR/security/advisories/new). Include:

- The affected RELYR and Windows versions
- The shortest steps needed to reproduce the problem
- The expected security impact
- Relevant logs or screenshots with sensitive information removed

Please allow the maintainer time to reproduce and address the report before publishing details. Reports and fixes are handled on a best-effort basis. RELYR does not currently operate a paid bug-bounty program.

## Download integrity

Download RELYR only from the [official GitHub Releases page](https://github.com/zitan-source/RELYR/releases). Each installer has a matching SHA-256 file. The current public-beta installers are not code-signed, so Windows SmartScreen may identify the publisher as unknown.
