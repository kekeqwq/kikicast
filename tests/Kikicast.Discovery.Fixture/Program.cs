using System.Windows;
using System.Windows.Threading;

namespace Kikicast.Discovery.Fixture;
public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Only an explicitly copied, UUID-named owned acceptance apphost opts into
        // the longer ordinary top-level layout fixture. Normal discovery unchanged.
        var layoutFixture = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath!).StartsWith("OwnedLayoutFixture-", StringComparison.Ordinal);
        if (layoutFixture)
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "owned-input-received.json"), System.Text.Json.JsonSerializer.Serialize(Environment.GetCommandLineArgs().Skip(1).ToArray())); // owned fixture argv only, never user process inspection
        var window = new Window { Title = "Kikicast owned discovery fixture", Width = 360, Height = 160, Content = "Owned discovery test — no user content", ShowInTaskbar = layoutFixture, ShowActivated = !layoutFixture };
        var show = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        show.Tick += (_, _) => { show.Stop(); window.Show(); };
        var exit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(layoutFixture ? 10 : 3) };
        exit.Tick += (_, _) => { exit.Stop(); app.Shutdown(); };
        show.Start(); exit.Start(); app.Run();
    }
}
