using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private async void ShowAnnotationEditor()
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || _session == null || XamlRoot == null) return;
        _optionsDialog = true;
        try
        {
            var selected = _session.EditableSelection();
            if (selected.Length != 1 || !TextEditing.IsEditable(selected[0])) throw new ArgumentException("Select one supported annotation.");
            var editor = new CadAnnotationEditor(_session, selected[0]);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = TextEditing.TextOf(selected[0]) != null ? "Edit text" : "Edit block attributes",
                Content = editor, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            CadUi.DescribeDialog(dialog, "annotation.dialog"); dialog.Resources["ContentDialogMaxWidth"] = 680.0;
            dialog.PrimaryButtonClick += (_, e) => {
                try { editor.Apply(); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
                { editor.ShowError(error.Message); e.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { CommandLine.AddMessage(error.Message); }
        finally { _optionsDialog = false; CommandLine.FocusInput(); }
    }
}
