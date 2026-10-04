using System.Runtime.InteropServices;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    private CancellationTokenSource? layoutCancellation;
    private IReadOnlyList<ForegroundTarget>? ownedLayoutTargets;
    public event Action<bool>? LayoutRunningChanged;
    public IReadOnlyList<LauncherEntry> LayoutApplications => entries;
    public void CancelCurrentLayout() => layoutCancellation?.Cancel();
    public async Task RunLayoutAsync(Guid id, bool global = false, string? learnedQuery = null)
    {
        if (executing || updatingPreferences) return;
        var layout = WindowLayout.Runnable(store.Preferences, id);
        if (layout == null) { SetStatus("This layout or its window/application master gate is disabled or unavailable."); ActionFailed?.Invoke(Status.Text); return; }
        executing = true;
        using var cancellation = new CancellationTokenSource(); layoutCancellation = cancellation; LayoutRunningChanged?.Invoke(true);
        try
        {
            if (IsVisible) Dismiss(true);
            var foreground = LayoutForeground();
            var preferences = store.Preferences;
            var required = layout.Entries.Select(x => x.ApplicationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var catalog = entries.Where(x => required.Contains(x.Id)).ToArray();
            var prepared = await Task.Run(() =>
            {
                var apps = LayoutWindowInventory.Applications(catalog, preferences);
                var screens = LayoutDisplays.Read();
                var inventory = ownedLayoutTargets != null && apps.Count == 1
                    ? LayoutWindowInventory.ReadOwned(ownedLayoutTargets, apps[0].MatchKey) : LayoutWindowInventory.Read(apps);
                var plan = WindowLayoutPlan.Make(layout, screens, apps, inventory.Windows, preferences.WindowGap);
                return (screens, inventory, plan, apps);
            });
            if (!LayoutStillCurrent(layout)) cancellation.Cancel();
            var existing = new List<LayoutPlacement>(); var sourceFailures = new List<LayoutSkip>();
            foreach (var placement in prepared.plan.Placements)
            {
                var authored = layout.Entries.First(x => x.Id == placement.EntryId);
                var app = prepared.apps.First(x => x.Id.Equals(authored.ApplicationId, StringComparison.OrdinalIgnoreCase));
                var check = new LayoutOpen(placement.EntryId, app.Id, app.MatchKey, placement.Frame, placement.Canvas, placement.Anchor, placement.DisplayIdentity);
                if (!cancellation.IsCancellationRequested && await LayoutApplicationCurrentAsync(layout, check, prepared.screens, cancellation.Token, requireLaunch: false)) existing.Add(placement);
                else sourceFailures.Add(new(placement.EntryId, "Current layout/master/application source changed; existing window not placed."));
            }
            var outcomes = (await windowManager.ApplyLayoutAsync(prepared.plan with { Placements = existing }, prepared.screens, prepared.inventory.Targets, cancellation.Token)).ToList();
            var placements = existing.ToList(); var targets = prepared.inventory.Targets.ToDictionary();
            var opening = await CompleteLayoutOpeningAsync(layout, prepared.plan, prepared.screens, prepared.inventory, prepared.apps, cancellation.Token);
            foreach (var pair in opening.Targets) targets.TryAdd(pair.Key, pair.Value);
            var openedPlan = new WindowLayoutPlan(opening.Result.Bound, [], prepared.plan.FrontmostEntryId);
            if (!LayoutStillCurrent(layout)) cancellation.Cancel();
            outcomes.AddRange(await windowManager.ApplyLayoutAsync(openedPlan, prepared.screens, targets, cancellation.Token));
            placements.AddRange(opening.Result.Bound);
            var placed = outcomes.Count(x => x.Placed);
            var failures = outcomes.Where(x => !x.Placed).Select(x => x.Error).Concat(prepared.plan.Skipped.Concat(sourceFailures).Select(x => x.Reason)).Concat(opening.Result.Failed.Select(x => x.Reason)).Distinct().Take(3).ToArray();
            var message = $"{layout.Name}: {placed} placed, {opening.Result.Launched} launched, {layout.Entries.Count - placed} skipped/refused.";
            if (failures.Length != 0) message += " " + string.Join(" ", failures);
            if (prepared.inventory.Limited || opening.Result.Limited) message += " The bounded window snapshot was incomplete.";
            if (placed > 0)
            {
                store.RecordLaunch(layout.EntryId, global ? null : learnedQuery);
                var front = placements.FirstOrDefault(x => x.EntryId == prepared.plan.FrontmostEntryId && outcomes.Any(y => y.EntryId == x.EntryId && y.Placed));
                if (front != null)
                {
                    if (cancellation.IsCancellationRequested || !LayoutStillCurrent(layout) || LayoutForeground() != foreground) message += " Bring to front skipped after cancellation or foreground change.";
                    else if (!targets.TryGetValue(front.Handle, out var target) || !target.IsValid()) message += " Bring to front target closed or became inaccessible.";
                    else if (!WindowActivation.Once(target.Handle, focusHost: true)) message += " Windows declined Bring to front; no retry was attempted.";
                }
            }
            SetStatus(message);
            if (failures.Length == 0) ActionCompleted?.Invoke(message); else ActionFailed?.Invoke(message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { SetStatus("Layout cancelled; remaining work stopped. Already launched applications are left running."); ActionFailed?.Invoke(Status.Text); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        { SetStatus("Layout stopped: " + ex.Message); ActionFailed?.Invoke(Status.Text); }
        finally { layoutCancellation = null; LayoutRunningChanged?.Invoke(false); executing = false; }
    }
    private bool LayoutStillCurrent(WindowLayout layout) => Equals(WindowLayout.Runnable(store.Preferences, layout.Id), layout);
    private async Task<(LayoutOpeningResult Result, IReadOnlyDictionary<long, ForegroundTarget> Targets)> CompleteLayoutOpeningAsync(WindowLayout layout,
        WindowLayoutPlan plan, IReadOnlyList<LayoutScreen> screens, LayoutWindowInventory.Snapshot baseline, IReadOnlyList<LayoutApplication> applications, CancellationToken cancellationToken)
    {
        var identities = new Dictionary<long, ForegroundTarget>();
        if (plan.Opens.Count == 0) return (new([], [], 0, false), identities);
        using var events = new LayoutWindowEvents(); // dispatcher/message-loop thread; disposed on every exit
        if (baseline.Limited || !events.IsEnabled)
            return (new([], plan.Opens.Select(x => new LayoutSkip(x.EntryId, baseline.Limited ? "Incomplete inventory; missing applications not guessed or launched." : "Window-event hooks unavailable; applications not launched.")).ToArray(), 0, baseline.Limited), identities);
        Task<bool> Current(LayoutOpen open, CancellationToken token) => LayoutApplicationCurrentAsync(layout, open, screens, token);
        async Task<string?> Launch(LayoutOpen open, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!LayoutStillCurrent(layout)) return "Current layout/master gate changed.";
                var entry = entries.FirstOrDefault(x => x.Id.Equals(open.ApplicationId, StringComparison.OrdinalIgnoreCase)); var preferences = store.Preferences;
                if (entry == null) return "Current application disappeared from the index.";
                // Keep dispatcher/Cancel responsive. An already-started public
                // launch call finishes; never detach it to secretly launch later.
                await Task.Run(() => { if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows required."); token.ThrowIfCancellationRequested(); ApplicationLauncher.Launch(entry, preferences, open.MatchKey, input: open.Input); }, token);
                return null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or ExternalException or System.IO.IOException or UnauthorizedAccessException)
            { return open.Input == null ? ex.Message : "Saved input activation refused (" + ex.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + "). Check the selected app/input support and current local resource; no fallback was attempted."; }
        }
        async Task<LayoutOpeningSnapshot> Read(CancellationToken token)
        {
            var snapshot = await Task.Run(() => { if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows required."); return LayoutWindowInventory.Read(applications); }, token);
            foreach (var pair in snapshot.Targets)
            {
                if (identities.Count >= LayoutWindowInventory.MaximumWindows && !identities.ContainsKey(pair.Key)) return new([], true);
                identities.TryAdd(pair.Key, pair.Value); // never replace an earlier HWND/PID/start cookie after handle reuse
            }
            return new(snapshot.Windows, snapshot.Limited);
        }
        var result = await WindowLayoutOpening.RunAsync(plan.Opens, baseline.Windows.Select(x => x.Handle).ToArray(), Current, Launch, Read, events.WaitAsync, cancellationToken);
        // Recheck source/master/current UUID immediately before native placement,
        // including windows bound earlier while another app was still waiting.
        var valid = new List<LayoutPlacement>(); var failed = result.Failed.ToList();
        foreach (var bound in result.Bound)
        {
            var open = plan.Opens.First(x => x.EntryId == bound.EntryId);
            if (!cancellationToken.IsCancellationRequested && await Current(open, cancellationToken)) valid.Add(bound);
            else failed.Add(new(bound.EntryId, "Cancelled or current layout/source changed; new window not placed."));
        }
        return (result with { Bound = valid, Failed = failed }, identities);
    }
    private async Task<bool> LayoutApplicationCurrentAsync(WindowLayout layout, LayoutOpen open, IReadOnlyList<LayoutScreen> screens, CancellationToken token, bool requireLaunch = true)
    {
        token.ThrowIfCancellationRequested();
        if (!LayoutStillCurrent(layout) || requireLaunch && !layout.LaunchMissingApplications) return false;
        var entry = entries.FirstOrDefault(x => x.Id.Equals(open.ApplicationId, StringComparison.OrdinalIgnoreCase)); var preferences = store.Preferences;
        if (entry == null || !entry.IsEnabled(preferences)) return false;
        return await Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) return false;
            var app = LayoutWindowInventory.Applications([entry], preferences).SingleOrDefault();
            var currentScreens = LayoutDisplays.Read();
            return app?.MatchKey.Equals(open.MatchKey, StringComparison.OrdinalIgnoreCase) == true
                && screens.Count == currentScreens.Count && screens.All(x => currentScreens.Any(y => y.Display.Identity.Equals(x.Display.Identity, StringComparison.OrdinalIgnoreCase) && y.Area == x.Area));
        }, token);
    }
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint LayoutForeground();
}
