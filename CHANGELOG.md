# Changelog

## Multiline editor reliability and shared display cache (unreleased)

- Fixed multiline TextBox initialization and single-line Properties auto-commit truncation; normalized edited CRLF/CR while preserving no-op text bytes and redo.
- Reject invalid Unicode edits before creating Undo/dirty state. Reuse weakly cached immutable text lines in Skia, GPU atlas preparation and bounds instead of repeated splitting.
- Added nine headless checks (439 total), including cache identity/concurrency and exact bounds equivalence. Browser coverage now reopens and applies multiline content without edits before testing Undo; failed checks retain actual recovery payloads.

## Multiline text exchange and formatting (unreleased)

- Added MTEXT creation and staged text-height/rotation editing in the reusable annotation dialog.
- Fixed native export of imported/placed MTEXT; retained source metadata for content/height edits and emitted Unicode-safe long-text chunks.
- Prevented styled TEXT local angle/position changes from reusing stale DXF coordinates; rejected unsupported MTEXT shear/nonuniform scale.
- No-op MATCHPROP preserves dirty/Undo/redo state. Added 29 headless cases (430 total), six independent MTEXT audits (18 total), and real browser download verification.
- See docs/MTEXT.md for exact scope and remaining limitations.

## Analytic drafting, DXF retention and annotations (unreleased)

- Added analytic line/arc/open-polyline JOIN, reversible taper preservation and indexed endpoint matching; extended PEDIT with reusable vertex editing, splitting and explicit straight reconnection.
- Added POLYGON, DONUT, MATCHPROP, DDEDIT and EATTEDIT with shared commands, ribbon/menu/Properties integration, staged text/attribute editing and 2D double-click entry.
- Preserve supported source-record geometry/property edits, APPDATA/XDATA and unambiguous vertex identifiers; edited native attributes retain INSERT/ATTRIB/SEQEND rather than flattening.
- Fixed binary version-header ordering, opaque payload retention and stale nested-field reuse. Added newer-generation browser checkpoint assertions.
- Added a sixth headless suite (67 checks, 401 total), six additional independent export audits (12 total) and browser editing/annotation scripts. Full contracts and remaining limits are in docs/EDITING.md.

## Integrated CAD workspace (unreleased)

- Added reusable application menu, Quick Access, search, ribbon panels/split buttons/selectors, document/layout tabs and workspace options.
- Integrated left/right/floating/auto-hide palette host and searchable Tool Palettes with existing commands and blocks.
- Replaced navigation placeholders with synchronized, pickable ViewCube, view/style menus and pan/zoom/orbit controls; fixed top/bottom camera matrix singularity.
- Added undoable layout/property operations, separate validated UI preference persistence, stable control diagnostics and browser interaction regressions.
- Small workspace controls use original vector chevrons, close, menu, floating-window and pin glyphs instead of depending on optional Unicode font coverage.
- Added optional classic menus/MENUBAR, staged Display/Workspace/Status Bar Options, persisted navigation visibility and console height, and status customization that does not disable drafting modes.
- Added Escape rollback for palette/console gestures, Ctrl-to-float, inside-edge right palette resizing, bounded open-drawing lists and unchanged-document-tab reuse. Fixed snap-menu callbacks after switching documents with identical initial settings.
- Added popup-aware read-only diagnostics and published interaction regressions; 334 headless cases cover the complete increment.
- Scope and remaining UI compatibility boundaries: docs/WORKSPACE.md.

## Unreleased — linetypes, layer management and selective tessellation

Added simple signed dash/gap/dot linetypes, ByLayer/ByBlock resolution, per-object/global scales and polyline generation flags. ASCII/binary DXF and native projects retain modeled style data; source-backed complex definitions are preserved rather than invented. Spline dash phase now continues across tessellation, and 3D polyline flags survive native/DXF and sampled affine exports.

Added reusable virtualized Layer Properties and Linetype managers, Properties assignments and LAYER/LINETYPE/LTSCALE/CELTYPE/CELTSCALE workflows. The registry contains 64 commands. Layer rows use compiled bindings to prevent blank cells after trimming. Opening managers preserves the 3D camera. Type-only assignment keeps mixed object scales; undo/load reconciles removed current layer/type settings.

Scene caching tracks each root's layer/block/linetype dependencies. A one-layer edit on 3,000 circles rebuilds one root and reuses 2,999; benchmark output checks equivalent geometry and records time/allocation observations. GPU patterns use distance attributes and shader batches without dash-mesh expansion. Skia clips before expanding dashes; bounded local cycle iteration avoids nonadvancing large-coordinate loops. Hatch origins, phase and intersection work have additional finite-range/work guards.

258 headless regressions cover existing and new paths, including randomized clipped-pattern comparisons and extreme inputs. Independent ASCII/binary audits validate native pattern values/references. Published-browser checks include dash pixels, trimmed row fields, manager camera preservation and actual layer edits verified through native recovery checkpoints. These tests do not establish full AutoCAD, physical-GPU or industrial interoperability parity.

## Earlier preview — selection, grips, recovery and invalidation

Added root-indexed window/crossing selection, whole-root text/face containment, deterministic overlap picking, add/remove/toggle selection, kind/layer Quick Select and Select Similar. Added transactional blue grip dragging with preview/cancel/stale-object guards and bounded crossing STRETCH for supported vertices. The registry contained 59 workflows.

Separated static drafting from cursor/grip feedback, gated 3D invalidation by drawing/camera/selection state, cached Properties/layout rebuilding, indexed selected entities and replaced recursive full-subtree sorting with bounded median partitioning. Tests record warmed window-query allocation/time and cold construction separately; RenderOverride counters support browser invalidation checks.

Added reusable two-slot checksummed recovery journaling. Dirty drawings checkpoint every five seconds when storage is available and recover as unsaved documents. Browser writes acknowledge IndexedDB transaction completion, and the adapter is embedded in Uno bootstrap resources; desktop uses LocalFolder. Payload/discovery limits, storage eviction/denial and power loss remain explicit boundaries. Escape from the command box also cancels captured grip gestures.

211 headless regressions and published-browser checks covered the increment. PR #3 passed desktop/browser builds, independent geometry audits, actual IndexedDB abort/rollback checks and recovery after reload before merge.

## Earlier preview — CAD expansion, performance and workspace

Expanded DXF transport/interpreters to ASCII/code pages and binary R12/R13+, OCS/affine geometry, legacy polylines/meshes, rational splines, supported hatch loops/patterns/islands, compound INSERT display and model/paper separation. Added native MESH/SPLINE/HATCH export and supported ownership/layout reconstruction. Native project v2 retains geometry and original bytes/provenance, checked against reparsed source. Corrected MTEXT radians and final orientation precedence.

Added bounded mesh Booleans, matching-profile capped loft, parallel-transport sweep, control-point spline/3D polyline creation, 3D rotation/reflection/alignment and analytic bulge explode. The registry contained 55 workflows.

Added BVH picking/snapping/culling, incremental root tessellation, dictionary-based bulk transforms, packed geometry buffers and independent changed-range GPU selection flags. Added deterministic performance/equivalence regressions and a 100,000-line warmed-picking comparison.

Refined the Uno workspace with stacked ribbon groups, minimize/restore, command completion/Tab, expandable history, cursor-adjacent input, context actions, hideable Properties and individual snap settings. The 3D viewport includes world-plane text, styles, picking/highlights, projection and uncapped clipping; browser readback uses the pinned RGBA adapter. Release trimming is exercised through published-app tests.

Verification comprised 169 headless tests, independent ASCII/binary audits, three desktop builds and browser pixel/interaction checks. CI packages six libraries and validates the deployed Pages revision.

## 0.1.0 — initial implementation

Introduced geometry/model/editing/exchange/rendering/controls libraries, Uno desktop/browser workspace, basic drafting, layers/blocks, mesh primitives, native storage and build/Pages/release workflows.
