using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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

    sealed class ElementState
    {
        internal bool Updating;
        internal readonly Dictionary<DependencyProperty, string> Sources = [];
        internal readonly Dictionary<DependencyProperty, EventHandler> Handlers = [];
    }

    static readonly ConditionalWeakTable<DependencyObject, ElementState> ElementStates = new();
    static readonly List<WeakReference<DependencyObject>> TrackedElements = [];
    static readonly object TrackingLock = new();
    static readonly IReadOnlyDictionary<string, string> EnglishText = LocalizationEnglish.Text;
    static bool initialized;
    static string currentLanguage = Japanese;

    internal static event Action? LanguageChanged;

    internal static string CurrentLanguage => currentLanguage;
    internal static bool IsEnglish => currentLanguage == English;

    internal static string Normalize(string? language)
    {
        string value = language?.Trim() ?? string.Empty;
        return value.Equals(English, StringComparison.OrdinalIgnoreCase)
            || value.Equals("en", StringComparison.OrdinalIgnoreCase)
            || value.Equals("english", StringComparison.OrdinalIgnoreCase)
            ? English
            : Japanese;
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
    }

    internal static void Apply(string? language)
    {
        Initialize();
        string normalized = Normalize(language);
        bool changed = !currentLanguage.Equals(normalized, StringComparison.Ordinal);
        currentLanguage = normalized;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(normalized);

        List<DependencyObject> elements = [];
        lock (TrackingLock)
        {
            for (int i = TrackedElements.Count - 1; i >= 0; i--)
            {
                if (TrackedElements[i].TryGetTarget(out DependencyObject? element))
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
        if (!IsEnglish || value.Length == 0)
            return value;
        if (EnglishText.TryGetValue(value, out string? translated))
            return translated;
        return TranslateRuntimeText(value);
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
        if (element is Visual or System.Windows.Media.Media3D.Visual3D)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                LocalizeTreeCore(VisualTreeHelper.GetChild(element, i), visited);
    }

    static string TranslateRuntimeText(string value)
    {
        Match match = Regex.Match(value, @"^(\d+)手順のマクロを実行します$");
        if (match.Success)
            return $"Runs a {match.Groups[1].Value}-step macro";
        match = Regex.Match(value, @"^(.*)からアプリを起動します$");
        if (match.Success)
            return $"Launches the app from {match.Groups[1].Value}";
        match = Regex.Match(value, @"^(\d+)×(\d+)のDeckを表示します$");
        if (match.Success)
            return $"Shows a {match.Groups[1].Value} × {match.Groups[2].Value} Deck";
        match = Regex.Match(value, @"^(\d+)件のAction$");
        if (match.Success)
            return $"{match.Groups[1].Value} actions";
        match = Regex.Match(value, @"^デスクトップ (\d+)$");
        if (match.Success)
            return $"Desktop {match.Groups[1].Value}";
        match = Regex.Match(value, @"^RELYR v(.+) — デスクトップ (\d+)$");
        if (match.Success)
            return $"RELYR v{match.Groups[1].Value} — Desktop {match.Groups[2].Value}";
        match = Regex.Match(value, @"^新しいバージョン v(.+) を利用できます$");
        if (match.Success)
            return $"Version {match.Groups[1].Value} is available";
        match = Regex.Match(value, @"^最新バージョンです（v(.+)）$");
        if (match.Success)
            return $"You are up to date (v{match.Groups[1].Value})";
        match = Regex.Match(value, @"^(.+) をダウンロード済み$");
        if (match.Success)
            return $"{match.Groups[1].Value} downloaded";
        match = Regex.Match(value, @"^(\d+)件$");
        if (match.Success)
            return $"{match.Groups[1].Value} items";
        match = Regex.Match(value, @"^キーボード（(.+)配列）とマウス$");
        if (match.Success)
            return $"Keyboard ({match.Groups[1].Value} layout) and mouse";
        match = Regex.Match(value, @"^直前に押したキー：(.+)$");
        if (match.Success)
            return $"Last key pressed: {match.Groups[1].Value}";
        match = Regex.Match(value, @"^現在の入力：(.+)$");
        if (match.Success)
            return $"Current input: {match.Groups[1].Value}";
        match = Regex.Match(value, @"^入力: (.+)$");
        if (match.Success)
            return $"Input: {match.Groups[1].Value}";
        match = Regex.Match(value, @"^検出: (.+)$");
        if (match.Success)
            return $"Detected: {match.Groups[1].Value}";
        match = Regex.Match(value, @"^割り当て先: (.+)$");
        if (match.Success)
            return $"Assignment target: {match.Groups[1].Value}";
        match = Regex.Match(value, @"^選択中: (.+)$");
        if (match.Success)
            return $"Selected: {match.Groups[1].Value}";
        match = Regex.Match(value, @"^(\d+)個のレイアウト$");
        if (match.Success)
            return $"{match.Groups[1].Value} layouts";
        match = Regex.Match(value, @"^列数 (\d+)・行数 (\d+)・(\d+)ボタン$");
        if (match.Success)
            return $"{match.Groups[1].Value} columns · {match.Groups[2].Value} rows · {match.Groups[3].Value} buttons";
        match = Regex.Match(value, @"^(\d+)個の入力へドラッグ$");
        if (match.Success)
            return $"Drag to {match.Groups[1].Value} inputs";
        match = Regex.Match(value, @"^HOLDの判定を(.+)に変更しました$");
        if (match.Success)
            return $"HOLD detection changed to {match.Groups[1].Value}";
        match = Regex.Match(value, @"^(\d+)件の手順をコピーしました。$");
        if (match.Success)
            return $"Copied {match.Groups[1].Value} steps.";
        match = Regex.Match(value, @"^(\d+)件の手順を選択中$");
        if (match.Success)
            return $"{match.Groups[1].Value} steps selected";
        match = Regex.Match(value, @"^(\d+) 手順・待機合計 (\d+) ms$");
        if (match.Success)
            return $"{match.Groups[1].Value} steps · {match.Groups[2].Value} ms total delay";
        match = Regex.Match(value, @"^新しいバージョンが利用可能です（v(.+)）$");
        if (match.Success)
            return $"A new version is available (v{match.Groups[1].Value})";
        match = Regex.Match(value, @"^現在のバージョンは v(.+) です。［アップデートを確認］から手動で確認できます。$");
        if (match.Success)
            return $"Current version: v{match.Groups[1].Value}. Use Check for Updates to check manually.";
        match = Regex.Match(value, @"^RELYR v(.+) の変更内容$");
        if (match.Success)
            return $"What's new in RELYR v{match.Groups[1].Value}";
        match = Regex.Match(value, @"^(.+)を押したまま、組み合わせるキーを押してください$");
        if (match.Success)
            return $"Hold {match.Groups[1].Value} and press the key to combine";
        return value;
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
        if (!state.Handlers.ContainsKey(property))
        {
            EventHandler handler = (_, _) => PropertyChanged(element, property);
            DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
            if (descriptor != null)
            {
                descriptor.AddValueChanged(element, handler);
                state.Handlers[property] = handler;
                lock (TrackingLock)
                    TrackedElements.Add(new WeakReference<DependencyObject>(element));
            }
        }

        if (!state.Sources.ContainsKey(property))
            state.Sources[property] = current;
        ApplyProperty(element, property, state, refreshFromSource ? state.Sources[property] : current);
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
            return;
        state.Updating = true;
        try
        {
            element.SetCurrentValue(property, translated);
        }
        finally
        {
            state.Updating = false;
        }
    }
}
