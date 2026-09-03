using System.Text.RegularExpressions;

namespace RELYR;

internal static partial class ReleaseNotesLocalization
{
    internal const string EnglishUnavailable =
        "English release notes are not available for this release. See the GitHub Release page for details.";

    internal static string SelectForCurrentLanguage(string? releaseBody) =>
        Select(releaseBody, LocalizationService.CurrentLanguage);

    internal static string Select(string? releaseBody, string? language)
    {
        string body = releaseBody?.Trim() ?? string.Empty;
        if (body.Length == 0)
            return LocalizationService.Normalize(language) == LocalizationService.Japanese
                ? "このリリースには更新内容が記載されていません。"
                : EnglishUnavailable;

        var sections = ParseSections(body);
        string preferredLanguage = LocalizationService.Normalize(language) == LocalizationService.Japanese
            ? LocalizationService.Japanese
            : LocalizationService.English;
        if (sections.TryGetValue(preferredLanguage, out string? preferred) && preferred.Length > 0)
            return preferred;
        if (sections.TryGetValue(LocalizationService.English, out string? english) && english.Length > 0)
            return english;
        if (sections.Count > 0)
            return preferredLanguage == LocalizationService.Japanese
                ? sections.Values.First(value => value.Length > 0)
                : EnglishUnavailable;

        // Legacy releases used one unmarked body. Keep Japanese notes for Japanese
        // users, but never expose a Japanese-only legacy body to overseas users.
        if (preferredLanguage != LocalizationService.Japanese && JapaneseTextRegex().IsMatch(body))
            return EnglishUnavailable;
        return body;
    }

    internal static IReadOnlyDictionary<string, string> ParseSections(string releaseBody)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in SectionRegex().Matches(releaseBody ?? string.Empty))
        {
            string language = LocalizationService.Normalize(match.Groups["language"].Value);
            if (language is not (LocalizationService.Japanese or LocalizationService.English))
                continue;
            string content = match.Groups["content"].Value.Trim();
            if (content.Length > 0)
                sections[language] = content;
        }
        return sections;
    }

    [GeneratedRegex(@"<!--\s*RELYR-RELEASE-NOTES:(?<language>ja-JP|en-US)\s*-->(?<content>.*?)<!--\s*/RELYR-RELEASE-NOTES\s*-->", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]", RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseTextRegex();
}
