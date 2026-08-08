using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Button = System.Windows.Controls.Button;

namespace RELYR;

public partial class MacroInputPickerWindow : Window
{
    const double Gap = 4;
    readonly List<Button> inputButtons = [];
    readonly string layout;
    bool shortcutEditingMode;
    bool syncingShortcutEditor;
    public event Action<string>? InputChosen;
    public event Action<string>? ShortcutChanged;
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }
    internal IReadOnlyList<Button> InputButtonsForTest => inputButtons;
    internal bool IsShortcutEditingModeForTest => shortcutEditingMode;

    public MacroInputPickerWindow(string keyboardLayout)
    {
        InitializeComponent();
        layout = keyboardLayout.Equals("US", StringComparison.OrdinalIgnoreCase) ? "US" : "JIS";
        KeyboardHeading.Text = $"キーボード（{layout}配列）とマウス";
        BuildInputSurface();
        MainWindow.FollowWindowsTitleBarTheme(this, value => TitleBarUsesDarkMode = value);
    }

    void BuildInputSurface()
    {
        InputCanvas.Children.Clear();
        inputButtons.Clear();
        InputCanvas.Width = layout == "US" ? 900 : 942;
        if (layout == "US")
            BuildUsKeyboard();
        else
            BuildJisKeyboard();
        AddExtendedFunctionKeys();
        BuildLowerGroups();
    }

    void BuildJisKeyboard()
    {
        AddTopFunctionRow(942);
        AddRow(44, [new("半角/全角", "半角/全角", 88), new("1", "1", 54), new("2", "2", 54), new("3", "3", 54), new("4", "4", 54), new("5", "5", 54), new("6", "6", 54), new("7", "7", 54), new("8", "8", 54), new("9", "9", 54), new("0", "0", 54), new("-", "-", 54), new("^", "^", 54), new("¥", "¥", 54), new("Back", "Backspace", 96)]);
        AddRow(100, [new("Tab", "Tab", 82), new("Q", "Q", 54), new("W", "W", 54), new("E", "E", 54), new("R", "R", 54), new("T", "T", 54), new("Y", "Y", 54), new("U", "U", 54), new("I", "I", 54), new("O", "O", 54), new("P", "P", 54), new("@", "@", 54), new("[", "[", 54)]);
        AddRow(156, [new("CapsLock", "CapsLock", 104), new("A", "A", 54), new("S", "S", 54), new("D", "D", 54), new("F", "F", 54), new("G", "G", 54), new("H", "H", 54), new("J", "J", 54), new("K", "K", 54), new("L", "L", 54), new(";", ";", 54), new(":", ":", 54), new("]", "]", 54)]);
        AddJisEnter();
        AddRow(212, [new("LeftShift", "Shift", 126), new("Z", "Z", 54), new("X", "X", 54), new("C", "C", 54), new("V", "V", 54), new("B", "B", 54), new("N", "N", 54), new("M", "M", 54), new(",", ",", 54), new(".", ".", 54), new("/", "/", 54), new("_", "＼  _", 54), new("RightShift", "Shift", 174)]);
        AddRow(268, [new("LeftCtrl", "Ctrl", 78), new("LWin", "Win", 64), new("LeftAlt", "Alt", 68), new("無変換", "無変換", 76), new("Space", "Space", 248), new("変換", "変換", 72), new("カタカナ", "カタカナ", 78), new("RightAlt", "Alt", 68), new("RWin", "Win", 64), new("RightCtrl", "Ctrl", 90)]);
    }

    void BuildUsKeyboard()
    {
        AddTopFunctionRow(900);
        AddRow(44, [new("`", "`", 56), new("1", "1", 56), new("2", "2", 56), new("3", "3", 56), new("4", "4", 56), new("5", "5", 56), new("6", "6", 56), new("7", "7", 56), new("8", "8", 56), new("9", "9", 56), new("0", "0", 56), new("-", "-", 56), new("=", "=", 56), new("Back", "Backspace", 120)]);
        AddRow(100, [new("Tab", "Tab", 88), new("Q", "Q", 56), new("W", "W", 56), new("E", "E", 56), new("R", "R", 56), new("T", "T", 56), new("Y", "Y", 56), new("U", "U", 56), new("I", "I", 56), new("O", "O", 56), new("P", "P", 56), new("[", "[", 56), new("]", "]", 56), new("\\", "＼", 88)]);
        AddRow(156, [new("CapsLock", "CapsLock", 102), new("A", "A", 56), new("S", "S", 56), new("D", "D", 56), new("F", "F", 56), new("G", "G", 56), new("H", "H", 56), new("J", "J", 56), new("K", "K", 56), new("L", "L", 56), new(";", ";", 56), new("'", "'", 56), new("Enter", "Enter", 134)]);
        AddRow(212, [new("LeftShift", "Shift", 136), new("Z", "Z", 56), new("X", "X", 56), new("C", "C", 56), new("V", "V", 56), new("B", "B", 56), new("N", "N", 56), new("M", "M", 56), new(",", ",", 56), new(".", ".", 56), new("/", "/", 56), new("RightShift", "Shift", 160)]);
        AddRow(268, [new("LeftCtrl", "Ctrl", 72), new("LWin", "Win", 72), new("LeftAlt", "Alt", 72), new("Space", "Space", 368), new("RightAlt", "Alt", 72), new("RWin", "Win", 72), new("Menu", "Menu", 72), new("RightCtrl", "Ctrl", 72)]);
    }

    void AddTopFunctionRow(double rightEdge)
    {
        const int count = 14;
        double width = (rightEdge - Gap * (count - 1)) / count, x = 0;
        AddButton("Esc", "Esc", x, 0, width, 26);
        x += width + Gap;
        for (int i = 1; i <= 12; i++)
        {
            AddButton($"F{i}", $"F{i}", x, 0, width, 26);
            x += width + Gap;
        }
        AddButton("Delete", "Delete", x, 0, width, 26);
    }

    void AddExtendedFunctionKeys()
    {
        double rightEdge = layout == "US" ? 900 : 942, width = (rightEdge - Gap * 11) / 12, x = 0;
        for (int i = 13; i <= 24; i++)
        {
            AddButton($"F{i}", $"F{i}", x, 338, width, 26);
            x += width + Gap;
        }
    }

    void BuildLowerGroups()
    {
        const double top = 390, unit = 54, keyHeight = 52, padding = 10, titleHeight = 26, groupGap = 12;
        double navigationWidth = padding * 2 + unit * 3 + Gap * 2;
        double navigationHeight = titleHeight + keyHeight * 3 + Gap * 2 + padding;
        double numpadX = navigationWidth + groupGap;
        double numpadWidth = padding * 2 + unit * 4 + Gap * 3;
        double numpadHeight = titleHeight + keyHeight * 5 + Gap * 4 + padding;
        double cursorX = numpadX + numpadWidth + groupGap;
        double cursorWidth = navigationWidth;
        double cursorHeight = titleHeight + keyHeight * 2 + Gap + padding;
        double mouseX = cursorX + cursorWidth + groupGap;
        double mouseWidth = InputCanvas.Width - mouseX;

        AddFrame("ナビゲーション", 0, top, navigationWidth, navigationHeight);
        AddFrame("テンキー", numpadX, top, numpadWidth, numpadHeight);
        AddFrame("カーソルキー", cursorX, top, cursorWidth, cursorHeight);
        AddFrame("マウス", mouseX, top, mouseWidth, numpadHeight);

        double navLeft = padding, firstY = top + titleHeight, step = unit + Gap;
        AddButton("Insert", "Insert", navLeft, firstY, unit, keyHeight);
        AddButton("Home", "Home", navLeft + step, firstY, unit, keyHeight);
        AddButton("PageUp", "Page\nUp", navLeft + step * 2, firstY, unit, keyHeight);
        AddButton("Delete", "Delete", navLeft, firstY + 56, unit, keyHeight);
        AddButton("End", "End", navLeft + step, firstY + 56, unit, keyHeight);
        AddButton("PageDown", "Page\nDown", navLeft + step * 2, firstY + 56, unit, keyHeight);
        AddButton("PrintScreen", "Print", navLeft, firstY + 112, unit, keyHeight);
        AddButton("ScrollLock", "Scroll", navLeft + step, firstY + 112, unit, keyHeight);
        AddButton("Pause", "Pause", navLeft + step * 2, firstY + 112, unit, keyHeight);

        double numLeft = numpadX + padding;
        AddButton("NumLock", "Num", numLeft, firstY, unit, keyHeight);
        AddButton("Divide", "÷", numLeft + step, firstY, unit, keyHeight);
        AddButton("Multiply", "×", numLeft + step * 2, firstY, unit, keyHeight);
        AddButton("Subtract", "−", numLeft + step * 3, firstY, unit, keyHeight);
        AddButton("NumPad7", "7", numLeft, firstY + 56, unit, keyHeight);
        AddButton("NumPad8", "8", numLeft + step, firstY + 56, unit, keyHeight);
        AddButton("NumPad9", "9", numLeft + step * 2, firstY + 56, unit, keyHeight);
        AddButton("Add", "＋", numLeft + step * 3, firstY + 56, unit, 108);
        AddButton("NumPad4", "4", numLeft, firstY + 112, unit, keyHeight);
        AddButton("NumPad5", "5", numLeft + step, firstY + 112, unit, keyHeight);
        AddButton("NumPad6", "6", numLeft + step * 2, firstY + 112, unit, keyHeight);
        AddButton("NumPad1", "1", numLeft, firstY + 168, unit, keyHeight);
        AddButton("NumPad2", "2", numLeft + step, firstY + 168, unit, keyHeight);
        AddButton("NumPad3", "3", numLeft + step * 2, firstY + 168, unit, keyHeight);
        AddButton("NumPadEnter", "Enter", numLeft + step * 3, firstY + 168, unit, 108);
        AddButton("NumPad0", "0", numLeft, firstY + 224, unit * 2 + Gap, keyHeight);
        AddButton("Decimal", ".", numLeft + step * 2, firstY + 224, unit, keyHeight);

        double cursorLeft = cursorX + padding;
        AddButton("Up", "↑", cursorLeft + step, firstY, unit, keyHeight);
        AddButton("Left", "←", cursorLeft, firstY + 56, unit, keyHeight);
        AddButton("Down", "↓", cursorLeft + step, firstY + 56, unit, keyHeight);
        AddButton("Right", "→", cursorLeft + step * 2, firstY + 56, unit, keyHeight);

        BuildMouse(mouseX, top, mouseWidth, numpadHeight);
    }

    void BuildMouse(double x, double y, double width, double height)
    {
        const double bodyWidth = 168;
        double bodyX = x + (width - bodyWidth) / 2, bodyY = y + 34;
        var body = new Border { Width = 156, Height = 238, CornerRadius = new CornerRadius(74), BorderThickness = new Thickness(2), BorderBrush = ThemeService.Brush("BorderBrush"), Background = ThemeService.Brush("CardBackground"), IsHitTestVisible = false };
        Canvas.SetLeft(body, bodyX + 6);
        Canvas.SetTop(body, bodyY + 8);
        InputCanvas.Children.Add(body);
        var centerLine = new Line { X1 = bodyX + 84, X2 = bodyX + 84, Y1 = bodyY + 10, Y2 = bodyY + 82, Stroke = ThemeService.Brush("BorderBrush"), StrokeThickness = 2, IsHitTestVisible = false };
        InputCanvas.Children.Add(centerLine);
        var split = new Line { X1 = bodyX + 8, X2 = bodyX + 160, Y1 = bodyY + 82, Y2 = bodyY + 82, Stroke = ThemeService.Brush("BorderBrush"), StrokeThickness = 2, IsHitTestVisible = false };
        InputCanvas.Children.Add(split);

        AddButton("MouseLeft", "左", bodyX + 14, bodyY + 16, 56, 54);
        AddButton("MouseRight", "右", bodyX + 98, bodyY + 16, 56, 54);
        AddButton("WheelUp", "▲", bodyX + 72, bodyY + 18, 24, 20);
        AddButton("MouseMiddle", "●", bodyX + 72, bodyY + 40, 24, 16);
        AddButton("WheelDown", "▼", bodyX + 72, bodyY + 58, 24, 20);

        var tiltLabel = new TextBlock { Text = "TILT", Width = 168, TextAlignment = TextAlignment.Center, Foreground = ThemeService.Brush("MutedText"), FontSize = 8, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
        Canvas.SetLeft(tiltLabel, bodyX);
        Canvas.SetTop(tiltLabel, bodyY + 96);
        InputCanvas.Children.Add(tiltLabel);
        AddButton("TiltLeft", "◀", bodyX + 44, bodyY + 108, 38, 28);
        AddButton("TiltRight", "▶", bodyX + 86, bodyY + 108, 38, 28);
        AddButton("MouseForward", "進む", bodyX + 20, bodyY + 151, 48, 31);
        AddButton("MouseBack", "戻る", bodyX + 20, bodyY + 186, 48, 31);
        AddButton("MouseX", "X1", bodyX + 112, bodyY + 151, 36, 66);
    }

    void AddFrame(string title, double x, double y, double width, double height)
    {
        var frame = new Border { Tag = title, Width = width, Height = height, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), BorderBrush = ThemeService.Brush("SubtleBorderBrush"), Background = ThemeService.Brush("AppBackground"), IsHitTestVisible = false };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        InputCanvas.Children.Add(frame);
        var heading = new TextBlock { Text = title, Foreground = ThemeService.Brush("MutedText"), FontSize = 11, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false };
        Canvas.SetLeft(heading, x + 10);
        Canvas.SetTop(heading, y + 4);
        InputCanvas.Children.Add(heading);
    }

    void AddRow(double y, IEnumerable<KeySpec> keys)
    {
        double x = 0;
        foreach (var key in keys)
        {
            AddButton(key.Key, key.Label, x, y, key.Width, 52);
            x += key.Width + Gap;
        }
    }

    void AddJisEnter()
    {
        var geometry = Geometry.Parse("M 4,0 L 156,0 Q 160,0 160,4 L 160,104 Q 160,108 156,108 L 26,108 Q 22,108 22,104 L 22,54 L 0,54 L 0,4 Q 0,0 4,0 Z");
        var button = CreateButton("Enter", "Enter", 160, 108);
        button.Style = (Style)FindResource("JisEnterButton");
        button.Clip = geometry;
        Canvas.SetLeft(button, 782);
        Canvas.SetTop(button, 100);
        InputCanvas.Children.Add(button);
    }

    void AddButton(string key, string label, double x, double y, double width, double height)
    {
        var button = CreateButton(key, label, width, height);
        Canvas.SetLeft(button, x);
        Canvas.SetTop(button, y);
        InputCanvas.Children.Add(button);
    }

    Button CreateButton(string key, string label, double width, double height)
    {
        var button = new Button { Tag = key, Content = KeyLabel(label), Width = width, Height = height, MinWidth = 0, MinHeight = 0, ToolTip = MainWindow.DisplayInputName(key) };
        button.Click += Input_Click;
        inputButtons.Add(button);
        return button;
    }
    static object KeyLabel(string label) => label.Contains('\n')
        ? new TextBlock { Text = label, TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center }
        : label;

    void Input_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string input })
            return;
        if (shortcutEditingMode)
        {
            SetShortcutValue(ActionPickerWindow.AddShortcutPart(ShortcutEditorBox.Text, input), true);
            LastChosenText.Text = "直前に押したキー：" + MainWindow.DisplayInputName(input);
            StatusText.Text = $"「{MainWindow.DisplayInputName(input)}」を追加しました。入力欄で自由に修正できます。";
        }
        else
        {
            SetShortcutPreview(MainWindow.DisplayInputName(input), input);
            InputChosen?.Invoke(input);
            StatusText.Text = $"「{MainWindow.DisplayInputName(input)}」を追加しました。続けて追加できます。";
        }
    }

    internal void ConfigureShortcutEditing(string initialValue)
    {
        shortcutEditingMode = true;
        ShortcutEditorPanel.Visibility = Visibility.Visible;
        ShortcutPreviewText.Visibility = Visibility.Collapsed;
        StatusText.Text = "キーを追加するか、下の入力欄を直接編集してください。";
        SetShortcutValue(initialValue, false);
    }

    internal void SetShortcutValue(string value, bool notify = false)
    {
        if (ShortcutEditorBox.Text == value)
            return;
        syncingShortcutEditor = true;
        ShortcutEditorBox.Text = value;
        ShortcutEditorBox.CaretIndex = ShortcutEditorBox.Text.Length;
        syncingShortcutEditor = false;
        if (notify)
            ShortcutChanged?.Invoke(value);
    }

    internal void SetShortcutPreview(string lastInput, string shortcut)
    {
        if (shortcutEditingMode)
        {
            SetShortcutValue(shortcut, false);
            return;
        }
        if (!string.IsNullOrWhiteSpace(lastInput))
            LastChosenText.Text = "直前に押したキー：" + lastInput;
        ShortcutPreviewText.Text = "現在の入力：" + (string.IsNullOrWhiteSpace(shortcut) ? "—" : string.Join(" + ", shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
    }

    void ShortcutEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (shortcutEditingMode && !syncingShortcutEditor)
            ShortcutChanged?.Invoke(ShortcutEditorBox.Text);
    }

    void ShortcutEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool modifierKey = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (!modifierKey && modifiers == ModifierKeys.None)
            return;
        SetShortcutValue(MainWindow.ShortcutTextForKey(key, modifiers), true);
        e.Handled = true;
    }

    void RemoveLastShortcutPart_Click(object sender, RoutedEventArgs e)
    {
        var parts = ShortcutEditorBox.Text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (parts.Count > 0)
            parts.RemoveAt(parts.Count - 1);
        SetShortcutValue(string.Join("+", parts), true);
        ShortcutEditorBox.Focus();
    }

    void ClearShortcut_Click(object sender, RoutedEventArgs e)
    {
        SetShortcutValue("", true);
        ShortcutEditorBox.Focus();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
    readonly record struct KeySpec(string Key, string Label, double Width);
}
