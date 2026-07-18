namespace RELYR;

public static class ConfigValidator
{
    public static IReadOnlyList<string> Validate(AppConfig config)
    {
        var errors = new List<string>();
        if (config.Profiles.Count == 0) errors.Add("プロファイルがありません。");
        if (config.Profiles.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Profiles.Count)
            errors.Add("同名のプロファイルがあります。");
        if(config.Macros.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=config.Macros.Count)errors.Add("同名のマクロがあります。");
        foreach(var macro in config.Macros)
        {
            if(string.IsNullOrWhiteSpace(macro.Name))errors.Add("名前が空のマクロがあります。");
            foreach(var step in macro.Steps)
            {
                if(step.DelayMs<0||step.DelayMs>600000)errors.Add($"{macro.Name}: 待機時間が範囲外です。");
                if(step.RecordedActionKind is { } recordedKind)
                {
                    if(recordedKind is ActionKind.None or ActionKind.Disabled||string.IsNullOrWhiteSpace(step.RecordedActionValue))errors.Add($"{macro.Name}: 記録された割り当てアクションが空です。");
                    if(recordedKind==ActionKind.Macro&&!config.Macros.Any(x=>x.Name.Equals(step.RecordedActionValue,StringComparison.OrdinalIgnoreCase)))errors.Add($"{macro.Name}: 記録されたマクロ「{step.RecordedActionValue}」が見つかりません。");
                    if(recordedKind==ActionKind.Profile&&!config.Profiles.Any(x=>x.Name.Equals(step.RecordedActionValue,StringComparison.OrdinalIgnoreCase)))errors.Add($"{macro.Name}: 記録されたプロファイル「{step.RecordedActionValue}」が見つかりません。");
                }
                else if(step.Event!="Wait"&&!InputEngine.IsValidRecordedEvent(step.Event))errors.Add($"{macro.Name}: 認識できない手順「{step.Event}」があります。");
            }
        }
        foreach (var profile in config.Profiles)
        {
            foreach (var group in profile.Mappings.Where(x => x.Enabled).GroupBy(x => (x.Input.ToUpperInvariant(), x.Application.ToUpperInvariant(), x.Layer.ToUpperInvariant())).Where(x => x.Count() > 1))
                errors.Add($"{profile.Name}: {group.Key.Item1} の割り当てが競合しています。");
            foreach (var map in profile.Mappings)
            {
                if (string.IsNullOrWhiteSpace(map.Input)) errors.Add($"{profile.Name}: 入力元が空の設定があります。");
                if (map.LongPressMs < 50 || map.LongPressMs > 10000) errors.Add($"{profile.Name}/{map.Input}: 長押し時間が範囲外です。");
                if(map.Kind==ActionKind.Macro&&!string.IsNullOrWhiteSpace(map.Value)&&!config.Macros.Any(x=>x.Name.Equals(map.Value,StringComparison.OrdinalIgnoreCase)))errors.Add($"{profile.Name}/{map.Input}: マクロ「{map.Value}」が見つかりません。");
                if(map.LongPressKind==ActionKind.Macro&&!string.IsNullOrWhiteSpace(map.LongPressValue)&&!config.Macros.Any(x=>x.Name.Equals(map.LongPressValue,StringComparison.OrdinalIgnoreCase)))errors.Add($"{profile.Name}/{map.Input}: 長押しマクロ「{map.LongPressValue}」が見つかりません。");
                if(map.Kind==ActionKind.Profile&&!string.IsNullOrWhiteSpace(map.Value)&&!config.Profiles.Any(x=>x.Name.Equals(map.Value,StringComparison.OrdinalIgnoreCase)))errors.Add($"{profile.Name}/{map.Input}: プロファイル「{map.Value}」が見つかりません。");
                if(map.LongPressKind==ActionKind.Profile&&!string.IsNullOrWhiteSpace(map.LongPressValue)&&!config.Profiles.Any(x=>x.Name.Equals(map.LongPressValue,StringComparison.OrdinalIgnoreCase)))errors.Add($"{profile.Name}/{map.Input}: 長押しプロファイル「{map.LongPressValue}」が見つかりません。");
            }
        }
        return errors;
    }
}
