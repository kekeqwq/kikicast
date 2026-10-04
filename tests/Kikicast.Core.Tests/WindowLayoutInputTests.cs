using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public sealed class WindowLayoutInputTests
{
    private static readonly LayoutDisplay Display = new(@"monitor:\\?\DISPLAY#OwnedInputPanel", "Owned panel");
    private static readonly LayoutScreen Screen = new(Display, new("session", new(0, 0, 1000, 800)));
    private const string App = @"app:C:\Owned.exe";
    private static WindowLayoutEntry Entry(WindowLayoutInput? input) => new(Guid.NewGuid(), App, Display) { Input = input };
    private static WindowLayout Layout(params WindowLayoutEntry[] entries) => new(Guid.NewGuid(), "Owned inputs") { Entries = entries.ToList(), LaunchMissingApplications = true };
    [Theory]
    [InlineData(@"C:\Owned\file.txt")][InlineData(@"C:/Owned/file.txt")][InlineData(@"~/Owned/file.txt")][InlineData(@"~\Owned\folder")]
    public void AuthoredLocalPathsValidateWithoutAnyFilesystemAccess(string path) => Assert.Null(new WindowLayoutInput(WindowLayoutInputKind.File) { Value = path }.Validate());
    [Theory]
    [InlineData(@"\\server\file")][InlineData(@"\\?\C:\file")][InlineData(@"C:\")][InlineData(@"C:relative")][InlineData("relative.txt")]
    [InlineData(@"C:\Owned\..\file")][InlineData(@"C:\Owned\file:stream")][InlineData(@"C:\Owned\*.txt")][InlineData(@"C:\Owned\file.")][InlineData(" C:\\Owned\\file")]
    public void RemoteDeviceRootTraversalAndAmbiguousPathsRefuseAtSchemaBoundary(string path) => Assert.NotNull(new WindowLayoutInput(WindowLayoutInputKind.File) { Value = path }.Validate());
    [Theory]
    [InlineData("https://example.invalid/a%20b?q=X#part")][InlineData("http://localhost:8000/owned")][InlineData("vscode://file/owned")][InlineData("mailto:owned@example.invalid")]
    public void ExplicitWebAndAppUrisRemainDataNotDefaultHandlerRequests(string uri) => Assert.Null(new WindowLayoutInput(WindowLayoutInputKind.Uri) { Value = uri }.Validate());
    [Theory]
    [InlineData("file:///C:/Owned/file")][InlineData("javascript:alert(1)")][InlineData("shell:AppsFolder")][InlineData("ms-msdt:/id")][InlineData("powershell:owned")]
    [InlineData("https://name:secret@example.invalid/")][InlineData("https://example.invalid/a b")][InlineData("https://")][InlineData("x:relative")][InlineData("not-a-uri")]
    public void UnsafeOrCredentialedUriSchemesRefuse(string uri) => Assert.NotNull(new WindowLayoutInput(WindowLayoutInputKind.Uri) { Value = uri }.Validate());
    [Fact]
    public void LiteralArgumentBoundariesEmptySlotsQuotesAndOperatorsSurviveWithoutShellParsing()
    {
        var args = new List<string> { "--new-window", "a b", "a\"b\\", "", "; $(not-run) & | %PATH%" };
        var input = new WindowLayoutInput(WindowLayoutInputKind.Arguments) { Arguments = args }; Assert.Null(input.Validate());
        var copy = JsonSerializer.Deserialize<WindowLayoutInput>(JsonSerializer.Serialize(input))!; Assert.Equal(args, copy.Arguments);
        Assert.NotEqual(input.TargetKey, (input with { Arguments = ["--new-window a b", "a\"b\\", "", "; $(not-run) & | %PATH%"] }).TargetKey);
        Assert.NotNull((input with { Arguments = [] }).Validate()); Assert.NotNull((input with { Arguments = Enumerable.Repeat("a", 9).ToList() }).Validate());
        Assert.NotNull((input with { Arguments = [new('x', 2049)] }).Validate()); Assert.NotNull((input with { Arguments = Enumerable.Repeat(new string('x', 2048), 5).ToList() }).Validate());
        Assert.NotNull((input with { Arguments = [null!] }).Validate()); Assert.NotNull((input with { Arguments = null! }).Validate());
        Assert.NotNull((input with { Arguments = ["a\n"] }).Validate()); Assert.NotNull((input with { Arguments = ["\ud800"] }).Validate());
        Assert.NotNull((input with { Value = "unexpected" }).Validate()); Assert.NotNull(new WindowLayoutInput((WindowLayoutInputKind)80).Validate());
    }
    [Fact]
    public void LegacyNullInputsDuplicateDeepCopyAndPrivacyDoNotExposeAuthoredValuesInSearch()
    {
        var input = new WindowLayoutInput(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/private-input-probe" };
        var layout = Layout(Entry(input)); var json = JsonSerializer.Serialize(layout);
        Assert.DoesNotContain("TargetKey", json); Assert.DoesNotContain("Description", json);
        Assert.Contains("private-input-probe", json); // explicit plaintext authored settings only
        Assert.DoesNotContain("private-input-probe", layout.Summary);
        Assert.Null(LauncherMatch.Match(LauncherSearchText.Create("private-input-probe"), layout.SearchFields.Title));
        var legacy = JsonSerializer.Deserialize<WindowLayout>(JsonSerializer.Serialize(Layout(Entry(null))))!; Assert.Null(legacy.Entries[0].Input);
        var args = new WindowLayoutInput(WindowLayoutInputKind.Arguments) { Arguments = ["one"] }; layout = Layout(Entry(args)); var duplicate = layout.Duplicate("Copy");
        duplicate.Entries[0].Input!.Arguments[0] = "two"; Assert.Equal("one", layout.Entries[0].Input!.Arguments[0]); Assert.NotEqual(layout.Entries[0].Id, duplicate.Entries[0].Id);
    }
    [Fact]
    public void InputEntriesNeverClaimExistingWindowsAndDeduplicateAppPlusExactInputWithoutCaseFoldingArguments()
    {
        var uri = new WindowLayoutInput(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/A" };
        var first = Entry(uri); var duplicate = Entry(uri.Copy()); var different = Entry(uri with { Value = "https://example.invalid/a" });
        var plain = Entry(null); var layout = Layout(first, duplicate, different, plain) with { FrontmostEntryId = first.Id };
        var existing = new LayoutWindow(1, "exe:owned", Screen.Area.WorkArea);
        var plan = WindowLayoutPlan.Make(layout, [Screen], [new(App, "EXE:OWNED")], [existing], 0);
        Assert.Equal(plain.Id, Assert.Single(plan.Placements).EntryId); Assert.Equal(2, plan.Opens.Count); Assert.Equal(first.Id, plan.FrontmostEntryId);
        Assert.Equal(duplicate.Id, Assert.Single(plan.Skipped).EntryId); Assert.Equal(uri.Value, plan.Opens[0].Input!.Value);
        Assert.Empty(WindowLayoutPlan.Make(layout with { LaunchMissingApplications = false }, [Screen], [new(App, "exe:owned")], [existing], 0).Opens);
        Assert.Empty(WindowLayoutPlan.Make(layout, [], [new(App, "exe:owned")], [existing], 0).Opens);
        Assert.Empty(WindowLayoutPlan.Make(layout, [Screen], [], [existing], 0).Opens);
    }
    [Fact]
    public void UnsupportedShortcutExtraInputsAndPackageArgvAreHonestSchemaErrors()
    {
        var input = new WindowLayoutInput(WindowLayoutInputKind.Arguments) { Arguments = ["one"] };
        Assert.NotNull((Entry(input) with { ApplicationId = @"app:C:\Owned.lnk" }).Validate());
        Assert.NotNull((Entry(input) with { ApplicationId = "packaged:Kikicast.Owned_abcdefghijklm!App" }).Validate());
        Assert.Null((Entry(new(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/" }) with { ApplicationId = "packaged:Kikicast.Owned_abcdefghijklm!App" }).Validate());
    }
    [Fact]
    public async Task SequentialSameAppInputsCannotLaunchNextUntilFirstNewWindowBindsOrReclaimSiblingWindows()
    {
        var one = new LayoutOpen(Guid.NewGuid(), App, "exe:owned", new(100, 100, 400, 400), Screen.Area.WorkArea, WindowSizeAnchor.Center, Display.Identity)
            { Input = new(WindowLayoutInputKind.Arguments) { Arguments = ["one"] } };
        var two = one with { EntryId = Guid.NewGuid(), Input = new(WindowLayoutInputKind.Arguments) { Arguments = ["two"] } };
        var launches = 0; var reads = 0; var waited = false;
        var result = await WindowLayoutOpening.RunAsync([one, two], [1], (_, _) => Task.FromResult(true),
            (open, _) => { if (launches == 1) Assert.True(waited); launches++; return Task.FromResult<string?>(null); },
            _ =>
            {
                reads++;
                if (reads == 1) return Task.FromResult(new LayoutOpeningSnapshot([]));
                return Task.FromResult(new LayoutOpeningSnapshot(launches == 1 ? [new(2, "exe:owned", Screen.Area.WorkArea), new(3, "exe:owned", Screen.Area.WorkArea)]
                    : [new(2, "exe:owned", Screen.Area.WorkArea), new(3, "exe:owned", Screen.Area.WorkArea), new(4, "exe:owned", Screen.Area.WorkArea)]));
            }, _ => { waited = true; return Task.CompletedTask; });
        Assert.Equal(new long[] { 2, 4 }, result.Bound.Select(x => x.Handle)); Assert.Equal(2, result.Launched); Assert.Empty(result.Failed);
    }
}
