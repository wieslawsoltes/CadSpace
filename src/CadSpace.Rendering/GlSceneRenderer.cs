using System.Numerics;
using CadSpace.Geometry;
using CadSpace.Model;
using Silk.NET.OpenGL;

namespace CadSpace.Rendering;

public enum ModelVisualStyle { Wireframe, HiddenLine, Shaded, ShadedEdges }

/// <summary>Depth-tested geometry, selection, section clipping and world-plane text in OpenGL 3.3 / GLES 3.0.</summary>
public sealed class GlSceneRenderer
{
    private uint _program, _vao, _vertices, _highlight;
    private readonly SelectionStream _flags = new();
    public long GeometryUploads { get; private set; }
    public long SelectionBytesUploaded { get; private set; }
    private int _matrix, _clip, _flat, _triangleCount, _lineCount, _pointCount;
    private int _patternCount, _patternLength, _patternElements;
    private readonly float[] _patternBuffer = new float[64];
    private readonly List<(int Start, int Count, StrokePattern? Pattern)> _lineBatches = new();
    private DrawingScene? _uploaded;
    private Vec3 _origin;
    private readonly GlTextRenderer _text = new();
    private HashSet<Guid> _selection = new();
    public string Device { get; private set; } = "Not initialized";
    public unsafe void Initialize(GL gl)
    {
        using var state = new GlState(gl);
        var language = gl.GetStringS(StringName.ShadingLanguageVersion);
        var version = language.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase) ? "#version 300 es" : "#version 330 core";
        try
        {
            _program = Link(gl, version + "\nprecision highp float;\nlayout(location=0) in vec3 aPosition;layout(location=1) in vec3 aNormal;layout(location=2) in vec3 aColor;layout(location=3) in float aSelected;layout(location=4) in float aDistance;out float vDistance;uniform mat4 uMatrix;out vec3 vColor;out vec3 vWorld;void main(){float n=length(aNormal);float light=n<0.1?1.0:(0.35+0.65*abs(dot(normalize(aNormal),normalize(vec3(0.4,-0.5,0.8)))));vColor=mix(aColor,vec3(0.337,0.675,1.0),aSelected)*light;vWorld=aPosition;vDistance=aDistance;gl_Position=uMatrix*vec4(aPosition,1.0);gl_PointSize=5.0;}",
                version + "\nprecision highp float;\nin vec3 vColor;in vec3 vWorld;uniform vec4 uClip;uniform int uFlat;in float vDistance;uniform int uPatternCount;uniform float uPatternLength;uniform float uPattern[64];out vec4 outColor;void main(){if(dot(uClip.xyz,vWorld)+uClip.w>0.0)discard;float pixel=max(fwidth(vDistance),0.000001);if(uPatternCount>0 && uPatternLength>=2.0*pixel){float phase=mod(vDistance,uPatternLength);float at=0.0;bool ink=false;for(int i=0;i<64;i++){if(i>=uPatternCount)break;float dash=uPattern[i];float next=at+abs(dash);if(dash>0.0 && phase>=at && phase<next)ink=true;if(dash==0.0){float d=abs(phase-at);if(min(d,uPatternLength-d)<=pixel*0.75)ink=true;}at=next;}if(!ink)discard;}outColor=vec4(uFlat==1?vec3(0.114,0.141,0.173):vColor,1.0);}");
            _matrix = gl.GetUniformLocation(_program, "uMatrix"); _clip = gl.GetUniformLocation(_program, "uClip"); _flat = gl.GetUniformLocation(_program, "uFlat");
            _patternCount = gl.GetUniformLocation(_program, "uPatternCount");
            _patternLength = gl.GetUniformLocation(_program, "uPatternLength");
            _patternElements = gl.GetUniformLocation(_program, "uPattern[0]");
            _vao = gl.GenVertexArray(); _vertices = gl.GenBuffer(); gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
            for (uint i = 0; i < 3; i++) { gl.VertexAttribPointer(i, 3, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)(i * 3 * sizeof(float))); gl.EnableVertexAttribArray(i); }
            gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)(9 * sizeof(float))); gl.EnableVertexAttribArray(4);
            _highlight=gl.GenBuffer(); gl.BindBuffer(BufferTargetARB.ArrayBuffer,_highlight);
            gl.VertexAttribPointer(3,1,VertexAttribPointerType.Float,false,sizeof(float),(void*)0); gl.EnableVertexAttribArray(3);
            _text.Initialize(gl, version); Device = gl.GetStringS(StringName.Renderer); _uploaded = null;
            Console.WriteLine($"CADSPACE_GPU_INITIALIZED: {Device}; {language}");
        }
        catch { Destroy(gl); throw; }
    }
    internal static uint Link(GL gl, string vertexSource, string fragmentSource)
    {
        uint vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = Compile(gl, ShaderType.VertexShader, vertexSource); fragment = Compile(gl, ShaderType.FragmentShader, fragmentSource);
            program = gl.CreateProgram(); gl.AttachShader(program, vertex); gl.AttachShader(program, fragment); gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var success);
            if (success == 0) throw new InvalidOperationException(gl.GetProgramInfoLog(program)); return program;
        }
        catch { if (program != 0) gl.DeleteProgram(program); throw; }
        finally { if (vertex != 0) gl.DeleteShader(vertex); if (fragment != 0) gl.DeleteShader(fragment); }
    }
    private static uint Compile(GL gl, ShaderType type, string source)
    {
        var shader = gl.CreateShader(type); gl.ShaderSource(shader, source); gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var success);
        if (success == 0) { var error = gl.GetShaderInfoLog(shader); gl.DeleteShader(shader); throw new InvalidOperationException(error); } return shader;
    }
    internal static void SetClip(GL gl, int location, Plane3? plane, Vec3 origin)
    {
        if (plane is Plane3 clip) gl.Uniform4(location, (float)clip.Normal.X, (float)clip.Normal.Y, (float)clip.Normal.Z, (float)clip.SignedDistance(origin));
        else gl.Uniform4(location, 0f, 0f, 0f, -1f);
    }
    public unsafe void Render(GL gl, DrawingScene scene, Camera3D camera, double width, double height,
        IReadOnlySet<Guid>? selection = null, ModelVisualStyle style = ModelVisualStyle.ShadedEdges, Plane3? clip = null)
    {
        selection ??= new HashSet<Guid>();
        using (var state = new GlState(gl))
        {
            gl.Enable(EnableCap.DepthTest); gl.DepthFunc(DepthFunction.Lequal); gl.DepthMask(true); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.CullFace); gl.Disable(EnableCap.ScissorTest); gl.ColorMask(true, true, true, true);
            gl.ClearColor(.114f, .141f, .173f, 1); gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            var geometryChanged = !ReferenceEquals(scene, _uploaded) || _origin != camera.Origin;
            if (geometryChanged)
            {
                Upload(gl, scene, camera.Origin, selection);
                _uploaded = scene; _origin = camera.Origin; GeometryUploads++;
                Console.WriteLine($"CADSPACE_GPU_FRAME: triangles={_triangleCount / 3}; lines={_lineCount / 2}");
            }
            if (geometryChanged || !_selection.SetEquals(selection))
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer,_highlight);
                foreach(var range in _flags.Update(selection))
                {
                    gl.BufferSubData<float>(BufferTargetARB.ArrayBuffer,(nint)(range.Start*sizeof(float)),_flags.Values.AsSpan(range.Start,range.Count));
                    SelectionBytesUploaded+=(long)range.Count*sizeof(float);
                }
                _text.Upload(gl,scene,camera.Origin,selection); _selection=selection.ToHashSet();
                Console.WriteLine($"CADSPACE_GPU_UPLOADS: geometry={GeometryUploads}; selectionBytes={SelectionBytesUploaded}");
            }
            gl.UseProgram(_program); gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
            var matrix = camera.Matrix(width / Math.Max(1, height)); gl.UniformMatrix4(_matrix, 1, false, (float*)&matrix); SetClip(gl, _clip, clip, camera.Origin);
            gl.Uniform1(_patternCount, 0);
            if (style != ModelVisualStyle.Wireframe)
            {
                gl.Uniform1(_flat, style == ModelVisualStyle.HiddenLine ? 1 : 0); gl.Enable(EnableCap.PolygonOffsetFill); gl.PolygonOffset(1, 1);
                gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_triangleCount); gl.Disable(EnableCap.PolygonOffsetFill);
            }
            gl.Uniform1(_flat, 0);
            if (style != ModelVisualStyle.Shaded) foreach (var batch in _lineBatches)
            {
                if (batch.Pattern is { } pattern)
                {
                    gl.Uniform1(_patternCount, pattern.Definition.Elements.Length); gl.Uniform1(_patternLength, (float)pattern.Length);
                    Array.Clear(_patternBuffer);
                    for (var i = 0; i < pattern.Definition.Elements.Length; i++) _patternBuffer[i] = (float)(pattern.Definition.Elements[i] * pattern.Scale);
                    fixed (float* values = _patternBuffer) gl.Uniform1(_patternElements, 64, values);
                }
                else gl.Uniform1(_patternCount, 0);
                gl.DrawArrays(PrimitiveType.Lines, batch.Start, (uint)batch.Count);
            }
            gl.Uniform1(_patternCount, 0);
            gl.DrawArrays(PrimitiveType.Points, _triangleCount + _lineCount, (uint)_pointCount);
            _text.Render(gl, matrix, camera.Origin, clip);
        }
        if (OperatingSystem.IsBrowser())
        {
            // Uno subsequently reads pixels into CPU memory. Define its packing contract explicitly.
            gl.BindBuffer(BufferTargetARB.PixelPackBuffer, 0); gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
            gl.PixelStore(PixelStoreParameter.PackRowLength, 0); gl.PixelStore(PixelStoreParameter.PackSkipRows, 0); gl.PixelStore(PixelStoreParameter.PackSkipPixels, 0);
        }
    }
    private void Upload(GL gl, DrawingScene scene, Vec3 origin, IReadOnlySet<Guid> selection)
    {
        var capacity = checked(scene.Triangles.Length*3 + scene.Paths.Where(p => p.VertexColors.IsEmpty).Sum(p => p.Points.Length==1?1:Math.Max(0,p.Points.Length-(p.Closed?0:1))*2));
        var data = new float[checked(capacity*10)]; var offset=0; _flags.Clear();
        void Vertex(Vec3 position, Vec3 normal, uint color, Guid id, double tint = 1, double distance = 0)
        {
            var p = position - origin;
            data[offset++]=(float)p.X; data[offset++]=(float)p.Y; data[offset++]=(float)p.Z;
            data[offset++]=(float)normal.X; data[offset++]=(float)normal.Y; data[offset++]=(float)normal.Z;
            data[offset++]=(float)(((color>>16)&255)/255.0*tint); data[offset++]=(float)(((color>>8)&255)/255.0*tint); data[offset++]=(float)((color&255)/255.0*tint);
            data[offset++]=(float)distance;
            _flags.AddVertex(id);
        }
        foreach (var triangle in scene.Triangles)
        {
            var n = (triangle.B - triangle.A).Cross(triangle.C - triangle.A); if (n.Length < 1e-12) continue; n = triangle.ColorB.HasValue ? default : n.Normalized;
            Vertex(triangle.A, n, triangle.Color, triangle.EntityId); Vertex(triangle.B, n, triangle.ColorB ?? triangle.Color, triangle.EntityId); Vertex(triangle.C, n, triangle.ColorC ?? triangle.Color, triangle.EntityId);
        }
        _triangleCount = offset / 10;
        _lineBatches.Clear();
        foreach (var group in scene.Paths.Where(p => p.Points.Length > 1 && p.VertexColors.IsEmpty).GroupBy(p => p.Pattern))
        {
            var start = offset / 10;
            foreach (var path in group)
            {
                double phase = 0;
                for (var i = 0; i < path.Points.Length - (path.Closed ? 0 : 1); i++)
                {
                    var a = path.Points[i]; var b = path.Points[(i + 1) % path.Points.Length]; var length = a.DistanceTo(b);
                    Vertex(a, default, path.Color, path.EntityId, .8, phase); Vertex(b, default, path.Color, path.EntityId, .8, phase + length);
                    phase = path.Pattern == null ? 0 : (phase + length) % path.Pattern.Length;
                }
            }
            _lineBatches.Add((start, offset / 10 - start, group.Key));
        }
        _lineCount = offset / 10 - _triangleCount;
        foreach (var path in scene.Paths.Where(p => p.Points.Length == 1)) Vertex(path.Points[0], default, path.Color, path.EntityId);
        _pointCount = offset / 10 - _triangleCount - _lineCount;
        gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices); gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data.AsSpan(0,offset), BufferUsageARB.StaticDraw);
        _flags.Allocate(); gl.BindBuffer(BufferTargetARB.ArrayBuffer,_highlight); gl.BufferData<float>(BufferTargetARB.ArrayBuffer,_flags.Values.AsSpan(),BufferUsageARB.DynamicDraw);
    }
    public void Destroy(GL gl)
    {
        if (_highlight != 0) gl.DeleteBuffer(_highlight); _highlight=0; _flags.Clear();
        _text.Destroy(gl); if (_vao != 0) gl.DeleteVertexArray(_vao); if (_vertices != 0) gl.DeleteBuffer(_vertices); if (_program != 0) gl.DeleteProgram(_program);
        _vao = _vertices = _program = 0; _uploaded = null;
    }
}

/// <summary>Preserves every state modified by the scene/text passes; framebuffer ownership remains with Uno.</summary>
internal sealed unsafe class GlState : IDisposable
{
    private readonly GL _gl;
    private readonly (EnableCap Cap, bool Enabled)[] _enabled;
    private readonly int _program, _vao, _buffer, _depthFunc, _depthMask, _activeTexture, _texture, _sampler, _unpackBuffer, _unpackAlignment, _unpackRow, _unpackRows, _unpackPixels;
    private readonly int _srcRgb, _dstRgb, _srcAlpha, _dstAlpha;
    private readonly float _factor, _units;
    private readonly int[] _mask = new int[4]; private readonly float[] _clear = new float[4];
    public GlState(GL gl)
    {
        _gl = gl; _program = gl.GetInteger(GetPName.CurrentProgram); _vao = gl.GetInteger(GetPName.VertexArrayBinding); _buffer = gl.GetInteger(GetPName.ArrayBufferBinding);
        _depthFunc = gl.GetInteger(GetPName.DepthFunc); _depthMask = gl.GetInteger(GetPName.DepthWritemask);
        _srcRgb = gl.GetInteger(GetPName.BlendSrcRgb); _dstRgb = gl.GetInteger(GetPName.BlendDstRgb); _srcAlpha = gl.GetInteger(GetPName.BlendSrcAlpha); _dstAlpha = gl.GetInteger(GetPName.BlendDstAlpha);
        _factor = gl.GetFloat(GetPName.PolygonOffsetFactor); _units = gl.GetFloat(GetPName.PolygonOffsetUnits);
        fixed (int* p = _mask) gl.GetInteger(GetPName.ColorWritemask, p); fixed (float* p = _clear) gl.GetFloat(GetPName.ColorClearValue, p);
        _enabled = new[] { EnableCap.DepthTest, EnableCap.Blend, EnableCap.CullFace, EnableCap.ScissorTest, EnableCap.PolygonOffsetFill }.Select(c => (c, gl.IsEnabled(c))).ToArray();
        _activeTexture = gl.GetInteger(GetPName.ActiveTexture); gl.ActiveTexture(TextureUnit.Texture0); _texture = gl.GetInteger(GetPName.TextureBinding2D); _sampler = gl.GetInteger(GetPName.SamplerBinding); gl.BindSampler(0, 0);
        _unpackBuffer = gl.GetInteger(GetPName.PixelUnpackBufferBinding); _unpackAlignment = gl.GetInteger(GetPName.UnpackAlignment); _unpackRow = gl.GetInteger(GetPName.UnpackRowLength); _unpackRows = gl.GetInteger(GetPName.UnpackSkipRows); _unpackPixels = gl.GetInteger(GetPName.UnpackSkipPixels);
        gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0); gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4); gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0); gl.PixelStore(PixelStoreParameter.UnpackSkipRows, 0); gl.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
    }
    public void Dispose()
    {
        var gl = _gl; gl.UseProgram((uint)_program); gl.BindVertexArray((uint)_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_buffer);
        gl.DepthFunc((DepthFunction)_depthFunc); gl.DepthMask(_depthMask != 0); gl.ColorMask(_mask[0] != 0, _mask[1] != 0, _mask[2] != 0, _mask[3] != 0); gl.ClearColor(_clear[0], _clear[1], _clear[2], _clear[3]);
        gl.BlendFuncSeparate((BlendingFactor)_srcRgb, (BlendingFactor)_dstRgb, (BlendingFactor)_srcAlpha, (BlendingFactor)_dstAlpha); gl.PolygonOffset(_factor, _units);
        foreach (var (cap, enabled) in _enabled) { if (enabled) gl.Enable(cap); else gl.Disable(cap); }
        gl.BindTexture(TextureTarget.Texture2D, (uint)_texture); gl.BindSampler(0, (uint)_sampler); gl.ActiveTexture((TextureUnit)_activeTexture);
        gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, (uint)_unpackBuffer); gl.PixelStore(PixelStoreParameter.UnpackAlignment, _unpackAlignment); gl.PixelStore(PixelStoreParameter.UnpackRowLength, _unpackRow); gl.PixelStore(PixelStoreParameter.UnpackSkipRows, _unpackRows); gl.PixelStore(PixelStoreParameter.UnpackSkipPixels, _unpackPixels);
    }
}
