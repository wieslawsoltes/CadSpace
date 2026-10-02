# Hatch authoring, gradients and retained attributes

## Hatch workflows

`HATCH` (`H`) creates a patterned hatch from selected coplanar closed centerline polylines or circles. Multiple boundaries become one hatch with island loops. `GRADIENT` (`GD`) creates a gradient from the same selection. Native polyline bulges remain analytic; a circle is represented by two semicircular bulges rather than many stored line segments. Boundaries must share the same local placement and layout. Open curves, wide outlines and mismatched planes are rejected atomically.

`HATCHEDIT` (`HE`) opens the staged **Hatch and Gradient** editor for one selected/picked hatch. It is also available through Properties, the contextual Hatch tab, Annotate ribbon, classic Modify menu and 2D double-click. The control can retain an existing custom pattern or explicitly replace it with Solid, a single line family, Crosshatch or Gradient. Island modes, replacement pattern spacing/angle, gradient profile, two RGB colors and shift are editable. Apply is one Undo transaction; Cancel preserves the original. Locked and stale captured objects reject edits.

`HATCHGENERATEBOUNDARY` (`HGB`) extracts selected hatch loops as closed polylines in one transaction, retaining modeled bulges and placements. Source edge-list curves that were sampled on import remain sampled in extracted/editable boundaries. Boundary selection is not automatic region finding or an associative constraint system.

## Gradient exchange and display

Native HATCH gradient metadata includes LINEAR, CYLINDER, INVCYLINDER, SPHERICAL, INVSPHERICAL, HEMISPHERICAL, INVHEMISPHERICAL, CURVED and INVCURVED; two RGB stops; angle; shift; and retained single-color/tint fields. The model uses degrees and the DXF codec converts the gradient angle to/from radians. Invalid/incomplete gradient representations are not silently imported as solid fills.

Gradient-bearing native projects use version 4 or a later version required by other entities. Native gradient DXF export remains a `HATCH`, not a colored triangle-mesh replacement. Uniform planar/OCS similarity transforms preserve supported gradient orientation; nonuniformly deformed gradient export is rejected instead of silently changing its color field. Native projects retain general placements.

Display uses bounded, weakly identity-cached color tessellation, shared 2D/3D scene colors, retained Skia triangle batches and unlit GPU vertex colors. The tessellation budget is 32,768 triangles per gradient. Linear, unshifted interpolation and hole preservation are tested through rendered pixels. **Nonlinear and shifted profiles are approximations**, not pixel-exact Autodesk gradient rendering. Fill assumes simple nonintersecting loops and retains existing numeric/work budgets. These optimizations are not a measured whole-application frame-rate claim.

## Retained block-attribute sequences

Supported `INSERT/ATTRIB/SEQEND` compounds retain their native records through attribute-value edits and native save/reopen, including invisible-only attributes. Validated compounds can now export those records **without the original whole-file `DxfSource`**. The writer reserves retained child handles, declares required application IDs and checks that the root handle still matches. The entire modeled display graph must still match the retained sequence except for supported value/common-property changes.

Moving, deforming or privately replacing compound children invalidates this reuse path. Copied or mismatched root handles cannot reintroduce a former native root identity. Unsupported compounds fall back to explicit display geometry with a warning. Application data and external references are retained, not universally remapped, validated or regenerated. Full ATTDEF creation/synchronization, multiline/field/constant attribute editing and dynamic blocks remain outside this path.

## APIs and verification

`HatchGradient` and `HatchGradientGeometry` live in Model; `HatchEditing` in Engine; gradient/native-record codecs in Dxf; and `CadHatchEditor` in Controls. Hosts use the same session transaction APIs as the application.

```sh
python -m pip install ezdxf==1.4.4
python tests/fixtures/editing.py
python tests/fixtures/hatches.py
CADSPACE_HATCH_OUTPUT=artifacts/hatch-audit \
  dotnet run --project tests/CadSpace.Hatch.Tests -c Release
python tests/fixtures/hatches.py artifacts/hatch-audit
```

The hatch suite contains 37 tests covering colors, budgets/cache identity, holes, native/binary exchange, command dispatch, analytic boundaries, atomic edits and retained attribute safety. Four independently audited exports require zero errors and zero repairs. The rendered browser script checks actual controls, red/blue fill pixels, a circular island, Apply/Cancel/Undo, boundary extraction and downloaded DXF. Passing these is not industrial corpus, physical-GPU or Autodesk open/AUDIT/save/reopen qualification.
