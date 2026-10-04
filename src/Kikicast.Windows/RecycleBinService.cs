using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kikicast.Core;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public sealed class RecycleBinService : IRecycleBin
{
    public void Open()
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var process = Process.Start(new ProcessStartInfo(explorer, "shell:RecycleBinFolder") { UseShellExecute = true });
    }
    public Task<RecycleBinInfo> QueryAsync() => OnShellThread(() =>
    {
        var info = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        Marshal.ThrowExceptionForHR(SHQueryRecycleBinW(null, ref info));
        return new RecycleBinInfo(info.Items, info.Bytes);
    });
    public Task EmptyConfirmedAsync() => OnShellThread(() =>
    {
        // Explicit application confirmation has already succeeded. No UI from a worker thread.
        // Null root selects the current user's accessible recycle bins; never delete $Recycle.Bin directly.
        Marshal.ThrowExceptionForHR(SHEmptyRecycleBinW(0, null, 1 | 2 | 4));
        return true;
    });
    private static Task<T> OnShellThread<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(operation()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Kikicast Recycle Bin" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueryInfo { public uint Size; public long Bytes, Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHQueryRecycleBinW(string? root, ref QueryInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHEmptyRecycleBinW(nint hwnd, string? root, uint flags);
}
