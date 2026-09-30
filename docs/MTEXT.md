# Multiline text and staged formatting

CadSpace supports point-based `MTEXT` (`MT`) creation. Enter an insertion point and content; `\P` inserts a paragraph. Enter at an empty prompt cancels without adding an object. The Annotate ribbon, classic Draw menu, command search and tool catalog use the same command. This is not AutoCAD's two-corner reference-box editor, full inline rich formatting, or complete font/shaping parity.

Select TEXT or MTEXT and use `DDEDIT` / Properties to edit content, local height and local rotation together. Apply validates and commits one Undo step; Cancel leaves all staged fields unchanged. A no-op preserves the immutable snapshot and redo history. Local coordinates/angles are relative to an imported text's existing OCS/affine placement; the editor does not pretend these are always world angles. `TextEditing.SetTextProperties` exposes the same atomic engine operation for hosts.

The editor enables multiline input before loading existing content, so reopening cannot truncate later paragraphs. The Properties palette shows a bounded read-only MTEXT preview and routes content changes through the staged editor, rather than an automatically committing single-line field. New edits normalize CRLF/CR to LF; a line-ending-only difference from the native control preserves the original text and Undo/redo history. The engine rejects unpaired UTF-16 surrogates before committing.

## DXF exchange

New and edited long MTEXT uses group-3 chunks of 250 Unicode scalar characters followed by a final group 1 (including an empty terminator at an exact multiple). UTF-16 surrogate pairs are never split. CRLF, CR and LF normalize to DXF paragraph escapes. Invalid surrogate sequences and NUL are rejected rather than silently replaced or truncated.

Source-backed MTEXT **content and height** changes retain the existing placement, reference width, attachment, background fields, APPDATA and XDATA. Braced application text is not interpreted as entity content. Stored display measurements, third-party associations and field caches are not regenerated; export retains the warning about those limits.

Placed MTEXT can now be exported without source provenance when its text-plane axes are orthogonal and equally scaled. Its world insertion, oriented plane, direction and scaled height remain native MTEXT. Unsupported shear/nonuniform glyph scaling is rejected. Changes to placement or local rotation use the canonical writer and report that unmodeled source metadata may be lost; they are not presented as source-preserving changes. Styled TEXT position/rotation changes also take this safe conversion route instead of confusing a local angle with the original DXF angle.

## Performance and verification

Content emission is a single pass over Unicode characters. `SceneTextLayout` caches immutable normalized lines and maximum line length by `SceneText` identity using weak keys. Skia drafting, GPU atlas preparation and conservative text bounds share it. An edited record copy receives its own layout; parallel callers share the published entry. The existing glyph metrics and rendering/atlas limits are unchanged.

A count-checked local .NET 10.0.401 Release comparison of 2,000 eight-line labels across 20 repetitions measured repeated splitting at 16.939 ms / 64,947,200 allocated bytes, versus warmed cached lookup at 0.545 ms / 0 bytes. Cold cache population is excluded. CI repeats the allocation/equivalence check and retains its own observations; this is not a full text-shaping or whole-application benchmark.

 Matching properties that are already equal no longer creates a dirty/Undo state or discards redo; unchanged roots retain their identity. This is not an application-FPS claim.

The engine registry contains 83 workflows. There are 439 headless regression cases (the editing suite contributes 105). Three focused cases demonstrated failures before the MTEXT repair: editing imported text, chunking long text and exporting a source-less placed MTEXT. Nine additional regression cases cover newline fidelity, no-op preservation, invalid Unicode edits, cache identity/concurrency, bounds equivalence and warmed allocations. New checks also cover Unicode boundaries, paragraph normalization, shape-preserving transforms, styled TEXT, atomic/no-op formatting and command cancellation.

`python tests/fixtures/mtext.py` generates independent ezdxf input. Its six ASCII/binary audits cover edited source-backed, transformed source-less and newly created text; they require zero errors and zero repairs. Together with the existing audits there are 18 checked exports. The browser MTEXT suite exercises normal controls, the real file chooser, Apply/Cancel/Undo and actual ASCII/binary download bytes. Read-only bounds/checkpoint inspection locates controls and observes results; no test mutates the document through a privileged hook.

```sh
python tests/fixtures/mtext.py
CADSPACE_EDITING_OUTPUT=artifacts/editing-audit dotnet run --project tests/CadSpace.Editing.Tests -c Release
python tests/fixtures/mtext.py artifacts/editing-audit
```

Primary references: Autodesk's MTEXT group-code reference and ezdxf's tag-format documentation:
- https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm
- https://ezdxf.readthedocs.io/en/stable/dxfinternals/dxftags.html
