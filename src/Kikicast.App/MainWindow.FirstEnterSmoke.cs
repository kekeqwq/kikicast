using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    internal async Task VerifyFirstEnterAsync(string? evidenceDirectory)
    {
        await indexTask;
        var preferences = store.Preferences;
        var history = store.History;
        var source = new Window { Title = "Kikicast owned first Enter source", Width = 820, Height = 600,
            ShowInTaskbar = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new System.Windows.Controls.TextBox { Text = "Owned source text; never a user window" } };
        var probes = new List<object>();
        var suppressOneEnterKeyUp = true;
        var sawRoutedRepeat = false;
        var sawNativeRepeat = false;
        HwndSourceHook lostKeyUp = (nint hwnd, int message, nint key, nint data, ref bool handled) =>
        {
            if (key == 13 && message == 0x0100) sawNativeRepeat = (data.ToInt64() & (1L << 30)) != 0;
            return 0;
        };
        PreProcessInputEventHandler missingRelease = (_, e) =>
        {
            if (suppressOneEnterKeyUp && e.StagingItem.Input is System.Windows.Input.KeyEventArgs { Key: Key.Enter } key
                && key.RoutedEvent == Keyboard.PreviewKeyUpEvent && key.InputSource == inputSourceForThisWindow())
            { suppressOneEnterKeyUp = false; e.Cancel(); } // Owned seam: release normally goes to another app after Enter closes palette.
        };
        HwndSource inputSourceForThisWindow() => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        System.Windows.Input.KeyEventHandler observe = (_, e) => { if (e.Key == Key.Enter) sawRoutedRepeat = e.IsRepeat; };
        var inputSource = HwndSource.FromHwnd(new WindowInteropHelper(this).EnsureHandle());
        inputSource.AddHook(lostKeyUp);
        InputManager.Current.PreProcessInput += missingRelease;
        AddHandler(Keyboard.PreviewKeyDownEvent, observe, handledEventsToo: true);
        try
        {
            await store.SavePreferencesAsync(preferences with { ApplicationsEnabled = false, WindowManagementEnabled = false,
                SavedCommandsEnabled = false, ExtensionsEnabled = false, ShellCommandsEnabled = false, SystemCommandsEnabled = false,
                CalculationHistoryEnabled = true, ShowSuggestions = false, FavoriteKeys = ["cmd:history"] });
            await store.UpdateHistoryAsync(h => h with { Calculations = [], Launches = [] });
            source.Show();
            var input = (System.Windows.Controls.TextBox)source.Content;
            input.Focus(); Keyboard.Focus(input);
            await OwnedInputAutomation.ClickToActivateAsync(source);
            source.Topmost = false;
            var hwnd = new WindowInteropHelper(source).Handle;
            var original = SourceInputState.Capture(hwnd);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Toggle();
                await Task.Delay(200);
                if (!IsVisible || !HasQueryFocus || Query.Text.Length != 0 || historyMode || Results.SelectedItem is not Row { Command: "history" })
                    throw new InvalidOperationException($"First Enter setup failed: cycle={cycle}, visible={IsVisible}, focus={HasQueryFocus}, empty={Query.Text.Length == 0}, selected={Results.SelectedIndex}, composing={composing}.");
                var wasComposing = composing;
                sawRoutedRepeat = sawNativeRepeat = false;
                await OwnedInputAutomation.ChordAsync(this, 13); // Exactly one physical press; no typing or second Enter.
                var success = historyMode && IsVisible && HasQueryFocus;
                probes.Add(new { cycle, emptyQuery = true, firstRowSelected = true, composingBeforeEnter = wasComposing, routedRepeat = sawRoutedRepeat, nativeRepeat = sawNativeRepeat, singleEnterExecuted = success, lostKeyUpOwnedSeam = cycle == 1 });
                if (evidenceDirectory != null)
                {
                    Directory.CreateDirectory(evidenceDirectory);
                    await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "first-enter-evidence.json"), JsonSerializer.Serialize(new
                    { probes, syntheticOwnedInput = true, realApplicationsLaunched = false, extensionCommandsExecuted = false, wallpaperChanged = false,
                        clipboardChanged = false, physicalDoubleCtrlAccepted = false }));
                }
                if (!success) throw new InvalidOperationException($"First empty-query Enter did not run selected history row: cycle={cycle}, composingBefore={wasComposing}, composingAfter={composing}, selected={Results.SelectedIndex}, history={historyMode}.");
                if (cycle == 1 && (!sawRoutedRepeat || sawNativeRepeat)) throw new InvalidOperationException("Owned missing-key-up seam did not reproduce WPF stale Enter repeat.");
                Dismiss(true); // No intervening key: retain stale WPF repeat from the owned missing-key-up seam.
                if (IsVisible || !WindowActivation.IsForeground(hwnd) || SourceInputState.Capture(hwnd) != original || input.Text != "Owned source text; never a user window")
                    throw new InvalidOperationException("First Enter flow changed owned source text/focus/native input restoration.");
            }
            await OwnedInputAutomation.HoldAcrossOpeningAsync(source, this, 13, async () =>
            { Toggle(); await Task.Delay(200); });
            if (!IsVisible || !HasQueryFocus || historyMode || Query.Text.Length != 0)
                throw new InvalidOperationException("Enter held across opening/repeated must not execute the first row.");
            await OwnedInputAutomation.ChordAsync(this, 13);
            if (!historyMode || !HasQueryFocus) throw new InvalidOperationException("Fresh Enter after held-key release did not execute.");
            Dismiss(true);
            if (!WindowActivation.IsForeground(hwnd) || SourceInputState.Capture(hwnd) != original || input.Text != "Owned source text; never a user window")
                throw new InvalidOperationException("Held Enter flow did not restore owned source input.");
            if (evidenceDirectory != null)
                await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "first-enter-evidence.json"), JsonSerializer.Serialize(new
                { probes, heldAcrossOpeningAndRepeatsRefused = true, freshPressAfterReleaseExecuted = true, sourceInputRestored = true,
                    syntheticOwnedInput = true, realApplicationsLaunched = false, extensionCommandsExecuted = false, wallpaperChanged = false,
                    clipboardChanged = false, physicalDoubleCtrlAccepted = false }));
        }
        finally
        {
            inputSource.RemoveHook(lostKeyUp);
            InputManager.Current.PreProcessInput -= missingRelease;
            RemoveHandler(Keyboard.PreviewKeyDownEvent, observe);
            if (IsVisible) Dismiss(true);
            source.Close();
            await store.SavePreferencesAsync(preferences);
            await store.UpdateHistoryAsync(_ => history);
            Query.Clear(); Refresh();
        }
    }
}
