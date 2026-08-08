namespace RELYR;

internal sealed class MacroStopShortcut
{
    readonly HashSet<string> held = new(StringComparer.OrdinalIgnoreCase);
    internal bool Process(string input)
    {
        bool down = input.EndsWith(" Down", StringComparison.OrdinalIgnoreCase), up = input.EndsWith(" Up", StringComparison.OrdinalIgnoreCase);
        if (!down && !up)
            return false;
        string key = input[..^(down ? 5 : 3)];
        if (down)
            held.Add(key);
        else
            held.Remove(key);
        bool ctrl = held.Contains("LeftCtrl") || held.Contains("RightCtrl"), shift = held.Contains("LeftShift") || held.Contains("RightShift");
        return down && key.Equals("F12", StringComparison.OrdinalIgnoreCase) && ctrl && shift;
    }
    internal void Reset() => held.Clear();
}
