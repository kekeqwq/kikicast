using System.Diagnostics;

namespace Kikicast.Core;

public sealed record LayoutOpeningSnapshot(IReadOnlyList<LayoutWindow> Windows, bool Limited = false);
public sealed record LayoutOpeningResult(IReadOnlyList<LayoutPlacement> Bound, IReadOnlyList<LayoutSkip> Failed, int Launched, bool Limited);

// Gesture-only, event-driven opening. Sequential requests avoid attributing two
// same-app input windows by forbidden title/document/argv inspection. A handler
// that reuses an old window times out rather than moving that window as new.
public static class WindowLayoutOpening
{
    public static async Task<LayoutOpeningResult> RunAsync(IReadOnlyList<LayoutOpen> requests, IReadOnlyCollection<long> baseline,
        Func<LayoutOpen, CancellationToken, Task<bool>> current, Func<LayoutOpen, CancellationToken, Task<string?>> launch,
        Func<CancellationToken, Task<LayoutOpeningSnapshot>> read, Func<CancellationToken, Task> changed,
        CancellationToken cancellationToken = default, TimeSpan? budget = null)
    {
        var timeout = budget ?? TimeSpan.FromSeconds(10);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(budget));
        var opens = requests.ToArray(); var claimed = baseline.ToHashSet();
        var failed = new List<LayoutSkip>(); var bound = new List<LayoutPlacement>();
        if (opens.Length > WindowLayout.MaximumEntries || baseline.Count > 512 || opens.Any(x => x == null || x.EntryId == Guid.Empty
            || !WindowLayout.ValidApplicationId(x.ApplicationId) || string.IsNullOrEmpty(x.MatchKey) || x.MatchKey.Length > 8192 || x.Input?.Validate() != null || x.Input?.ValidateApplication(x.ApplicationId) != null
            || !x.Frame.IsValid || !x.Canvas.IsValid || !LayoutDisplay.ValidIdentity(x.DisplayIdentity) || !Enum.IsDefined(x.Anchor)
            || x.Frame.X < x.Canvas.X - 1 || x.Frame.Y < x.Canvas.Y - 1 || x.Frame.Right > x.Canvas.Right + 1 || x.Frame.Bottom > x.Canvas.Bottom + 1)
            || opens.Select(x => x.EntryId).Distinct().Count() != opens.Length
            || opens.Select(x => x.TargetKey).Distinct(StringComparer.Ordinal).Count() != opens.Length)
            return new([], opens.Where(x => x != null).Select(x => new LayoutSkip(x.EntryId, "Invalid or oversized opening snapshot.")).ToArray(), 0, false);
        if (opens.Length == 0) return new([], [], 0, false);
        opens = opens.Select(x => x with { Input = x.Input?.Copy() }).ToArray();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout); var token = deadline.Token; var elapsed = Stopwatch.StartNew();
        var launched = 0; var limited = false; var processed = 0; var snapshots = 0;
        try
        {
            foreach (var open in opens)
            {
                token.ThrowIfCancellationRequested();
                if (!await current(open, token)) { failed.Add(new(open.EntryId, "Layout, master gate or current application source changed; not launched.")); processed++; continue; }
                token.ThrowIfCancellationRequested();
                if (processed > 0)
                {
                    var beforeNext = await read(token); token.ThrowIfCancellationRequested(); snapshots++;
                    if (beforeNext.Limited || beforeNext.Windows.Count > 512 || snapshots >= 64) { limited = true; break; }
                    foreach (var window in beforeNext.Windows) claimed.Add(window.Handle);
                    if (claimed.Count > 1024) { limited = true; break; }
                }
                var error = await launch(open, token);
                if (error != null) { failed.Add(new(open.EntryId, "Application launch refused: " + error)); processed++; continue; }
                launched++; token.ThrowIfCancellationRequested();
                var finished = false;
                while (!finished)
                {
                    token.ThrowIfCancellationRequested();
                    if (elapsed.Elapsed >= timeout || ++snapshots > 64) break;
                    var snapshot = await read(token); token.ThrowIfCancellationRequested();
                    if (snapshot.Limited || snapshot.Windows.Count > 512) { limited = true; break; }
                    var available = snapshot.Windows.Where(x => x.Handle != 0 && x.Frame.IsValid && !claimed.Contains(x.Handle))
                        .GroupBy(x => x.Handle).Where(x => x.Count() == 1).Select(x => x.Single())
                        .OrderBy(x => x.Frame.Y).ThenBy(x => x.Frame.X).ThenBy(x => x.Handle).ToArray();
                    if (!await current(open, token)) { failed.Add(new(open.EntryId, "Layout, master gate or source changed during wait; window not placed.")); finished = true; break; }
                    token.ThrowIfCancellationRequested();
                    var window = available.Where(x => x.MatchKey.Equals(open.MatchKey, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => Math.Abs(x.Frame.X + x.Frame.Width / 2 - open.Frame.X - open.Frame.Width / 2)
                            + Math.Abs(x.Frame.Y + x.Frame.Height / 2 - open.Frame.Y - open.Frame.Height / 2)).FirstOrDefault();
                    if (window != null)
                    {
                        claimed.Add(window.Handle); bound.Add(open.Bind(window.Handle)); finished = true;
                        // Every observed pre-next-launch handle is excluded too;
                        // startup/sibling windows must not become the next input's result.
                        foreach (var observed in snapshot.Windows) claimed.Add(observed.Handle);
                        if (claimed.Count > 1024) { limited = true; break; }
                        break;
                    }
                    await changed(token); // native signal, never a polling tick
                }
                if (!finished || limited) break;
                processed++;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        var reason = cancellationToken.IsCancellationRequested ? "Cancelled; no remaining launches or placement. Already opened apps are left running."
            : limited ? "Incomplete bounded window inventory; new windows not guessed." : "No new eligible window within the 10-second opening budget/event limit.";
        failed.AddRange(opens.Skip(processed).Where(x => !bound.Any(y => y.EntryId == x.EntryId) && !failed.Any(y => y.EntryId == x.EntryId)).Select(x => new LayoutSkip(x.EntryId, reason)));
        return new(bound, failed, launched, limited);
    }
}
