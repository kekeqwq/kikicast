using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class RecycleBinTests
{
    private sealed class FakeBin(long count = 2, long bytes = 100) : IRecycleBin
    {
        public int Deletes;
        public bool Fail;
        public Task<RecycleBinInfo> QueryAsync() => Task.FromResult(new RecycleBinInfo(count, bytes));
        public Task EmptyConfirmedAsync()
        { Deletes++; return Fail ? Task.FromException(new IOException("Access denied")) : Task.CompletedTask; }
    }
    [Fact]
    public async Task EmptyBinDoesNotConfirmOrDelete()
    {
        var bin = new FakeBin(0);
        var result = await RecycleBinCommand.EmptyAsync(bin, _ => throw new Exception("Unexpected confirmation"));
        Assert.Equal(RecycleBinResult.AlreadyEmpty, result);
        Assert.Equal(0, bin.Deletes);
    }
    [Fact]
    public async Task CancellationNeverDeletes()
    {
        var bin = new FakeBin();
        Assert.Equal(RecycleBinResult.Cancelled, await RecycleBinCommand.EmptyAsync(bin, _ => Task.FromResult(false)));
        Assert.Equal(0, bin.Deletes);
    }
    [Fact]
    public async Task ConfirmationPrecedesDeletionAndFailureIsNotSuccess()
    {
        var bin = new FakeBin();
        Assert.Equal(RecycleBinResult.Emptied, await RecycleBinCommand.EmptyAsync(bin, info =>
        { Assert.Equal(0, bin.Deletes); Assert.Equal(2, info.Items); return Task.FromResult(true); }));
        Assert.Equal(1, bin.Deletes);
        bin.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => RecycleBinCommand.EmptyAsync(bin, _ => Task.FromResult(true)));
    }
    [Theory]
    [InlineData(-1, 0)][InlineData(0, -1)]
    public async Task InvalidStatisticsNeverDelete(long count, long bytes)
    {
        var bin = new FakeBin(count, bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RecycleBinCommand.EmptyAsync(bin, _ => Task.FromResult(true)));
        Assert.Equal(0, bin.Deletes);
    }
    [Theory]
    [InlineData(RecycleBinCommand.OpenId, "open trash")]
    [InlineData(RecycleBinCommand.OpenId, "open trash bin")]
    [InlineData(RecycleBinCommand.EmptyId, "empty trash")]
    [InlineData(RecycleBinCommand.EmptyId, "empty trash bin")]
    public void OriginalQueriesStaySearchable(string id, string query) => Assert.True(RecycleBinCommand.Score(id, query) >= 0);
}
