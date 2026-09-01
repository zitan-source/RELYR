using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RELYR;

internal static class LocalizationService
{
    internal const string Japanese = "ja-JP";
    internal const string English = "en-US";
    internal const string ChineseSimplified = "zh-CN";
    internal const string ChineseTraditional = "zh-TW";
    internal const string Korean = "ko-KR";
    internal const string French = "fr-FR";
    internal const string German = "de-DE";
    internal const string Spanish = "es-ES";

    internal sealed record LanguageOption(string Code, string NativeName);
    internal static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new(Japanese, "日本語"),
        new(English, "English"),
        new(ChineseSimplified, "简体中文"),
        new(ChineseTraditional, "繁體中文"),
        new(Korean, "한국어"),
        new(French, "Français"),
        new(German, "Deutsch"),
        new(Spanish, "Español")
    ];

    sealed class ElementState
    {
        internal bool Updating;
        internal bool Tracked;
        internal readonly Dictionary<DependencyProperty, string> Sources = [];
        internal readonly Dictionary<DependencyProperty, string> LastApplied = [];
        internal Dictionary<DependencyProperty, EventHandler>? Handlers;
    }

    static readonly ConditionalWeakTable<DependencyObject, ElementState> ElementStates = new();
    static readonly List<WeakReference<DependencyObject>> TrackedElements = [];
    static readonly object TrackingLock = new();
    static bool initialized;
    static string currentLanguage = Japanese;
    static IReadOnlyDictionary<string, string>? currentText;
    const string RuntimePrefix = "\u0001runtime:";

    internal static event Action? LanguageChanged;

    internal static string CurrentLanguage => currentLanguage;
    internal static bool IsEnglish => currentLanguage == English;
    internal static bool IsJapanese => currentLanguage == Japanese;
    internal static int CurrentCatalogCountForTest => currentText?.Count ?? 0;
    internal static bool CurrentCatalogContainsForTest(string key) => currentText?.ContainsKey(key) == true;
    internal static int TrackedReferenceCountForTest
    {
        get { lock (TrackingLock) return TrackedElements.Count; }
    }

    internal static string Normalize(string? language)
    {
        string value = (language?.Trim() ?? string.Empty).Replace('_', '-');
        if (value.Equals("english", StringComparison.OrdinalIgnoreCase) || value.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return English;
        if (value.Equals("日本語", StringComparison.OrdinalIgnoreCase) || value.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return Japanese;
        if (value.Equals("繁體中文", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase))
            return ChineseTraditional;
        if (value.Equals("简体中文", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return ChineseSimplified;
        if (value.Equals("한국어", StringComparison.OrdinalIgnoreCase) || value.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return Korean;
        if (value.Equals("français", StringComparison.OrdinalIgnoreCase) || value.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
            return French;
        if (value.Equals("deutsch", StringComparison.OrdinalIgnoreCase) || value.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            return German;
        if (value.Equals("español", StringComparison.OrdinalIgnoreCase) || value.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            return Spanish;
        return Japanese;
    }

    internal static void Initialize()
    {
        if (initialized)
            return;
        initialized = true;
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ElementLoaded));
        // A Window's Loaded notification does not guarantee that every child
        // is observed by the generic class handler. Localize the complete tree
        // once after it has been composed so secondary windows cannot end up
        // with only their title translated.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WindowLoaded));
        EventManager.RegisterClassHandler(
            typeof(FrameworkContentElement),
            FrameworkContentElement.LoadedEvent,
            new RoutedEventHandler(ElementLoaded));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(ElementUnloaded));
        EventManager.RegisterClassHandler(
            typeof(FrameworkContentElement),
            FrameworkContentElement.UnloadedEvent,
            new RoutedEventHandler(ElementUnloaded));
    }

    internal static void Apply(string? language)
    {
        Initialize();
        string normalized = Normalize(language);
        bool changed = !currentLanguage.Equals(normalized, StringComparison.Ordinal);
        IReadOnlyDictionary<string, string>? languageText = changed ? LoadLanguageText(normalized) : currentText;
        currentLanguage = normalized;
        currentText = languageText;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(normalized);

        List<DependencyObject> elements = [];
        lock (TrackingLock)
        {
            for (int i = TrackedElements.Count - 1; i >= 0; i--)
            {
                if (TrackedElements[i].TryGetTarget(out DependencyObject? element)
                    && element is FrameworkElement { IsLoaded: true } or FrameworkContentElement { IsLoaded: true })
                    elements.Add(element);
                else
                    TrackedElements.RemoveAt(i);
            }
        }
        foreach (DependencyObject element in elements)
            LocalizeElement(element, refreshFromSource: true);
        if (System.Windows.Application.Current != null)
            foreach (Window window in System.Windows.Application.Current.Windows)
                LocalizeTree(window);

        if (changed)
            LanguageChanged?.Invoke();
    }

    internal static string Text(string? source)
    {
        string value = source ?? string.Empty;
        if (IsJapanese || value.Length == 0)
            return value;
        if (currentText != null && currentText.TryGetValue(value, out string? translated))
            return translated;
        string runtimeTranslation = TranslateRuntimeText(value);
        if (!runtimeTranslation.Equals(value, StringComparison.Ordinal))
            return runtimeTranslation;
        // A newly introduced UI string must never expose the Japanese source
        // in another language. Locale catalogs still receive native entries,
        // while English is the safe final fallback for a missing key.
        if (!IsEnglish && LocalizationEnglish.Text.TryGetValue(value, out string? englishFallback))
            return englishFallback;
        return value;
    }

    internal static string DisplayGeneratedName(string? source)
    {
        string value = source ?? string.Empty;
        if (IsJapanese)
            return value;
        return value switch
        {
            "標準" => Text("標準"),
            "標準Deck" or "標準 Deck" => Text("標準Deck"),
            "標準プロファイル" => Text("標準プロファイル"),
            "新しいDeck" => Text("新しいDeck"),
            "新しいジェスチャー" => Text("新しいジェスチャー"),
            "新しいマクロ" => Text("新しいマクロ"),
            _ => value
        };
    }

    static string Runtime(string englishTemplate, params object[] values)
    {
        string template = englishTemplate;
        if (!IsEnglish && currentText != null
            && currentText.TryGetValue(RuntimePrefix + englishTemplate, out string? translated))
            template = translated;
        return string.Format(CultureInfo.CurrentUICulture, template, values);
    }

    static IReadOnlyDictionary<string, string>? LoadLanguageText(string language)
    {
        if (language == Japanese)
            return null;
        if (language == English)
            return LocalizationEnglish.Text;

        string suffix = $".Localization.{language}.json";
        Assembly assembly = typeof(LocalizationService).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Missing localization resource: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Invalid localization resource: {language}");
    }

    internal static void LocalizeTree(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        LocalizeTreeCore(root, visited);
    }

    static void LocalizeTreeCore(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element))
            return;
        LocalizeElement(element, refreshFromSource: true);
        if (element is FrameworkElement or FrameworkContentElement)
            foreach (object child in LogicalTreeHelper.GetChildren(element))
                if (child is DependencyObject dependencyChild)
                    LocalizeTreeCore(dependencyChild, visited);
        if (element is ItemsControl itemsControl)
            foreach (object item in itemsControl.Items)
                if (item is DependencyObject dependencyItem)
                    LocalizeTreeCore(dependencyItem, visited);
        if (element is Visual or System.Windows.Media.Media3D.Visual3D)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                LocalizeTreeCore(VisualTreeHelper.GetChild(element, i), visited);
    }

    static string TranslateRuntimeText(string value)
    {
        if (value.StartsWith("ⓘ ", StringComparison.Ordinal))
            return "ⓘ " + Text(value[2..]);
        if (value.StartsWith("⚠ ", StringComparison.Ordinal))
            return "⚠ " + Text(value[2..]);
        Match match = Regex.Match(value, @"^(\d+)手順のマクロを実行します$");
        if (match.Success)
            return Runtime("Runs a {0}-step macro", match.Groups[1].Value);
        match = Regex.Match(value, @"^(.*)からアプリを起動します$");
        if (match.Success)
            return Runtime("Launches the app from {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^(\d+)×(\d+)のDeckを表示します$");
        if (match.Success)
            return Runtime("Shows a {0} × {1} Deck", match.Groups[1].Value, match.Groups[2].Value);
        match = Regex.Match(value, @"^(\d+)件のAction$");
        if (match.Success)
            return Runtime("{0} actions", match.Groups[1].Value);
        match = Regex.Match(value, @"^デスクトップ (\d+)$");
        if (match.Success)
            return Runtime("Desktop {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^RELYR v(.+) — デスクトップ (\d+)$");
        if (match.Success)
            return Runtime("RELYR v{0} — Desktop {1}", match.Groups[1].Value, match.Groups[2].Value);
        match = Regex.Match(value, @"^新しいバージョン v(.+) を利用できます$");
        if (match.Success)
            return Runtime("Version {0} is available", match.Groups[1].Value);
        match = Regex.Match(value, @"^最新バージョンです（v(.+)）$");
        if (match.Success)
            return Runtime("You are up to date (v{0})", match.Groups[1].Value);
        match = Regex.Match(value, @"^(.+) をダウンロード済み$");
        if (match.Success)
            return Runtime("{0} downloaded", match.Groups[1].Value);
        match = Regex.Match(value, @"^(\d+)件$");
        if (match.Success)
            return Runtime("{0} items", match.Groups[1].Value);
        match = Regex.Match(value, @"^キーボード（(.+)配列）とマウス$");
        if (match.Success)
            return Runtime("Keyboard ({0} layout) and mouse", match.Groups[1].Value);
        match = Regex.Match(value, @"^直前に押したキー：(.+)$");
        if (match.Success)
            return Runtime("Last key pressed: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^現在の入力：(.+)$");
        if (match.Success)
            return Runtime("Current input: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^入力: (.+)$");
        if (match.Success)
            return Runtime("Input: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^検出: (.+)$");
        if (match.Success)
            return Runtime("Detected: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^割り当て先: (.+)$");
        if (match.Success)
            return Runtime("Assignment target: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^選択中: (.+)$");
        if (match.Success)
            return Runtime("Selected: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^(\d+)個のレイアウト$");
        if (match.Success)
            return Runtime("{0} layouts", match.Groups[1].Value);
        match = Regex.Match(value, @"^(\d+)×(\d+)・(\d+)ボタン(?:\s*・\s*(既定))?$");
        if (match.Success)
            return match.Groups[4].Success
                ? Runtime("{0} × {1} · {2} buttons · Default", match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value)
                : Runtime("{0} × {1} · {2} buttons", match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        match = Regex.Match(value, @"^列数 (\d+)・行数 (\d+)・(\d+)ボタン$");
        if (match.Success)
            return Runtime("{0} columns · {1} rows · {2} buttons", match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        match = Regex.Match(value, @"^(\d+)個の入力へドラッグ$");
        if (match.Success)
            return Runtime("Drag to {0} inputs", match.Groups[1].Value);
        match = Regex.Match(value, @"^HOLDの判定を(.+)に変更しました$");
        if (match.Success)
            return Runtime("HOLD detection changed to {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^(\d+)件の手順をコピーしました。$");
        if (match.Success)
            return Runtime("Copied {0} steps.", match.Groups[1].Value);
        match = Regex.Match(value, @"^(\d+)件の手順を選択中$");
        if (match.Success)
            return Runtime("{0} steps selected", match.Groups[1].Value);
        match = Regex.Match(value, @"^(\d+) 手順・待機合計 (\d+) ms$");
        if (match.Success)
            return Runtime("{0} steps · {1} ms total delay", match.Groups[1].Value, match.Groups[2].Value);
        match = Regex.Match(value, @"^新しいバージョンが利用可能です（v(.+)）$");
        if (match.Success)
            return Runtime("A new version is available (v{0})", match.Groups[1].Value);
        match = Regex.Match(value, @"^現在のバージョンは v(.+) です。［アップデートを確認］から手動で確認できます。$");
        if (match.Success)
            return Runtime("Current version: v{0}. Use Check for Updates to check manually.", match.Groups[1].Value);
        match = Regex.Match(value, @"^RELYR v(.+) の変更内容$");
        if (match.Success)
            return Runtime("What's new in RELYR v{0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^(.+)を押したまま、組み合わせるキーを押してください$");
        if (match.Success)
            return Runtime("Hold {0} and press the key to combine", match.Groups[1].Value);
        match = Regex.Match(value, @"^実行: (.+)$");
        if (match.Success)
            return Runtime("Running: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^(.+) の TAP / HOLDへドラッグ$");
        if (match.Success)
            return Runtime("Drag to {0}'s TAP / HOLD slot", match.Groups[1].Value);
        match = Regex.Match(value, @"^(.+)に割り当てました$");
        if (match.Success)
            return Runtime("Assigned to {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^(.+)を編集$");
        if (match.Success)
            return Runtime("Edit {0}", DisplayGeneratedName(match.Groups[1].Value));
        match = Regex.Match(value, @"^(.+)（未保存）$");
        if (match.Success)
            return Runtime("{0} (unsaved)", Text(match.Groups[1].Value));
        match = Regex.Match(value, @"^待機 (\d+) ms$");
        if (match.Success)
            return Runtime("Wait {0} ms", match.Groups[1].Value);
        match = Regex.Match(value, @"^文字列を入力「(.+)」$");
        if (match.Success)
            return Runtime("Type text: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^開く: (.+)$");
        if (match.Success)
            return Runtime("Open: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^マクロを実行: (.+)$");
        if (match.Success)
            return Runtime("Run macro: {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^プロファイル切替: (.+)$");
        if (match.Success)
            return Runtime("Switch profile: {0}", DisplayGeneratedName(match.Groups[1].Value));
        match = Regex.Match(value, @"^(.+) を押す$");
        if (match.Success)
            return Runtime("Press {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^(.+) を離す$");
        if (match.Success)
            return Runtime("Release {0}", match.Groups[1].Value);
        match = Regex.Match(value, @"^割り当て (\d+)件 / 対象アプリ (\d+)件$");
        if (match.Success)
            return Runtime("{0} assignments / {1} target apps", match.Groups[1].Value, match.Groups[2].Value);
        match = Regex.Match(value, @"^(\d+)分$");
        if (match.Success)
            return Runtime("{0} min", match.Groups[1].Value);
        match = Regex.Match(value, @"^RELYR Deck - (.+)$");
        if (match.Success)
            return Runtime("RELYR Deck - {0}", DisplayGeneratedName(match.Groups[1].Value));
        match = Regex.Match(value, @"^Edit (.+)$");
        if (match.Success)
            return Runtime("Edit {0}", DisplayGeneratedName(match.Groups[1].Value));
        if (Regex.IsMatch(value, @"^＋\s+新しいプロファイル$"))
            return "+  New Profile";
        return value;
    }

    static void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            LocalizeTree(window);
    }

    static void ElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element)
            LocalizeElement(element, refreshFromSource: false);
    }

    static void LocalizeElement(DependencyObject element, bool refreshFromSource)
    {
        if (element is TextBlock)
            TrackProperty(element, TextBlock.TextProperty, refreshFromSource);
        if (element is AccessText)
            TrackProperty(element, AccessText.TextProperty, refreshFromSource);
        if (element is Window)
            TrackProperty(element, Window.TitleProperty, refreshFromSource);
        if (element is ContentControl)
            TrackProperty(element, ContentControl.ContentProperty, refreshFromSource);
        if (element is HeaderedContentControl)
            TrackProperty(element, HeaderedContentControl.HeaderProperty, refreshFromSource);
        if (element is FrameworkElement)
            TrackProperty(element, FrameworkElement.ToolTipProperty, refreshFromSource);
    }

    static void TrackProperty(DependencyObject element, DependencyProperty property, bool refreshFromSource)
    {
        if (BindingOperations.IsDataBound(element, property) || element.GetValue(property) is not string current)
            return;
        if ((property == ContentControl.ContentProperty || property == HeaderedContentControl.HeaderProperty)
            && element is FrameworkElement { DataContext: string dataItem }
            && dataItem.Equals(current, StringComparison.Ordinal))
            return;

        ElementState state = ElementStates.GetOrCreateValue(element);
        TrackElement(element, state);
        if (ShouldObserveChanges(element) && !(state.Handlers?.ContainsKey(property) ?? false))
        {
            EventHandler handler = (_, _) => PropertyChanged(element, property);
            DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
            if (descriptor != null)
            {
                descriptor.AddValueChanged(element, handler);
                (state.Handlers ??= [])[property] = handler;
            }
        }

        if (!state.Sources.ContainsKey(property))
            state.Sources[property] = current;
        else if (refreshFromSource && state.LastApplied.TryGetValue(property, out string? lastApplied)
            && !current.Equals(lastApplied, StringComparison.Ordinal))
            state.Sources[property] = current;
        ApplyProperty(element, property, state, state.Sources[property]);
    }

    static bool ShouldObserveChanges(DependencyObject element) => true;

    static void TrackElement(DependencyObject element, ElementState state)
    {
        if (state.Tracked)
            return;
        state.Tracked = true;
        lock (TrackingLock)
        {
            if (TrackedElements.Count >= 256)
                TrackedElements.RemoveAll(reference => !reference.TryGetTarget(out _));
            TrackedElements.Add(new WeakReference<DependencyObject>(element));
        }
    }

    static void ElementUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject element
            || !ElementStates.TryGetValue(element, out ElementState? state))
            return;
        if (state.Handlers != null)
        {
            foreach (var (property, handler) in state.Handlers)
                DependencyPropertyDescriptor.FromProperty(property, element.GetType())?.RemoveValueChanged(element, handler);
            state.Handlers.Clear();
            state.Handlers = null;
        }
        state.Tracked = false;
        lock (TrackingLock)
            TrackedElements.RemoveAll(reference => !reference.TryGetTarget(out DependencyObject? target) || ReferenceEquals(target, element));
    }

    static void PropertyChanged(DependencyObject element, DependencyProperty property)
    {
        ElementState state = ElementStates.GetOrCreateValue(element);
        if (state.Updating || BindingOperations.IsDataBound(element, property) || element.GetValue(property) is not string current)
            return;
        state.Sources[property] = current;
        ApplyProperty(element, property, state, current);
    }

    static void ApplyProperty(DependencyObject element, DependencyProperty property, ElementState state, string source)
    {
        string translated = Text(source);
        if (Equals(element.GetValue(property), translated))
        {
            state.LastApplied[property] = translated;
            return;
        }
        state.Updating = true;
        try
        {
            element.SetCurrentValue(property, translated);
            state.LastApplied[property] = translated;
        }
        finally
        {
            state.Updating = false;
        }
    }
}

public sealed class LocalizedGeneratedNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => LocalizationService.DisplayGeneratedName(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class LocalizedMacroStepCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value is int number ? number : 0;
        return LocalizationService.IsJapanese ? $"{count} 手順" : $"{count} steps";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
