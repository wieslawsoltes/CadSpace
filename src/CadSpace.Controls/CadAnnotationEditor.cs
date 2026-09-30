using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Staged content editor for plain TEXT/MTEXT and retained block-attribute values. Apply is one undo transaction.</summary>
public sealed class CadAnnotationEditor : UserControl
{
    private readonly CadSession _session;
    private readonly Entity _expected;
    private readonly TextBox _value;
    private readonly TextBlock _status = CadTheme.Text("", 11, CadTheme.Muted);
    private readonly ImmutableArray<DxfAttributeValue> _attributes;
    private readonly Dictionary<int, string> _changes = new();
    private int _index;
    public CadAnnotationEditor(CadSession session, Entity expected)
    {
        _session = session; _expected = expected;
        var body = new StackPanel { Spacing = 12, MinWidth = 330, MaxWidth = 580 };
        var text = TextEditing.TextOf(expected);
        _value = CadUi.Identify(new TextBox { FontSize = 13, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch }, "annotation.value", "Text value");
        if (text != null)
        {
            _attributes = [];
            _value.Header = text.Multiline ? "MTEXT content (raw formatting codes)" : "Text content";
            _value.Text = text.Text; _value.AcceptsReturn = text.Multiline;
            _value.MinHeight = text.Multiline ? 170 : 36; _value.MaxHeight = 260;
            _value.MaxLength = TextEditing.MaximumCharacters;
            _status.Text = text.Multiline ? "Edits the stored MTEXT content. Full rich-text formatting and layout are not implemented." : "Position, height, rotation, placement and common properties are retained.";
        }
        else if (expected is CompositeEntity insert)
        {
            _attributes = DxfAttributeEditing.Read(insert);
            if (_attributes.IsEmpty) throw new NotSupportedException("This block has no editable attribute sequence.");
            var choose = CadUi.Identify(new ComboBox { Header = "Attribute tag", ItemsSource = _attributes.Select(a => $"{a.Index + 1}. {a.Tag}" + (a.Invisible ? " (invisible)" : "")).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 }, "annotation.attribute", "Attribute tag");
            body.Children.Add(choose);
            _value.Header = "Attribute value"; _value.MaxLength = DxfAttributeEditing.MaximumValueCharacters; _value.MinHeight = 36;
            ShowAttribute();
            choose.SelectionChanged += (_, _) => { StageAttribute(); _index = Math.Max(0, choose.SelectedIndex); ShowAttribute(); };
        }
        else throw new NotSupportedException("Select supported text or an attributed block.");
        _status.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(_value); body.Children.Add(_status);
        body.Children.Add(CadTheme.Text("Apply commits one Undo step. Cancel leaves the drawing unchanged.", 11, CadTheme.Muted));
        Content = body;
    }
    private void StageAttribute()
    {
        if (!_attributes.IsEmpty && _attributes[_index].Editable)
        {
            if (_value.Text == _attributes[_index].Value) _changes.Remove(_index);
            else _changes[_index] = _value.Text;
        }
    }
    private void ShowAttribute()
    {
        var attribute = _attributes[_index]; _value.Text = _changes.GetValueOrDefault(_index, attribute.Value);
        _value.IsReadOnly = !attribute.Editable;
        _status.Text = attribute.Editable
            ? "Edits the value only. Tag, flags, placement, handles and application data stay unchanged."
            : "Read only: constant, field-backed or multiline attribute semantics are not editable here.";
    }
    public void ShowError(string message) => _status.Text = message;
    public void Apply()
    {
        if (_attributes.IsEmpty) { _session.SetText(_expected, _value.Text); return; }
        if (!_session.EditableSelection().Any(e => ReferenceEquals(e, _expected)))
            throw new InvalidOperationException("The block or selection changed. Reopen the editor.");
        StageAttribute();
        _session.Document.Edit("Edit block attributes", d => DxfAttributeEditing.Apply(d, (CompositeEntity)_expected, _changes));
    }
}
