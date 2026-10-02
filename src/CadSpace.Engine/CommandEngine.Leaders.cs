using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed partial class CommandEngine
{
    private bool TrySubmitLeader(string input)
    {
        if (!IsActive)
        {
            var name = input.ToUpperInvariant() switch { "LE" => "LEADER", "LED" => "LEADEREDIT", _ => input.ToUpperInvariant() };
            if (name is not ("LEADER" or "LEADEREDIT")) return false;
            _active = name; _points.Clear(); Message?.Invoke("Command: " + name);
            if (name == "LEADEREDIT" && Session.Selection.Count > 0) OpenLeaderEditor();
            else { Prompt = name == "LEADEREDIT" ? "Pick a straight leader; Enter cancels" : "Specify leader arrow tip"; Changed?.Invoke(); }
            return true;
        }
        if (_active is not ("LEADER" or "LEADEREDIT")) return false;
        if (input.Length == 0)
        {
            if (_active == "LEADER" && _points.Count >= 2) Session.Add("Leader", new LeaderEntity(_points.ToImmutableArray()));
            Cancel(); return true;
        }
        if (_active == "LEADER" && input.Equals("U", StringComparison.OrdinalIgnoreCase))
        { if (_points.Count > 0) _points.RemoveAt(_points.Count-1); Prompt = "Specify next leader vertex; U removes last; Enter finishes"; Changed?.Invoke(); return true; }
        if (!GeometryMath.TryParsePoint(input, ReferencePoint ?? default, out var p)) throw new ArgumentException("Enter x,y,z, U or Enter.");
        TryLeaderPoint(p); return true;
    }
    private bool TryLeaderPoint(Vec3 point)
    {
        if (_active is not ("LEADER" or "LEADEREDIT")) return false;
        if (_active == "LEADEREDIT")
        {
            var id = Session.HitTest(point, PickTolerance); var root = Session.Document.Drawing.Entities.FirstOrDefault(e => e.Id == id);
            if (root == null || LeaderGeometry.Unwrap(root) == null) throw new ArgumentException("No editable straight leader at this point.");
            Session.Select(id); OpenLeaderEditor(); return true;
        }
        if (_points.Count >= LeaderGeometry.MaximumVertices) throw new ArgumentException("Leader vertex budget exceeded.");
        if (_points.Count > 0 && point.DistanceTo(_points[^1]) < 1e-12) { Message?.Invoke("Specify a different vertex."); return true; }
        _points.Add(point); Prompt = "Specify next leader vertex; U removes last; Enter finishes"; Changed?.Invoke(); return true;
    }
    private void OpenLeaderEditor()
    {
        var e = Session.EditableSelection();
        if (e.Length != 1 || LeaderGeometry.Unwrap(e[0]) == null) throw new ArgumentException("Select one straight leader.");
        Cancel(); ViewRequested?.Invoke("LEADEREDITOR");
    }
    private IReadOnlyList<Entity>? LeaderPreview(Vec3 point)
    {
        if (_active == "LEADEREDIT") return [];
        if (_active != "LEADER") return null;
        if (!point.IsFinite || _points.Count == 0 || _points.Count >= LeaderGeometry.MaximumVertices || point.DistanceTo(_points[^1]) < 1e-12) return [];
        return [new LeaderEntity(_points.Append(point).ToImmutableArray())];
    }
}
