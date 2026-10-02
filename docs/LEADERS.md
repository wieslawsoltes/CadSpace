# Native LEADER exchange and editing

CadSpace models straight-path DXF `LEADER` records as `LeaderEntity`. The model retains WCS vertices, normal/horizontal vectors, arrow visibility and size, stored hookline dimensions/direction, annotation type/handle, offsets, block color and source dimension-style name. The same definition feeds native persistence, 2D/3D display, selection, supported grips, snaps, and DXF export.

This is not `MULTILEADER`/`MLEADER` support or complete AutoCAD annotation parity. Spline-path leaders remain source-backed display fallbacks, not editable straight leaders. Referenced text, tolerance and block annotations are separate drawing objects; moving a leader does not move or regenerate them.

## Create and edit

`LEADER` (`LE`) accepts an arrow-tip point followed by further vertices. Enter finishes after at least two points; `U` removes the last uncommitted vertex; Escape cancels. The preview and point input use the shared command/session APIs. Creating the leader is one Undo transaction. The workflow creates the path and arrow only, not attached text.

`LEADEREDIT` (`LED`) edits one preselected or picked straight leader. The Annotate ribbon, contextual Leader tab, Properties action, classic Dimension menu and 2D double-click open the same reusable `CadLeaderEditor`. Command search and Tool Palettes discover the registry entry.

The editor has one indexed vertex field, Previous/Next, Split Segment, Delete Vertex, arrow visibility and local arrow size. Splitting inserts a midpoint into the staged vertex array; deletion reconnects adjacent vertices directly. All actions remain staged until Apply. Apply creates one Undo step, and Cancel does not change the drawing. Unchanged values retain the original drawing snapshot and redo history. Invalid geometry, a locked layer, a stale captured root or a changed selection reject the edit.

A placed leader's editor coordinates are local to its stored placement, not necessarily WCS. The shared viewport supplies vertex grips, endpoint/midpoint snapping and supported partial crossing STRETCH. Existing multi-selection/grip-count limits apply; there is no full 3D manipulator or automatic associated-annotation update.

```text
LEADER
0,0
70,50
120,50

ZOOM
SELECTALL
LEADEREDIT
```

The blank line completes creation. The native vertex budget is 32,767, matching group 76's signed 16-bit binary transport. Sizes are finite, nonnegative and no larger than 1e12. Consecutive identical vertices are rejected.

## Source retention and canonical export

Unchanged documents still support exact original-byte pass-through. For changed documents, supported straight-leader vertex edits, topology changes and arrow-visibility edits can patch the native record while retaining its style, annotation references, offsets, application data and XDATA. Common-property edits are handled by the same existing source-record patcher. Record identity and handle must still match. Braced application data and XDATA are not interpreted as vertex coordinates.

Arrow-size and other unsupported definition/placement edits use canonical native `LEADER` output rather than claiming exact source-record retention. The export warning identifies potential loss of unmodeled metadata. Canonical output uses a fresh, collision-free `CadSpaceLeader` DIMSTYLE plus per-entity ACAD:DSTYLE size overrides. New leader annotation width/height defaults are positive. Canonical regeneration rejects zero stored annotation dimensions rather than allowing a downstream reader to silently substitute its defaults; unchanged or supported patched source records retain their original fields. It does not accidentally inherit unrelated source `Standard` settings, and many leaders share one generated style rather than creating a style for every object.

Uniform orthogonal 3D transforms preserve native vertices, directions, offsets and scaled sizes. Nonuniform scaling or shear is rejected by canonical DXF export; a native project retains the general placed representation. This does not establish all native arrow-block or hookline behavior. The displayed arrow is a closed filled triangle; custom source arrow blocks remain source metadata. Hookline rendering uses stored text width/gap and the direction convention exercised by the independent fixture.

Before reusing a modeled leader record in a changed document, an export-scoped numeric handle index checks its annotation target. Target type and layout must be compatible and its handle must be unambiguous. A deleted, incompatible, ambiguously identified or differently laid-out target forces canonical output with a detached reference and a warning. Handle spelling is normalized to the actual retained target on canonical output. The index is created lazily, not rebuilt or linearly scanned for each leader.

This is scoped reference validation, not a universal DXF dependency-repair engine. It does not regenerate private associations, annotation placement, field expressions or application caches. Exact unchanged pass-through intentionally does not repair an already-invalid source drawing. Keep source files and review export reports.

## Native project compatibility

Documents or retained source graphs containing a modeled leader write native format version **5**. Existing versions 1–4 remain readable. The version gate prevents older readers from silently treating the new entity fields as a previously known primitive. Native save/reopen preserves leader definitions, original DXF provenance and identifiers.

Older source-backed native projects can contain the former `CompositeEntity("LEADER", ...)` interpretation. Compatibility validation reconstructs that earlier representation and compares the complete source data and modeled properties before permitting reuse. A saved source graph is not trusted merely because it claims to match the original.

## Reusable APIs

`LeaderEntity` and `LeaderGeometry` are in `CadSpace.Model`; `LeaderEditing` and commands are in `CadSpace.Engine`; native/group-code persistence is in `CadSpace.Dxf`; `CadLeaderEditor` is in `CadSpace.Controls`. No application-specific file dialog is required to use the model or edit operations.

```csharp
using System.Collections.Immutable;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var leader = new LeaderEntity([new(0, 0), new(70, 50), new(120, 50)]);
var session = new CadSession(new CadDocument(Drawing.Empty with { Entities = [leader] }));
session.Select(leader.Id);

// Capture the current immutable root; reuse of a stale capture is rejected.
session.SetLeader(leader,
    leader.Vertices.SetItem(1, new Vec3(75, 55)),
    arrow: true, size: 6);
session.Document.Undo();
```

## Verification and performance scope

`tests/CadSpace.Leader.Tests` exercises commands, atomic edits, no-op/redo, invalid captures, grips/snaps, native persistence, provenance checks, source record retention, topology edits, 3D transforms, reference detachment, style collisions and the binary vertex-count limit. A warmed cache test reports time/allocation for repeated leader geometry lookups, not cold tessellation or whole-app frame rate.

`tests/fixtures/leaders.py` generates independent ezdxf input with an MTEXT annotation, DIMSTYLE overrides, application data and XDATA. It audits six ASCII/binary outputs: retained source edits, new/transformed/paper/block leaders, and detached annotations. Zero audit errors and zero repairs are required.

`tests/browser/leaders.py` uses real command/ribbon/Properties/double-click input, staged Apply/Cancel/Undo, native Save/reopen and actual ASCII/binary downloads. It independently audits the downloaded files and checks that the model renderer receives arrow geometry. Read-only bounds and recovery checkpoints observe the application; no drawing-mutation hook is used. Build and release workflows include the new headless suite and audits; the browser job includes the rendered workflow.

```sh
python -m pip install ezdxf==1.4.4
python tests/fixtures/leaders.py
CADSPACE_LEADER_OUTPUT=artifacts/leader-audit \
  dotnet run --project tests/CadSpace.Leader.Tests -c Release
python tests/fixtures/leaders.py artifacts/leader-audit
```

The checks do not establish native AutoCAD open/AUDIT/save/reopen compatibility, industrial-file coverage, physical-GPU qualification or complete DXF version semantics.

## Primary references

- Autodesk LEADER group codes: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-396B2369-F89F-47D7-8223-8B7FB794F9F3.htm
- ezdxf LEADER entity and DIMSTYLE overrides: https://ezdxf.readthedocs.io/en/stable/dxfentities/leader.html
