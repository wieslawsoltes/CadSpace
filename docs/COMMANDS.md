# Command reference

Enter one command or prompted value at a time. Coordinates use invariant decimal syntax: `10,20`, `10,20,30`, `@5,-2`, or `@50<30`. Enter finishes a line/polyline sequence; Escape cancels. Select objects before starting a modification command. Extra pointer clicks at a numeric-only prompt are ignored instead of corrupting command state.

| Command | Alias | Inputs / behavior |
| --- | --- | --- |
| LINE | L | Start point; following points; Enter finishes. Each segment is an undo transaction. |
| PLINE | PL | Points; Enter finishes open; C closes after at least three points. |
| RECTANG | REC | Two opposite corners in the XY plane. |
| CIRCLE | C | Center; numeric radius or a point on the circle. |
| ARC | A | Three points on an XY arc; collinear points are rejected. |
| ELLIPSE | EL | Center; XY major-axis endpoint; minor radius no larger than the major radius. |
| POINT | PO | One coordinate. |
| TEXT | T | Insertion point; text. Initial text height is 12 units; edit it in Properties. |
| DIMALIGNED | DAL | Two extension points; dimension-line location. |
| HATCH | H | Select closed polylines first. Adds a basic 45-degree hatch with 10-unit spacing. |
| MOVE | M | Selected objects; base point; destination point. |
| COPY | CO | Selected objects; base point; destination point. |
| ROTATE | RO | Selected objects; base point; numeric angle in degrees. |
| SCALE | SC | Selected objects; base point; positive uniform scale factor. |
| MIRROR | MI | Selected objects; two points defining the XY mirror axis. Replaces originals. |
| OFFSET | O | Selected lines/circles/arcs; signed distance. Positive is line-left or circle/arc-outward. |
| TRIM | TR | Selected line boundaries; point on an unselected line portion to remove. |
| EXTEND | EX | Selected line boundaries; point near the unselected line end to extend. |
| FILLET | F | Exactly two selected coplanar XY lines; radius. |
| CHAMFER | CHA | Exactly two selected coplanar XY lines; equal distance. |
| JOIN | J | Selected connected lines/arcs/open polylines in a shared plane; analytic bulges and widths retained. |
| BREAK | BR | One selected line; two projected break points. |
| ERASE | E | Deletes selected editable objects. Locked/opaque objects prevent the transaction. |
| EXPLODE | X | Selected supported blocks or polylines, including bulged segments. |
| ARRAY | AR | Selected objects; `columns,rows,x-spacing,y-spacing`. At most 10,000 instances and 100,000 resulting objects. |
| BLOCK | B | Selected objects; unique block name; base point. Replaces selection with an insert. |
| INSERT | I | Existing block name; insertion point. Edit scale/rotation in Properties. |
| BOX | BOX | Two base corners; numeric height. Creates a triangle mesh. |
| CYLINDER | CYL | Center; radius or radius point; numeric height. |
| SPHERE | SPH | Center; radius or radius point. |
| CONE | CONE | Center; radius or radius point; positive height. |
| EXTRUDE | EXT | Selected closed XY polylines/circles; height. Retains original profiles. |
| REVOLVE | REV | Selected polylines; axis start; axis end; angle up to 360 degrees. Creates a sampled surface, not a capped solid. |
| DIST | DI | Two points; reports distance and coordinate delta. |
| AREA | AA | Reports area of selected closed polylines using their tessellated boundary. |
| UNDO | U | Undo the most recent document transaction. |
| REDO | REDO | Redo the most recently undone transaction. |
| SELECTALL | ALL | Select visible entities of the active layout. |
| ZOOM | Z | Zoom extents; no additional ZOOM command options yet. |
| TOP | TOP | Switch to the top drafting viewport. |
| 3DORBIT | 3DO | Switch to the 3D viewport; click selects, drag orbits. |
| HELP | ? | Print implemented command names and aliases. |

See [line-editing details and boundaries](LINE-EDITING.md). Modification errors are reported in command history and do not partially update the drawing. Many standard AutoCAD options/subcommands are not implemented; command-name familiarity does not imply full option parity.

## Additional 3D and view workflows

| Command | Alias | Inputs and boundary |
| --- | --- | --- |
| 3DPOLY | 3P | WCS points; Enter finishes, C closes. |
| SPLINE | SPL | Control points; Enter creates a clamped spline of degree up to 3. Not fit-point interpolation. |
| ROTATE3D | 3R | Selected objects; two axis points and angle in degrees. |
| MIRROR3D | 3M | Selected objects; three noncollinear mirror-plane points. |
| ALIGN3D | 3A | Three source frame points followed by three target frame points; rigid, no scaling. |
| LOFT | LOFT | Selected closed planar profiles with matching sampled vertex counts, in drawing order; capped polygon mesh. |
| SWEEP | SW | One closed profile and one open polyline path; bounded parallel-transport mesh. |
| UNION | UNI | Union selected closed triangle meshes. |
| SUBTRACT | SU | Subtract others from the first selected mesh in drawing order. |
| INTERSECT | IN | Intersection of selected closed triangle meshes. |
| VSCURRENT | VS | Wireframe / HiddenLine / Shaded / ShadedEdges. |
| PERSPECTIVE | PERSPECTIVE | 1 perspective, 0 orthographic. |
| CLIP3D | CLIP3D | x,y,z,nx,ny,nz or OFF; retains normal·(point-origin) <= 0, uncapped display only. |

The complete registry has **83 commands**. EXPLODE handles analytic bulged-polyline segments as arcs. SelectAll is limited to visible entities of the active layout. Mesh tools do not imply ACIS/B-rep or every AutoCAD option.

Tab accepts completion in an idle command box; F2 expands history, F12 toggles dynamic input, and Ctrl+1 toggles Properties. Snap options expose per-mode choices including line intersections/perpendiculars and circle/arc tangents. Browser shortcuts may take precedence.

## Selection and grip workflows

| Command | Alias | Inputs / behavior |
| --- | --- | --- |
| STRETCH | S | First crossing-window corner; opposite corner; base point; displacement point. Fully enclosed roots translate. Partial LINE/LWPOLYLINE/3D POLYLINE/SPLINE/DIMENSION vertices or supported insertion points inside the box move. |
| QSELECT | QS | `kind,layer[,mode[,scope]]`, for example `LINE,*,Replace,All`. `*` matches any whole field, not a general wildcard expression. Mode is Replace/Add/Remove/Toggle. Scope is All (visible active-layout objects) or Selection. The ribbon instead opens a type/layer dialog. |
| SELECTSIMILAR | SE | Select visible active-layout objects matching the selected roots' kind and layer. No other property-match options yet. |
| RENDERSTATS | RS | Report actual scene, overlay and model RenderOverride counters. These are recording/render counts, not presented frames or GPU timings. |

STRETCH uses an explicit crossing box regardless of drag direction. It does not support fence/lasso/multiple-box selection, arbitrary UCS, arc deformation, NURBS surface deformation, constraint propagation or every AutoCAD option. Partial objects without an eligible grip in the box are ignored; unsupported partial deformation with an included grip is rejected. Locked candidates reject the transaction. A partial XY polyline remains planar; bulges are retained, not constraint-solved. Invalid or unsupported multi-object edits leave the document unchanged.

In the 2D viewport, normal clicks/windows add, Shift removes and Ctrl toggles. Clicking empty space without a modifier clears selection. SC enables an overlap menu capped at 25 roots; Ctrl+W is an alternative where the browser does not reserve it. Text uses conservative envelopes. The 3D click-selection behavior remains separate.

Visible blue grips edit line endpoints/midpoints, circle center/radius, polyline vertices, spline control points, dimension extension/location points, point/text/block insertions and placed equivalents. Arc/ellipse grips currently move only the center. Meshes, composite imported inserts and opaque geometry do not have editable subobject grips. Grip drag previews are transient; release creates one undo step, Escape and capture loss cancel. Locked objects expose no grips. More than 200 selected roots or 4,096 handles hides grips; commands remain available.

## Layers and linetypes

| Command | Alias | Inputs / behavior |
| --- | --- | --- |
| LAYER | LA | Opens the Layer Properties Manager; no scripted subcommands yet. Search/create/rename, current layer, RGB color, visibility, locking, lineweight, loaded linetype and unused-layer deletion. |
| LINETYPE | LT | Opens the Linetype Manager. Load built-ins, create/update simple patterns, current/selected assignments and scales. |
| CELTYPE | CELTYPE | BYLAYER, BYBLOCK or a loaded/built-in pattern name. Built-ins load by name if absent. Applies to new objects. |
| CELTSCALE | CELTSCALE | Positive scale no larger than 1e9 for new objects. This transient session setting is not an undoable drawing change. |
| LTSCALE | LTSCALE | Positive global drawing scale no larger than 1e9. Undoable and persisted in DXF/native projects. |

The managers do not switch an existing 3D view to drafting. Drawing changes use the shared engine transaction API. Renaming a layer updates modeled nested references, but refuses opaque source references; Layer 0 cannot be renamed/deleted. Used and current layers cannot be deleted. Undo/load that removes a current layer/type resets the transient setting to 0/BYLAYER.

Simple patterns use positive dash lengths, negative gaps and zero dots in drawing units. Example: `12,-4,0,-4` is dash-gap-dot-gap. Up to 64 finite elements are supported; the sum of absolute lengths must be between 1e-9 and 1e12. Resolved render periods are bounded separately. Dense subpixel patterns intentionally appear continuous.

Properties type-only selection changes preserve mixed per-object scales. The Linetype Manager's explicit **Apply to selection** sets both type and the entered scale. Patterns on splines continue through tessellation; supported 2D/3D polyline generation flags choose continuous phase or per-segment restart. DXF groups and native projects retain these flags.

Complex SHX/text/shape linetypes are retained as source-backed definitions with warnings and continuous display fallback. They are not editable simple patterns, and export without their original provenance is rejected. Exact curve arclength, endpoint fitting, affine/insert-scale conventions and all paper-space plotting scales are not fully qualified.


## Native polyline widths

| Command | Alias | Inputs / behavior |
| --- | --- | --- |
| PLINEWID | PLINEWID | Nonnegative default width for newly created PLINE/RECTANG geometry, in drawing units. Zero creates centerlines. This transient setting does not edit existing objects. |
| PEDIT | PE | Preselect 2D polylines, including OCS/affine placements. Choose Width (W) and a nonnegative value, Open (O), Close (C), or Reverse (R). Each operation is one undoable transaction. |

Widths are geometry, not lineweight. Global width and outgoing segment start/end widths are editable in Properties; the segment editor uses one vertex index rather than creating controls for an entire large vertex array. Uniform transformations scale widths; reflection and reversal preserve the outgoing segment semantics. Wide EXPLODE is rejected rather than silently discarding width. Set width to zero explicitly before exploding.

The current PEDIT workflow is preselection-based and does not include every AutoCAD subcommand. Wide strip rendering uses sampled curves and bevel joins; fills currently remain continuous despite assigned linetypes. See [native width support and rendering limits](WIDE-POLYLINES.md).

## Workspace commands

PROPERTIES / PROPERTIESCLOSE show/hide Properties; TOOLPALETTES (TP) / TOOLPALETTESCLOSE show/hide tools; RIBBON / RIBBONCLOSE expand/minimize the ribbon; CLEANSCREENON / CLEANSCREENOFF hide/restore workspace chrome; OPTIONS (OP) opens workspace settings. UISTATS reports read-only control bounds and view state. See [Workspace controls](WORKSPACE.md).

## Classic menus and workspace preferences

`MENUBAR` accepts `1` to display the classic menu bar and `0` to hide it; it does not edit the drawing or create an undo state. `OPTIONS` (`OP`) opens staged Display, Workspace and Status Bar tabs. Apply validates settings, Cancel discards them, and Reset restores the default workspace. Hiding a status control does not disable its drafting mode. Display-only options preserve the camera and model-view mode.

Hold Ctrl to keep a dragged palette floating near an edge. Escape cancels palette movement, palette resizing or command-window resizing. Command height, navigation visibility and status customization are persisted with other UI-only workspace preferences. See [Workspace controls](WORKSPACE.md) for supported controls, reuse and remaining boundaries.


## Additional editing workflows

`POLYGON` (`POL`): 3–1024 sides, center, Inscribed/Circumscribed, radius. `DONUT` (`DO`): inside/outside diameters followed by repeated center points; Enter finishes. `MATCHPROP` (`MA`): preselect destinations, then pick a source. `PEDIT Join` uses the same analytic JOIN implementation; `PEDIT Edit` opens the indexed vertex editor. `DDEDIT` (`ED`) edits one selected/picked text or supported attributed block; `EATTEDIT` (`ATE`) opens attribute values. Apply is one undo step; Cancel preserves the drawing. See [editing documentation](EDITING.md) for input contracts, source-retention rules and non-parity boundaries.

`MTEXT` (`MT`) creates multiline text at an insertion point; `\P` inserts a paragraph. DDEDIT also stages local height/rotation edits. See [multiline text](MTEXT.md).
