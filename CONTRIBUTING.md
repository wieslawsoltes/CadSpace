# Contributing

Keep behavior in its owning reusable layer. Geometry/Model/Engine/Dxf must not require Uno; Controls must not reference App. Preserve original records and expose unsupported/lossy conversions.

Run all five suites as described in README. Generate independent fixtures first with pinned test-only ezdxf 1.4.4. Set `CADSPACE_EXCHANGE_OUTPUT=artifacts/dxf-audit` for the advanced suite, then run `python tests/fixtures/audit.py` with the same environment; audits require zero errors and repairs. Performance tests assert correctness/work bounds, not brittle elapsed-time thresholds.

Build Windows/macOS/Linux and browser targets for UI/rendering changes. Test the published Release output—including trimming and the version-pinned reflective RGBA adapter—not only Debug. Attach screenshots and console output, and state whether the context was physical or software-backed.

Commands need atomicity, undo/redo, degeneracy, finite-value, layer/layout and selection tests. Index changes need brute-force comparisons and stale-cache tests. Exchange features need independently produced fixtures and preservation tests. Mesh algorithms must not be labeled ACIS/B-rep without implementing that kernel.

Release tags attach single-file desktop apps and distributions to a GitHub Release and publish the libraries to NuGet.org through Trusted Publishing; they do not sign or notarize. Native AutoCAD, physical GPU, large-corpus, accessibility and security qualification are separate gates. Never include proprietary assets, credentials or confidential customer drawings.
