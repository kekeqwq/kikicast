using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public sealed class WindowLayoutOpeningTests
{
    private static readonly LayoutDisplay Display = new(@"monitor:\\?\DISPLAY#OpeningOwnedPanel", "Owned panel");
    private static readonly LayoutScreen Screen = new(Display, new("session", new(0, 0, 1000, 800)));
    private const string App = @"app:C:\Owned.exe";
    private static WindowLayoutEntry Entry(string app = App) => new(Guid.NewGuid(), app, Display);
    private static WindowLayout Layout(params WindowLayoutEntry[] entries) => new(Guid.NewGuid(), "Owned opening") { Entries = entries.ToList(), LaunchMissingApplications = true };
    private static LayoutOpen Open(string key = "exe:owned") => new(Guid.NewGuid(), App, key, new(100, 100, 500, 400), Screen.Area.WorkArea, WindowSizeAnchor.Center, Display.Identity);
    private static LayoutWindow Window(long handle = 2, string key = "exe:owned") => new(handle, key, new(100, 100, 500, 400));
    private sealed class Host
    {
        public bool Current = true;
        public int Launches, Reads, Waits;
        public string? Error;
        public LayoutOpeningSnapshot Snapshot = new([Window()]);
        public Func<CancellationToken, Task>? OnWait;
        public Func<int, LayoutOpeningSnapshot>? OnRead;
        public Func<int, bool>? OnCurrent;
        private int checks;
        public Task<LayoutOpeningResult> Run(LayoutOpen[]? opens = null, long[]? baseline = null, CancellationToken token = default, TimeSpan? budget = null)
            => WindowLayoutOpening.RunAsync(opens ?? [Open()], baseline ?? [],
                (_, _) => Task.FromResult(OnCurrent?.Invoke(++checks) ?? Current),
                (_, _) => { Launches++; return Task.FromResult(Error); },
                _ => { Reads++; return Task.FromResult(OnRead?.Invoke(Reads) ?? Snapshot); },
                async ct => { Waits++; if (OnWait != null) await OnWait(ct); else await Task.Delay(Timeout.Infinite, ct); }, token, budget);
    }
    [Fact]
    public void LegacyAndNewDefinitionsDefaultOffWithoutRewriteButSavedOptInSurvivesCopyRename()
    {
        var layout = Layout(Entry()) with { LaunchMissingApplications = false };
        var json = JsonSerializer.Serialize(layout); using var document = JsonDocument.Parse(json);
        var legacy = "{" + string.Join(",", document.RootElement.EnumerateObject().Where(x => x.Name != "LaunchMissingApplications").Select(x => JsonSerializer.Serialize(x.Name) + ":" + x.Value.GetRawText())) + "}";
        var loaded = JsonSerializer.Deserialize<WindowLayout>(legacy)!; Assert.False(loaded.LaunchMissingApplications); Assert.Null(loaded.Validate());
        var enabled = layout with { LaunchMissingApplications = true }; Assert.True(JsonSerializer.Deserialize<WindowLayout>(JsonSerializer.Serialize(enabled))!.LaunchMissingApplications);
        Assert.True(enabled.Duplicate("Copy").LaunchMissingApplications); Assert.True((enabled with { Name = "Rename" }).LaunchMissingApplications);
        Assert.Contains("Application opening allowed", enabled.Summary); Assert.Contains("Application opening off", loaded.Summary);
    }
    [Fact]
    public void PlainMissingLaunchesDeduplicateActualKeysAndKeepUnchangedResolvedGeometry()
    {
        var first = Entry(); var alias = Entry(@"app:C:\Alias.lnk"); var layout = Layout(first, alias) with { FrontmostEntryId = first.Id };
        LayoutApplication[] apps = [new(App, "exe:owned"), new(alias.ApplicationId, "EXE:OWNED")];
        var plan = WindowLayoutPlan.Make(layout, [Screen], apps, [], 12); var open = Assert.Single(plan.Opens);
        Assert.Single(plan.Skipped); Assert.Contains("Duplicate", plan.Skipped[0].Reason); Assert.Equal(first.Id, plan.FrontmostEntryId);
        Assert.Equal(WindowLayoutGeometry.Resolve(first, Screen.Area, 12), open.Frame); Assert.Equal(WindowLayoutGeometry.Box(Screen.Area, 12), open.Canvas);
        Assert.Empty(WindowLayoutPlan.Make(layout with { LaunchMissingApplications = false }, [Screen], apps, [], 0).Opens);
        Assert.Empty(WindowLayoutPlan.Make(layout with { Enabled = false }, [Screen], apps, [], 0).Opens);
        Assert.Empty(WindowLayoutPlan.Make(layout, [], apps, [], 0).Opens);
        Assert.Empty(WindowLayoutPlan.Make(layout, [Screen, Screen], apps, [], 0).Opens);
        Assert.Empty(WindowLayoutPlan.Make(layout, [Screen], [], [], 0).Opens);
    }
    [Fact]
    public void ExistingWindowsRemainGreedilyClaimedAndOnlyOneExtraOpenIsRequested()
    {
        var first = Entry(); var second = Entry(); var third = Entry();
        var plan = WindowLayoutPlan.Make(Layout(first, second, third), [Screen], [new(App, "exe:owned")], [Window(1)], 0);
        Assert.Equal(first.Id, Assert.Single(plan.Placements).EntryId); Assert.Equal(second.Id, Assert.Single(plan.Opens).EntryId);
        Assert.Equal(third.Id, Assert.Single(plan.Skipped).EntryId);
    }
    [Fact]
    public async Task FreshWindowsBindDeterministicallyNeverReclaimBaselineAndRetainAuthoredCanvas()
    {
        var open = Open(); var host = new Host { Snapshot = new([Window(1), Window(5), Window(2)]) };
        var result = await host.Run([open], [1]); var bound = Assert.Single(result.Bound);
        Assert.Equal(2, bound.Handle); Assert.Equal(open.Bind(2), bound); Assert.Empty(result.Failed);
        Assert.Equal(1, host.Launches); Assert.Equal(1, result.Launched); Assert.Equal(0, host.Waits);
    }
    [Fact]
    public async Task NoBackgroundPollingOnlySignalsTriggerAnotherBoundedMetadataSnapshot()
    {
        var host = new Host { OnRead = count => count == 1 ? new([]) : new([Window()]), OnWait = _ => Task.CompletedTask };
        Assert.Single((await host.Run()).Bound); Assert.Equal(2, host.Reads); Assert.Equal(1, host.Waits);
    }
    [Fact]
    public async Task DisabledDeletedSourceAndMasterChangesNeverLaunchOrPlace()
    {
        var before = new Host { Current = false }; var skipped = await before.Run();
        Assert.Single(skipped.Failed); Assert.Empty(skipped.Bound); Assert.Equal(0, before.Launches); Assert.Equal(0, before.Reads);
        var during = new Host { OnCurrent = check => check == 1 }; skipped = await during.Run();
        Assert.Single(skipped.Failed); Assert.Empty(skipped.Bound); Assert.Equal(1, skipped.Launched);
    }
    [Fact]
    public async Task LaunchFailureHasNoWaitOrPlacementAndNoRetry()
    {
        var host = new Host { Error = "controlled refusal" }; var result = await host.Run();
        Assert.Contains("controlled refusal", Assert.Single(result.Failed).Reason); Assert.Empty(result.Bound); Assert.Equal(0, result.Launched);
        Assert.Equal(1, host.Launches); Assert.Equal(0, host.Reads); Assert.Equal(0, host.Waits);
    }
    [Fact]
    public async Task PrecancellationPreventsLaunchAndWaitCancellationNeverTerminatesOpenedApps()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var before = new Host();
        var result = await before.Run(token: cancellation.Token); Assert.Equal(0, before.Launches); Assert.Single(result.Failed); Assert.Empty(result.Bound);
        using var during = new CancellationTokenSource(); var host = new Host { Snapshot = new([]), OnWait = token => { during.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; } };
        result = await host.Run(token: during.Token); Assert.Equal(1, result.Launched); Assert.Empty(result.Bound);
        Assert.Contains("left running", Assert.Single(result.Failed).Reason); Assert.Equal(1, host.Reads);
    }
    [Fact]
    public async Task EventlessWaitEndsAtDeadlineWithoutSyntheticTicksOrRetries()
    {
        var host = new Host { Snapshot = new([]) }; var result = await host.Run(budget: TimeSpan.FromMilliseconds(30));
        Assert.Equal(1, result.Launched); Assert.Empty(result.Bound); Assert.Single(result.Failed);
        Assert.Equal(1, host.Reads); Assert.Equal(1, host.Waits);
    }
    [Fact]
    public async Task IncompleteDuplicateOversizedAndUnrelatedInventoriesDoNotGuessNewWindows()
    {
        var host = new Host { Snapshot = new([Window()], true) }; var result = await host.Run();
        Assert.True(result.Limited); Assert.Empty(result.Bound); Assert.Single(result.Failed);
        foreach (var windows in new[] { new[] { Window(), Window() }, new[] { Window(key: "exe:unrelated") }, Enumerable.Repeat(Window(), 513).ToArray() })
        {
            host = new() { Snapshot = new(windows), OnWait = _ => throw new OperationCanceledException() };
            // A deliberately externally cancelled host is tested with a cancelled token at its wait.
            using var stop = new CancellationTokenSource(); host.OnWait = ct => { stop.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
            result = await host.Run(token: stop.Token); Assert.Empty(result.Bound); Assert.Single(result.Failed);
        }
    }
    [Fact]
    public async Task EventFloodCannotCauseUnboundedInventorySweeps()
    {
        var host = new Host { Snapshot = new([]), OnWait = _ => Task.CompletedTask }; var result = await host.Run();
        Assert.Equal(64, host.Reads); Assert.Single(result.Failed); Assert.Empty(result.Bound);
    }
    [Fact]
    public async Task MultipleOpensCannotClaimOneHandleOrLosePerEntryFailures()
    {
        var one = Open(); var two = Open("exe:two"); var host = new Host { OnWait = _ => Task.CompletedTask };
        var result = await host.Run([one, two]); Assert.Equal(one.EntryId, Assert.Single(result.Bound).EntryId);
        Assert.Equal(two.EntryId, Assert.Single(result.Failed).EntryId); Assert.Equal(2, result.Launched);
    }
    [Fact]
    public async Task MalformedOpeningRequestsAndBudgetsHaveNoLaunchSideEffects()
    {
        foreach (var opens in new[] { new[] { Open() with { EntryId = Guid.Empty } }, new[] { Open() with { Frame = new(-500, 0, 300, 300) } },
            new[] { Open(), Open() }, Enumerable.Range(0, 33).Select(i => Open("exe:" + i)).ToArray() })
        { var host = new Host(); Assert.Empty((await host.Run(opens)).Bound); Assert.Equal(0, host.Launches); }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new Host().Run(budget: TimeSpan.FromSeconds(11)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new Host().Run(budget: TimeSpan.Zero));
    }
}
