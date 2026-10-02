using CadSpace.Model;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private async void ShowLeaderEditor()
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || _session == null || XamlRoot == null) return;
        _optionsDialog = true; var session = _session;
        try
        {
            var selected = session.EditableSelection();
            if (selected.Length != 1 || LeaderGeometry.Unwrap(selected[0]) == null) throw new ArgumentException("Select one straight leader.");
            var editor = new CadLeaderEditor(session,selected[0]);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Leader Properties", Content = editor, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Resources["ContentDialogMaxWidth"] = 680.0; CadUi.DescribeDialog(dialog,"leader.dialog");
            dialog.PrimaryButtonClick += (_,e) => {
                try { if (!ReferenceEquals(session,_session)) throw new InvalidOperationException("The active drawing changed."); editor.Apply(); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { editor.ShowError(error.Message); e.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { CommandLine.AddMessage(e.Message); }
        finally { _optionsDialog = false; CommandLine.FocusInput(); }
    }
}
