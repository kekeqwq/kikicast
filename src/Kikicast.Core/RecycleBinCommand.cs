namespace Kikicast.Core;

public sealed record RecycleBinInfo(long Items, long Bytes);
public enum RecycleBinResult { AlreadyEmpty, Cancelled, Emptied }
public interface IRecycleBin
{
    Task<RecycleBinInfo> QueryAsync();
    Task EmptyConfirmedAsync();
}

public static class RecycleBinCommand
{
    public const string OpenId = "open-recycle-bin", EmptyId = "empty-recycle-bin";
    private static readonly SearchProfile OpenProfile = SearchProfile.Create("Open Recycle Bin open trash open trash bin dakai huishouzhan");
    private static readonly SearchProfile EmptyProfile = SearchProfile.Create("Empty Recycle Bin empty trash empty trash bin qingkong huishouzhan");
    public static int Score(string id, string query) => id switch
    { OpenId => OpenProfile.Score(query), EmptyId => EmptyProfile.Score(query), _ => -1 };

    // All future entry points (including global shortcuts) must go through this confirmation gate.
    public static async Task<RecycleBinResult> EmptyAsync(IRecycleBin backend, Func<RecycleBinInfo, Task<bool>> confirm)
    {
        var info = await backend.QueryAsync();
        if (info.Items < 0 || info.Bytes < 0) throw new InvalidOperationException("Windows returned invalid Recycle Bin statistics.");
        if (info.Items == 0) return RecycleBinResult.AlreadyEmpty;
        if (!await confirm(info)) return RecycleBinResult.Cancelled;
        await backend.EmptyConfirmedAsync();
        return RecycleBinResult.Emptied;
    }
}
