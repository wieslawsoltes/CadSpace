# Native dimensions

CadSpace models all seven standard DIMENSION subtypes: rotated/linear, aligned, two-line angular, diameter, radius, three-point angular and ordinate. This is a bounded dimension implementation, not all DXF or AutoCAD dimension functionality.

## Commands and workspace

`DIMALIGNED` retains its existing three-point workflow. The following commands share pointer input, command-line coordinates, live previews, document validation and Undo:

| Command | Inputs |
| --- | --- |
| DIMLINEAR / DLI | Two extension origins, dimension-line location. H/V select a horizontal/vertical axis. |
| DIMROTATED / DROT | Axis angle in degrees, two extension origins, dimension-line location. |
| DIMANGULAR / DAN | Angle vertex, two ray points, arc location. The arc location selects the sector, including a reflex sector. |
| DIMANGULAR2 / DA2 | Four endpoints defining two nonparallel lines, then arc location. |
| DIMRADIUS / DRA | Pick a circle/arc or preselect one, then place text/leader. |
| DIMDIAMETER / DDI | Pick a circle/arc or preselect one, then place text/leader. |
| DIMORDINATE / DOR | X/Y, explicit datum origin, feature location, leader endpoint. Measurements use the absolute axis distance. |
| DIMEDIT / DED | Select/pick one modeled dimension and open staged Dimension Properties. |

The Annotate ribbon, Dimension contextual ribbon, classic Dimension menu, command search, Tool Palettes, Properties action and 2D double-click route to the same engine. Numeric prompts ignore accidental point clicks. Cancel creates no drawing object. Radius/diameter tools reject a source circle that changed while placing the dimension.

The reusable `CadDimensionEditor` stages the label/template, decimal precision, text and arrow sizes, gaps, extension offsets, overall scale, measurement factor and local dimension-line/leader position. Apply validates and commits one Undo step; Cancel discards the staged fields. Values are displayed with round-trip precision so applying unchanged fields does not move a dimension. Locked/stale captures are rejected. Coordinates are local to a placed dimension, not necessarily world coordinates.

## Geometry, rendering and reuse

`DimensionGeometry` lives in Model, and `DimensionEditing` in Engine. Geometry feeds the same scene used by Skia drafting, GPU text/faces, selection, snapping and grips. Angular dimensions expose their additional definition-point grips. Native-project edits, grips and supported STRETCH operations invalidate an imported picture when its immutable definition changes.

Layouts and definition signatures are weakly cached by immutable entity identity and safely published across threads. Record copies cannot inherit stale layout results. The scene cache tracks dependencies on retained picture blocks. Repeated exports share equal DIMSTYLE definitions; monotonically increasing name counters avoid rescanning all previous picture names. Regeneration is bounded to 20,000 pictures per export. Existing scene/vertex/triangle budgets also apply.

## DXF and native persistence

Import reads native definition points using their documented coordinate systems: groups 10/13/14/15 are WCS, and 11/12/16 are OCS. Supported decimal DIMSTYLE fields and ACAD:DSTYLE overrides are interpreted without confusing unrelated APPDATA or XDATA with geometry. Original source records and graphics remain available.

An imported anonymous picture is displayed while its definition signature matches. Changed definitions regenerate supported geometry. Fresh or regenerated exports write actual DIMENSION entities with subtype subclasses, OCS normal, definition points, DIMSTYLE references, unique anonymous BLOCK/BLOCK_RECORD pictures, ownership links, text and SOLID arrowheads. Dimensions inside block definitions and paper layouts use the same export path. Unchanged records and common-property-only changes retain their original source data. Regenerated imported dimensions report that private styles, fields and associations are not rebuilt.

Dimension-bearing native projects use format **version 3**, because older readers must not silently interpret all new subtypes as basic aligned dimensions. Non-dimension projects remain version 2. Readers still accept version 1/2 projects. Older saved dimension display wrappers and simple aligned definitions are checked against their complete earlier interpretation of the original DXF before provenance reuse; forged picture names or geometry are rejected.

## Verification

The dimension suite adds 64 headless cases, bringing the repository to 503. Eight additional independent ASCII/binary audits cover new, imported/edited, tilted and nested dimensions; the full audit set contains 26 exports with zero errors and zero repairs required. Tests cover measurements, all subtypes, OCS, templates, invalid inputs, cache publication/dependencies, transactions, grips, native provenance and old-project compatibility. A 1,000-dimension benchmark verifies unique picture names and shared styles without a timing gate.

The ezdxf 1.4.4 fixture generator explicitly corrects two generator conventions: its aligned helper emits a rotated type, and its tilted two-line angular helper emits group 16 in WCS. Fixtures set the actual aligned subtype and convert group 16 to the OCS required by Autodesk. This correction is in the independent generator, not a relaxation of the application parser.

The browser regression uses real controls, file choosers, downloads and strictly newer recovery generations. It creates all subtypes, edits via Properties, checks Apply/Cancel/Undo, exports/reopens ASCII and binary DXF, and exercises 3D display. CI also builds Windows/macOS/Linux and the trimmed browser. Local headless results alone do not qualify those UI paths.

## Remaining boundaries

No associative constraint solver, DIMASSOC relationship repair, field evaluation, DIMSTYLE manager, arbitrary style-variable parity, fit rules, tolerances/alternate units, annotative scaling, jogged radius, arc-length dimension, baseline/continue chains or complete AutoCAD command-option parity is implied. Decimal formatting and approximate label centering are supported; font-dependent fitting, collision avoidance and rich dimension MTEXT are not exact. Two-line angular placement is sector-based, and ordinate input uses an explicit datum rather than a complete UCS authoring system.

Native regeneration requires a planar similarity. General affine placements can remain in native projects, but unsupported shear/nonuniform scaling is rejected by DXF export. Original graphics can still represent unsupported dimension semantics; such records are retained as display data, not offered as fully editable definitions. Unrelated major DXF boundaries (ACIS/B-rep, dynamic blocks, MLEADER/TABLE semantics, plotting and paper-space model viewports) remain.

Independent audits are not Autodesk open/AUDIT/save/reopen, industrial-corpus, native GPU/driver or manufacturing qualification.

## Primary references

- Autodesk, Common Dimension Group Codes: https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-EDD54EAC-A339-4EBA-AEA6-EC8066505E2B.htm
- Autodesk, Angular Dimension Group Codes: https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-09821B78-9F8E-43BA-82F2-8C931485EDC9.htm
- Autodesk, DIMSTYLE: https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm
- Autodesk, AddDimOrdinate: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-EC499091-4A07-4B31-9B85-6A35A6009E3E.htm
