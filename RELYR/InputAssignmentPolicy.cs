namespace RELYR;

internal static class InputAssignmentPolicy
{
    static readonly string[] LayerSourceInputs = ["MouseRight", "MouseBack", "MouseForward"];

    internal static string BaseInput(string? input)
    {
        string value = (input ?? "").Trim();
        int separator = value.LastIndexOf('+');
        return separator >= 0 ? value[(separator + 1)..] : value;
    }

    internal static string Layer(string? input)
    {
        string value = (input ?? "").Trim();
        int separator = value.IndexOf('+');
        return separator > 0 ? value[..separator] : "通常";
    }

    internal static bool IsImpulseInput(string? input)
    {
        string value = BaseInput(input);
        return value.Equals("WheelUp", StringComparison.OrdinalIgnoreCase)
            || value.Equals("WheelDown", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TiltLeft", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TiltRight", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSelfLayerInput(string? input)
    {
        string layer = Layer(input);
        return LayerSourceInputs.Append("Space").Append("CapsLock").Contains(layer, StringComparer.OrdinalIgnoreCase)
            && layer.Equals(BaseInput(input), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsNormalAlphabetInput(string? input)
    {
        string value = (input ?? "").Trim();
        return !value.Contains('+') && value.Length == 1
            && value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    internal static bool IsUnreachableInput(string? input)
    {
        string value = (input ?? "").Trim();
        return IsFullyReservedInput(value)
            || BaseInput(value).Equals("MouseX", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Space", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CapsLock", StringComparison.OrdinalIgnoreCase)
            || IsSelfLayerInput(value);
    }

    // Holding the taskbar's primary button is how Windows starts dragging a
    // pinned app icon. A long-press detector would capture that Down first, so
    // this complete input belongs to Windows.
    internal static bool IsFullyReservedInput(string? input)
        => (input ?? "").Trim().Equals("Taskbar+MouseLeft", StringComparison.OrdinalIgnoreCase);

    internal static bool PreservesNativeShortPress(string? input)
    {
        string value = (input ?? "").Trim();
        return value.Equals("Taskbar+MouseLeft", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Taskbar+MouseRight", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool CanAssignShortPress(string? input)
        => !IsUnreachableInput(input) && !PreservesNativeShortPress(input);

    internal static string? ShortPressUnavailableReason(string? input)
    {
        string value = (input ?? "").Trim();
        if (value.Equals("Taskbar+MouseLeft", StringComparison.OrdinalIgnoreCase))
            return "タスクバーの左クリック／ドラッグはWindows専用です";
        if (value.Equals("Taskbar+MouseRight", StringComparison.OrdinalIgnoreCase))
            return "タスクバーの右クリックはWindows操作専用です";
        return UnavailableInputReason(input);
    }

    internal static string? NativeShortPressDisplayName(string? input)
    {
        string value = (input ?? "").Trim();
        if (value.Equals("Taskbar+MouseLeft", StringComparison.OrdinalIgnoreCase))
            return "Windowsの左クリック";
        if (value.Equals("Taskbar+MouseRight", StringComparison.OrdinalIgnoreCase))
            return "Windowsの右クリック";
        return null;
    }

    internal static bool ClearReservedShortPress(Mapping? mapping)
    {
        if (mapping == null || !PreservesNativeShortPress(mapping.Input))
            return false;
        bool changed = HasConfiguredShortAction(mapping) || !string.IsNullOrWhiteSpace(mapping.Value);
        mapping.Kind = ActionKind.None;
        mapping.Value = "";
        return changed;
    }

    internal static string? UnavailableInputReason(string? input)
    {
        if (IsFullyReservedInput(input))
            return "タスクバーの左クリック／ドラッグはWindows専用です";
        if (BaseInput(input).Equals("MouseX", StringComparison.OrdinalIgnoreCase))
            return "追加ボタンは入力として使用できません";
        if ((input ?? "").Trim().Equals("Space", StringComparison.OrdinalIgnoreCase))
            return "Spaceキーはレイヤー専用のため変更できません";
        if ((input ?? "").Trim().Equals("CapsLock", StringComparison.OrdinalIgnoreCase))
            return "CapsLockは割り当て元にはできません";
        if (IsSelfLayerInput(input))
            return "レイヤーと同じボタンには設定できません";
        return null;
    }

    internal static bool SupportsGesture(string? input)
        => !IsUnreachableInput(input) && !IsImpulseInput(input);

    internal static bool HasConfiguredLayerMappings(IReadOnlyList<Mapping>? mappings, string? input)
    {
        if (mappings == null || input == null || !LayerSourceInputs.Contains(input, StringComparer.OrdinalIgnoreCase))
            return false;
        string prefix = input + "+";
        return mappings.Any(mapping => mapping.Input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !IsUnreachableInput(mapping.Input)
            && HasConfiguredAction(mapping));
    }

    internal static bool CanExecuteLongPress(Mapping? mapping, IReadOnlyList<Mapping>? mappings = null)
        => mapping != null
           && !IsUnreachableInput(mapping.Input)
           && !IsImpulseInput(mapping.Input)
           && !IsNormalAlphabetInput(mapping.Input)
           && mapping.Kind != ActionKind.Gesture
           && !(mapping.Kind == ActionKind.Mouse && MappingExecutor.IsModifierDrag(mapping.Value))
           && !HasConfiguredLayerMappings(mappings, mapping.Input);

    internal static string? LongPressUnavailableReason(Mapping? mapping, IReadOnlyList<Mapping>? mappings = null)
    {
        if (mapping == null)
            return "このキーでは長押しを使えません";
        if (UnavailableInputReason(mapping.Input) is { } unavailable)
            return unavailable;
        if (IsImpulseInput(mapping.Input))
            return "ホイール／チルトでは長押し不可";
        if (IsNormalAlphabetInput(mapping.Input))
            return "通常の英字では長押し不可";
        if (mapping.Kind == ActionKind.Gesture)
            return "ジェスチャーとの併用不可";
        if (mapping.Kind == ActionKind.Mouse && MappingExecutor.IsModifierDrag(mapping.Value))
            return "修飾クリックとの併用不可";
        if (HasConfiguredLayerMappings(mappings, mapping.Input))
            return "レイヤー使用中は長押し不可";
        return null;
    }

    internal static bool ClearImpossibleLongPress(Mapping? mapping, IReadOnlyList<Mapping>? mappings = null)
    {
        if (mapping == null || CanExecuteLongPress(mapping, mappings))
            return false;
        bool changed = HasConfiguredLongPress(mapping) || !string.IsNullOrWhiteSpace(mapping.LongPressValue);
        mapping.LongPressKind = ActionKind.None;
        mapping.LongPressValue = "";
        return changed;
    }

    internal static bool SanitizeMappings(List<Mapping> mappings)
    {
        bool changed = mappings.RemoveAll(mapping => IsUnreachableInput(mapping.Input)) > 0;
        foreach (var mapping in mappings)
        {
            changed |= ClearReservedShortPress(mapping);
            if (IsImpulseInput(mapping.Input) && mapping.Kind == ActionKind.Gesture)
            {
                mapping.Kind = ActionKind.None;
                mapping.Value = "";
                changed = true;
            }
            changed |= ClearImpossibleLongPress(mapping);
        }
        foreach (var mapping in mappings)
            changed |= ClearImpossibleLongPress(mapping, mappings);
        changed |= mappings.RemoveAll(mapping => PreservesNativeShortPress(mapping.Input) && !HasConfiguredLongPress(mapping)) > 0;
        return changed;
    }

    internal static bool HasConfiguredAction(Mapping? mapping)
        => HasConfiguredShortAction(mapping) || HasConfiguredLongPress(mapping);

    internal static bool HasConfiguredShortAction(Mapping? mapping)
        => mapping != null && mapping.Kind != ActionKind.None
           && (mapping.Kind == ActionKind.Disabled || !string.IsNullOrWhiteSpace(mapping.Value));

    internal static bool HasConfiguredLongPress(Mapping? mapping)
        => mapping != null && mapping.LongPressKind != ActionKind.None
           && (mapping.LongPressKind == ActionKind.Disabled || !string.IsNullOrWhiteSpace(mapping.LongPressValue));
}
