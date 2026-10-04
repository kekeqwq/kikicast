using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public sealed class ApplicationIconState : INotifyPropertyChanged
{
    public BitmapSource? Image { get; private set; }
    public bool HasImage => Image != null;
    internal string Path = "";
    internal string? Fallback, PackageRoot, AppUserModelId;
    internal int Index;
    internal long Used;
    internal bool Pending, Complete;
    internal void Publish(BitmapSource image) { Image = image; PropertyChanged?.Invoke(this, new(nameof(Image))); PropertyChanged?.Invoke(this, new(nameof(HasImage))); }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ApplicationIconCache : IDisposable
{
    private readonly Dictionary<string, ApplicationIconState> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<ApplicationIconState> requests = Channel.CreateBounded<ApplicationIconState>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dispatcher dispatcher;
    private readonly object gate = new();
    private readonly Task worker;
    private long clock;
    private volatile bool enabled = true, packagedEnabled = true, disposed;
    private static readonly ApplicationIconState empty = new();
    public ApplicationIconCache(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        var token = lifetime.Token;
        worker = Task.Run(async () =>
        {
            try
            {
                await foreach (var state in requests.Reader.ReadAllAsync(token))
                {
                    if (disposed || !enabled || state.AppUserModelId != null && !packagedEnabled) { lock (gate) state.Pending = false; continue; }
                    BitmapSource? image = null;
                    try
                    {
                        if (state.AppUserModelId != null)
                        {
                            var bytes = PackageLogoResource.Read(state.PackageRoot, state.AppUserModelId);
                            if (bytes != null)
                            {
                                using var stream = new System.IO.MemoryStream(bytes, writable: false);
                                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; bitmap.DecodePixelWidth = 64; bitmap.StreamSource = stream;
                                bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
                            }
                        }
                        else
                        {
                            using var icon = IconResource.Read(state.Path, state.Index, 64) ?? (enabled && !disposed && state.Fallback != null ? IconResource.Read(state.Fallback, 0, 64) : null);
                            if (icon != null)
                            {
                                image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.DangerousGetHandle(), System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                image.Freeze();
                            }
                        }
                    }
                    catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or System.IO.IOException or NotSupportedException or FormatException) { }
                    lock (gate)
                    {
                        state.Pending = false; state.Complete = true;
                        if (enabled && !disposed)
                            foreach (var waiting in cache.Values.Where(x => !x.Complete && !x.Pending && (x.AppUserModelId == null || packagedEnabled)).OrderByDescending(x => x.Used).Take(64))
                            { if (!requests.Writer.TryWrite(waiting)) break; waiting.Pending = true; }
                    }
                    if (image != null && !disposed && !dispatcher.HasShutdownStarted)
                    {
                        var captured = image;
                        _ = dispatcher.BeginInvoke(() => { if (!disposed) state.Publish(captured); });
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }
    internal int CachedCount { get { lock (gate) return cache.Count; } }
    public void SetEnabled(bool value) => enabled = value;
    public void SetPackagedEnabled(bool value) => packagedEnabled = value;
    public void Invalidate() { lock (gate) cache.Clear(); }
    public ApplicationIconState Get(LauncherEntry entry)
    {
        if (!enabled || disposed || entry.AppUserModelId != null && !packagedEnabled) return empty;
        var path = entry.AppUserModelId != null ? entry.PackageInstallPath : entry.IconPath ?? entry.ExecutablePath ?? (entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? entry.Path : null);
        if (path == null) return empty;
        var index = entry.IconPath == null ? 0 : entry.IconIndex;
        var fallback = entry.IconPath != null ? entry.ExecutablePath : null;
        var key = index.ToString(CultureInfo.InvariantCulture) + "|" + path + "|" + fallback + "|" + entry.AppUserModelId;
        lock (gate)
        {
            if (!cache.TryGetValue(key, out var state))
            {
                if (cache.Count == 256) cache.Remove(cache.MinBy(x => x.Value.Used).Key);
                state = new() { Path = path, Index = index, Fallback = fallback, AppUserModelId = entry.AppUserModelId, PackageRoot = entry.PackageInstallPath }; cache.Add(key, state);
            }
            state.Used = ++clock;
            if (!state.Complete && !state.Pending) { state.Pending = requests.Writer.TryWrite(state); }
            return state;
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; requests.Writer.TryComplete(); lifetime.Cancel(); lifetime.Dispose();
        lock (gate) cache.Clear();
        _ = worker; // background only; never block the UI on resource IO during shutdown
    }
}

public sealed class ApplicationIconConverter : IMultiValueConverter
{
    public ApplicationIconCache? Cache { get; set; }
    public object? Convert(object[] values, Type type, object parameter, CultureInfo culture) => values.Length == 2 && values[0] is LauncherEntry entry && values[1] is true ? Cache?.Get(entry) : null;
    public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
