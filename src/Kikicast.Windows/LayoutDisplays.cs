using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kikicast.Core;

namespace Kikicast.Windows;

// Public active-path/device-name contracts only. No display writes, registry,
// EDID scan, ordinal/name fallback, persistent HMONITOR or file IO on device IDs.
[SupportedOSPlatform("windows")]
public static class LayoutDisplays
{
    public const int MaximumPaths = 128, MaximumModes = 512;
    private const int PathBytes = 72, ModeBytes = 64;
    public sealed record MonitorRecord(string SourceName, DisplayArea Area);
    public sealed record PathRecord(string SourceName, string DevicePath, string Name, Rect SourceBounds);
    public static bool NativeContractsValid => Marshal.SizeOf<SourceName>() == 84 && Marshal.SizeOf<TargetName>() == 420 && Marshal.SizeOf<MonitorInfo>() == 104;
    public static IReadOnlyList<LayoutScreen> Bind(IReadOnlyList<MonitorRecord> monitors, IReadOnlyList<PathRecord> paths)
    {
        if (monitors.Count > WindowCycle.MaximumDisplays || paths.Count > MaximumPaths) return [];
        var uniquePaths = paths.GroupBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() == 1)
            .Select(x => x.Single()).Where(x => LayoutDisplay.ValidIdentity("monitor:" + x.DevicePath))
            .GroupBy(x => x.DevicePath, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() == 1).Select(x => x.Single())
            .ToDictionary(x => x.SourceName, StringComparer.OrdinalIgnoreCase);
        var result = new List<LayoutScreen>();
        foreach (var monitor in monitors.GroupBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() == 1).Select(x => x.Single()))
        {
            if (!uniquePaths.TryGetValue(monitor.SourceName, out var path) || !monitor.Area.WorkArea.IsValid || path.SourceBounds != monitor.Area.Bounds) continue;
            var name = string.IsNullOrWhiteSpace(path.Name) ? "Display" : path.Name.Trim();
            var display = new LayoutDisplay("monitor:" + path.DevicePath.ToLowerInvariant(), name);
            if (display.Validate() == null) result.Add(new(display, monitor.Area));
        }
        return result;
    }
    public static IReadOnlyList<LayoutScreen> Read()
    {
        if (!NativeContractsValid) return [];
        var monitors = ReadMonitors();
        if (monitors.Count == 0) return [];
        // Buffer sizes can race a topology change. At most two read attempts;
        // coordinates must also agree with the native monitor snapshot.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount) != 0
                || pathCount is < 1 or > MaximumPaths || modeCount is < 1 or > MaximumModes) return [];
            var pathCapacity = pathCount; var modeCapacity = modeCount;
            var pathBuffer = Marshal.AllocHGlobal(checked((int)pathCount * PathBytes));
            try
            {
                var modeBuffer = Marshal.AllocHGlobal(checked((int)modeCount * ModeBytes));
                try
                {
                    var status = QueryDisplayConfig(2, ref pathCount, pathBuffer, ref modeCount, modeBuffer, 0);
                    if (status == 122) continue;
                    if (status != 0 || pathCount > pathCapacity || modeCount > modeCapacity) return [];
                    var records = new List<PathRecord>();
                    for (var i = 0; i < pathCount; i++)
                    {
                        var p = pathBuffer + i * PathBytes;
                        if ((Marshal.ReadInt32(p, 68) & 1) == 0 || Marshal.ReadInt32(p, 60) == 0) continue;
                        var sourceId = unchecked((uint)Marshal.ReadInt32(p, 8));
                        var sourceAdapter = new Luid { Low = unchecked((uint)Marshal.ReadInt32(p)), High = Marshal.ReadInt32(p, 4) };
                        var modeIndex = unchecked((uint)Marshal.ReadInt32(p, 12));
                        if (modeIndex >= modeCount) continue;
                        var m = modeBuffer + checked((int)modeIndex * ModeBytes);
                        if (Marshal.ReadInt32(m) != 1 || unchecked((uint)Marshal.ReadInt32(m, 4)) != sourceId
                            || Marshal.ReadInt32(m, 8) != unchecked((int)sourceAdapter.Low) || Marshal.ReadInt32(m, 12) != sourceAdapter.High) continue;
                        var width = unchecked((uint)Marshal.ReadInt32(m, 16)); var height = unchecked((uint)Marshal.ReadInt32(m, 20));
                        if (width is < 1 or > 32768 || height is < 1 or > 32768) continue;
                        var bounds = new Rect(Marshal.ReadInt32(m, 28), Marshal.ReadInt32(m, 32), width, height);
                        var source = new SourceName { Header = new() { Type = 1, Size = 84, Adapter = sourceAdapter, Id = sourceId }, Name = "" };
                        var target = new TargetName { Header = new() { Type = 2, Size = 420,
                            Adapter = new() { Low = unchecked((uint)Marshal.ReadInt32(p, 20)), High = Marshal.ReadInt32(p, 24) }, Id = unchecked((uint)Marshal.ReadInt32(p, 28)) }, Name = "", DevicePath = "" };
                        if (ReadSourceName(ref source) == 0 && ReadTargetName(ref target) == 0)
                            records.Add(new(source.Name, target.DevicePath, target.Name, bounds));
                    }
                    return Bind(monitors, records);
                }
                finally { Marshal.FreeHGlobal(modeBuffer); }
            }
            finally { Marshal.FreeHGlobal(pathBuffer); }
        }
        return [];
    }
    private static IReadOnlyList<MonitorRecord> ReadMonitors()
    {
        var records = new List<MonitorRecord>(); var invalid = false;
        MonitorCallback callback = (nint monitor, nint hdc, ref NativeRect bounds, nint state) =>
        {
            if (records.Count >= WindowCycle.MaximumDisplays) { invalid = true; return false; }
            var info = new MonitorInfo { Size = 104, Name = "" };
            if (!GetMonitorInfoW(monitor, ref info)) { invalid = true; return false; }
            var scale = GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x / 96d : 1;
            var b = info.Bounds; var r = info.Work;
            records.Add(new(info.Name, new(monitor.ToString(), new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), scale,
                new(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top))));
            return true;
        };
        var ok = EnumDisplayMonitors(0, 0, callback, 0); GC.KeepAlive(callback);
        return ok && !invalid ? records : [];
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
    { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    { public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public uint Size; public NativeRect Bounds, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    private delegate bool MonitorCallback(nint monitor, nint hdc, ref NativeRect bounds, nint state);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, nint pathBuffer, ref uint modes, nint modeBuffer, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)] private static extern int ReadSourceName(ref SourceName source);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)] private static extern int ReadTargetName(ref TargetName target);
}
