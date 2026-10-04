using System.Windows;
using System.Windows.Controls;
using Kikicast.Core;

namespace Kikicast.App;
public partial class MainWindow
{
    private Guid? argumentCommandId;
    private IReadOnlyList<SavedCommandArgument> argumentDefinition = [];
    private System.Windows.Controls.TextBox[] ArgumentBoxes => [ArgumentValue1, ArgumentValue2, ArgumentValue3];
    private void ArgumentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArgumentStrip == null) return;
        var command = (Results.SelectedItem as Row)?.Custom;
        if (command == null || command.Arguments.Count == 0) { ClearArguments(); return; }
        if (argumentCommandId == command.Id && argumentDefinition.SequenceEqual(command.Arguments)) return;
        argumentCommandId = command.Id; argumentDefinition = command.Arguments.ToArray();
        var slots = new[] { ArgumentSlot1, ArgumentSlot2, ArgumentSlot3 };
        var labels = new[] { ArgumentLabel1, ArgumentLabel2, ArgumentLabel3 };
        var boxes = ArgumentBoxes;
        for (var i = 0; i < 3; i++)
        {
            boxes[i].Clear(); slots[i].Visibility = i < command.Arguments.Count ? Visibility.Visible : Visibility.Collapsed;
            if (i < command.Arguments.Count) labels[i].Text = $"$args[{i}] · {command.Arguments[i].Name}" + (command.Arguments[i].Optional ? " (optional)" : " *");
        }
        ArgumentStrip.Visibility = Visibility.Visible;
    }
    private void ClearArguments()
    {
        argumentCommandId = null; argumentDefinition = []; ArgumentStrip.Visibility = Visibility.Collapsed;
        foreach (var box in ArgumentBoxes) box.Clear();
    }
    internal async Task VerifyArgumentFieldsAsync()
    {
        var preferences = store.Preferences; var library = commands.Current;
        var command = new SavedCommand(Guid.NewGuid(), "Owned argument fields", "Write-Output $args[0]")
        { Arguments = [new("Duplicate name"), new("Duplicate name", true)] };
        try
        {
            await commands.UpdateAsync(x => x.Upsert(command));
            await store.UpdatePreferencesAsync(p => p with { SavedCommandsEnabled = true, ShowSavedCommands = true });
            Query.Text = command.Name; Refresh();
            if (ArgumentStrip.Visibility != Visibility.Visible || ArgumentSlot1.Visibility != Visibility.Visible
                || ArgumentSlot2.Visibility != Visibility.Visible || ArgumentSlot3.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Declared argument fields do not match selection");
            if (BoundScript(command) != null) throw new InvalidOperationException("Missing required argument allowed execution");
            ArgumentValue1.Text = "'; throw 'must stay data'; #";
            var bound = BoundScript(command);
            if (bound == null || !bound.Contains(",''")) throw new InvalidOperationException("Literal data / optional empty slot lost");
            Query.Clear(); Refresh(); ClearArguments();
            if (ArgumentValue1.Text.Length != 0 || ArgumentStrip.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Transient argument values survived clearing");
        }
        finally { await commands.UpdateAsync(_ => library); await store.SavePreferencesAsync(preferences); Query.Clear(); Refresh(); }
    }

    private string? BoundScript(SavedCommand command)
    {
        if (command.Arguments.Count == 0) return command.Script;
        if (argumentCommandId != command.Id || !argumentDefinition.SequenceEqual(command.Arguments))
        { RefreshKeepingSelection(); return null; }
        var boxes = ArgumentBoxes;
        for (var i = 0; i < command.Arguments.Count; i++)
            if (!command.Arguments[i].Optional && boxes[i].Text.Length == 0)
            { boxes[i].Focus(); Status.Text = "Fill the required argument: " + command.Arguments[i].Name; return null; }
        return command.ScriptWithArguments(boxes.Take(command.Arguments.Count).Select(x => x.Text).ToArray());
    }
}
