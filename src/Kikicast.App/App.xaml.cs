using System.IO;
using System.Windows;
using System.Windows.Input;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private bool ownsInstance, settingsOpening;
    private GlobalHotKeys? hotkeys;
    private System.Windows.Forms.NotifyIcon? tray;
    private System.Windows.Forms.ContextMenuStrip? trayMenu;
    private UserStore? store;
    private MainWindow? palette;
    private SettingsWindow? settingsWindow;
    private string? smokeDirectory;
    private ThemeService? theme;
    private RunningApplicationDiscovery? discovery;
    private SavedCommandStore? commands;
    private System.Windows.Forms.ToolStripItem? shellMenuItem;
    private readonly List<ShellCommandWindow> shellWindows = [];
    private bool automateOwnedInput;
    private string? evidenceDirectory;
    private Window? automationHost;
    private ForegroundTarget? automationTarget;
    private bool automationSettingsPassed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        const string channel = "Kikicast.Dev";
#else
        const string channel = "Kikicast";
#endif
        var smoke = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        if (!smoke && e.Args.Any(x => x.StartsWith("--smoke-", StringComparison.Ordinal)))
        { Console.Error.WriteLine("Smoke-only arguments require the exact --smoke-test flag; refusing normal-profile startup."); Shutdown(1); return; }
        automateOwnedInput = smoke && e.Args.Contains("--smoke-automate-input", StringComparer.Ordinal);
        var evidenceArgument = Array.IndexOf(e.Args, "--smoke-evidence");
        if (automateOwnedInput && evidenceArgument >= 0 && evidenceArgument + 1 < e.Args.Length) evidenceDirectory = Path.GetFullPath(e.Args[evidenceArgument + 1]);
        var instanceName = smoke ? channel + ".Smoke." + Guid.NewGuid().ToString("N") : channel;
        instance = new Mutex(true, "Local\\" + instanceName, out ownsInstance);
        if (!ownsInstance) { Shutdown(); return; }
        string? migrationWarning = null;
        string directory;
        if (smoke) directory = smokeDirectory = Path.Combine(Path.GetTempPath(), "KikicastSmoke-" + Guid.NewGuid().ToString("N"));
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            try { directory = UserDataDirectory.Prepare(home, local, channel == "Kikicast.Dev"); }
            catch (Exception ex) when (UserStore.IsStorageError(ex))
            {
                // A partial migration never costs history: stay on the old channel until retry succeeds.
                directory = Path.Combine(local, channel);
                migrationWarning = "Home configuration migration failed; using the previous data folder for this session: " + ex.Message;
            }
        }
        store = new UserStore(directory);
        theme = new ThemeService(this);
        var cacheDirectory = Path.Combine(smoke ? directory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), channel), "Cache");
        discovery = new RunningApplicationDiscovery(Path.Combine(directory, "discovered-apps.json"));
        discovery.SetEnabled(!smoke && store.Preferences.ApplicationsEnabled && store.Preferences.DiscoverRunningApplications);
        commands = new SavedCommandStore(Path.Combine(directory, "commands.json"));
        palette = new MainWindow(store, cacheDirectory, discovery, commands);
        if (commands.Warning != null) palette.SetStatus(commands.Warning);
        discovery.Failed += text => Dispatcher.BeginInvoke(() => palette.SetStatus(text));
        if (discovery.Warning != null) palette.SetStatus(discovery.Warning);
        theme.Register(palette, true);
        MainWindow = palette;
        palette.SettingsRequested += () => ShowSettings();
        palette.LauncherSettingsRequested += () => ShowSettings("Launcher");
        palette.ShellRequested += ShowShellCommand;
        palette.ActionFailed += text => tray?.ShowBalloonTip(3000, "Kikicast", text, System.Windows.Forms.ToolTipIcon.Warning);
        palette.ActionCompleted += text => tray?.ShowBalloonTip(2000, "Kikicast", text, System.Windows.Forms.ToolTipIcon.Info);
        store.WriteFailed += text => Dispatcher.BeginInvoke(() => palette.SetStatus(text));
        var menu = trayMenu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        shellMenuItem = menu.Items.Add("Shell commands…", null, (_, _) => ShowShellCommand("", false));
        shellMenuItem.Enabled = store.Preferences.ShellCommandsEnabled;
        var cancelLayout = menu.Items.Add("Cancel running window layout", null, (_, _) => palette.CancelCurrentLayout());
        cancelLayout.Enabled = false; palette.LayoutRunningChanged += running => cancelLayout.Enabled = running;
        menu.Items.Add("Exit", null, async (_, _) =>
        {
            if (palette.IsExecuting) { tray?.ShowBalloonTip(2000, "Kikicast", "Wait for the current action to finish.", System.Windows.Forms.ToolTipIcon.Info); return; }
            await store.FlushAsync(); await commands.FlushAsync(); Shutdown();
        });
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "kikicast.ico");
        tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "Kikicast", Icon = new System.Drawing.Icon(iconPath),
            ContextMenuStrip = menu, Visible = true
        };
        tray.DoubleClick += (_, _) => ShowSettings();
        hotkeys = new GlobalHotKeys();
        hotkeys.Triggered += action => Dispatcher.BeginInvoke(() => DispatchHotKey(action));
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
        var error = await hotkeys.ConfigureAsync(store.Preferences);
        if (error != null)
        {
            // Keep the primary available if only the optional fallback was unavailable.
            var primaryError = await hotkeys.ConfigureAsync(store.Preferences with { AltSpaceFallback = false, WindowBindings = [], EntryBindings = [] });
            palette.SetStatus(error + (primaryError == null ? " The primary shortcut is still available." : " Reconfigure it from the tray."));
        }
        if (migrationWarning != null || store.LoadWarning != null)
            palette.SetStatus(string.Join("\n", new[] { migrationWarning, store.LoadWarning }.OfType<string>()));
        palette.SetShortcutLabel(store.Preferences);
        // Normal startup never activates the palette. Only hotkeys summon it.
        if (smoke && !e.Args.Contains("--smoke-test-startup", StringComparer.Ordinal)
            && !e.Args.Contains("--smoke-test-windows", StringComparer.Ordinal)
            && !e.Args.Contains("--smoke-test-placement", StringComparer.Ordinal)
            && !e.Args.Contains("--smoke-test-models", StringComparer.Ordinal))
        {
            if (automateOwnedInput)
            {
                automationHost = new Window { Title = "Kikicast owned activation probe", Width = 820, Height = 600,
                    ShowInTaskbar = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new System.Windows.Controls.TextBox { Text = "Owned automation fixture — no user content" } };
                automationHost.Show(); await Task.Delay(150);
                await OwnedInputAutomation.ClickToActivateAsync(automationHost, evidenceDirectory == null ? null : Path.Combine(evidenceDirectory, "activation-point.png"));
            }
            palette.Toggle();
            if (automationHost != null) automationHost.Topmost = false;
            if (!palette.IsEnglishInputActive)
            { Console.Error.WriteLine($"English keyboard session was not activated · visible={palette.IsVisible}, focus={palette.HasQueryFocus}, status={palette.ActivationDiagnostic}"); Shutdown(1); return; }
        }
        if (smoke && e.Args.Contains("--smoke-test-models", StringComparer.Ordinal))
        {
            try
            {
                await palette.VerifyLauncherSectionsAsync(); await palette.VerifyFeatureSettingsAsync();
                await palette.VerifyLauncherActionsAsync(verifyFocus: false); await palette.VerifyArgumentFieldsAsync();
                await palette.VerifyApplicationFolderSettingsAsync(); await palette.VerifySearchRankingAsync(); await palette.VerifyEntryBindingModelsAsync(); await palette.VerifyPackagedApplicationsAsync(evidenceDirectory); await palette.VerifyCustomWindowSizeModelsAsync(evidenceDirectory); await palette.VerifyLayoutModelsAsync();
                ShowSettings("Launcher"); // Validate actual category/binding rows without requiring palette activation.
                var modelTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                modelTimer.Tick += (_, _) => { modelTimer.Stop(); Shutdown(); }; modelTimer.Start();
            }
            catch (Exception ex) { Console.Error.WriteLine("WPF model smoke failed: " + ex); Shutdown(1); }
            return;
        }
        if (smoke && (e.Args.Contains("--smoke-test-windows", StringComparer.Ordinal) || e.Args.Contains("--smoke-test-placement", StringComparer.Ordinal)))
        {
            var fixtureArgument = Array.IndexOf(e.Args, "--discovery-fixture");
            await RunWindowSmokeTestAsync(fixtureArgument >= 0 && fixtureArgument + 1 < e.Args.Length ? e.Args[fixtureArgument + 1] : null, e.Args.Contains("--smoke-test-placement", StringComparer.Ordinal));
            return;
        }
        if (smoke)
        {
            if (automateOwnedInput && !e.Args.Contains("--smoke-test-startup", StringComparer.Ordinal))
                await palette.VerifySimulatedKeysAsync(evidenceDirectory, verifyRegisteredActivation: false);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(automateOwnedInput && e.Args.Contains("--smoke-test-settings", StringComparer.Ordinal) ? 60 : 5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (automateOwnedInput && e.Args.Contains("--smoke-test-settings", StringComparer.Ordinal) && !automationSettingsPassed)
                { Console.Error.WriteLine("Settings input smoke did not finish before its bounded deadline."); Shutdown(1); }
                else Shutdown();
            };
            timer.Start();
            if (e.Args.Contains("--smoke-test-settings", StringComparer.Ordinal))
                _ = Dispatcher.BeginInvoke(() => ShowSettings());
        }
    }

    private async void DispatchHotKey(ActionBinding action)
    {
        if (settingsOpening || palette == null || store == null || !LauncherBindings.IsCurrent(store.Preferences, action)) return;
        var captured = automateOwnedInput && automationTarget is { } owned && WindowActivation.IsForeground(owned.Handle) ? owned : null;
        if (action.EntryId != null) await palette.RunEntryBindingAsync(action, captured);
        else if (action.WindowAction is { } windowAction) await palette.RunWindowActionAsync(windowAction, global: true, capturedWindow: captured);
        else if (captured != null && !palette.IsVisible) palette.ToggleFrom(captured);
        else palette.Toggle();
    }

    private async Task RunWindowSmokeTestAsync(string? discoveryFixture, bool placementOnly)
    {
        // Operate only on a test fixture owned by this process, never the user's windows.
        palette?.HideForSettings();
        var fixture = new Window { Title = "Kikicast window test", Width = 500, Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false };
        var focusFixture = new Window { Title = "Kikicast focus test", Width = 360, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false,
            Topmost = automateOwnedInput,
            Content = new System.Windows.Controls.TextBox { Name = "FocusInput" } };
        var exitCode = 0;
        try
        {
            fixture.Show();
            await Task.Delay(150);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(fixture).Handle;
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var target = new ForegroundTarget(hwnd, (uint)process.Id, process.StartTime);
            var before = WindowManager.ReadFrame(hwnd) ?? throw new InvalidOperationException("Fixture frame unavailable");
            if (hotkeys != null && store != null)
            {
                var configured = store.Preferences with { AltSpaceFallback = false, WindowManagementEnabled = true,
                    WindowBindings = new() { [WindowAction.LeftHalf] = new(BindingKind.Combo, 134, 7),
                        [WindowAction.RightHalf] = new(BindingKind.DoubleTap, 18, Side: KeySide.Right) } };
                if (await hotkeys.ConfigureAsync(configured) is { } bindingError) throw new InvalidOperationException(bindingError);
                var conflicting = configured with { WindowBindings = new() { [WindowAction.LeftHalf] = configured.PaletteBinding! } };
                if (await hotkeys.ConfigureAsync(conflicting) == null) throw new InvalidOperationException("Conflict was accepted");
                await hotkeys.PauseAsync();
                if (await hotkeys.ConfigureAsync(store.Preferences with { AltSpaceFallback = false }) is { } resumeError)
                    throw new InvalidOperationException(resumeError);
            }
            var manager = new WindowManager();
            if (await manager.ExecuteAsync(WindowAction.LeftHalf, target, cycleHalfSizes: true) is { } leftError) throw new InvalidOperationException(leftError);
            var left = WindowManager.ReadFrame(hwnd) ?? throw new InvalidOperationException("Left frame unavailable");
            var work = (WindowManager.DisplayForWindow(hwnd) ?? throw new InvalidOperationException("Left-half fixture display unavailable.")).WorkArea;
            if (Math.Abs(left.X - work.X) > 4 || Math.Abs(left.Width - work.Width / 2) > 4)
                throw new InvalidOperationException("Left-half geometry mismatch");
            for (var step = 1; step <= 2; step++)
            {
                if (await manager.ExecuteAsync(WindowAction.LeftHalf, target, cycleHalfSizes: true) is { } cycleError)
                    throw new InvalidOperationException(cycleError);
                var cycled = WindowManager.ReadFrame(hwnd) ?? throw new InvalidOperationException("Cycle frame unavailable");
                if (Math.Abs(cycled.Width - work.Width * WindowCycle.Fraction(step)) > 4)
                    throw new InvalidOperationException("Cycle geometry mismatch");
            }
            if (await manager.ExecuteAsync(WindowAction.RightHalf, target) is { } rightError) throw new InvalidOperationException(rightError);
            if (await manager.ExecuteAsync(WindowAction.Restore, target) is { } restoreError) throw new InvalidOperationException(restoreError);
            await Task.Delay(150);
            var after = WindowManager.ReadFrame(hwnd) ?? throw new InvalidOperationException("Restored frame unavailable");
            if (Math.Abs(before.X - after.X) > 4 || Math.Abs(before.Y - after.Y) > 4
                || Math.Abs(before.Width - after.Width) > 4 || Math.Abs(before.Height - after.Height) > 4)
                throw new InvalidOperationException("Restore geometry mismatch");
            foreach (var action in Enum.GetValues<WindowAction>().Where(x => (int)x >= 22))
            {
                var starting = WindowManager.ReadFrame(hwnd) ?? throw new InvalidOperationException("Additional-action fixture frame unavailable");
                var host = WindowManager.DisplayForWindow(hwnd) ?? throw new InvalidOperationException("Additional-action fixture display unavailable.");
                var expected = WindowGeometry.Place(action, starting, host, WindowManager.Displays())!.Value;
                if (await manager.ExecuteAsync(action, target) is { } actionError) throw new InvalidOperationException(action + ": " + actionError);
                var observed = WindowManager.ReadFrame(hwnd)!.Value;
                if (Math.Abs(observed.X - expected.X) > 4 || Math.Abs(observed.Y - expected.Y) > 4
                    || Math.Abs(observed.Width - expected.Width) > 4 || Math.Abs(observed.Height - expected.Height) > 4)
                    throw new InvalidOperationException("Additional-action geometry mismatch: " + action + " observed=" + observed + ", expected=" + expected + ", display=" + host.Id);
                if (await manager.ExecuteAsync(WindowAction.Restore, target) is { } undoError) throw new InvalidOperationException(action + " restore: " + undoError);
            }
            await palette!.VerifyCustomWindowSizeNativeAsync(fixture, target, evidenceDirectory);
            if (placementOnly) return;
            if (automateOwnedInput)
            {
                fixture.Topmost = true;
                await OwnedInputAutomation.ClickToActivateAsync(fixture);
                automationTarget = target;
                try
                {
                    await palette.VerifyRegisteredCustomSizeAsync(fixture, target, hotkeys!, evidenceDirectory);
                    await palette.VerifyRegisteredHalfCycleAsync(fixture, target, hotkeys!, evidenceDirectory);
                    await palette.VerifyRegisteredLayoutAsync(fixture, target, hotkeys!, evidenceDirectory);
                }
                finally { automationTarget = null; fixture.Topmost = false; }
            }
            if (automateOwnedInput && discoveryFixture != null) await palette!.VerifyMissingLayoutLaunchAsync(fixture, hotkeys!, discoveryFixture, evidenceDirectory);
            focusFixture.Show();
            var focusBox = (System.Windows.Controls.TextBox)focusFixture.Content;
            focusBox.Focus(); Keyboard.Focus(focusBox);
            var focusHwnd = new System.Windows.Interop.WindowInteropHelper(focusFixture).Handle;
            if (automateOwnedInput) await OwnedInputAutomation.ClickToActivateAsync(focusFixture, evidenceDirectory == null ? null : Path.Combine(evidenceDirectory, "activation-point.png"));
            if (!WindowActivation.Once(focusHwnd, focusHost: true) || !WindowActivation.IsForeground(focusHwnd))
                throw new InvalidOperationException("Focus fixture could not be activated");
            focusBox.Focus(); Keyboard.Focus(focusBox);
            var sourceLayout = automateOwnedInput ? InputLanguageManager.Current.AvailableInputLanguages.Cast<System.Globalization.CultureInfo>()
                .FirstOrDefault(x => x.TwoLetterISOLanguageName == "zh") : null;
            if (sourceLayout != null)
            {
                InputLanguageManager.Current.CurrentInputLanguage = sourceLayout;
                await Task.Delay(100);
            }
            var beforeLayout = InputLanguageManager.Current.CurrentInputLanguage;
            var beforeSourceState = SourceInputState.Capture(focusHwnd);
            if (automateOwnedInput)
            {
                System.Windows.Input.InputMethod.SetIsInputMethodEnabled(focusBox, false);
                await OwnedInputAutomation.TextAsync(focusFixture, "Owned source input");
                System.Windows.Input.InputMethod.SetIsInputMethodEnabled(focusBox, true);
                await Task.Delay(80); beforeSourceState = SourceInputState.Capture(focusHwnd);
                automationTarget = new(focusHwnd, (uint)process.Id, process.StartTime);
                var combo = LauncherBindings.Set(store!.Preferences with { PaletteBinding = new(BindingKind.Combo, 134, 3), AltSpaceFallback = false }, "cmd:history", new(BindingKind.Combo, 135, 3));
                await store.UpdatePreferencesAsync(_ => combo);
                if (await hotkeys!.ConfigureAsync(combo) is { } comboError) throw new InvalidOperationException(comboError);
                await OwnedInputAutomation.ChordAsync(focusFixture, 135, 17, 18);
                await Task.Delay(200);
                if (!palette!.IsVisible || !palette.IsHistoryMode || !palette.HasQueryFocus || !palette.IsEnglishInputActive)
                    throw new InvalidOperationException("Registered item chord did not open the owned history mode.");
                await OwnedInputAutomation.ChordAsync(palette, 135, 17, 18);
                await Task.Delay(200);
                if (palette.IsVisible || SourceInputState.Capture(focusHwnd) != beforeSourceState || !WindowActivation.IsForeground(focusHwnd))
                    throw new InvalidOperationException("Registered history toggle did not restore the exact owned source state.");
                if (evidenceDirectory != null) await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "global-item-evidence.json"), "{\"registeredCtrlAltF24\":true,\"historyModeToggle\":true,\"nativeSourceStateExact\":true,\"scope\":\"Owned synthetic input; not physical hotkey/IME acceptance\"}");
                await OwnedInputAutomation.ChordAsync(focusFixture, 134, 17, 18); // Exclusive registered Ctrl+Alt+F23, not injected double-Ctrl.
            }
            else palette?.ToggleFrom(new(focusHwnd, (uint)process.Id, process.StartTime));
            await Task.Delay(250);
            if (palette?.IsVisible != true || palette.InvocationTarget?.Handle != focusHwnd || !palette.HasQueryFocus || !palette.IsEnglishInputActive)
                throw new InvalidOperationException("Palette did not capture the fixture and query focus: " + palette?.ActivationDiagnostic);
            if (palette.WantsBackdrop != palette.HasBackdrop)
                throw new InvalidOperationException("Palette did not capture its enabled background, or captured with transparency disabled");
            await palette.VerifyLauncherSectionsAsync();
            await palette.VerifyFeatureSettingsAsync();
            await palette.VerifyLauncherActionsAsync();
            await palette.VerifyArgumentFieldsAsync();
            await palette.VerifyApplicationFolderSettingsAsync(); await palette.VerifySearchRankingAsync(); await palette.VerifyEntryBindingModelsAsync(); await palette.VerifyPackagedApplicationsAsync(evidenceDirectory); await palette.VerifyCustomWindowSizeModelsAsync(evidenceDirectory); await palette.VerifyLayoutModelsAsync();
            if (automateOwnedInput) await palette.VerifySimulatedKeysAsync(evidenceDirectory);
            var binInfo = await new RecycleBinService().QueryAsync(); // Read-only; never empty the real bin in smoke.
            if (binInfo.Items < 0 || binInfo.Bytes < 0) throw new InvalidOperationException("Invalid native Recycle Bin query");
            if (!palette.HasQueryFocus || !palette.IsEnglishInputActive)
                throw new InvalidOperationException("Grouped launcher refresh took query focus or English input");
            if (automateOwnedInput) await OwnedInputAutomation.ChordAsync(palette, 27);
            else palette.Toggle();
            await Task.Delay(250);
            if (palette.IsVisible || palette.HasBackdrop || !WindowActivation.IsForeground(focusHwnd) || InputLanguageManager.Current.CurrentInputLanguage.Name != beforeLayout.Name)
                throw new InvalidOperationException("Close did not return to the captured fixture and input language");
            if (automateOwnedInput && (focusBox.Text != "Owned source input" || !focusBox.IsKeyboardFocused))
                throw new InvalidOperationException("Injected palette chords altered source text or failed to restore source input focus.");
            var restoredSourceState = SourceInputState.Capture(focusHwnd);
            if (beforeSourceState != restoredSourceState) throw new InvalidOperationException("Native source HKL/focus/available IMM metadata did not restore exactly.");
            if (automateOwnedInput && evidenceDirectory != null)
            {
                OwnedInputAutomation.Screenshot(focusFixture, Path.Combine(evidenceDirectory, "source-restored.png"));
                await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "restoration-evidence.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    sourceTextPreserved = true, nativeForegroundRestored = true, sourceInputFocusRestored = true, inputLanguageRestored = true,
                    sourceLanguage = beforeLayout.Name, restoredLanguage = InputLanguageManager.Current.CurrentInputLanguage.Name,
                    chineseSourceExercised = sourceLayout != null, nativeSourceStateExact = true, immMetadataAvailable = beforeSourceState?.Open != null,
                    sourceLayoutHandle = beforeSourceState?.Layout.ToInt64(), restoredLayoutHandle = restoredSourceState?.Layout.ToInt64(), scope = "Owned synthetic probe with installed-layout switch; not physical IME/TSF composition acceptance"
                }));
            }
            if (discoveryFixture == null) throw new InvalidOperationException("No discovery fixture supplied by smoke script");
            await VerifyDiscoveryAsync(discoveryFixture);
            var shellFixture = new ShellCommandWindow("Write-Output 'Kikicast smoke'");
            theme?.Register(shellFixture, false);
            shellFixture.Show();
            await Task.Delay(100);
            if (!shellFixture.IsVisible) throw new InvalidOperationException("Shell editor did not render");
            shellFixture.Close();
            if (PowerShellRunner.FindExecutable() != null)
            {
                using var commandFixture = PowerShellRunner.Start("Write-Output 'Kikicast smoke — 你好'; exit 0", null);
                try
                {
                    var exit = await commandFixture.Completion.WaitAsync(TimeSpan.FromSeconds(8));
                    if (exit != 0 || !commandFixture.Output.Snapshot().Text.Contains("Kikicast smoke — 你好"))
                        throw new InvalidOperationException("Controlled PowerShell UTF-8 output or exit code failed");
                }
                finally { if (!commandFixture.Completion.IsCompleted) commandFixture.Stop(); }
                // Only our diagnostic script runs: inspect real terminal handles, write one temporary probe, exit.
                var probePath = Path.Combine(Path.GetTempPath(), "KikicastTerminal-" + Guid.NewGuid().ToString("N") + ".json");
                System.Diagnostics.Process? terminalFixture = null;
                try
                {
                    var script = "$ErrorActionPreference = 'Stop'; $available = [Console]::KeyAvailable; @{ InputRedirected = [Console]::IsInputRedirected; OutputRedirected = [Console]::IsOutputRedirected; WindowWidth = $Host.UI.RawUI.WindowSize.Width }"
                        + " | ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath '" + probePath.Replace("'", "''") + "'; exit 0";
                    terminalFixture = System.Diagnostics.Process.Start(PowerShellRunner.InteractiveStartInfo(PowerShellRunner.FindExecutable()!, script, null, false));
                    if (terminalFixture == null) throw new InvalidOperationException("Interactive terminal fixture did not start");
                    await terminalFixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                    using var probe = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(probePath));
                    if (terminalFixture.ExitCode != 0 || probe.RootElement.GetProperty("InputRedirected").GetBoolean()
                        || probe.RootElement.GetProperty("OutputRedirected").GetBoolean() || probe.RootElement.GetProperty("WindowWidth").GetInt32() <= 0)
                        throw new InvalidOperationException("Interactive shell inherited redirected input/output instead of a console");
                }
                finally
                {
                    if (terminalFixture != null) { if (!terminalFixture.HasExited) terminalFixture.Kill(entireProcessTree: true); terminalFixture.Dispose(); }
                    if (File.Exists(probePath)) File.Delete(probePath);
                }
            }
        }
        catch (Exception ex)
        {
            exitCode = 1;
            using var report = new System.IO.StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            report.WriteLine("Window smoke failed: " + ex);
        }
        finally { fixture.Close(); focusFixture.Close(); Shutdown(exitCode); }
    }

    private async Task VerifyDiscoveryAsync(string fixture)
    {
        var folder = Path.Combine(smokeDirectory!, "OwnedPortable");
        Directory.CreateDirectory(folder);
        foreach (var suffix in new[] { ".exe", ".dll", ".deps.json", ".runtimeconfig.json" })
            File.Copy(Path.Combine(Path.GetDirectoryName(fixture)!, "PortableFixture" + suffix), Path.Combine(folder, "PortableFixture" + suffix));
        var exe = Path.Combine(folder, "PortableFixture.exe");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Discovery fixture did not start");
        try
        {
            using var observer = new RunningApplicationDiscovery(Path.Combine(folder, "observed.json"), (uint)process.Id);
            await observer.SetKnownApplicationsAsync([]);
            observer.SetEnabled(true);
            if (!observer.IsEnabled) throw new InvalidOperationException("WinEvent discovery hooks were not installed");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
            while (observer.Applications.Count == 0 && DateTimeOffset.UtcNow < deadline) await Task.Delay(50);
            if (observer.Applications.Count != 1 || !observer.Applications[0].Path.Equals(exe, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Visible owned EXE was not discovered exactly once");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(exe);
            deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (observer.Applications.Count != 0 && DateTimeOffset.UtcNow < deadline) await Task.Delay(50);
            if (observer.Applications.Count != 0) throw new InvalidOperationException("Deleted owned EXE remained discoverable");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private void ShowShellCommand(string script, bool runImmediately)
    {
        if (store?.Preferences.ShellCommandsEnabled != true) return;
        palette?.HideForSettings();
        if (runImmediately)
        {
            try
            {
                PowerShellRunner.LaunchInteractive(script);
                store?.RecordLaunch("cmd:shell"); // Launch only, never retain script text or input.
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or IOException)
            { tray?.ShowBalloonTip(3000, "Kikicast", "Terminal could not be started: " + ex.Message, System.Windows.Forms.ToolTipIcon.Warning); }
            return;
        }
        var window = new ShellCommandWindow(script);
        theme?.Register(window, false);
        shellWindows.Add(window);
        window.TerminalOpened += () => store?.RecordLaunch("cmd:shell");
        window.Closed += (_, _) => shellWindows.Remove(window);
        window.Show();
    }

    private async void ShowSettings(string? category = null)
    {
        if (settingsOpening) { settingsWindow?.Activate(); return; }
        if (hotkeys == null || store == null || palette == null) return;
        if (palette.IsExecuting) return;
        settingsOpening = true;
        Guid? applyLayout = null;
        palette.HideForSettings();
        try
        {
            await hotkeys.PauseAsync();
            settingsWindow = new SettingsWindow(store.Preferences, async preferences =>
            {
                var previous = store.Preferences;
                preferences = WindowLayout.MergeReferences(previous, preferences);
                try
                {
                    if (await hotkeys.ConfigureAsync(preferences) is { } registrationError) return registrationError;
                    try { await store.UpdatePreferencesAsync(current => WindowLayout.MergeReferences(current, preferences)); }
                    catch (Exception ex) when (UserStore.IsStorageError(ex))
                    {
                        var rollback = await hotkeys.ConfigureAsync(previous);
                        return "Settings were not saved: " + ex.Message + (rollback == null ? "" : " " + rollback);
                    }
                    if (shellMenuItem != null) shellMenuItem.Enabled = preferences.ShellCommandsEnabled;
                    if (!preferences.ShellCommandsEnabled) foreach (var editor in shellWindows.ToArray()) editor.Close();
                    discovery?.SetEnabled(smokeDirectory == null && preferences.ApplicationsEnabled && preferences.DiscoverRunningApplications);
                    await palette.RefreshApplicationsAsync();
                    palette.SetShortcutLabel(preferences);
                    palette.SetStatus("Settings saved · Shortcut: " + (preferences.PaletteBinding?.Display ?? "unbound"));
                    return null;
                }
                finally { await hotkeys.PauseAsync(); }
            }, discovery!, palette.RefreshApplicationsAsync, commands!, store, () => palette.SettingsItems, palette.RefreshKeepingSelection, () => palette.LayoutApplications);
            if (category != null) settingsWindow.SelectCategory(category);
            theme?.Register(settingsWindow, false);
            if (smokeDirectory != null && (settingsWindow.CategoryCount != 6 || settingsWindow.VisibleBindingCount != 1 + WindowGeometry.Commands.Count))
            { Console.Error.WriteLine("Settings categories or binding rows missing"); Shutdown(1); return; }
            if (automateOwnedInput && evidenceDirectory != null)
            {
                var ownedSettings = settingsWindow;
                ownedSettings.Loaded += async (_, _) =>
                {
                    try
                    {
                        await OwnedInputAutomation.ClickToActivateAsync(ownedSettings, Path.Combine(evidenceDirectory, "settings-activation-point.png"));
                        await ownedSettings.VerifyCategoryInputAsync(evidenceDirectory);
                        automationSettingsPassed = true;
                        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "settings-input-evidence.json"), "{\"categoryCount\":6,\"forwardCtrlTab\":true,\"wrap\":true,\"reverseCtrlShiftTab\":true,\"saveRoundTripSensitivity\":true,\"saveRoundTripIcons\":true,\"saveRoundTripDepth\":true,\"saveRoundTripAppPaths\":true,\"saveRoundTripPackagedApps\":true,\"saveRoundTripItemShortcut\":true,\"shortcutWriteFailureRollback\":true,\"shortcutConflictRefused\":true,\"scope\":\"Owned synthetic input, not physical acceptance\"}");
                    }
                    catch (Exception ex) { Console.Error.WriteLine("Settings input smoke failed: " + ex); Shutdown(1); }
                };
            }
            settingsWindow.ShowDialog();
            applyLayout = settingsWindow.ApplySavedLayoutId;
        }
        finally
        {
            settingsWindow = null;
            var error = await hotkeys.ConfigureAsync(store.Preferences);
            if (error != null) palette.SetStatus(error + " Reconfigure it from the tray.");
            settingsOpening = false;
        }
        if (applyLayout is { } id) await palette.RunLayoutAsync(id);
    }

    private void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e) => hotkeys?.Reset();
    private void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) => hotkeys?.Reset();

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged;
        palette?.CancelCurrentLayout();
        hotkeys?.Dispose();
        discovery?.Dispose();
        automationHost?.Close();
        theme?.Dispose();
        tray?.Dispose(); trayMenu?.Dispose();
        store?.FlushAsync().Wait(TimeSpan.FromSeconds(2));
        commands?.FlushAsync().Wait(TimeSpan.FromSeconds(2));
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        if (smokeDirectory != null && Directory.Exists(smokeDirectory)) Directory.Delete(smokeDirectory, true);
        base.OnExit(e);
    }
}
