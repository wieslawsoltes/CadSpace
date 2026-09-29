using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed partial class CadWorkspace
{
    private bool _styleDialog;
    private async void OnStyleRequested(string command)
    {
        if (command == "POLYLINEEDITOR") { ShowPolylineEditor(); return; }
        if (command is not ("LAYER" or "LINETYPE") || _session == null || XamlRoot == null || _styleDialog) return;
        _styleDialog = true;
        CadLayerManager? layers = null;
        try
        {
            UIElement content;
            if (command == "LAYER") { layers = new(); layers.Bind(_session); content = layers; }
            else { var types = new CadLinetypeManager(); types.Bind(_session); content = types; }
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = command == "LAYER" ? "Layer Properties Manager" : "Linetype Manager", Content = content, CloseButtonText = "Close" };
            dialog.Resources["ContentDialogMaxWidth"] = 920.0;
            await dialog.ShowAsync();
        }
        catch (Exception error) { CommandLine.AddMessage(error.Message); }
        finally { layers?.Unbind(); _styleDialog = false; CommandLine.FocusInput(); }
    }
}
