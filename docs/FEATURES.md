# Coverage and remaining boundaries

This matrix describes implemented behavior, not full AutoCAD parity. Opaque byte preservation is not semantic support.

| Area | Implemented | Remaining boundaries |
| --- | --- | --- |
| DXF transport | ASCII, legacy R12 and R13+ binary groups, UTF-8/declared legacy code pages, original-byte preservation | Semantic conversion across every release and private extension |
| Geometry | LINE/POINT/CIRCLE/ARC/ELLIPSE, supported LWPOLYLINE, OCS/affine placement, legacy 2D/3D polylines and polyface/polygon meshes | Exact wide-polyline miter/linetype semantics, fitted legacy curves, all invisibility/edge/subdivision flags |
| Splines | Rational control points, knots/weights, homogeneous evaluation, adaptive tessellation, control-point creation | Fit-only reconstruction, complete spline editing and NURBS surface kernel |
| Mesh exchange | MESH/3DFACE/SOLID/TRACE, native indexed MESH output | ACIS/SAT/SAB 3DSOLID/BODY/REGION decoding and subdivision regeneration |
| Hatches | Bulged polyline/edge-list loops, line/arc/ellipse/spline edges, island styles, bounded dash patterns, native HATCH output | Gradients, complete authoring/associativity; edge-list curves are sampled for editing; simple nonintersecting loop assumptions |
| Blocks | Nested definitions/base points/placements, supported attributed/array INSERT display children | Dynamic actions/constraints, full attribute editor, xref lifecycle and all array semantics |
| Linetypes | Simple signed dash/gap/dot definitions, ByLayer/ByBlock resolution, object/global scales, DXF LTYPE/6/48/LTSCALE and 2D/3D polyline generation flags, continuous spline phase, native persistence | SHX/text/shape patterns, exact curve arclength/endpoint fitting, every affine/insert/plot/paper-space scale convention; complex source styles remain noneditable display fallbacks |
| Typography | Plain TEXT/MTEXT, width/oblique transforms, OCS/world-plane rendering; MTEXT radians/final orientation precedence | SHX, complete font/style substitution, every alignment/MTEXT grammar/column, shaping and annotative-text qualification |
| Annotations | Aligned dimensions, anonymous dimension-block display, LEADER vertex paths | Associative styles/constraints, native authored DIMENSION/MLEADER/TABLE parity |
| Structure | Entity visibility, model/paper-space separation, layout selector, supported ownership/block-record/layout dictionaries | Embedded paper-space model VIEWPORTs, sheet authoring, plotting/styles and exhaustive dependency repair |
| Drafting | 83 commands; line-based trim/extend/fillet/chamfer/join/break, transforms, arrays, bulge explode, bounded crossing STRETCH and immutable grip edits, undo/redo | Complete multifunction/subobject grips, every STRETCH selection/deformation option, arbitrary UCS editing, every command option and AutoCAD API/plugin compatibility |
| Snaps | Indexed nested/OCS endpoints/midpoints/centers/quadrants, line intersections/perpendiculars, circle/arc tangents, nearest closure | Every curve intersection/tangent/perpendicular combination, extension tracking and fully screen-space 3D/UCS snap behavior |
| Modeling | Primitives/extrusion/revolved surfaces, capped matching-profile polygon loft, parallel-transport sweep, bounded mesh Booleans | Analytic B-rep/ACIS topology, industrial solid fillets/chamfers/shelling, general NURBS surfaces, full self-intersection/manufacturing certification |
| UI | Application menu, Quick Access/search, dense contextual ribbon with property selectors, document/layout tabs, resizable command history, docked/floating/auto-hide palettes, searchable Tool Palettes, synchronized ViewCube/navigation, existing selection/grips and Layer/Linetype managers | Pixel-exact AutoCAD UI, detached native windows, arbitrary palette split/tab groups, CUI customization, Sheet Set Manager, custom tool catalog authoring, complete touch/accessibility parity |

## Modeling boundaries

UNION/SUBTRACT/INTERSECT use iterative double-precision BSP operations with weld/stitch handling and manifold/orientation checks. Inputs are limited to 16,000 combined triangles and work is bounded to 20 million steps. Unsupported operations fail atomically. These are triangle-mesh operations, not exact analytic solid construction.

LOFT requires planar closed profiles with matching vertex counts and correspondence in drawing order. SWEEP transports a polygonal profile along an open polyline and rejects reversal/degeneracy. Both are bounded, capped triangle meshes. Neither provides guide rails, arbitrary correspondence, exact sweep surfaces, universal self-intersection rejection or certified mass properties.

## Rendering and performance boundaries

The viewport supports shaded/wireframe/hidden-line styles, text atlases, CPU picking/highlights, perspective/orthographic cameras, anchored zoom and a clipping half-space. Clipping is uncapped and display-only. There is no comprehensive PBR/material/texture/shadow system, transparency ordering, exact hidden-line vector export or full 3D manipulation gizmo.

BVHs accelerate candidates; immutable roots reuse tessellation; selection uses a separate GPU attribute stream. Root-indexed window selection traverses candidates instead of grouping the full scene per query; large/crowded windows still approach linear work. Document validation, aggregate scene rebuilding and some text updates remain proportional to drawing size. Static 2D recording is separate from cursor/grip feedback; idle pointer moves do not request a new 3D render. Host composition and framebuffer readback on real model changes remain. Cold index construction, allocations and complex block graphs still require profiling. No claim of whole-app speedup follows from the narrow benchmarks.

Linetype expansion clips before generating visible strokes and uses bounded local cycle iteration rather than potentially nonadvancing absolute cycle numbers. Hatch pattern origins/phases and intersection work are guarded. These prevent specific stalls; they do not establish exact rendering at arbitrary coordinate magnitudes. GPU distance interpolation still has float precision and batching costs.

The current Uno host uses framebuffer readback, not zero-copy WebGPU/Vulkan. The browser adapter depends on a pinned private RGBA-conversion field and must be requalified on upgrade. GPU text atlases are bounded to eight 2048-square pages; complex/large labels may exceed the budget.

## Persistence and reliability

Native project v2 stores implemented entities, placements, compound identities, layout membership, styles/generation flags and original DXF provenance/bytes. It validates provenance against reparsed original geometry and tables before reusing source records. Basic v1 projects are accepted. Editing unsupported metadata or compound semantics can still be lossy; keep the original and review export reports.

Undo history, cameras, active selection, open-tab arrangement and transient input are not persisted. Best-effort local recovery writes dirty native projects into alternating checksummed slots every five seconds after storage initialization. Readback verification and validated decode reject torn/corrupt slots and fall back to the preceding valid generation. Restored projects remain dirty; saving, undoing to a clean state or explicit tab discard clears checkpoints. Each payload is limited to 32 Mi-characters; discovery accepts at most 256 slots. Storage denial/eviction, private mode, power loss and edits after the last checkpoint are not protected. There is no guaranteed crash-safe persistence, unload-save guarantee, shared editing, encrypted storage or enterprise permission system.

Browser checkpoints use acknowledged IndexedDB transactions; the published application is tested across reload and the adapter across aborted writes. Desktop LocalFolder persistence is not independently power-loss-qualified.

## Qualification

430 headless tests include independent synthetic DXF fixtures, native persistence/provenance, Booleans, projection/picking, spatial-index equivalence, modeling, snapping, completion, whole-root windows, grip transactions, STRETCH, Quick Select, recovery failure fallback, style exchange, mixed-scale assignments, continuous spline/polyline patterns, large-coordinate guards and randomized clipped-stroke comparison. CI independently audits canonical ASCII/binary geometry and style exports.

Published-browser checks exercise actual mesh/dash pixels, upload/invalidation counters, grips, recovery, compiled layer cells, manager view preservation and UI layer edits inspected through real native checkpoints. Passing these does not imply every interaction has been qualified.

**Not completed:** Autodesk AutoCAD open/AUDIT/save/reopen, exhaustive industrial DXF corpora, physical GPU/driver matrices, general large-document performance budgets, security/accessibility audits, signed/notarized installers or engineering certification. Software-backed CI is not evidence of those qualifications.

## Native polyline width increment

Constant and per-segment tapered widths are modeled, persisted and exchanged as native DXF. Straight/curved width fills are visible and selectable in 2D/3D, including OCS placements; PEDIT Width/Open/Close/Reverse and PLINEWID are available. Properties includes a bounded vertex editor. See [Wide polylines](WIDE-POLYLINES.md) for sampled-curve, bevel-join, linetype and loss-report boundaries. Scene bounds are identity-cached without changing the existing fit calculation.

## Integrated CAD workspace

See [Workspace controls](WORKSPACE.md) for application/Quick Access/search, ribbon selectors and contextual panels, palette docking/floating/auto-hide, Tool Palettes, ViewCube and navigation, layout lifecycle, saved preferences and specific remaining UI boundaries.


## Analytic editing and source-backed annotations

[Editing guide](EDITING.md) covers analytic JOIN/PEDIT Join; indexed vertex editing/splitting; Polygon/Donut/Match Properties; DDEDIT/EATTEDIT and the staged annotation control; and preservation of supported edited DXF records and attribute sequences. Remaining limits include a shared coplanar join frame, sampled wide-fill display, raw MTEXT rather than rich editing, no ATTDEF authoring/attribute synchronization, and no external-association/cache regeneration. Supported preservation is not universal lossless DXF editing.

## Multiline text increment

[MTEXT](MTEXT.md) covers source-preserving content/height changes, Unicode chunking, placed native output and staged formatting. Rich-text layout, arbitrary affine glyph export, automatic association/cache regeneration and full AutoCAD parity remain outside these changes.
