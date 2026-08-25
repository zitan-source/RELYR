using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfMessageBox = RELYR.AppDialog;

namespace RELYR;

public partial class MainWindow
{
    void SwitchProfile(string name, bool refresh, bool persist = true)
    {
        if (!config.Profiles.Any(x => x.Name == name))
            return;
        bool preserveDeckSelection = deckManagementMode && MultiSelectToggle.IsChecked == true;
        string[] preservedDeckInputs = preserveDeckSelection ? [.. multiSelectedInputs] : [];
        bool changed = !config.ActiveProfile.Equals(name, StringComparison.OrdinalIgnoreCase) || !appliedConfig.ActiveProfile.Equals(name, StringComparison.OrdinalIgnoreCase);
        suppressAutomaticProfileSwitchUntil = DateTime.UtcNow.AddSeconds(2);
        explicitProfileSwitchProcess = ConditionMatcher.ForegroundProcessName();
        automaticProfileReturnName = "";
        string? selectedDeckGroup = deckManagementMode && selectedDeckLayout?.ProfileSwitchEnabled == true ? selectedDeckLayout.ProfileGroupId : null;
        config.ActiveProfile = name;
        if (appliedConfig.Profiles.Any(x => x.Name == name))
        {
            appliedConfig.ActiveProfile = name;
            if (persist)
            {
                var persisted = store.Load();
                if (persisted.Profiles.Any(x => x.Name == name))
                {
                    persisted.ActiveProfile = name;
                    store.Save(persisted);
                }
            }
        }
        ClearSelectedInput();
        if (selectedDeckGroup != null)
        {
            var profile = DeckPanelLayout.ActiveProfile(config);
            var variant = config.DeckLayouts.FirstOrDefault(layout => layout.ProfileSwitchEnabled
                && layout.ProfileGroupId.Equals(selectedDeckGroup, StringComparison.OrdinalIgnoreCase)
                && profile != null && layout.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            if (variant != null)
                EditDeckLayout(variant);
        }
        if (preserveDeckSelection)
        {
            int visibleSlots = selectedDeckLayout == null ? 0 : DeckPanelLayout.VisibleSlotCount(selectedDeckLayout);
            multiSelectedInputs.Clear();
            foreach (string input in preservedDeckInputs.Where(input => DeckPanelLayout.SlotNumber(input) is int slot && slot >= 1 && slot <= visibleSlots))
                multiSelectedInputs.Add(input);
            MultiSelectToggle.IsChecked = true;
            UpdateMultiSelectControls();
            ColorDeckManagementButtons();
        }
        if (IsMouseLayerBlockedByDirectGesture(config.Profiles, CurrentProfile.Name, currentLayer))
            currentLayer = "通常";
        if (refresh)
            RefreshProfiles();
        UpdateLayerButtons();
        UpdateStatus();
        RebuildTrayMenu();
        if (runtimeRole == RuntimeRole.UiHost && changed)
            IpcRuntime.RequestReload();
        if (changed)
        {
            OverlayService.RefreshDeckPanelForProfileChange();
            ShowProfileOverlay(name);
        }
    }
    AppConfig DeckOverlayConfig()
    {
        var snapshot = store.Clone(config);
        snapshot.ActiveProfile = appliedConfig.ActiveProfile;
        // The active profile is runtime state, but Deck editing must use one
        // shared model. Cloning the layouts here made the editor and the live
        // overlay modify different objects until the process was rebuilt.
        snapshot.DeckLayouts = config.DeckLayouts;
        snapshot.SharedDeckMappings = config.SharedDeckMappings;
        return snapshot;
    }
    void ShowProfileOverlay(string profileName)
    {
        if (!appliedConfig.ShowProfileSwitchOverlay)
            return;
        if (profileOverlay?.IsVisible == true && lastProfileOverlayName.Equals(profileName, StringComparison.OrdinalIgnoreCase))
            return;
        profileOverlay?.Close();
        lastProfileOverlayName = profileName;
        var overlay = new ProfileSwitchOverlay(profileName);
        profileOverlay = overlay;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(profileOverlay, overlay))
                profileOverlay = null;
            if (lastProfileOverlayName.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                lastProfileOverlayName = "";
        };
        overlay.Show();
    }
    void AutoSwitchProfile()
    {
        bool needsForegroundProcess = appliedConfig.InputDisabledApplications.Count > 0
            || appliedConfig.Profiles.Skip(1).Any(x => x.AutoSwitchEnabled);
        string process = needsForegroundProcess ? ConditionMatcher.ForegroundProcessName() : "";
        RefreshInputProcessingSuppression(process);
        if (profileDropDownOpen || DateTime.UtcNow < suppressAutomaticProfileSwitchUntil)
        {
            LogAutomaticProfileSwitch($"paused dropdown={profileDropDownOpen} suppressUntil={suppressAutomaticProfileSwitchUntil:O}");
            return;
        }
        if (!appliedConfig.Profiles.Skip(1).Any(x => x.AutoSwitchEnabled))
        {
            bool changed = TryApplyAutomaticProfile(config, appliedConfig, config.ActiveProfile, engine.TryPrepareForProfileChange);
            LogAutomaticProfileSwitch($"no-enabled-profiles editor={config.ActiveProfile} runtime={appliedConfig.ActiveProfile} changed={changed}");
            if (changed)
            {
                if (runtimeRole == RuntimeRole.UiHost)
                    IpcRuntime.RequestReload();
                RebuildTrayMenu();
                OverlayService.RefreshDeckPanelForProfileChange();
                ShowProfileOverlay(appliedConfig.ActiveProfile);
            }
            return;
        }
        string[] processes = string.IsNullOrWhiteSpace(process) ? [] : [process];
        var (Target, ReturnProfile) = ResolveAutomaticProfileTarget(appliedConfig.Profiles, appliedConfig.ActiveProfile, automaticProfileReturnName, processes, false);
        int requiredSamples = AutomaticProfileRequiredSamples(appliedConfig.Profiles, Target);
        bool stable = ObserveAutomaticProfileCandidate(Target, requiredSamples);
        LogAutomaticProfileSwitch($"observe foreground={process} candidate={Target} samples={automaticProfileCandidateSamples}/{requiredSamples} stable={stable} runtime={appliedConfig.ActiveProfile} return={automaticProfileReturnName}");
        if (!stable)
            return;
        ResetAutomaticProfileCandidate();
        if (ShouldKeepExplicitProfile(explicitProfileSwitchProcess, process, false))
        {
            LogAutomaticProfileSwitch($"manual-hold original={explicitProfileSwitchProcess} current={process}");
            return;
        }
        explicitProfileSwitchProcess = "";
        string before = appliedConfig.ActiveProfile;
        if (TryApplyAutomaticProfileForProcesses(processes, false, out string target))
        {
            LogAutomaticProfileSwitch($"applied before={before} target={target} runtime={appliedConfig.ActiveProfile} return={automaticProfileReturnName}");
        }
        else
            LogAutomaticProfileSwitch($"not-applied before={before} target={target} runtime={appliedConfig.ActiveProfile} captured={engine.HasCapturedPhysicalInput}");
    }

    void RefreshInputProcessingSuppression(string? foregroundProcess = null)
    {
        if (appliedConfig.InputDisabledApplications.Count == 0)
        {
            Volatile.Write(ref inputProcessingSuppressedForForeground, false);
            return;
        }
        foregroundProcess ??= ConditionMatcher.ForegroundProcessName();
        Volatile.Write(ref inputProcessingSuppressedForForeground,
            IsInputProcessingDisabledForApplication(appliedConfig.InputDisabledApplications, foregroundProcess));
    }

    internal static bool IsInputProcessingDisabledForApplication(IEnumerable<string> applications, string foregroundProcess)
        => !string.IsNullOrWhiteSpace(foregroundProcess)
            && applications.Any(application => ConditionMatcher.Matches(application, foregroundProcess));
    bool TryApplyAutomaticProfileForProcesses(IReadOnlyCollection<string> processes, bool cursorOverTaskbar, out string target)
    {
        if (!TryResolveAndApplyAutomaticProfile(config, appliedConfig, processes, cursorOverTaskbar, engine.TryPrepareForProfileChange, ref automaticProfileReturnName, out target))
            return false;
        RebuildTrayMenu();
        if (runtimeRole == RuntimeRole.UiHost)
            IpcRuntime.RequestReload();
        OverlayService.RefreshDeckPanelForProfileChange();
        ShowProfileOverlay(target);
        return true;
    }
    void LogAutomaticProfileSwitch(string message)
    {
        if (string.IsNullOrWhiteSpace(automaticProfileDiagnosticLog))
            return;
        try
        {
            string? directory = Path.GetDirectoryName(automaticProfileDiagnosticLog);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(automaticProfileDiagnosticLog, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
    void ResetAutomaticProfileCandidate()
    {
        automaticProfileCandidateSignature = "";
        automaticProfileCandidateSamples = 0;
    }
    bool ObserveAutomaticProfileCandidate(string signature, int requiredSamples)
    {
        if (!automaticProfileCandidateSignature.Equals(signature, StringComparison.OrdinalIgnoreCase))
        {
            automaticProfileCandidateSignature = signature;
            automaticProfileCandidateSamples = 1;
            return requiredSamples <= 1;
        }
        automaticProfileCandidateSamples = Math.Min(requiredSamples, automaticProfileCandidateSamples + 1);
        return automaticProfileCandidateSamples >= requiredSamples;
    }
    internal static int AutomaticProfileRequiredSamples(IEnumerable<Profile> profiles, string target)
        => profiles.FirstOrDefault(profile => profile.Name.Equals(target, StringComparison.OrdinalIgnoreCase))?.AutoSwitchEnabled == true ? 1 : 2;
    internal static bool TryApplyAutomaticProfile(AppConfig editingConfig, AppConfig runtimeConfig, string targetName, Func<bool> prepare)
    {
        if (runtimeConfig.ActiveProfile == targetName || !runtimeConfig.Profiles.Any(x => x.Name == targetName))
            return false;
        if (!prepare())
            return false;
        return ApplyAutomaticProfile(editingConfig, runtimeConfig, targetName);
    }
    internal static bool TryResolveAndApplyAutomaticProfile(AppConfig editingConfig, AppConfig runtimeConfig, IReadOnlyCollection<string> processes, bool cursorOverTaskbar, Func<bool> prepare, ref string returnProfile, out string target)
    {
        var (Target, ReturnProfile) = ResolveAutomaticProfileTarget(runtimeConfig.Profiles, runtimeConfig.ActiveProfile, returnProfile, processes, cursorOverTaskbar);
        target = Target;
        if (runtimeConfig.ActiveProfile.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            returnProfile = ReturnProfile;
            return false;
        }
        if (!TryApplyAutomaticProfile(editingConfig, runtimeConfig, target, prepare))
            return false;
        returnProfile = ReturnProfile;
        return true;
    }
    internal static bool ApplyAutomaticProfile(AppConfig editingConfig, AppConfig runtimeConfig, string targetName)
    {
        if (runtimeConfig.ActiveProfile == targetName || !runtimeConfig.Profiles.Any(x => x.Name == targetName))
            return false;
        // Automatic switching is a runtime concern. Never move the profile that the
        // user is currently editing, even when the cursor or virtual desktop changes.
        runtimeConfig.ActiveProfile = targetName;
        return true;
    }
    internal static Profile SelectAutomaticProfile(IReadOnlyList<Profile> profiles, string process) => profiles.Skip(1).FirstOrDefault(x => x.AutoSwitchEnabled && x.AutoSwitchApplications.Any(app => ConditionMatcher.Matches(app, process))) ?? profiles[0];
    internal static string SelectAutomaticProfileNameForLocation(IReadOnlyList<Profile> profiles, string currentProfile, string process, bool cursorOverTaskbar) => cursorOverTaskbar && profiles.Any(x => x.Name == currentProfile) ? currentProfile : SelectAutomaticProfile(profiles, process).Name;
    internal static (string Target, string ReturnProfile) ResolveAutomaticProfileTarget(IReadOnlyList<Profile> profiles, string currentProfile, string returnProfile, string process, bool cursorOverTaskbar)
        => ResolveAutomaticProfileTarget(profiles, currentProfile, returnProfile, string.IsNullOrWhiteSpace(process) ? [] : [process], cursorOverTaskbar);
    internal static (string Target, string ReturnProfile) ResolveAutomaticProfileTarget(IReadOnlyList<Profile> profiles, string currentProfile, string returnProfile, IReadOnlyCollection<string> processes, bool cursorOverTaskbar)
    {
        if (cursorOverTaskbar)
            return (currentProfile, returnProfile);
        string defaultProfile = profiles[0].Name;
        var matched = profiles.Skip(1).FirstOrDefault(x => x.AutoSwitchEnabled && x.AutoSwitchApplications.Any(app => processes.Any(process => ConditionMatcher.Matches(app, process))));
        if (matched != null)
        {
            string returnTarget = ValidManualReturnProfile(profiles, returnProfile)
                ?? ValidManualReturnProfile(profiles, currentProfile)
                ?? defaultProfile;
            return (matched.Name, returnTarget);
        }
        if (!string.IsNullOrWhiteSpace(returnProfile) && profiles.Any(x => x.Name == returnProfile))
            return (returnProfile, "");
        // An automatically selected profile is not a safe fallback: it may
        // belong to an app on a different virtual desktop. Return to the
        // manually selected non-automatic profile, or the standard profile.
        return (ValidManualReturnProfile(profiles, currentProfile) ?? defaultProfile, "");
    }
    static string? ValidManualReturnProfile(IReadOnlyList<Profile> profiles, string profileName)
        => profiles.FirstOrDefault(x => x.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) && !x.AutoSwitchEnabled)?.Name;
    internal static bool ShouldKeepExplicitProfile(string originalProcess, string currentProcess, bool cursorOverTaskbar) => cursorOverTaskbar || (!string.IsNullOrWhiteSpace(originalProcess) && ConditionMatcher.Matches(originalProcess, currentProcess));
    internal static bool IsOwnProcess(string process, string? executablePath = null)
        => !string.IsNullOrWhiteSpace(process)
            && ConditionMatcher.Matches(Path.GetFileNameWithoutExtension(executablePath ?? Environment.ProcessPath ?? "RELYR"), process);
}
