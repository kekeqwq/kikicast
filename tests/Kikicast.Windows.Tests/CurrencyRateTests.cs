using System.Net;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class CurrencyRateTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Feed : HttpMessageHandler
    {
        public int Calls;
        public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Equal(CurrencyRateService.Feed, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent("{\"base\":\"USD\",\"date\":\"2026-10-02\",\"rates\":{\"CNY\":7,\"EUR\":0.9}}") });
        }
    }
    [Fact]
    public async Task RequestsAreCoalescedCachedAndFailuresKeepOldSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "KikicastRates-" + Guid.NewGuid());
        var path = Path.Combine(directory, "currency.json");
        var clock = new Clock(); var feed = new Feed();
        using var http = new HttpClient(feed);
        try
        {
            using var service = new CurrencyRateService(path, http, clock);
            await Task.WhenAll(service.RefreshAsync(), service.RefreshAsync());
            Assert.Equal(1, feed.Calls);
            Assert.Equal("7.00", CurrencyConversion.Parse("1 usd to cny")!.Answer(service.Current));
            using var reopened = new CurrencyRateService(path, http, clock);
            Assert.False(reopened.NeedsRefresh);
            clock.Now = clock.Now.AddDays(2); feed.Fail = true;
            var original = File.ReadAllBytes(path);
            await service.RefreshAsync();
            Assert.NotNull(service.Error);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal("7.00", CurrencyConversion.Parse("1 usd to cny")!.Answer(service.Current));
            await service.RefreshAsync(); Assert.Equal(2, feed.Calls); // 30-minute failure backoff
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
