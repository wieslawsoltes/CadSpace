using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class AnnotationRegression
{
    public static void Register(Action<string, Action> test)
    {
        static void Check(bool value) { if (!value) throw new Exception("Annotation assertion failed."); }
        static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { return; } throw new Exception("Expected annotation rejection."); }
        static DxfReadResult Read() => DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "independent-editing.dxf")));
        static CompositeEntity Insert(Drawing drawing) => drawing.Entities.OfType<CompositeEntity>().Single(e => e.DxfType == "INSERT");
        static CadSession Session(Drawing drawing) { var s = new CadSession(new(drawing)); s.SelectAll(); return s; }
        foreach (var binary in new[] { false, true })
        {
            test($"attribute values retain native INSERT/ATTRIB sequence binary={binary}", () => {
                var input = Read(); var insert = Insert(input.Drawing); var before = insert.SourceRecord;
                var values = DxfAttributeEditing.Read(insert); Check(values[0].Tag == "LABEL" && values[0].Value == "Part A");
                var edited = DxfAttributeEditing.Apply(input.Drawing, insert, new Dictionary<int, string> { [0] = "Part Ω / 42" });
                var after = Insert(edited); Check(after.Id == insert.Id && after.Children.Select(e => e.Id).SequenceEqual(insert.Children.Select(e => e.Id)));
                Check(insert.SourceRecord == before && after.SourceRecord.Contains("Part Ω / 42"));
                var native = CadProjectCodec.Read(CadProjectCodec.Write(edited, input.Source));
                var output = DxfBinary.Write(native.Drawing, native.DxfSource, binary); var reopened = DxfBinary.Read(output.Bytes);
                Check(DxfAttributeEditing.Read(Insert(reopened.Drawing))[0].Value == "Part Ω / 42");
                Check(reopened.Source.Text.Contains("0\nATTRIB\n") && reopened.Source.Text.Contains("0\nSEQEND\n"));
                Check(!output.Warnings.Any(w => w.Contains("display children") || w.Contains("metadata may")));
                var folder = Environment.GetEnvironmentVariable("CADSPACE_EDITING_OUTPUT");
                if (folder != null) { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, binary ? "attributes-binary.dxf" : "attributes.dxf"), output.Bytes); }
            });
        }
        test("attribute edits and undo preserve root and child identities", () => {
            var input = Read(); var insert = Insert(input.Drawing); var s = Session(input.Drawing);
            s.Document.Edit("Attributes", d => DxfAttributeEditing.Apply(d, insert, new Dictionary<int,string> { [0] = "Changed" }));
            Check(DxfAttributeEditing.Read(Insert(s.Document.Drawing))[0].Value == "Changed");
            s.Document.Undo(); Check(s.Document.Drawing == input.Drawing);
            s.Document.Redo(); Check(DxfAttributeEditing.Read(Insert(s.Document.Drawing))[0].Value == "Changed");
        });
        test("no-op attribute editing creates no undo state", () => {
            var input = Read(); var s = Session(input.Drawing); var insert = Insert(input.Drawing);
            s.Document.Edit("No-op", d => DxfAttributeEditing.Apply(d, insert, new Dictionary<int,string> { [0] = "Part A" }));
            Check(!s.Document.CanUndo && s.Document.Drawing == input.Drawing);
        });
        test("attribute edit rejects stale root without overwriting current state", () => {
            var input = Read(); var insert = Insert(input.Drawing);
            var changed = DxfAttributeEditing.Apply(input.Drawing, insert, new Dictionary<int,string> { [0] = "Changed" });
            Reject(() => DxfAttributeEditing.Apply(changed, insert, new Dictionary<int,string> { [0] = "Overwrite" }));
            Check(DxfAttributeEditing.Read(Insert(changed))[0].Value == "Changed");
        });
        test("attribute edit validates all changes atomically", () => {
            var input = Read(); var insert = Insert(input.Drawing);
            Reject(() => DxfAttributeEditing.Apply(input.Drawing, insert, new Dictionary<int,string> { [0] = "Valid", [9999] = "Invalid" }));
            Check(DxfAttributeEditing.Read(insert)[0].Value == "Part A");
            foreach (var value in new[] { "A\nB", "A\0B", "%<field>%", new string('a', 2049) })
                Reject(() => DxfAttributeEditing.Apply(input.Drawing, insert, new Dictionary<int,string> { [0] = value }));
        });
        test("attribute root and child layer locks block changes", () => {
            var input = Read(); var insert = Insert(input.Drawing);
            var locked = input.Drawing with { Layers = input.Drawing.Layers.SetItem(insert.Layer, input.Drawing.LayerFor(insert) with { Locked = true }) };
            Reject(() => DxfAttributeEditing.Apply(locked, insert, new Dictionary<int,string> { [0] = "No" }));
        });
        test("changed root style is retained with edited attributes", () => {
            var input = Read(); var insert = Insert(input.Drawing) with { ColorIndex = 4, LinetypeScale = 3 };
            var d = input.Drawing with { Entities = input.Drawing.Entities.Select(e => e.Id == insert.Id ? insert : e).ToImmutableArray() };
            var edited = DxfAttributeEditing.Apply(d, insert, new Dictionary<int,string> { [0] = "Changed" });
            var output = DxfCodec.Write(edited, input.Source); var read = DxfCodec.Read(output.Text);
            Check(Insert(read.Drawing).ColorIndex == 4 && Insert(read.Drawing).LinetypeScale == 3 && DxfAttributeEditing.Read(Insert(read.Drawing))[0].Value == "Changed");
        });
        test("moved block refuses stale retained attribute geometry", () => {
            var input = Read(); var moved = (CompositeEntity)EntityGeometry.Transform(Insert(input.Drawing), Transform3.Translation(new(7,8)));
            Reject(() => DxfAttributeEditing.Read(moved));
        });
        test("tampered attribute source cannot claim lossless native export", () => {
            var input = Read(); var insert = Insert(input.Drawing);
            var edited = DxfAttributeEditing.Apply(input.Drawing, insert, new Dictionary<int,string> { [0] = "Changed" });
            var after = Insert(edited) with { SourceRecord = Insert(edited).SourceRecord.Replace("\nATTRIB\n", "\nTEXT\n") };
            var output = DxfCodec.Write(edited with { Entities = edited.Entities.Select(e => e.Id == after.Id ? after : e).ToImmutableArray() }, input.Source);
            Check(output.Warnings.Any(w => w.Contains("display children")));
        });
        test("attribute constant and field flags are read only", () => {
            var input = Read(); var insert = Insert(input.Drawing); var record = insert.SourceRecord;
            // Replace only the visible value, then independently parse the complete file.
            var fieldFile = input.Source.Text.Replace("Part A", "%<field>%");
            var field = DxfCodec.Read(fieldFile); var target = Insert(field.Drawing);
            Check(!DxfAttributeEditing.Read(target)[0].Editable);
            Reject(() => DxfAttributeEditing.Apply(field.Drawing, target, new Dictionary<int,string> { [0] = "No" }));
        });
        test("hidden values are editable without creating a visible child", () => {
            var input = Read(); var insert = Insert(input.Drawing); var values = DxfAttributeEditing.Read(insert);
            Check(values[1].Invisible && values[1].Editable && !values[2].Editable);
            var changed = DxfAttributeEditing.Apply(input.Drawing, insert, new Dictionary<int,string> { [1] = "Hidden Ω" });
            Check(Insert(changed).Children.Length == insert.Children.Length && DxfAttributeEditing.Read(Insert(changed))[1].Value == "Hidden Ω");
            var reread = DxfCodec.Read(DxfCodec.Write(changed, input.Source).Text);
            Check(DxfAttributeEditing.Read(Insert(reread.Drawing))[1].Value == "Hidden Ω");
            Reject(() => DxfAttributeEditing.Apply(input.Drawing, insert, new Dictionary<int,string> { [2] = "No" }));
        });
        test("attribute payload limit rejects before parsing", () => Reject(() => DxfAttributeEditing.Read(new("INSERT", [], new string('x', DxfCodec.MaximumCharacters + 1)))));
        test("plain text editing retains every nontext property and is one Undo", () => {
            var text = new TextEntity(new(10,20,30), "Before", 7, 21) { Handle = "AB", ColorIndex = 4, LinetypeScale = 3 };
            var s = Session(Drawing.Empty with { Entities = [text] }); s.SetText(text, "After Ω");
            var next = (TextEntity)s.Document.Drawing.Entities[0]; Check(next with { Text = text.Text } == text);
            s.Document.Undo(); Check(s.Document.Drawing.Entities[0] == text);
        });
        test("placed text editing preserves its complete affine frame", () => {
            var text = new PlacedEntity(new TextEntity(default, "Before", 5), Transform3.Scaling(new(2,3,1)).Then(Coordinates3D.ObjectCoordinateSystem(new(1,2,3))));
            var s = Session(Drawing.Empty with { Entities = [text] }); s.SetText(text, "After");
            var next = (PlacedEntity)s.Document.Drawing.Entities[0]; Check(next.Placement == text.Placement && next.Id == text.Id && next.Geometry.Id == text.Geometry.Id);
            Check(TextEditing.TextOf(next)!.Text == "After");
        });
        test("text no-op and invalid newline edits do not manufacture undo", () => {
            var text = new TextEntity(default, "Before"); var s = Session(Drawing.Empty with { Entities = [text] });
            s.SetText(text, "Before"); Check(!s.Document.CanUndo);
            Reject(() => s.SetText(text, "A\nB")); Reject(() => s.SetText(text, new string('x', TextEditing.MaximumCharacters + 1)));
            Check(!s.Document.CanUndo);
        });
        test("raw MTEXT content can contain newlines without moving the text", () => {
            var text = new TextEntity(new(1,2), "Before", 5, 0, true); var s = Session(Drawing.Empty with { Entities = [text] });
            s.SetText(text, "A\nB\\PΩ"); Check(TextEditing.TextOf(s.Document.Drawing.Entities[0])!.Text == "A\nB\\PΩ");
        });
        test("text edit rejects stale and locked selections", () => {
            var text = new TextEntity(default, "Before"); var s = Session(Drawing.Empty with { Entities = [text] });
            s.SetText(text, "After"); Reject(() => s.SetText(text, "Old"));
            var current = s.Document.Drawing.Entities[0]; s.Document.Edit("Lock", d => d with { Layers = d.Layers.SetItem("0", new("0", Locked:true)) });
            Reject(() => s.SetText(current, "No"));
        });
        test("DDEDIT preselection dispatches editor after finishing command state", () => {
            var s = Session(Drawing.Empty with { Entities = [new TextEntity(default, "A")] }); var c = new CommandEngine(s); var request = "";
            c.ViewRequested += r => { Check(!c.IsActive); request = r; }; c.Start("ED"); Check(request == "TEXTEDITOR");
        });
        test("DDEDIT point selection uses actual text hit testing", () => {
            var text = new TextEntity(default, "A", 10); var s = new CadSession(new(Drawing.Empty with { Entities = [text] })); var c = new CommandEngine(s); var request = "";
            c.ViewRequested += r => request = r; c.Start("DDEDIT"); c.Submit("2,2"); Check(request == "TEXTEDITOR" && s.Selection.Contains(text.Id));
        });
        test("EATTEDIT dispatches attributed block editor, rejects unrelated geometry", () => {
            var input = Read(); var s = new CadSession(new(input.Drawing)); s.Select(Insert(input.Drawing).Id); var c = new CommandEngine(s); var request = "";
            c.ViewRequested += r => request = r; c.Start("ATE"); Check(request == "ATTRIBUTEEDITOR");
            s.Select(input.Drawing.Entities.OfType<LineEntity>().First().Id); request = ""; c.Start("DDEDIT"); Check(request == "" && !c.IsActive);
        });
    }
}
