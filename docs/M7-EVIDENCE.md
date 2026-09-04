# M7 `.lui` cutover evidence

The M7 cross-language tooling implementation is a **pending candidate**. The prior final evidence, package hashes, manual-gate record, clean-machine record, and PASS claims were invalidated by the final editor-correctness changes and must not be used for acceptance.

## Candidate scope

- evaluated `ProjectReference` source declarations and references participate in cross-language rename and Find All References;
- paired `.lui` tag names and compiler-owned keyed-`foreach` local provenance lower to exact authored edits;
- the VS Code extension exposes Lucent results from `.lui` and C# documents only in workspaces containing `.lui` files;
- source-map ambiguity, unmappable locations, overlap, stale identities, and stale local epochs reject publication.

## Pending evidence

| Record | Status |
| --- | --- |
| Candidate source commit and independent review | Pending |
| Debug/Release compiler and language-server builds and corpora | Pending rerun against this candidate |
| VS Code extension test and package inspection | Pending rerun against this candidate |
| `Verify-LuiSdk` and `Measure-LuiTooling -Verify` | Pending rerun against this candidate |
| Package, manual, clean-machine, and final M6 records | Pending; no hash is currently claimed |

No release or issue-closure decision is supported until the pending records are regenerated and independently reviewed.
