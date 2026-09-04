# M7 `.lui` cutover evidence

The M7 editor implementation is a **pending candidate**. This source changed after the prior package, manual, clean-machine, and final records; their hashes and PASS claims are invalid and must not be used for acceptance.

## Candidate scope

- project-graph C# and `.lui` source maps participate in exact cross-language navigation, references, and rename;
- paired tags and keyed-`foreach` locals retain authored provenance, including source-document identity;
- the bounded VS Code bridge synchronizes participating C# buffers and delegates unrelated C# symbols to existing C# tooling;
- stale, ambiguous, unmappable, overlapping, or incomplete mappings fail closed.

## Pending records

| Record | Status |
| --- | --- |
| Candidate commit and independent review | Pending |
| Debug/Release compiler and LSP corpora | Pending rerun against this candidate |
| VS Code package/manual verification | Pending rerun against this candidate |
| `Verify-LuiSdk` and `Measure-LuiTooling -Verify` | Pending rerun against this candidate |
| Package, manual, clean-machine, and final M6 evidence | Pending; no hash is claimed |

No release or issue-closure decision is supported until these records are regenerated and independently reviewed.
