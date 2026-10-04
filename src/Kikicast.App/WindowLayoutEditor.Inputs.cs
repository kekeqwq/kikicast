using System.Text.Json;
using System.Windows;
using Kikicast.Core;

namespace Kikicast.App;

public partial class WindowLayoutEditor
{
    private void FillInput(WindowLayoutInput? input)
    {
        InputKindChoice.SelectedIndex = input == null ? 0 : (int)input.Kind + 1;
        InputValue.Text = input == null ? "" : input.Kind == WindowLayoutInputKind.Arguments ? JsonSerializer.Serialize(input.Arguments) : input.Value ?? "";
    }
    private void InputKindChanged(object sender, RoutedEventArgs e)
    { if (ready && !filling) UpdateInputHint(); }
    private void UpdateInputHint()
    {
        InputValue.IsEnabled = InputKindChoice.SelectedIndex > 0;
        InputHint.Text = InputKindChoice.SelectedIndex switch
        {
            1 => "Existing local path or ~/ path, no network/linked/traversal/drive-root path. EXEs receive one literal path; a package must support file activation (not folders).",
            2 => "One explicit URI, e.g. https://example.com or an app deeplink. No credentials/file/shell/script/system-command schemes. Only the selected app is called, never a default-browser fallback.",
            3 => "JSON array of 1–8 strings, e.g. [\"--new-window\",\"a b\",\"\"]. Each is one literal argv value; no shell splitting, templates or substitution. Direct EXE only; its app-specific semantics apply.",
            _ => "No authored input: use nearest unclaimed existing window, or plain launch only when opening is enabled. To preserve original shortcut arguments, extra inputs on .lnk entries are explicitly unsupported."
        };
    }
    private bool ReadInput(out WindowLayoutInput? input)
    {
        input = null;
        if (InputKindChoice.SelectedIndex == 0) return true;
        if (InputKindChoice.SelectedIndex is < 1 or > 3) return false;
        var kind = (WindowLayoutInputKind)(InputKindChoice.SelectedIndex - 1);
        if (kind != WindowLayoutInputKind.Arguments) input = new(kind) { Value = InputValue.Text.Trim() };
        else
        {
            try
            {
                var args = JsonSerializer.Deserialize<List<string>>(InputValue.Text, new JsonSerializerOptions { MaxDepth = 2 });
                if (args == null) return false;
                input = new(kind) { Arguments = args };
            }
            catch (JsonException) { return false; }
        }
        return true; // Validate returns specific errors rather than silently dropping authored input.
    }
}
