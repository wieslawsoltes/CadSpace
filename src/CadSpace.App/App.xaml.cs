using CadSpace.Controls;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;

namespace CadSpace.App;

public sealed partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    private CadWorkspace? _workspace;
    private readonly List<OpenDrawing> _documents = new();
    private OpenDrawing? _active;
    private bool _fileOperation;
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Dark; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _workspace = new CadWorkspace();
        MainWindow = new Window { Title = "CadSpace — Drafting & Modeling", Content = _workspace };
        _workspace.FileRequested += ExecuteFile;
        _workspace.PreferencesChanged += QueueWorkspaceSave;
        _workspace.DocumentTabs.MoveRequested += (key, index) => {
            if (key is OpenDrawing drawing && _documents.Remove(drawing)) { _documents.Insert(Math.Clamp(index, 0, _documents.Count), drawing); RefreshTabs(); }
        };
        _workspace.DocumentTabs.ActivateRequested += key => Activate((OpenDrawing)key);
        _workspace.DocumentTabs.CloseRequested += async key => await Close((OpenDrawing)key);
        _workspace.Loaded += (_, _) => { _workspace.CommandLine.FocusInput(); StartRecovery(); LoadWorkspacePreferences(); };
        Open(SampleDrawings.StudioPlan()); MainWindow.Activate();
    }
    private void Open(Drawing drawing, DxfSource? source = null, bool model = false, string? displayName = null)
    {
        var document = new OpenDrawing(drawing, source, displayName); _documents.Add(document); document.Session.Document.Changed += RefreshTabs; Activate(document);
        if (model) _workspace!.Viewport.Set3D(true);
    }
    private void Activate(OpenDrawing document)
    {
        _active = document; _workspace!.Bind(document.Session, document.Commands); _workspace.SetTitle(document.DisplayName); RefreshTabs(); _workspace.CommandLine.FocusInput();
    }
    private void RefreshTabs()
    {
        if (_active == null || _workspace == null) return;
        _workspace.DocumentTabs.SetDocuments(_documents.Select(d => ((object)d, d.DisplayName, d.Session.Document.IsDirty)), _active);
        _workspace.SetTitle(_active.DisplayName);
        if (MainWindow != null) MainWindow.Title = _active.DisplayName + (_active.Session.Document.IsDirty ? " *" : "") + " — CadSpace";
    }
    private async void ExecuteFile(string action)
    {
        if (_fileOperation || _workspace == null) return; _fileOperation = true;
        try
        {
            switch (action)
            {
                case "NEW": Open(Drawing.Empty with { Name = $"Drawing{_documents.Count + 1}.cadspace" }); break;
                case "STUDIO": Open(SampleDrawings.StudioPlan()); break;
                case "MODEL": Open(SampleDrawings.ModelStudy(), model: true); break;
                case "OPEN": await OpenFile(); break;
                case "SAVE": await SaveProject(); break;
                case "RECOVER": await RecoverDrawings(); break;
                case "EXPORT": await ExportDxf(); break;
                case "EXPORT_BINARY": await ExportDxf(true); break;
                case "ABOUT":
                    await Dialog("CadSpace preview", "Independent CAD software built with Uno Platform, Skia and OpenGL/WebGL.\n\nThe workspace supports drafting, layers, blocks, rational splines, hatching, triangle meshes and bounded mesh Boolean operations. The 3D viewport includes picking, visual styles, world-plane text and section clipping. Type HELP for commands.\n\nOpen accepts ASCII and binary DXF. Save writes a native .cadspace project including DXF provenance; DXF export reports conversion losses.\n\nThis is not a complete AutoCAD replacement. ACIS/B-rep solids, DWG, dynamic block editing, constraints and paper-space plotting are not implemented. Keep backups of original files.\n\nMIT licensed • github.com/wieslawsoltes/CadSpace"); break;
            }
        }
        catch (Exception error) { _workspace.CommandLine.AddMessage(error.Message); await Dialog("Operation could not be completed", error.Message); }
        finally { _fileOperation = false; }
    }
    private async Task OpenFile()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".dxf"); picker.FileTypeFilter.Add(".cadspace");
        var file = await picker.PickSingleFileAsync(); if (file == null) return;
        var native = file.Name.EndsWith(".cadspace", StringComparison.OrdinalIgnoreCase);
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > (ulong)(native ? CadProjectCodec.MaximumCharacters : DxfCodec.MaximumCharacters)) throw new InvalidOperationException("The file exceeds the configured import size limit.");
        if (native)
        {
            var project = CadProjectCodec.Read(await FileIO.ReadTextAsync(file)); Open(project.Drawing, project.DxfSource, displayName: file.Name);
            _workspace!.CommandLine.AddMessage($"Opened native project {file.Name}. Geometry and DXF provenance restored.");
        }
        else
        {
            var buffer = await FileIO.ReadBufferAsync(file); using var reader = DataReader.FromBuffer(buffer);
            var bytes = new byte[checked((int)buffer.Length)]; reader.ReadBytes(bytes);
            var read = DxfBinary.Read(bytes, file.Name); Open(read.Drawing, read.Source);
            if (!read.Warnings.IsEmpty) await Dialog("DXF import report", string.Join("\n\n", read.Warnings));
            _workspace!.CommandLine.AddMessage($"Opened {file.Name}: {read.Drawing.Entities.Length} drawing records.");
        }
    }
    private async Task SaveProject()
    {
        if (_active == null) return;
        var document = _active; var drawing = document.Session.Document.Drawing;
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(document.DisplayName) };
        picker.FileTypeChoices.Add("CadSpace project", new List<string> { ".cadspace" });
        var file = await picker.PickSaveFileAsync(); if (file == null) return;
        var text = CadProjectCodec.Write(drawing, document.Source);
        CachedFileManager.DeferUpdates(file);
        await FileIO.WriteTextAsync(file, text);
        await CompleteFileUpdate(file);
        document.DisplayName = file.Name;
        if (document.Session.Document.Drawing == drawing) { document.Session.Document.MarkSaved(); await ForgetRecovery(document); }
        RefreshTabs(); _workspace!.CommandLine.AddMessage($"Saved {file.Name}. Editable geometry, layers, blocks, and DXF provenance retained.");
    }
    private async Task ExportDxf(bool binary = false)
    {
        if (_active == null) return;
        var document = _active; var drawing = document.Session.Document.Drawing; var result = DxfBinary.Write(drawing, document.Source, binary);
        if (!result.Warnings.IsEmpty)
        {
            var dialog = new ContentDialog { XamlRoot = _workspace!.XamlRoot, Title = "Review DXF export", Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = string.Join("\n\n", result.Warnings) + "\n\nExport a copy and keep your original file. Use Save for a lossless native project.", TextWrapping = TextWrapping.Wrap } }, PrimaryButtonText = "Export copy", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            CadUi.DescribeDialog(dialog, "export.dialog");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(document.DisplayName) + (result.Warnings.IsEmpty ? "" : "-export") };
        picker.FileTypeChoices.Add(binary ? "Binary DXF drawing" : "ASCII DXF drawing", new List<string> { ".dxf" });
        var file = await picker.PickSaveFileAsync(); if (file == null) return;
        CachedFileManager.DeferUpdates(file);
        await FileIO.WriteBytesAsync(file, result.Bytes);
        await CompleteFileUpdate(file);
        _workspace!.CommandLine.AddMessage($"Exported {file.Name}." + (result.Warnings.IsEmpty ? "" : " Use Save to retain native editing semantics."));
    }
    private static async Task CompleteFileUpdate(StorageFile file)
    {
        // The browser fallback writes a temporary file; completion starts the actual download.
        // Do not report success or clear native dirty/recovery state before the provider completes.
        var status = await CachedFileManager.CompleteUpdatesAsync(file);
        if (status is not (FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed))
            throw new IOException($"The file provider did not complete saving {file.Name} ({status}). Save again; the drawing has not been marked as saved.");
    }
    private async Task Close(OpenDrawing document)
    {
        if (_fileOperation) return; _fileOperation = true;
        try
        {
            if (document.Session.Document.IsDirty)
            {
                var dialog = new ContentDialog { XamlRoot = _workspace!.XamlRoot, Title = "Discard unsaved changes?", Content = $"Changes to {document.DisplayName} have not been saved to a native project. Closing the tab will discard this editing state.", PrimaryButtonText = "Discard", CloseButtonText = "Keep open", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            document.Session.Document.Changed -= RefreshTabs; _documents.Remove(document); await ForgetRecovery(document);
            if (_documents.Count == 0) Open(Drawing.Empty with { Name = "Drawing1.cadspace" });
            else if (_active == document) Activate(_documents[^1]); else RefreshTabs();
        }
        finally { _fileOperation = false; }
    }
    private async Task Dialog(string title, string message)
    {
        if (_workspace?.XamlRoot == null) return;
        var dialog = new ContentDialog { XamlRoot = _workspace.XamlRoot, Title = title, Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }, CloseButtonText = "Close" };
        CadUi.DescribeDialog(dialog, "file.report"); await dialog.ShowAsync();
    }
    private sealed class OpenDrawing
    {
        public CadSession Session { get; }
        public CommandEngine Commands { get; }
        public DxfSource? Source { get; }
        public string DisplayName { get; set; }
        public Guid RecoveryKey { get; } = Guid.NewGuid();
        public long RecoveryGeneration { get; set; }
        public Drawing? Checkpoint { get; set; }
        public OpenDrawing(Drawing drawing, DxfSource? source, string? displayName = null) { Session = new(new CadDocument(drawing)); Commands = new(Session); Source = source; DisplayName = displayName ?? drawing.Name; }
    }
}
