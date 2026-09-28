# CadSpace

### A modular CAD workspace for desktop and the browser

[![Build, test and deploy](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

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

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `CadSpace.Geometry` | Double vectors/transforms, OCS, rays/planes, intersections, triangulation, spatial index |
| `CadSpace.Model` | Immutable geometry/styles/blocks, scene cache, spline/hatch and mesh algorithms |
| `CadSpace.Engine` | Sessions, commands, selection/grips/snapping, undoable editing, workspace preferences |
| `CadSpace.Dxf` | DXF transport/interpreters/writers, native projects, recovery journal |
| `CadSpace.Rendering` | Cameras, Skia drafting, OpenGL geometry/text/pattern/selection passes |
| `CadSpace.Controls` | Public Uno workspace components, managers, palette host, ribbon and navigation |

All six are independently packable; Controls is one package, not one package per widget. Geometry/model/editing/exchange do not require Uno or a graphics context. App owns file dialogs and document lifetime.

```csharp
var session = new CadSpace.Engine.CadSession();
var commands = new CadSpace.Engine.CommandEngine(session);
var workspace = new CadSpace.Controls.CadWorkspace();
workspace.Bind(session, commands);
// Connect workspace.FileRequested and PreferencesChanged to your host.
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

The five suites contain **334 headless regressions**. CI independently audits tested ASCII/binary geometry, styles and widths with zero errors/repairs required; builds Windows/macOS/Linux; packages all libraries; publishes trimmed WebAssembly; and runs real rendered-browser interaction checks. Screenshots, console logs and native checkpoints are retained as artifacts. Current-main builds deploy to GitHub Pages and verify the served commit. Tags run release tests and package desktop/browser/source distributions with checksums. NuGet.org publication, signing and notarization are not automatic.

Software-backed Chromium is not physical-GPU, accessibility or Autodesk interoperability qualification. Uno's host still uses framebuffer readback through a pinned RGBA adapter, **not zero-copy WebGPU/Vulkan**. Analytic solids, full typography/dynamic blocks/constraints, paper-space viewports/plotting and complete UI/API parity remain substantial work. Read the [coverage matrix](docs/FEATURES.md).

MIT licensed. See [Contributing](CONTRIBUTING.md), [Security](SECURITY.md) and [Third-party notices](THIRD-PARTY-NOTICES.md). CadSpace is independent of Autodesk and contains no Autodesk source, icons, fonts, ACIS or RealDWG components.
