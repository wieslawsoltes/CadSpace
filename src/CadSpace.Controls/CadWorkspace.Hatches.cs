using CadSpace.Engine;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private async void ShowHatchEditor()
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || _session == null || XamlRoot == null) return;
        _optionsDialog = true; var session = _session;
        try
        {
            var selected = session.EditableSelection();
            if (selected.Length != 1 || HatchEditing.Unwrap(selected[0]) == null) throw new ArgumentException("Select one hatch.");
            var editor = new CadHatchEditor(session, selected[0]);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Hatch and Gradient", Content = editor, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Resources["ContentDialogMaxWidth"] = 680.0; CadUi.DescribeDialog(dialog, "hatch.dialog");
            dialog.PrimaryButtonClick += (_, e) => {
                try { if (!ReferenceEquals(session, _session)) throw new InvalidOperationException("The drawing changed."); editor.Apply(); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { editor.ShowError(ex.Message); e.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { CommandLine.AddMessage(e.Message); }
        finally { _optionsDialog = false; CommandLine.FocusInput(); }
    }
}
