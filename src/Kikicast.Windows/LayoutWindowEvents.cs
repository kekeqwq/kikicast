using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;

namespace Kikicast.Windows;

// Created and disposed on the invoking message-loop thread, only for an explicit
// layout opening gesture. No process polling, title reads, steady-state timer or
// native work in the callback. Signals coalesce into one bounded slot.
[SupportedOSPlatform("windows")]
public sealed class LayoutWindowEvents : IDisposable
{
    private readonly Channel<byte> changes = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly WinEvent callback;
    private readonly List<nint> hooks = [];
    private bool disposed;
    public bool IsEnabled => !disposed && hooks.Count == 3;
    public LayoutWindowEvents()
    {
        callback = (_, _, hwnd, objectId, childId, _, _) =>
        { if (!disposed && hwnd != 0 && objectId == 0 && childId == 0) changes.Writer.TryWrite(1); };
        foreach (var eventId in new uint[] { 3, 0x8002, 0x800B }) // foreground, show, location (frame becomes usable)
        {
            var hook = SetWinEventHook(eventId, eventId, 0, callback, 0, 0, 2); // OUTOFCONTEXT + SKIPOWNPROCESS
            if (hook == 0) { Dispose(); return; }
            hooks.Add(hook);
        }
    }
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled) throw new InvalidOperationException("Layout window-event observation is unavailable; no apps are launched.");
        await changes.Reader.ReadAsync(cancellationToken);
        // Coalesce active desktop event bursts, not a polling loop. The single
        // immediate read after submission covers shows that preceded this wait.
        await Task.Delay(200, cancellationToken);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var hook in hooks) UnhookWinEvent(hook);
        hooks.Clear(); changes.Writer.TryComplete(); GC.KeepAlive(callback);
    }
    private delegate void WinEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint threadId, uint timestamp);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
