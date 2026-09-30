# Analytic editing and DXF record retention

This increment extends existing commands and public engine APIs. It does not establish complete AutoCAD command, UI or file-format parity.

## Join curves

Select two or more connected lines, arcs or open 2D polylines, then use `JOIN` (`J`) or `PEDIT` → `Join`. Arcs retain their analytic bulge rather than being sampled into short lines. Input directions can differ; reversed segments swap taper endpoints and bulge direction. Global widths are materialized as segment widths so differently sized input curves retain their geometry.

The first selected root in **drawing order** supplies identity, handle and common properties. The result replaces the selected inputs in one undo transaction. Inputs must form one nonbranching chain or ring, share a layout and the exact same combined OCS/affine placement, and be planar in that local frame. Disconnected, ambiguous, branched, already-closed or unsupported input curves are rejected atomically. Endpoint welding uses a default local-coordinate tolerance of 1e-7, not arbitrary curve extension/fitting. Two semicircles can form a two-bulge closed polyline.

`PolylineTools.Join` exposes a configurable tolerance in (0,1]. Work is bounded to 10,000 roots and 100,000 segments. Endpoints are indexed in a rebased spatial hash with adjacent-cell checks; the normalized coordinate range is limited to 4e15. This avoids all-pairs endpoint searches, but geometric reconstruction, validation and the document edit still have size-dependent costs. The test suite reports a 10,000-segment count-checked observation, not a portable timing guarantee.

## Vertex and segment editor

Select one 2D polyline and use `PEDIT` → `Edit`. The same reusable `CadPolylineEditor` is used by Properties and the dedicated dialog. The dialog supports placed OCS polylines; coordinates are in the polyline's local frame. The editor has one indexed set of fields rather than a visual row for every vertex.

Previous/Read/Next navigate vertices. Apply updates local coordinates, start/end widths and bulge. Inserting an outgoing segment midpoint splits the original analytic arc and linearly interpolates the taper; the result is not an approximation of the arc. Straighten clears that segment's bulge. Delete explicitly reconnects the neighboring vertices with a straight segment and retains the boundary widths. It does **not** invent a replacement arc or preserve the deleted corner's shape.

Each successful action is one undoable edit. Close dismisses the dialog without reverting prior actions. Invalid, locked or stale captured objects are rejected. Applying local fields materializes an existing global width into explicit widths so unrelated segments are not erased. Open polylines retain at least two vertices; closed polylines retain at least three after deletion. Two-vertex curved rings can be split but cannot have a vertex removed. The last open vertex has no outgoing segment to split/straighten.

Fit/Spline/Decurve, interactive subobject transforms, constraint solving and every AutoCAD PEDIT option remain outside this implementation.

## Drafting and property matching

`POLYGON` (`POL`) accepts 3–1024 sides, a center, Inscribed/Circumscribed and a positive radius or radius point. The radius is a vertex radius for Inscribed and an apothem for Circumscribed. Orientation is fixed in local XY; there is no edge construction option.

`DONUT` (`DO`) accepts inner and outer diameters and repeated center placements. Zero inner diameter makes a filled disc. Each placement is a native closed two-bulge wide polyline and a separate undo step; Enter finishes. Native width rendering remains sampled at display time with the existing join/pattern limits.

`MATCHPROP` (`MA`) uses **preselected destinations, then a picked source**, not every AutoCAD selection option. It copies layer, color, linetype, linetype scale and lineweight, never geometry, identity, handle, layout or visibility. The public `PropertyMatching.MatchProperties` API accepts a field mask. Destinations must be modeled, editable objects in the active layout; invalid or locked inputs reject the complete transaction. A drawing change while choosing the source cancels the stale workflow.

```csharp
var joined = CadSpace.Engine.PolylineTools.Join(selectedRoots);
var split = CadSpace.Engine.PolylineTools.SplitSegment(polyline, segmentIndex, 0.5);
// Extension methods require: using CadSpace.Engine;
session.EditVertex(capturedRoot, p => PolylineTools.SplitSegment(p, 0));
session.MatchProperties(sourceId, destinationIds, PropertyMatchFields.Color);
```

The host supplies the referenced objects/IDs and keeps immutable capture semantics. Ribbon alternatives, classic menus, command search and Tool Palettes use the same commands and previews.

## DXF preservation boundaries

Import interprets geometric/common fields outside balanced 102 application-data groups and outside XDATA. The original record remains untouched in provenance. Unsupported entities retain the full raw record, including application and extended data, even when exported without their original source object.

For source-backed typed roots with unchanged identity/handle, property-only edits patch the root common fields and retain remaining source fields. Compound INSERT/ATTRIB sequences are preserved for these common-property changes; attribute contents and geometry are not regenerated. Supported single-record geometric patches cover LINE, POINT, CIRCLE, ARC, plain TEXT and same-count LWPOLYLINE edits, including unchanged OCS placements. Same-count unambiguous polyline edits retain group-91 vertex identifiers. Reordering existing points when IDs are present falls back instead of guessing the identifier mapping. Topology changes, changed placements and unsupported entity changes retain the existing conversion/loss-warning path.

Retaining data does **not** make external associations, field caches, XDATA coordinate semantics or third-party application payloads current after a geometric edit. The export report explicitly warns that they are not regenerated. Keep source files and review those reports. The supported retention path is not universal lossless DXF editing, DWG/ACIS decoding or Autodesk open/AUDIT/save/reopen qualification.

Regenerated headers place `$ACADVER` first. This avoids a demonstrated external binary-reader failure on long source headers: late version discovery can otherwise select the wrong group-code width. Unchanged byte pass-through remains unchanged.

## Verification

The executable editing suite covers analytic joins, width reversal, curve splitting, numeric/graph rejection, atomic/stale edits, command dispatch, APPDATA/XDATA isolation, opaque payload retention, vertex IDs and ASCII/binary round trips. Independent ezdxf-generated input has application data, extended point coordinates, vertex identifiers and an attributed block. Four additional independently audited exports require zero errors and zero repairs. Browser tests use real control clicks and monotonically newer native recovery generations for midpoint insertion/Undo, menu-driven Polygon/Donut and a real MATCHPROP style change. These checks are not physical-GPU or production interoperability qualification.

## Format references

- Autodesk LWPOLYLINE group codes: https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm
- Autodesk binary DXF encoding: https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
- Autodesk application-defined groups: https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-6939D69E-04CB-4F4C-87B2-67BC540FCF58.htm
