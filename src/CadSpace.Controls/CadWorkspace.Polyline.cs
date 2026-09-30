using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private async void ShowPolylineEditor()
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || _session == null || XamlRoot == null) return;
        _optionsDialog = true;
        try
        {
            var selected = _session.EditableSelection();
            if (selected.Length != 1 || PolylineTools.Unwrap(selected[0]).Geometry is not PolylineEntity)
                throw new ArgumentException("Select exactly one 2D polyline for vertex editing.");
            var editor = new CadPolylineEditor("polylineDialog") { Width = 330 }; editor.Bind(_session, selected[0]);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Polyline vertex editor", Content = new ScrollViewer { Content = editor, MaxHeight = 540 }, CloseButtonText = "Close" };
            CadUi.DescribeDialog(dialog, "polyline.dialog"); await dialog.ShowAsync();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { CommandLine.AddMessage(error.Message); }
        finally { _optionsDialog = false; CommandLine.FocusInput(); }
    }
}
