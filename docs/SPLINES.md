# Rational spline editing and DXF retention

`SPLINEDIT` (`SPE`) opens the spline editor for one preselected or picked spline. Properties, the Modify ribbon, the contextual Spline ribbon, classic Modify menu, command search, tool catalogs and 2D double-click route through the same command/session workflow. Existing SPLINE creation and control-point grips remain available.

## Indexed control editing

The reusable `CadSplineEditor` shows one indexed set of fields, not one visual control per point. Previous / Read point / Next navigate the control polygon. Apply control point changes local XYZ and a positive rational weight together. Changing the index without reading it first is rejected, preventing accidental application of another point's fields. An unweighted curve materializes unit weights only when a weight actually changes.

Each successful Apply or Insert action commits one Undo transaction. Close only dismisses the editor; it does not revert earlier actions. No-op edits retain the original immutable snapshot and redo state. Stale object/selection captures, locked layers and a changed active document reject the edit. An existing placed wrapper and root/leaf identifiers are retained. Coordinates are local to that wrapper, not necessarily world coordinates.

## Knot insertion

Insert knot accepts a finite parameter strictly inside the active domain, from `Knots[Degree]` to `Knots[ControlPoints.Length]`. It adds one knot and one control point using rational, homogeneous interpolation. This refines the control representation without intentionally changing the curve, up to floating-point rounding; moving a point or changing its weight does change the curve.

Degrees 1–16 and up to 100,000 resulting control points are supported. Interior multiplicity cannot exceed the degree. Invalid domains, unsupported numeric intervals, endpoint insertion and over-budget operations reject before committing. The implementation locates the span by binary search, copies the immutable arrays once and blends only the affected controls. Adjacent weights are normalized during interpolation to avoid unnecessary weighted-position overflow. This does not qualify every extreme-coordinate operation in the rest of the spline evaluator or renderer.

Periodic splines are retained and shown read-only in this editor: coordinated seam editing is not implemented. Independently moving a closed curve's endpoint is rejected rather than silently opening the seam. Fit-point reconstruction/editing, knot removal, degree elevation, every AutoCAD SPLINEDIT option and a NURBS surface kernel remain outside this increment.

```csharp
using CadSpace.Engine;

// capturedRoot belongs to session's current editable selection.
session.EditSpline(capturedRoot, spline => SplineEditing.InsertKnot(spline, parameter));

// The pure helpers are also usable without an application/UI.
var refined = SplineEditing.InsertKnot(spline, parameter);
var changed = SplineEditing.SetControlPoint(refined, controlIndex, localPoint, weight);
```

## Native DXF preservation

Supported source-backed control-only SPLINE edits update native degree/counts, knots, rational weights and WCS control-point groups. Original application groups, XDATA, tolerances and other unedited fields stay in the record. The record writer changes only the geometric subclass, never similarly numbered values inside application data. Native project save/reopen retains the source baseline needed for this path; no additional native format version is introduced.

The preservation path rejects sources containing fit points or tangent constraints, because retaining those after changing the control structure could leave contradictory geometry. A retained planar flag/normal is accepted only while the changed controls remain in that plane within the documented numeric checks. Unsupported or uncheckable cases use the existing canonical writer and its metadata-loss warning rather than claiming lossless editing. General placed spline conversions retain the existing export behavior. Export without a `DxfSource` writes canonical native SPLINE geometry, not the original private metadata.

Retaining application data does not regenerate external associations, fit constraints, fields or private caches. Keep the original DXF and review export warnings. This increment is not universal lossless DXF editing, complete AutoCAD parity, or Autodesk open/AUDIT/save/reopen qualification.

## Verification

The spline suite contains 38 checks, bringing the complete headless total to 541. It exercises rational/nonrational degrees 1/2/3/5/8/16, repeated knots, a rational circular arc, nonuniform/unclamped knots, near-endpoint insertion, numeric safeguards, closed/periodic rejection, immutable/undo/stale/locked behavior, native persistence and source data retention.

Independent ezdxf-generated fixtures retain application data, XDATA and tolerances. Four ASCII/binary exports require zero audit errors and zero repairs. Refined curves are compared at 501 parameter values with ezdxf's evaluator, not just CadSpace's own round-trip reader. The complete set now audits 30 exports. A 20,000-control-point insertion records an observational timing with exact point/knot counts; this is not an application frame-rate or hardware performance guarantee.

The browser spline script exercises normal contextual/Properties controls, actual point/weight/knot edits, two Undo operations, the file chooser, source-preserving ASCII/binary downloads, binary reimport and 3D display. Strictly newer native checkpoint generations observe the edits; no drawing-mutation test hook is used. Browser/desktop CI results must be checked for the specific commit under test.

```sh
python -m pip install ezdxf==1.4.4
python tests/fixtures/splines.py
CADSPACE_SPLINE_OUTPUT=artifacts/spline-audit dotnet run --project tests/CadSpace.Spline.Tests -c Release
python tests/fixtures/splines.py artifacts/spline-audit
```

Primary format and mathematical references:
- Autodesk SPLINE DXF group codes: https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm
- Michigan Technological University, rational knot insertion: https://pages.mtu.edu/~shene/COURSES/cs3621/NOTES/surface/NURBS-knot-insert.html
