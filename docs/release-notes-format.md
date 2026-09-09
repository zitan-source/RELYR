# Release notes format

Every public GitHub Release must show English first and Japanese second. Keep the
markers exactly as written: RELYR uses them to show Japanese notes only when the
app language is Japanese and English notes for every other language.

```markdown
## English

<!-- RELYR-RELEASE-NOTES:en-US -->
Short English summary.

- English change one
- English change two
<!-- /RELYR-RELEASE-NOTES -->

## 日本語

<!-- RELYR-RELEASE-NOTES:ja-JP -->
日本語の短い概要。

- 日本語の変更点1
- 日本語の変更点2
<!-- /RELYR-RELEASE-NOTES -->
```

Both sections are required. Do not place download instructions or generated
asset lists inside either marked section unless they should also appear in the
in-app release-notes window.

## Mandatory publication gate

- Never publish Japanese-only notes. Both sections must describe the same changes; a generic English fallback message does not qualify.
- Run `tools/Verify-ReleaseNotes.ps1 -Path docs/releases/v<version>.md` before publishing and on the fetched GitHub body afterward. Review semantic translation parity manually.
- Applies to new releases, updates, and corrections. Installer UI localization is separate.
- Notes-only corrections do not require rebuilding, version bumps, or asset replacement. Already-cached updater notes may retain the old body.

For compatibility with older releases, an unmarked English body is displayed in
every language. An unmarked Japanese body is displayed only in Japanese; other
languages receive a short English notice instead of Japanese text.
