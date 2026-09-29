# CadSpace

### A modular CAD workspace for desktop and the browser

[![Build, test and deploy](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/CadSpace.Geometry.svg?label=NuGet)](https://www.nuget.org/packages/CadSpace.Geometry)
[![Downloads](https://img.shields.io/nuget/dt/CadSpace.Geometry.svg)](https://www.nuget.org/packages/CadSpace.Geometry)

**[Launch CadSpace](https://wieslawsoltes.github.io/CadSpace/)** · **[Workspace guide](docs/WORKSPACE.md)** · **[Commands](docs/COMMANDS.md)** · **[Coverage](docs/FEATURES.md)** · **[Architecture](docs/ARCHITECTURE.md)**

CadSpace is an independent C# CAD application built with [Uno Platform](https://platform.uno), Skia and OpenGL/WebGL. It combines a familiar dark ribbon workspace with double-precision drafting, layers, blocks, rational splines, triangle-mesh modeling, ASCII/binary DXF exchange and independently reusable libraries.

> **Development preview, not full AutoCAD parity.** The interface uses original CadSpace artwork and implemented command workflows. It is not pixel-exact AutoCAD or CUI/API compatible. Mesh modeling is not an analytic ACIS/B-rep kernel; arbitrary DXF editing is not universally lossless. Keep original files and review export warnings.

## An integrated CAD workspace

The startup document is an editable studio floor plan. The **CS application menu** provides file operations, recovery, open drawings and the 3D model example. The following controls are connected to the same editing/session state, rather than separate demonstration actions.

| Workspace area | Integrated controls and behavior |
| --- | --- |
| Application and discovery | Application menu, customizable Quick Access toolbar, command search, optional classic File/Edit/View/Draw/Modify/Tools/Help menus |
| Ribbon | Large and split command buttons, compact tools, grouped panels, dialog launchers, overflow, minimization and selection-context tabs |
| Drawing properties | Inline layer/color/linetype/lineweight selectors, Properties, virtualized Layer Properties Manager, Linetype Manager |
| Documents and layouts | Open-document tabs, dirty indicators, close confirmation, overflow/reordering, Model/Layout tabs and undoable supported layout creation/rename/deletion |
| Palettes | Left/right docking, in-window floating, auto-hide, drag-to-edge, Ctrl-to-float, resizing and Escape rollback; searchable command/block Tool Palettes |
| Viewport | Camera-synchronized face/edge/corner ViewCube, standard-view/style menus, vertical pan/zoom/orbit/projection/clipping controls |
| Commands and precision | Resizable history/input, completion and Tab acceptance, dynamic input, selection/grips, individual snap modes and customizable drafting-status controls |
| Options | Staged Display/Workspace/Status Bar tabs; Apply/Cancel/Reset, persisted chrome and palette preferences, three workspace presets |

`MENUBAR` → `1` shows the classic menu bar. `OPTIONS` (`OP`) controls display settings without editing the drawing. Display-only changes preserve the current camera/view; hiding a status control does not disable its mode. Palette and command-window gestures can be cancelled with Escape.

Workspace preferences are stored separately from drawings: browser localStorage or desktop LocalFolder. Preset, palette placement/visibility/pinning, menu/navigation visibility, ribbon/clean screen, command height and status customization survive reload. Quick Access customization, camera positions, custom tool catalogs and per-document drafting settings are not persisted. These preferences are not a crash-durability guarantee.

Read the [workspace guide](docs/WORKSPACE.md) for individual control APIs, integration examples and exact boundaries. Floating palettes stay inside the app; detached native windows, arbitrary split/tab docking, CUI editing and every AutoCAD dialog remain outside the implemented scope.

## Drafting, modeling and interchange

The engine exposes **77 command workflows**, including UI commands. Names do not imply every AutoCAD option. See the [command reference](docs/COMMANDS.md).

| Area | Implemented scope |
| --- | --- |
| Drafting | Lines, 2D/3D polylines, rectangles, circles, arcs, ellipses, control-point splines, points, plain text, aligned dimensions and supported hatches |
| Editing | Transforms, supported offsets/arrays/explode, line-based trim/extend/fillet/chamfer/join/break, crossing STRETCH, bounded transactional grips and undo/redo |
| Precision | Absolute/relative/polar input; indexed block/OCS anchors, supported intersections/perpendiculars/tangents, grid, ortho and polar guidance |
| Organization | Layers, nested blocks, simple linetypes and scales, model/paper-space entity separation |
| Mesh modeling | Primitives, extrusion, revolved surfaces, capped matching-profile loft, parallel-transport sweep and bounded closed-mesh Booleans |
| Rendering | Depth-tested faces/feature edges, selection, world-plane plain text, visual styles, perspective/orthographic cameras and uncapped display clipping |
| DXF | ASCII/binary transport, supported code pages, OCS/affine geometry, legacy polylines/meshes, rational splines, hatches, supported compound display, native MESH/SPLINE/HATCH/style output |

Native **polyline widths** retain constant and tapered segment widths, bulges and supported OCS placements. `PLINEWID` sets the default for new polylines/rectangles; `PEDIT` supports Width/Open/Close/Reverse. Properties includes a vertex-indexed editor without creating one control per vertex. Width fills are selectable in 2D/3D. Joins currently use bevels, curves are sampled and wide fills display continuously despite assigned linetypes. See [width semantics](docs/WIDE-POLYLINES.md).

Simple dash/gap/dot linetypes support ByLayer/ByBlock, per-object/global scales and polyline generation flags. `LAYER`, `LINETYPE`, `CELTYPE`, `CELTSCALE` and `LTSCALE` expose supported style management. Complex SHX/text/shape patterns remain source-backed fallbacks, not complete typography support.

## Files and recovery

**Open** accepts `.cadspace` and ASCII/binary `.dxf`. **Save** writes a native project. DXF export writes an interchange copy and does not clear native dirty state. Native version 2 stores implemented geometry and original DXF provenance/bytes; saved provenance is checked against reparsed source before raw records are reused. Unknown data remains opaque. Modified compound records, sampled boundaries, generated dimensions and unmodeled metadata can require lossy conversion or rejection. There is no DWG/ACIS decoder or universal DXF-version semantic conversion.

Dirty documents checkpoint every five seconds after storage initialization into alternating checksummed native-project slots. Browser writes acknowledge IndexedDB transaction completion; desktop uses LocalFolder. **Recover** opens retained checkpoints as unsaved documents. Native save, clean undo or explicit discard clears applicable checkpoints. Recovery does not preserve undo, cameras or tab arrangement. Payloads are limited to 32 Mi-characters and discovery to 256 slots; storage denial/eviction, private mode, power loss and changes after the last checkpoint remain risks. Save projects and keep backups.

## Try it

Enter one command or prompted value per line:

```text
RECTANG
0,0
240,140
CIRCLE
120,70
30
ZOOM
```

Select closed XY profiles before `EXTRUDE`. `LOFT` requires matching closed profiles in drawing order; `SWEEP` requires one closed profile and one open polyline. Mesh Booleans require supported closed, consistently oriented meshes. `SUBTRACT` uses the first selected mesh in drawing order, not click order.

Normal 2D clicks/windows add, Shift removes and Ctrl toggles selection. Left-to-right windows contain; right-to-left windows cross. Drag supported blue grips to preview/commit one undoable edit; Escape cancels. Middle-drag pans; wheel zoom anchors at the pointer. In 3D, click selects and drag orbits. Clipping is display-only and uncapped.

| Shortcut | Action |
| --- | --- |
| Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+E | New / open / native save / ASCII DXF export |
| Ctrl+Z / Ctrl+Y / Ctrl+A | Undo / redo / select all, respecting text editing |
| Ctrl+1 / Ctrl+3 / Ctrl+0 | Properties / Tool Palettes / clean screen |
| F2 / F6 / Ctrl+K | Command history / command focus / search focus |
| F3 / F7 / F8 / F9 / F10 / F12 | Object snap / grid / ortho / grid snap / polar / dynamic input |
| Tab / Escape | Accept idle command completion / cancel |

Browser-reserved shortcuts can take precedence; visible controls also expose primary actions.

## Build and run

Pinned dependencies: **Uno.Sdk 6.7.30**, **Uno graphics 6.7.135**, **SkiaSharp 3.119.2**, **Silk.NET.OpenGL 2.23.0**, **.NET 10**. `global.json` accepts newer stable .NET 10 feature bands.

```sh
git clone https://github.com/wieslawsoltes/CadSpace.git
cd CadSpace
dotnet workload install wasm-tools

# Windows, macOS or Linux/X11
dotnet run --project src/CadSpace.App -f net10.0-desktop -p:CadSpaceDesktopOnly=true

# Browser development host
dotnet run --project src/CadSpace.App -f net10.0-browserwasm

# Publish for this repository's GitHub Pages path
dotnet publish src/CadSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/CadSpace/
```

3D requires a compatible accelerated OpenGL/GLES/WebGL context. Linux requires Uno/Skia's native dependencies and a display. Release browser builds enable IL/XAML trimming and the jiterpreter; `-p:CadSpaceUntrimmed=true` retains the diagnostic untrimmed build.

## Download

Every [release](https://github.com/wieslawsoltes/CadSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `CadSpace-<version>-win-x64.zip` | `CadSpace-<version>-win-arm64.zip` |
| macOS | `CadSpace-<version>-osx-x64.tar.gz` | `CadSpace-<version>-osx-arm64.tar.gz` |
| Linux | `CadSpace-<version>-linux-x64.tar.gz` | `CadSpace-<version>-linux-arm64.tar.gz` |

Extract and run `CadSpace` (`CadSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine CadSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS.txt`.

## NuGet packages

All six libraries are MIT-licensed, versioned together with the app and published to [NuGet.org](https://www.nuget.org/packages?q=CadSpace) on tagged releases, with symbol packages (`.snupkg`) and SourceLink. The first five target `net10.0`: geometry, model, editing and exchange require neither Uno nor a graphics context, and only `CadSpace.Rendering` brings SkiaSharp and Silk.NET OpenGL. `CadSpace.Controls` is the Uno Platform package (`net10.0-desktop` and `net10.0-browserwasm`) and ships every workspace component in one package, not one package per widget. The app owns file dialogs and document lifetime.

```bash
dotnet add package CadSpace.Geometry
```

| Package | Version | Downloads | Description |
|---|---|---|---|
| [CadSpace.Geometry](https://www.nuget.org/packages/CadSpace.Geometry) | [![NuGet](https://img.shields.io/nuget/vpre/CadSpace.Geometry.svg)](https://www.nuget.org/packages/CadSpace.Geometry) | [![Downloads](https://img.shields.io/nuget/dt/CadSpace.Geometry.svg)](https://www.nuget.org/packages/CadSpace.Geometry) | Double-precision vectors/transforms, OCS, rays/planes, intersections, triangulation and spatial index. |
| [CadSpace.Model](https://www.nuget.org/packages/CadSpace.Model) | [![NuGet](https://img.shields.io/nuget/vpre/CadSpace.Model.svg)](https://www.nuget.org/packages/CadSpace.Model) | [![Downloads](https://img.shields.io/nuget/dt/CadSpace.Model.svg)](https://www.nuget.org/packages/CadSpace.Model) | Immutable entities, styles and blocks, undoable documents, scene cache, spline/hatch and mesh algorithms. |
| [CadSpace.Engine](https://www.nuget.org/packages/CadSpace.Engine) | [![NuGet](https://img.shields.io/nuget/vpre/CadSpace.Engine.svg)](https://www.nuget.org/packages/CadSpace.Engine) | [![Downloads](https://img.shields.io/nuget/dt/CadSpace.Engine.svg)](https://www.nuget.org/packages/CadSpace.Engine) | Sessions, commands, selection, grips, snapping, undoable editing and workspace preferences. |
| [CadSpace.Dxf](https://www.nuget.org/packages/CadSpace.Dxf) | [![NuGet](https://img.shields.io/nuget/vpre/CadSpace.Dxf.svg)](https://www.nuget.org/packages/CadSpace.Dxf) | [![Downloads](https://img.shields.io/nuget/dt/CadSpace.Dxf.svg)](https://www.nuget.org/packages/CadSpace.Dxf) | ASCII/binary DXF reading and writing, native `.cadspace` projects and a checksummed recovery journal. |
| [CadSpace.Rendering](https://www.nuget.org/packages/CadSpace.Rendering) | [![NuGet](https://img.shields.io/nuget/vpre/CadSpace.Rendering.svg)](https://www.nuget.org/packages/CadSpace.Rendering) | [![Downloads](https://img.shields.io/nuget/dt/CadSpace.Rendering.svg)](https://www.nuget.org/packages/CadSpace.Rendering) | Cameras, SkiaSharp 2D drafting and OpenGL geometry, text, pattern and selection passes. |
| [CadSpace.Controls](https://www.nuget.org/packages/CadSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/CadSpace.Controls.svg)](https://www.nuget.org/packages/CadSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/CadSpace.Controls.svg)](https://www.nuget.org/packages/CadSpace.Controls) | Uno workspace: viewport, ribbon, palettes, managers, command line and navigation. |

Dependencies follow the project references: `Model` → `Geometry`; `Engine`, `Dxf` and `Rendering` → `Model` (`Rendering` adds SkiaSharp and Silk.NET.OpenGL); `Controls` → `Engine` + `Rendering`.

### CadSpace.Geometry

The dependency-free math layer: `Vec3`, affine `Transform3`, bounds, rays and planes, object coordinate systems (OCS), 2D/3D intersection helpers, polygon triangulation and a bounding-volume `SpatialIndex` for fast window/ray queries. Use it for any double-precision CAD geometry work; no UI.

```bash
dotnet add package CadSpace.Geometry
```

**Key types** (namespace `CadSpace.Geometry`)

- `Vec3`, `Bounds3`, `Ray3`, `Plane3` – immutable value types with dot/cross, `Include`/`Union`, plane and triangle intersection.
- `Transform3` – `Translation`, `Scaling`, `RotationZ`, `RotationAxis`, `MirrorXY`, composed with `Then`.
- `GeometryMath` – `IntersectLinesXY`, `CircleThrough`, `NearestOnSegment`, `PointInPolygon`, `TryParsePoint`.
- `Coordinates3D` – OCS (`ObjectCoordinateSystem`), `Frame` and `Inverse`.
- `Triangulation` / `SpatialIndex` – polygon triangulation and indexed box/ray queries.

**Usage**

```csharp
using CadSpace.Geometry;

var a = new Vec3(0, 0);
var b = new Vec3(100, 50);
if (GeometryMath.IntersectLinesXY(a, b, new Vec3(0, 50), new Vec3(100, 0), out var hit))
    Console.WriteLine($"Intersection at {hit}");

var transform = Transform3.RotationZ(45, center: new Vec3(50, 25)).Then(Transform3.Translation(new Vec3(10, 0)));
Vec3 moved = transform.Point(b);
var (center, radius) = GeometryMath.CircleThrough(a, b, new Vec3(100, 0));

Vec3[] square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
int[] triangles = Triangulation.Polygon(square);                // index triples

var index = new SpatialIndex([Bounds3.From(square), Bounds3.From([moved, moved + new Vec3(5, 5)])]);
var found = new List<int>();
index.Query(new Bounds3(new Vec3(-1, -1), new Vec3(20, 20)), found, xyOnly: true);
```

### CadSpace.Model

The immutable drawing model: entity records (lines, polylines with bulges/widths, arcs, circles, ellipses, splines, text, dimensions, hatches, meshes, block references), layers, linetypes and blocks in a `Drawing`, an undoable `CadDocument`, scene tessellation and mesh algorithms (primitives, extrude/revolve and closed-mesh Booleans). Depends on `CadSpace.Geometry`; no UI.

```bash
dotnet add package CadSpace.Model
```

**Key types** (namespace `CadSpace.Model`)

- `Drawing` – immutable entities, `Layers`, `Blocks`, `Linetypes`; start from `Drawing.Empty`.
- `CadDocument` – validated, undoable wrapper: `Edit`, `Add`, `Undo`/`Redo`, `IsDirty`, `Changed`.
- `LineEntity`, `CircleEntity`, `PolylineEntity`, `TextEntity`, `HatchEntity`, `MeshEntity`, … – `Entity` records with layer/color/linetype.
- `EntityGeometry.BuildScene(drawing)` – tessellates to a `DrawingScene` of paths, texts and triangles.
- `MeshFactory` / `MeshBoolean` – boxes, cylinders, spheres, extrusion, revolution and union/subtract/intersect.

**Usage**

```csharp
using CadSpace.Geometry;
using CadSpace.Model;

var document = new CadDocument();
document.Edit("Add layer", d => d with { Layers = d.Layers.Add("Walls", new Layer("Walls", 0xFFFFC857)) });
document.Add("Draw walls",
    PolylineEntity.FromPoints([new(0, 0), new(240, 0), new(240, 140), new(0, 140)], closed: true) with { Layer = "Walls" },
    new CircleEntity(new Vec3(120, 70), 30));

var box = MeshFactory.Box(new Vec3(0, 0, 0), new Vec3(50, 50, 50));
var drill = MeshFactory.Cylinder(new Vec3(25, 25, -10), 10, 70);
document.Add("Drill", MeshBoolean.Apply(box, drill, MeshBooleanOperation.Subtract));

DrawingScene scene = EntityGeometry.BuildScene(document.Drawing);
Console.WriteLine($"{document.Drawing.Entities.Length} entities, {scene.Paths.Length} paths, {scene.Triangles.Length} triangles");
document.Undo();
```

### CadSpace.Engine

The headless CAD session: selection (pick, window, crossing), object snaps, grips, transforms, offset/trim/extend/fillet and other edits, block creation, mesh modeling commands and a text `CommandEngine` implementing the command-line workflows. Also holds the `WorkspaceLayout` preference codec and sample drawings. Depends on `CadSpace.Model`; no UI.

```bash
dotnet add package CadSpace.Engine
```

**Key types** (namespace `CadSpace.Engine`)

- `CadSession` – `Document`, `Selection`, `SelectWindow`, `HitTest`, `Snap`, `TransformSelection`, `Offset`, `Extrude`, `Changed`.
- `CommandEngine` – `Submit(text)`, `Point(vec)`, `Preview(cursor)`, `Prompt`, `Message`; `Commands` lists every workflow.
- `CommandCatalog.Suggest(query)` – command completion.
- `GripEditing`, `LineEditing`, `PolylineEditing`, `AdvancedEditing` – reusable entity edits.
- `WorkspaceLayout` / `SampleDrawings` – preference `Encode`/`Decode` and the `StudioPlan`/`ModelStudy` samples.

**Usage**

```csharp
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var session = new CadSession(new CadDocument(SampleDrawings.StudioPlan()));
var commands = new CommandEngine(session);
commands.Message += Console.WriteLine;

foreach (var line in new[] { "RECTANG", "0,0", "240,140", "CIRCLE", "120,70", "30" })
    commands.Submit(line);                                     // same input as the command line

session.SelectWindow(new Vec3(-10, -10), new Vec3(250, 150), crossing: false);
session.TransformSelection("Copy", Transform3.Translation(new Vec3(500, 0)), copy: true);
SnapResult snap = session.Snap(new Vec3(501, 2), tolerance: 5);
Console.WriteLine($"{snap.Kind} at {snap.Point}; {commands.Prompt}");
session.Document.Undo();
```

### CadSpace.Dxf

DXF exchange and persistence: ASCII and binary DXF reading with code-page detection, writers that reuse unmodified source records, the native `.cadspace` project format (geometry plus original DXF provenance) and a checksummed, alternating-slot `RecoveryJournal` over a host-supplied `IRecoveryStorage`. Depends on `CadSpace.Model`; no UI. Review returned warnings for lossy conversions.

```bash
dotnet add package CadSpace.Dxf
```

**Key types** (namespace `CadSpace.Dxf`)

- `DxfBinary` – `Read(bytes)` for ASCII or binary input, `Write(drawing, source, binary)`, `IsBinary`.
- `DxfCodec` – text `Read`/`Write` returning `DxfReadResult`/`DxfWriteResult` with warnings.
- `CadProjectCodec` – native project `Write`/`Read` preserving the `DxfSource`.
- `RecoveryJournal` / `IRecoveryStorage` – `SaveAsync`, `ReadAsync`, `RemoveAsync` checkpoints.

**Usage**

```csharp
using CadSpace.Dxf;

DxfReadResult read = DxfBinary.Read(File.ReadAllBytes("plan.dxf"), "plan.dxf");   // ASCII or binary
foreach (var warning in read.Warnings) Console.WriteLine(warning);

DxfWriteResult ascii = DxfCodec.Write(read.Drawing, read.Source);    // reuses unmodified source records
File.WriteAllText("plan-copy.dxf", ascii.Text);
DxfBytesResult binary = DxfBinary.Write(read.Drawing, read.Source, binary: true);
File.WriteAllBytes("plan-binary.dxf", binary.Bytes);

File.WriteAllText("plan.cadspace", CadProjectCodec.Write(read.Drawing, read.Source));
CadProjectReadResult project = CadProjectCodec.Read(File.ReadAllText("plan.cadspace"));

var journal = new RecoveryJournal(storage);                          // your IRecoveryStorage
await journal.SaveAsync(Guid.NewGuid(), generation: 1, "plan.dxf", project.Drawing, project.DxfSource);
```

### CadSpace.Rendering

Rendering building blocks without a UI framework: 2D and 3D cameras, a SkiaSharp drafting renderer (grid, linetypes, widths, selection, snap/cursor overlays) and OpenGL scene, text, pattern and selection-attribute passes through Silk.NET, plus ViewCube geometry. Depends on `CadSpace.Model`, SkiaSharp and Silk.NET.OpenGL; the OpenGL passes need a current GL/GLES/WebGL context supplied by the host.

```bash
dotnet add package CadSpace.Rendering
```

**Key types** (namespace `CadSpace.Rendering`)

- `Camera2D` – `Fit`, `Pan`, `Zoom`, `WorldToScreen`/`ScreenToWorld`, `VisibleBounds`.
- `Camera3D` – orbit/zoom/pan, orthographic or perspective `Matrix`, `Ray` picking and `Project`.
- `SkiaDraftRenderer` – `Render(canvas, camera, scene, selection)` and `DrawInteraction` overlays.
- `GlSceneRenderer` – `Initialize(gl)`, `Render(gl, scene, camera, …)` with `ModelVisualStyle`, `Destroy(gl)`.
- `ViewCubeGeometry` – named orientations, cube faces and picking.

**Usage**

```csharp
using CadSpace.Geometry;
using CadSpace.Model;
using CadSpace.Rendering;
using SkiaSharp;

var drawing = Drawing.Empty with { Entities = [new LineEntity(new Vec3(0, 0), new Vec3(240, 140)), new CircleEntity(new Vec3(120, 70), 30)] };
DrawingScene scene = EntityGeometry.BuildScene(drawing);

var camera = new Camera2D { Width = 1200, Height = 800 };
camera.Fit(Bounds3.From(scene.Paths.SelectMany(p => p.Points)));

using var surface = SKSurface.Create(new SKImageInfo(1200, 800));
using var renderer = new SkiaDraftRenderer();
renderer.Render(surface.Canvas, camera, scene, new HashSet<Guid>(), grid: true);
using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes("plan.png", png.ToArray());
```

### CadSpace.Controls

The Uno Platform CAD workspace: `CadWorkspace` assembles the application bar, ribbon, classic menus, document/layout tabs, retained Skia/OpenGL `CadViewport` with ViewCube and navigation bar, dockable palettes, Properties and Layer/Linetype managers, command line with dynamic input and the status bar. Each component can also be hosted on its own. Depends on `CadSpace.Engine` and `CadSpace.Rendering`; requires Uno Platform (Skia renderer; 3D needs OpenGL/GLES/WebGL).

```bash
dotnet add package CadSpace.Controls
```

**Key types** (namespace `CadSpace.Controls`)

- `CadWorkspace` – `Bind(session, commands)`, `Invoke(command)`, `FileRequested`, `PreferencesChanged`, `RestorePreferences`.
- `CadViewport` – `Bind`, `Fit()`, `Set3D`, `Camera`/`ModelCamera`, `VisualStyle`, `ClippingPlane`.
- `CadCommandLine` – command history and input bound to a `CommandEngine`.
- `CadRibbon`, `CadDockHost`/`CadDockPane`, `CadPropertySelectors`, `CadLayerManager`, `CadStatusBar`, `CadWorkspaceOptions`.

**Usage**

```csharp
using CadSpace.Controls;
using CadSpace.Engine;
using CadSpace.Model;

var session = new CadSession(new CadDocument(SampleDrawings.StudioPlan()));
var commands = new CommandEngine(session);
var workspace = new CadWorkspace();
workspace.Bind(session, commands);
workspace.FileRequested += action => { /* Host supplies NEW/OPEN/SAVE/export/recovery. */ };
workspace.PreferencesChanged += layout => SaveUiPreferences(layout.Encode());
window.Content = workspace;

// Or host individual components:
var viewport = new CadViewport();
viewport.Bind(session, commands);
var console = new CadCommandLine();
console.Bind(commands);
```

## Performance and verification

Spatial indexes accelerate picking, snapping and whole-root windows. Immutable roots retain unaffected tessellation; selection uses a separate GPU attribute buffer; static drafting and interaction overlays are separate; idle pointer movement does not request a new model render. Bounds are cached by immutable scene identity. Linetype shaders avoid dash-mesh expansion, and Skia clips before generating visible dashes. Unchanged document tabs no longer rebuild their controls.

The reproducible suites compare 100,000-line indexed queries with linear implementations, repeated bounds reads with the former scan, and a one-layer edit among 3,000 circles with a full rebuild. Results must match; the layer edit must rebuild one root and reuse 2,999. CI retains timings and allocations. These are narrow workload measurements, **not whole-application FPS, cold-start or physical-GPU performance guarantees**. Validation, aggregate rebuilding and actual geometry uploads retain drawing-size costs. See [methodology](docs/PERFORMANCE.md).

```sh
# Independent test-only fixture generator; not an application dependency.
python -m pip install ezdxf==1.4.4
python tests/fixtures/generate.py

dotnet run --project tests/CadSpace.Tests -c Release
dotnet run --project tests/CadSpace.Exchange.Tests -c Release
dotnet run --project tests/CadSpace.Persistence.Tests -c Release
dotnet run --project tests/CadSpace.Advanced.Tests -c Release
dotnet run --project tests/CadSpace.Performance.Tests -c Release
```

The five suites contain **334 headless regressions**. CI independently audits tested ASCII/binary geometry, styles and widths with zero errors/repairs required; builds Windows/macOS/Linux; packages all libraries; publishes trimmed WebAssembly; and runs real rendered-browser interaction checks. Screenshots, console logs and native checkpoints are retained as artifacts. Current-main builds deploy to GitHub Pages and verify the served commit. Release runs for `v*` tags or a supplied manual version: it repeats the release tests, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), packs all six libraries with symbols, archives browser/source distributions and emits `SHA256SUMS.txt`. Tags attach the assets to a GitHub Release and publish the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment; manual runs are dry runs that only upload workflow artifacts. Signing and notarization are not automatic.

Software-backed Chromium is not physical-GPU, accessibility or Autodesk interoperability qualification. Uno's host still uses framebuffer readback through a pinned RGBA adapter, **not zero-copy WebGPU/Vulkan**. Analytic solids, full typography/dynamic blocks/constraints, paper-space viewports/plotting and complete UI/API parity remain substantial work. Read the [coverage matrix](docs/FEATURES.md).

MIT licensed. See [Contributing](CONTRIBUTING.md), [Security](SECURITY.md) and [Third-party notices](THIRD-PARTY-NOTICES.md). CadSpace is independent of Autodesk and contains no Autodesk source, icons, fonts, ACIS or RealDWG components.
