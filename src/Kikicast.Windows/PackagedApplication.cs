using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kikicast.Core;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public static class PackagedApplication
{
    public static void Activate(LauncherEntry entry, AppPreferences preferences, Func<string, bool>? registered = null, Action<string>? activation = null)
    {
        if (entry.AppUserModelId is not { } id || !PackagedApplicationId.IsValid(id)) throw new ArgumentException("Invalid packaged application identity.");
        if (!entry.IsEnabled(preferences)) throw new InvalidOperationException("Packaged applications are disabled.");
        if (!(registered ?? PackageRegistration.IsRegistered)(id)) throw new InvalidOperationException("This packaged application was removed or is unavailable.");
        (activation ?? ActivateNative)(id);
    }
    internal static void OpenInput(LauncherEntry entry, AppPreferences preferences, PreparedLayoutInput input, Func<string, bool> registered, Action<string, PreparedLayoutInput>? activation)
    {
        if (entry.AppUserModelId is not { } id || !PackagedApplicationId.IsValid(id) || !entry.IsEnabled(preferences) || !registered(id))
            throw new InvalidOperationException("Current packaged application/source is unavailable.");
        if (input.Kind is not (WindowLayoutInputKind.File or WindowLayoutInputKind.Uri)) throw new ArgumentException("Unsupported package input contract.");
        if (activation != null) activation(id, input); else ActivateInputNative(id, input);
    }
    private static void ActivateInputNative(string id, PreparedLayoutInput input)
    {
        object? instance = null;
        using var items = new PackageInputItems(input.Value!); // exactly one validated file/URI, no folder enumeration
        try
        {
            instance = new ApplicationActivationManager(); _ = CoAllowSetForegroundWindow(instance, 0);
            var manager = (IApplicationActivationManager)instance;
            var result = input.Kind == WindowLayoutInputKind.File ? manager.ActivateForFile(id, items.Handle, "open", out _) : manager.ActivateForProtocol(id, items.Handle, out _);
            Marshal.ThrowExceptionForHR(result); // unsupported file/protocol contract refuses; never default-handler fallback
        }
        finally { PackagedApplicationIndex.Release(instance); }
    }
    private static void ActivateNative(string id)
    {
        object? instance = null;
        try
        {
            instance = new ApplicationActivationManager();
            // Delegate existing foreground eligibility to the COM broker via the public
            // COM contract; a denied grant is not bypassed or retried. Activation may
            // still succeed without a foreground grant.
            _ = CoAllowSetForegroundWindow(instance, 0);
            // Public activation API, no shell command line, generic host, elevation or arguments.
            Marshal.ThrowExceptionForHR(((IApplicationActivationManager)instance).ActivateApplication(id, null, 2, out _)); // AO_NOERRORUI: failures go to Kikicast
        }
        finally { PackagedApplicationIndex.Release(instance); }
    }
    [DllImport("ole32.dll")] private static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object instance, nint reserved);
    [ComImport, Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c")] private class ApplicationActivationManager { }
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
        [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string id, nint items, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
        [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string id, nint items, out uint processId);
    }
}
