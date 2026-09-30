using System.Numerics;
using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace CadSpace.Rendering;

/// <summary>Bounded label atlases and depth-tested world-plane text. Typography uses the host Skia typeface, not SHX.</summary>
internal sealed class GlTextRenderer
{
    private const int AtlasSize = 2048;
    private const int FontSize = 48;
    private const int MaximumPages = 8;
    private uint _program, _vao, _buffer;
    private int _matrix, _clip;
    private readonly List<(uint Texture, int Start, int Count)> _pages = new();
    private ImmutableArray<SceneText> _labels = [];
    private Vec3 _origin;
    private float[] _vertexData = [];
    private readonly List<(int Start, Guid Id, uint Color)> _labelsInBuffer = new();
    public unsafe void Initialize(GL gl, string version)
    {
        _program = GlSceneRenderer.Link(gl, version + "\nprecision highp float;\nlayout(location=0) in vec3 aPosition;layout(location=1) in vec3 aColor;layout(location=2) in vec2 aUv;uniform mat4 uMatrix;out vec3 vColor;out vec3 vWorld;out vec2 vUv;void main(){vColor=aColor;vWorld=aPosition;vUv=aUv;gl_Position=uMatrix*vec4(aPosition,1.0);}",
            version + "\nprecision highp float;\nin vec3 vColor;in vec3 vWorld;in vec2 vUv;uniform sampler2D uAtlas;uniform vec4 uClip;out vec4 outColor;void main(){if(dot(uClip.xyz,vWorld)+uClip.w>0.0)discard;float a=texture(uAtlas,vUv).a;if(a<0.01)discard;outColor=vec4(vColor,a);}");
        _matrix = gl.GetUniformLocation(_program, "uMatrix"); _clip = gl.GetUniformLocation(_program, "uClip");
        _vao = gl.GenVertexArray(); _buffer = gl.GenBuffer();
        gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(0); gl.EnableVertexAttribArray(1); gl.EnableVertexAttribArray(2);
        gl.UseProgram(_program); gl.Uniform1(gl.GetUniformLocation(_program, "uAtlas"), 0);
    }
    public unsafe void Upload(GL gl, DrawingScene scene, Vec3 origin, IReadOnlySet<Guid> selection)
    {
        if (_labels.SequenceEqual(scene.Texts) && _origin == origin && _vertexData.Length > 0)
        {
            foreach (var label in _labelsInBuffer)
            {
                var color = selection.Contains(label.Id) ? 0xFF56ACFFu : label.Color;
                for (var i = 0; i < 6; i++) { var offset = (label.Start + i) * 8 + 3; _vertexData[offset] = ((color >> 16) & 255) / 255f; _vertexData[offset + 1] = ((color >> 8) & 255) / 255f; _vertexData[offset + 2] = (color & 255) / 255f; }
            }
            gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer); gl.BufferData<float>(BufferTargetARB.ArrayBuffer, _vertexData.AsSpan(), BufferUsageARB.DynamicDraw); return;
        }
        foreach (var page in _pages) gl.DeleteTexture(page.Texture); _pages.Clear(); _labelsInBuffer.Clear();
        var data = new List<float>();
        using var font = new SKFont(SKTypeface.Default, FontSize);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var metrics = font.Metrics; var top = -metrics.Ascent + 3; var lineHeight = FontSize * 1.3f;
        SKBitmap? bitmap = null; SKCanvas? canvas = null; int x = 0, y = 0, rowHeight = 0, start = 0;
        var cache = new Dictionary<string, (int X, int Y, int W, int H)>(StringComparer.Ordinal);
        void NewPage()
        {
            bitmap = new SKBitmap(new SKImageInfo(AtlasSize, AtlasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent); x = y = rowHeight = 0; start = data.Count / 8; cache.Clear();
        }
        void Finish()
        {
            if (bitmap == null) return;
            var texture = gl.GenTexture(); gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, AtlasSize, AtlasSize, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)bitmap.GetPixels());
            _pages.Add((texture, start, data.Count / 8 - start)); canvas!.Dispose(); bitmap.Dispose(); canvas = null; bitmap = null;
        }
        try
        {
            foreach (var text in scene.Texts)
            {
                if (string.IsNullOrEmpty(text.Text)) continue;
                var lines = SceneTextLayout.For(text).Lines;
                var width = (int)Math.Ceiling(lines.Max(l => font.MeasureText(l))) + 6;
                var height = (int)Math.Ceiling(top + metrics.Descent + (lines.Length - 1) * lineHeight) + 3;
                if (width > AtlasSize || height > AtlasSize) throw new NotSupportedException("A text label exceeds the GPU atlas size. Split long paragraphs into shorter labels.");
                if (bitmap == null) NewPage();
                if (!cache.TryGetValue(text.Text, out var rect))
                {
                    if (x + width > AtlasSize) { x = 0; y += rowHeight; rowHeight = 0; }
                    if (y + height > AtlasSize)
                    {
                        Finish(); if (_pages.Count >= MaximumPages) throw new NotSupportedException("The drawing exceeds the bounded 8-page GPU text atlas budget."); NewPage();
                    }
                    rect = (x, y, width, height); cache[text.Text] = rect;
                    for (var i = 0; i < lines.Length; i++) canvas!.DrawText(lines[i], x + 3, y + top + i * lineHeight, font, paint);
                    x += width; rowHeight = Math.Max(rowHeight, height);
                }
                _labelsInBuffer.Add((data.Count / 8, text.EntityId, text.Color));
                var color = selection.Contains(text.EntityId) ? 0xFF56ACFFu : text.Color;
                void Vertex(double sx, double sy, float u, float v)
                {
                    var point = text.Position - origin + text.AxisX * ((sx - 3) * text.Height / FontSize) + text.AxisY * ((top - sy) * text.Height / FontSize);
                    data.AddRange([(float)point.X, (float)point.Y, (float)point.Z, ((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, u, v]);
                }
                var u0 = (float)rect.X / AtlasSize; var v0 = (float)rect.Y / AtlasSize;
                var u1 = (float)(rect.X + rect.W) / AtlasSize; var v1 = (float)(rect.Y + rect.H) / AtlasSize;
                Vertex(0, 0, u0, v0); Vertex(0, rect.H, u0, v1); Vertex(rect.W, rect.H, u1, v1);
                Vertex(0, 0, u0, v0); Vertex(rect.W, rect.H, u1, v1); Vertex(rect.W, 0, u1, v0);
            }
            Finish();
            gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
            _vertexData = data.ToArray(); _labels = scene.Texts; _origin = origin;
            gl.BufferData<float>(BufferTargetARB.ArrayBuffer, _vertexData.AsSpan(), BufferUsageARB.StaticDraw);
            if (scene.Texts.Length > 0) Console.WriteLine($"CADSPACE_GPU_TEXT: labels={scene.Texts.Length}; atlases={_pages.Count}");
        }
        finally { canvas?.Dispose(); bitmap?.Dispose(); }
    }
    public unsafe void Render(GL gl, Matrix4x4 matrix, Vec3 origin, Plane3? clip)
    {
        if (_pages.Count == 0) return;
        gl.UseProgram(_program); gl.BindVertexArray(_vao); gl.UniformMatrix4(_matrix, 1, false, (float*)&matrix);
        GlSceneRenderer.SetClip(gl, _clip, clip, origin);
        gl.Enable(EnableCap.Blend); gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        foreach (var page in _pages) { gl.BindTexture(TextureTarget.Texture2D, page.Texture); gl.DrawArrays(PrimitiveType.Triangles, page.Start, (uint)page.Count); }
        gl.DepthMask(true); gl.Disable(EnableCap.Blend);
    }
    public void Destroy(GL gl)
    {
        foreach (var page in _pages) gl.DeleteTexture(page.Texture); _pages.Clear();
        if (_program != 0) gl.DeleteProgram(_program); if (_vao != 0) gl.DeleteVertexArray(_vao); if (_buffer != 0) gl.DeleteBuffer(_buffer);
        _program = _vao = _buffer = 0; _labels = []; _vertexData = []; _labelsInBuffer.Clear();
    }
}
