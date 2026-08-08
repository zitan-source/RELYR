namespace RELYR;

public static class ConfigValidator
{
    public static IReadOnlyList<string> Validate(AppConfig config)
    {
        var errors = new List<string>();
        if (config.Profiles.Count == 0)
            errors.Add("プロファイルがありません。");
        if (config.Profiles.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Profiles.Count)
            errors.Add("同名のプロファイルがあります。");
        if (config.Macros.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Macros.Count)
            errors.Add("同名のマクロがあります。");
        if (config.Gestures.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Gestures.Count)
            errors.Add("同名のジェスチャーがあります。");
        if (config.DeckLayouts.Count == 0)
            errors.Add("Deckレイアウトがありません。");
        if (config.DeckLayouts.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.DeckLayouts.Count)
            errors.Add("Deckレイアウトの識別子が重複しています。");
        foreach (var layout in config.DeckLayouts)
        {
            if (string.IsNullOrWhiteSpace(layout.Name))
                errors.Add("名前が空のDeckレイアウトがあります。");
            if (layout.Rows is < 1 or > DeckPanelLayout.MaximumRows || layout.Columns is < 1 or > DeckPanelLayout.MaximumColumns)
                errors.Add($"{layout.Name}: グリッドサイズは1～18の範囲で指定してください。");
            ValidateMappings(config, "Deck/" + layout.Name, layout.Mappings, errors);
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
        foreach (var macro in config.Macros)
        {
            var cycle = FindMacroCycle(macro, macroByName, [], []);
            if (cycle != null)
                errors.Add("マクロが循環しています: " + string.Join(" → ", cycle));
        }
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
            if (map.LongPressMs < 50 || map.LongPressMs > 10000)
                errors.Add($"{scope}/{map.Input}: 長押し時間が範囲外です。");
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

    static void ValidateDeckAction(AppConfig config, string scope, ActionKind kind, string value, List<string> errors)
    {
        if (kind == ActionKind.Shortcut && value.StartsWith(DeckPanelLayout.ActionPrefix, StringComparison.OrdinalIgnoreCase) && DeckPanelLayout.ResolveActionLayout(config, value) == null)
            errors.Add($"{scope}: 参照先のDeckレイアウトが見つかりません。");
    }

    static IReadOnlyList<string>? FindMacroCycle(MacroDefinition macro, IReadOnlyDictionary<string, MacroDefinition> macros, HashSet<string> visiting, List<string> path)
    {
        string identity = string.IsNullOrWhiteSpace(macro.Id) ? macro.Name : macro.Id;
        if (!visiting.Add(identity))
        {
            int start = path.FindIndex(x => x.Equals(macro.Name, StringComparison.OrdinalIgnoreCase));
            return start >= 0 ? [.. path.Skip(start), macro.Name] : [.. path, macro.Name];
        }
        path.Add(macro.Name);
        foreach (string targetName in macro.Steps.Where(x => x.RecordedActionKind == ActionKind.Macro).Select(x => x.RecordedActionValue))
            if (macros.TryGetValue(targetName, out var target) && FindMacroCycle(target, macros, visiting, path) is { } cycle)
                return cycle;
        path.RemoveAt(path.Count - 1);
        visiting.Remove(identity);
        return null;
    }
}
