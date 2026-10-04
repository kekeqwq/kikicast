using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Kikicast.Core;

namespace Kikicast.Windows;

public sealed class GlobalHotKeys : IDisposable
{
    private delegate nint HookProc(int code, nint wParam, nint lParam);
    private readonly HookProc keyboardCallback;
    private readonly HookProc mouseCallback;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly HashSet<int> held = [];
    private readonly DoubleTapRecognizer detector = new();
    private uint threadId;
    private nint keyboardHook, mouseHook;
    private readonly HashSet<int> registeredIds = [];
    private IReadOnlyList<ActionBinding> activeBindings = [];
    private bool paused;
    private AppPreferences? current;
    private int disposed;
    private uint generation;
    public event Action<ActionBinding>? Triggered;

    public GlobalHotKeys()
    {
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        thread = new Thread(Run) { IsBackground = true, Name = "Kikicast hotkeys" };
        thread.Start();
        ready.Wait();
    }

    public Task<string?> ConfigureAsync(AppPreferences preferences) => Dispatch(() =>
    {
        if (preferences.Validate() is { } invalid) return invalid;
        var previous = current;
        RemoveBindings();
        paused = false;
        var error = Install(preferences);
        if (error == null) { current = preferences; return null; }
        RemoveBindings();
        current = previous;
        if (previous != null && Install(previous) is { } rollback)
            return error + " Restoring the previous bindings also failed: " + rollback + ". Reconfigure them from the tray.";
        return error;
    });

    public Task<string?> PauseAsync() => Dispatch(() =>
    {
        paused = true;
        RemoveBindings();
        return null;
    });

    private Task<string?> Dispatch(Func<string?> operation)
    {
        if (Volatile.Read(ref disposed) != 0) return Task.FromResult<string?>("The shortcut service has exited.");
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue(() =>
        {
            try { completion.TrySetResult(operation()); }
            catch (Exception e) { completion.TrySetException(e); }
        });
        if (!PostThreadMessage(threadId, 0x8002, 0, 0)) completion.TrySetResult("The shortcut thread could not be updated.");
        return completion.Task;
    }

    private string? Install(AppPreferences preferences)
    {
        generation++;
        // Entry registration slots are ephemeral; a changed catalogue must not let an
        // already queued WM_HOTKEY for an old slot dispatch a different entry. Keep the
        // established palette/window IDs unchanged, and rotate item slots below 0xBFFF.
        activeBindings = BindingCatalog.Build(preferences).Select(x => x.EntryId == null ? x
            : x with { Id = x.Id + (int)(generation % 128) * LauncherBindings.MaximumBindings }).ToArray();
        foreach (var item in activeBindings.Where(x => x.Binding.Kind == BindingKind.Combo))
        {
            if (!RegisterHotKey(0, item.Id, item.Binding.Modifiers | 0x4000, (uint)item.Binding.Key))
                return $"{item.Name}: {item.Binding.Display} could not be registered. Windows or another app may already use it.";
            registeredIds.Add(item.Id);
        }
        if (activeBindings.Any(x => x.Binding.Kind == BindingKind.DoubleTap))
        {
            keyboardHook = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
            mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
            if (keyboardHook == 0 || mouseHook == 0) return "The double-tap listener could not be installed.";
            SynchronizePhysicalModifiers();
        }
        return null;
    }

    private void RemoveBindings()
    {
        foreach (var id in registeredIds) UnregisterHotKey(0, id);
        registeredIds.Clear();
        activeBindings = [];
        if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
        held.Clear(); detector.Cancel();
    }

    private void Run()
    {
        threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0);
        ready.Set();
        try
        {
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == 0x8001) SynchronizePhysicalModifiers();
                else if (message.Message == 0x8002) { while (commands.TryDequeue(out var action)) action(); }
                else if (message.Message is 0x0312 or 0x8003)
                {
                    var item = activeBindings.FirstOrDefault(x => x.Id == (int)message.WParam);
                    if (!paused && item != null && (message.Message == 0x8003 ? (uint)message.LParam == generation
                        : item.Binding.Kind == BindingKind.Combo && (uint)((long)message.LParam >> 16 & 0xffff) == item.Binding.Key
                            && ((uint)message.LParam & 15) == item.Binding.Modifiers)) Triggered?.Invoke(item);
                }
                else { TranslateMessage(ref message); DispatchMessage(ref message); }
            }
        }
        finally { RemoveBindings(); }
    }

    private nint Keyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !paused)
        {
            var data = Marshal.PtrToStructure<KeyboardData>(lParam);
            if ((data.Flags & 0x10) != 0) detector.Cancel();
            else
            {
                var key = (int)data.Key;
                var down = wParam == 0x100 || wParam == 0x104;
                var up = wParam == 0x101 || wParam == 0x105;
                var modifier = key is >= 160 and <= 165 or 91 or 92;
                var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                var item = activeBindings.FirstOrDefault(x => x.Binding.AcceptsModifier(key));
                var binding = item?.Binding;
                // General side binding treats the left/right twins as one modifier.
                var identity = binding?.Side == KeySide.Any ? binding.Key : key;
                if (!modifier) { if (down) detector.Cancel(); }
                else if (down && held.Add(key))
                {
                    if (held.Count == 1 && binding != null) detector.Press(identity, now);
                    else detector.Cancel();
                }
                else if (up)
                {
                    var wasHeld = held.Remove(key);
                    if (wasHeld && held.Count == 0 && binding != null)
                    {
                        if (detector.Release(identity, now)) PostThreadMessage(threadId, 0x8003, (nuint)item!.Id, (nint)generation);
                    }
                    else detector.Cancel();
                }
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private nint Mouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam is 0x201 or 0x204 or 0x207 or 0x20B) detector.Cancel();
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void SynchronizePhysicalModifiers()
    {
        held.Clear(); detector.Cancel();
        foreach (var key in new[] { 160, 161, 162, 163, 164, 165, 91, 92 })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) held.Add(key);
    }

    public void Reset() => PostThreadMessage(threadId, 0x8001, 0, 0);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (thread.IsAlive) { PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(); }
        ready.Dispose();
        GC.KeepAlive(keyboardCallback); GC.KeepAlive(mouseCallback);
    }

    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData
    { public uint Key, ScanCode, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MessageData
    { public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MessageData message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MessageData message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MessageData message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MessageData message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
