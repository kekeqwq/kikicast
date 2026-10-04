using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Windows;

// Public Frankfurter daily reference feed; never send the user's amount/query to a server.
public sealed class CurrencyRateService : IDisposable
{
    public const string Feed = "https://api.frankfurter.dev/v1/latest?base=USD";
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string cachePath;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CurrencySnapshot? current;
    private long lastAttemptTicks;
    public CurrencySnapshot? Current => Volatile.Read(ref current);
    public string? Error { get; private set; }
    public CurrencyRateService(string cachePath, HttpClient? client = null, TimeProvider? clock = null)
    {
        this.cachePath = cachePath; this.clock = clock ?? TimeProvider.System;
        ownsClient = client == null;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1_048_576 };
        try
        {
            var saved = JsonFile.Load<CurrencySnapshot?>(cachePath, () => null);
            if (saved?.IsValid == true && saved.FetchedAt <= this.clock.GetUtcNow().AddDays(1)) current = saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
    }
    public bool NeedsRefresh
    {
        get
        {
            var now = clock.GetUtcNow(); var snapshot = Current;
            return (snapshot == null || now - snapshot.FetchedAt >= TimeSpan.FromHours(24))
                && now.UtcTicks - Interlocked.Read(ref lastAttemptTicks) >= TimeSpan.FromMinutes(30).Ticks;
        }
    }
    public async Task RefreshAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!NeedsRefresh) return;
            var now = clock.GetUtcNow(); Interlocked.Exchange(ref lastAttemptTicks, now.UtcTicks);
            try
            {
                var bytes = await client.GetByteArrayAsync(Feed).ConfigureAwait(false);
                if (bytes.Length > 1_048_576) throw new InvalidDataException("Exchange-rate response was too large.");
                using var json = JsonDocument.Parse(bytes);
                var root = json.RootElement;
                var basis = root.GetProperty("base").GetString()!;
                var date = DateOnly.ParseExact(root.GetProperty("date").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var rates = root.GetProperty("rates").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetDouble());
                rates[basis] = 1;
                var snapshot = new CurrencySnapshot(basis, rates, date, now);
                if (!snapshot.IsValid || basis != "USD" || date > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1))
                    throw new InvalidDataException("Invalid exchange-rate data.");
                Volatile.Write(ref current, snapshot); Error = null;
                try { JsonFile.Save(cachePath, snapshot); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = "Rates loaded, but the local cache could not be saved."; }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException
                or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            { Error = "Exchange rates unavailable. Check your connection; cached rates are kept."; }
        }
        finally { gate.Release(); }
    }
    public void Dispose() { if (ownsClient) client.Dispose(); }
}
