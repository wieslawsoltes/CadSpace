using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed partial class CommandEngine
{
    private bool TrySubmitMText(string input)
    {
        if (!IsActive)
        {
            if (!input.Equals("MTEXT", StringComparison.OrdinalIgnoreCase) && !input.Equals("MT", StringComparison.OrdinalIgnoreCase)) return false;
            _active = "MTEXT"; _points.Clear(); Prompt = "Specify multiline text insertion point";
            Message?.Invoke("Command: MTEXT"); Changed?.Invoke(); return true;
        }
        if (_active != "MTEXT") return false;
        if (input.Length == 0) { Cancel(); return true; }
        if (_points.Count == 0)
        {
            if (!GeometryMath.TryParsePoint(input, default, out var point)) throw new ArgumentException("Specify the insertion point as x,y or x,y,z.");
            TryMTextPoint(point);
        }
        else
        {
            TextEditing.ValidateValue(input, true);
            Session.Add("Multiline text", new TextEntity(_points[0], input, Multiline: true)); Cancel();
        }
        return true;
    }
    private bool TryMTextPoint(Vec3 point)
    {
        if (_active != "MTEXT") return false;
        if (_points.Count == 0) { _points.Add(point); Prompt = "Enter content (\\P for a paragraph); use DDEDIT for multiline editing"; Changed?.Invoke(); }
        else Message?.Invoke(Prompt);
        return true;
    }
}
