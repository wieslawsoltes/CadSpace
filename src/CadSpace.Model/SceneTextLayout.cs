using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace CadSpace.Model;

/// <summary>Plain multiline display data shared by bounds, Skia drafting and GPU text preparation.
/// Cached by immutable SceneText identity; record copies cannot inherit a stale layout.</summary>
public sealed class SceneTextLayout
{
    private static readonly ConditionalWeakTable<SceneText, SceneTextLayout> Cache = new();
    public ImmutableArray<string> Lines { get; }
    public int MaximumLineLength { get; }
    private SceneTextLayout(SceneText text)
    {
        Lines = NormalizeLineEndings(text.Text).Split('\n').ToImmutableArray();
        MaximumLineLength = Lines.Max(line => line.Length);
    }
    public static SceneTextLayout For(SceneText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Cache.GetValue(text, static value => new(value));
    }
    /// <summary>Normalize CRLF and CR only. Preserve every other character, including trailing empty lines.</summary>
    public static string NormalizeLineEndings(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.IndexOf('\r') < 0 ? value : value.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
