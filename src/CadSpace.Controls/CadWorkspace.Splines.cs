using CadSpace.Engine;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private async void ShowSplineEditor()
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || _session == null || XamlRoot == null) return;
        _optionsDialog = true;
        try
        {
            var session = _session;
            var selected = session.EditableSelection();
            if (selected.Length != 1 || SplineEditing.Unwrap(selected[0]) == null) throw new ArgumentException("Select one spline.");
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Spline Editor", Content = new CadSplineEditor(session, selected[0]) { IsCurrentSession = () => ReferenceEquals(session, _session) }, CloseButtonText = "Close" };
            dialog.Resources["ContentDialogMaxWidth"] = 620.0; CadUi.DescribeDialog(dialog, "spline.dialog"); await dialog.ShowAsync();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { CommandLine.AddMessage(e.Message); }
        finally { _optionsDialog = false; CommandLine.FocusInput(); }
    }
}
