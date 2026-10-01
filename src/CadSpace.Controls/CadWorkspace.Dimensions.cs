using CadSpace.Model;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private async void ShowDimensionEditor()
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || _session == null || XamlRoot == null) return;
        _optionsDialog = true; var session = _session;
        try
        {
            var selected = session.EditableSelection();
            if (selected.Length != 1 || DimensionGeometry.Unwrap(selected[0]) == null) throw new ArgumentException("Select one modeled dimension.");
            var editor = new CadDimensionEditor(session, selected[0]);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Dimension Properties", Content = editor,
                PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            CadUi.DescribeDialog(dialog, "dimension.dialog"); dialog.Resources["ContentDialogMaxWidth"] = 700.0;
            dialog.PrimaryButtonClick += (_, e) => {
                try { if (!ReferenceEquals(session, _session)) throw new InvalidOperationException("The active drawing changed."); editor.Apply(); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { editor.ShowError(error.Message); e.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { CommandLine.AddMessage(e.Message); }
        finally { _optionsDialog = false; CommandLine.FocusInput(); }
    }
}
