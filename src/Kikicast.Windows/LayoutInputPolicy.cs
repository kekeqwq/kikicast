using System.Runtime.Versioning;
using Kikicast.Core;

namespace Kikicast.Windows;

public sealed record PreparedLayoutInput(WindowLayoutInputKind Kind, string? Value, IReadOnlyList<string> Arguments);
[SupportedOSPlatform("windows")]
public static class LayoutInputPolicy
{
    public static PreparedLayoutInput Prepare(LauncherEntry entry, WindowLayoutInput input)
    {
        if (input.Validate() is { } error) throw new ArgumentException(error);
        if (entry.AppUserModelId == null && !entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Saved inputs require a direct EXE or registered packaged application. Original shortcut launches/arguments are not replaced or appended.");
        if (entry.AppUserModelId != null && input.Kind == WindowLayoutInputKind.Arguments)
            throw new InvalidOperationException("Literal argv lists require a direct EXE; package activation arguments are app-specific and not emulated.");
        if (input.Kind == WindowLayoutInputKind.File)
        {
            var path = ApplicationFolders.Expand(input.Value!);
            if (!LocalPathSafety.TryInspect(path, out var attributes)) throw new InvalidOperationException("Saved file/folder is missing, remote or linked; not opened.");
            if (entry.AppUserModelId != null && (attributes & FileAttributes.Directory) != 0)
                throw new InvalidOperationException("Packaged file activation requires a file, not a folder.");
            return new(input.Kind, path, []);
        }
        return new(input.Kind, input.Value, input.Arguments.ToArray());
    }
}
