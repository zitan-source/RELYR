namespace RELYR;

public static class ConfigValidator
{
    public static IReadOnlyList<string> Validate(AppConfig config)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(config.WindowActionTarget) || !Enum.IsDefined(config.ThemeMode)
            || !Enum.IsDefined(config.ClockBackgroundMode) || !Enum.IsDefined(config.ClockDisplayMode))
            errors.Add("設定に認識できない列挙値があります。");
        if (config.Profiles.Count == 0)
            errors.Add("プロファイルがありません。");
        if (config.Profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
            errors.Add("名前が空のプロファイルがあります。");
        if (config.Profiles.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Profiles.Count)
            errors.Add("同名のプロファイルがあります。");
        if (config.Profiles.Count > 0 && !config.Profiles.Any(profile => profile.Name.Equals(config.ActiveProfile, StringComparison.OrdinalIgnoreCase)))
            errors.Add("使用中のプロファイルが見つかりません。");
        if (config.Profiles.Any(profile => profile.AutoSwitchApplications.Any(string.IsNullOrWhiteSpace)))
            errors.Add("自動切替アプリ名が空の設定があります。");
        if (config.Macros.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Macros.Count)
            errors.Add("同名のマクロがあります。");
        if (config.Macros.Any(macro => string.IsNullOrWhiteSpace(macro.Id))
            || config.Macros.Select(macro => macro.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Macros.Count)
            errors.Add("マクロの識別子が空か重複しています。");
        if (config.Gestures.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Gestures.Count)
            errors.Add("同名のジェスチャーがあります。");
        if (config.DeckLayouts.Count == 0)
            errors.Add("Deckレイアウトがありません。");
        if (config.DeckLayouts.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.DeckLayouts.Count)
            errors.Add("Deckレイアウトの識別子が重複しています。");
        if (config.DeckLayouts.Any(layout => string.IsNullOrWhiteSpace(layout.Id)))
            errors.Add("識別子が空のDeckレイアウトがあります。");
        if (config.Profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Id))
            || config.Profiles.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Profiles.Count)
            errors.Add("プロファイルの識別子が空か重複しています。");
        if (config.SpaceHoldRepeatDelayMs is < 100 or > 2000 || config.MouseDragPixels is < 1 or > 100
            || config.GestureThresholdPixels is < 3 or > 100 || config.InputPanelOpacityPercent is < 40 or > 100)
            errors.Add("入力のタイミング・距離・不透明度設定が範囲外です。");
        foreach (var layout in config.DeckLayouts)
        {
            if (string.IsNullOrWhiteSpace(layout.Name))
                errors.Add("名前が空のDeckレイアウトがあります。");
            if (layout.Rows is < 1 or > DeckPanelLayout.MaximumRows || layout.Columns is < 1 or > DeckPanelLayout.MaximumColumns)
                errors.Add($"{layout.Name}: グリッドサイズは1～18の範囲で指定してください。");
            ValidateMappings(config, "Deck/" + layout.Name, layout.Mappings, errors);
            if (layout.ProfileSwitchEnabled && (string.IsNullOrWhiteSpace(layout.ProfileGroupId)
                || !config.Profiles.Any(profile => profile.Id.Equals(layout.ProfileId, StringComparison.OrdinalIgnoreCase))))
                errors.Add($"{layout.Name}: 連動先のプロファイルが見つかりません。");
        }
        if (!config.DeckLayouts.Any(x => x.Id.Equals(config.DefaultDeckLayoutId, StringComparison.OrdinalIgnoreCase)))
            errors.Add("既定のDeckレイアウトが見つかりません。");
        foreach (var gesture in config.Gestures)
        {
            if (string.IsNullOrWhiteSpace(gesture.Name))
                errors.Add("名前が空のジェスチャーがあります。");
            ValidateGestureAction(config, gesture, "上", gesture.UpKind, gesture.UpValue, errors);
            ValidateGestureAction(config, gesture, "下", gesture.DownKind, gesture.DownValue, errors);
            ValidateGestureAction(config, gesture, "左", gesture.LeftKind, gesture.LeftValue, errors);
            ValidateGestureAction(config, gesture, "右", gesture.RightKind, gesture.RightValue, errors);
            ValidateGestureAction(config, gesture, "短押し", gesture.CenterKind, gesture.CenterValue, errors);
        }
        foreach (var macro in config.Macros)
        {
            if (string.IsNullOrWhiteSpace(macro.Name))
                errors.Add("名前が空のマクロがあります。");
            if (macro.Steps.Count > 10000)
                errors.Add($"{macro.Name}: 手順が10000件を超えています。");
            foreach (var step in macro.Steps)
            {
                if (step.DelayMs < 0 || step.DelayMs > 600000)
                    errors.Add($"{macro.Name}: 待機時間が範囲外です。");
                if (step.RecordedActionKind is { } recordedKind)
                {
                    ValidateExecutableAction(macro.Name, recordedKind, step.RecordedActionValue, errors);
                    if (recordedKind is ActionKind.None or ActionKind.Disabled || string.IsNullOrWhiteSpace(step.RecordedActionValue))
                        errors.Add($"{macro.Name}: 記録された割り当てアクションが空です。");
                    if (recordedKind == ActionKind.Macro && !config.Macros.Any(x => x.Name.Equals(step.RecordedActionValue, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"{macro.Name}: 記録されたマクロ「{step.RecordedActionValue}」が見つかりません。");
                    if (recordedKind == ActionKind.Profile && !config.Profiles.Any(x => x.Name.Equals(step.RecordedActionValue, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"{macro.Name}: 記録されたプロファイル「{step.RecordedActionValue}」が見つかりません。");
                    ValidateDeckAction(config, macro.Name, recordedKind, step.RecordedActionValue, errors);
                }
                else if (step.Event != "Wait" && !InputEngine.IsValidRecordedEvent(step.Event))
                    errors.Add($"{macro.Name}: 認識できない手順「{step.Event}」があります。");
            }
        }
        var macroByName = config.Macros.Where(x => !string.IsNullOrWhiteSpace(x.Name)).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        if (FindMacroCycle(macroByName) is { } cycle)
            errors.Add("マクロが循環しています: " + string.Join(" → ", cycle));
        foreach (var profile in config.Profiles)
        {
            ValidateMappings(config, profile.Name, profile.Mappings, errors);
        }
        return errors;
    }

    static void ValidateMappings(AppConfig config, string scope, IReadOnlyList<Mapping> mappings, List<string> errors)
    {
        foreach (var group in mappings.GroupBy(x => (x.Input.ToUpperInvariant(), x.Application.ToUpperInvariant(), x.Layer.ToUpperInvariant())).Where(x => x.Count() > 1))
            errors.Add($"{scope}: {group.Key.Item1} の割り当てが競合しています。");
        foreach (var map in mappings)
        {
            if (string.IsNullOrWhiteSpace(map.Input))
                errors.Add($"{scope}: 入力元が空の設定があります。");
            if (map.Input.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase) && !map.Input.Contains('+'))
                errors.Add($"{scope}/{map.Input}: 通常レイヤーの左クリックは割り当てできません。");
            if (map.LongPressMs < 50 || map.LongPressMs > 10000)
                errors.Add($"{scope}/{map.Input}: 長押し時間が範囲外です。");
            ValidateExecutableAction($"{scope}/{map.Input}", map.Kind, map.Value, errors);
            ValidateExecutableAction($"{scope}/{map.Input}/長押し", map.LongPressKind, map.LongPressValue, errors);
            if (map.Kind == ActionKind.Macro && !string.IsNullOrWhiteSpace(map.Value) && !config.Macros.Any(x => x.Name.Equals(map.Value, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{scope}/{map.Input}: マクロ「{map.Value}」が見つかりません。");
            if (map.LongPressKind == ActionKind.Macro && !string.IsNullOrWhiteSpace(map.LongPressValue) && !config.Macros.Any(x => x.Name.Equals(map.LongPressValue, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{scope}/{map.Input}: 長押しマクロ「{map.LongPressValue}」が見つかりません。");
            if (map.Kind == ActionKind.Profile && !string.IsNullOrWhiteSpace(map.Value) && !config.Profiles.Any(x => x.Name.Equals(map.Value, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{scope}/{map.Input}: プロファイル「{map.Value}」が見つかりません。");
            if (map.LongPressKind == ActionKind.Profile && !string.IsNullOrWhiteSpace(map.LongPressValue) && !config.Profiles.Any(x => x.Name.Equals(map.LongPressValue, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{scope}/{map.Input}: 長押しプロファイル「{map.LongPressValue}」が見つかりません。");
            if (map.Kind == ActionKind.Gesture && !string.IsNullOrWhiteSpace(map.Value) && !config.Gestures.Any(x => x.Name.Equals(map.Value, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{scope}/{map.Input}: ジェスチャー「{map.Value}」が見つかりません。");
            if (map.Kind == ActionKind.Gesture && map.LongPressKind != ActionKind.None)
                errors.Add($"{scope}/{map.Input}: ジェスチャーと長押し動作は同時に設定できません。");
            if (map.LongPressKind == ActionKind.Gesture && !string.IsNullOrWhiteSpace(map.LongPressValue) && !config.Gestures.Any(x => x.Name.Equals(map.LongPressValue, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{scope}/{map.Input}: ジェスチャー「{map.LongPressValue}」が見つかりません。");
            ValidateDeckAction(config, $"{scope}/{map.Input}", map.Kind, map.Value, errors);
            ValidateDeckAction(config, $"{scope}/{map.Input}/長押し", map.LongPressKind, map.LongPressValue, errors);
        }
    }

    static void ValidateGestureAction(AppConfig config, GestureDefinition gesture, string direction, ActionKind kind, string value, List<string> errors)
    {
        ValidateExecutableAction($"{gesture.Name}/{direction}", kind, value, errors);
        if (kind == ActionKind.Gesture)
            errors.Add($"{gesture.Name}/{direction}: ジェスチャーを入れ子にはできません。");
        if (kind is not ActionKind.None and not ActionKind.Disabled && string.IsNullOrWhiteSpace(value))
            errors.Add($"{gesture.Name}/{direction}: 実行内容が空です。");
        if (kind == ActionKind.Macro && !config.Macros.Any(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{gesture.Name}/{direction}: マクロ「{value}」が見つかりません。");
        if (kind == ActionKind.Profile && !config.Profiles.Any(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{gesture.Name}/{direction}: プロファイル「{value}」が見つかりません。");
        ValidateDeckAction(config, $"{gesture.Name}/{direction}", kind, value, errors);
    }

    static void ValidateExecutableAction(string scope, ActionKind kind, string value, List<string> errors)
    {
        if (!Enum.IsDefined(kind))
        {
            errors.Add($"{scope}: 認識できないアクション種別です。");
            return;
        }
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (kind is ActionKind.Key or ActionKind.Shortcut && !InputEngine.IsRecognizedShortcut(value))
            errors.Add($"{scope}: 認識できないキーまたはショートカットです: {value}");
        if (kind == ActionKind.Mouse && !ActionCatalog.TryNormalizeMouseAction(value, out _))
            errors.Add($"{scope}: 認識できないマウス操作です: {value}");
    }

    static void ValidateDeckAction(AppConfig config, string scope, ActionKind kind, string value, List<string> errors)
    {
        if (kind == ActionKind.Shortcut && value.StartsWith(DeckPanelLayout.ActionPrefix, StringComparison.OrdinalIgnoreCase) && DeckPanelLayout.ResolveActionLayout(config, value) == null)
            errors.Add($"{scope}: 参照先のDeckレイアウトが見つかりません。");
    }

    static IReadOnlyList<string>? FindMacroCycle(IReadOnlyDictionary<string, MacroDefinition> macros)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in macros.Values)
        {
            if (state.GetValueOrDefault(root.Name) == 2)
                continue;
            var path = new List<string>();
            var pathIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<(MacroDefinition Macro, string[] Targets, int Next)>();
            Push(root);
            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                if (frame.Next >= frame.Targets.Length)
                {
                    state[frame.Macro.Name] = 2;
                    pathIndexes.Remove(frame.Macro.Name);
                    path.RemoveAt(path.Count - 1);
                    continue;
                }
                string targetName = frame.Targets[frame.Next];
                stack.Push((frame.Macro, frame.Targets, frame.Next + 1));
                if (!macros.TryGetValue(targetName, out var target))
                    continue;
                if (state.GetValueOrDefault(target.Name) == 1)
                {
                    int start = pathIndexes[target.Name];
                    return [.. path.Skip(start), target.Name];
                }
                if (state.GetValueOrDefault(target.Name) != 2)
                    Push(target);
            }

            void Push(MacroDefinition macro)
            {
                state[macro.Name] = 1;
                pathIndexes[macro.Name] = path.Count;
                path.Add(macro.Name);
                stack.Push((macro, [.. macro.Steps
                    .Where(step => step.RecordedActionKind == ActionKind.Macro)
                    .Select(step => step.RecordedActionValue)], 0));
            }
        }
        return null;
    }
}
