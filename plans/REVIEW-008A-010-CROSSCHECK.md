# Plan 008a–010 cross-check review

> **Roadmap split note**: This review predates the split of the former combined
> Plan 010 into Plan 010 (example migration), Plan 011 (Workbench dogfood), and
> Plan 012 (packaging). Its technical findings still apply to their inherited
> seams, but it is not acceptance of the rewritten plan contracts.

Plans 008a, 009, 009a, 009b, and 010 are **PASS** after repeated fresh read-only
adversarial reviews, including a new review cycle after the user grilling changed
Plan 008a's native-compatibility scope. This record covers the Plan 008a addition
and the Akbura-informed amendments made to the existing 009-series package/style
plans.

No implementation tests were run. These are TODO-plan contracts; their named
fixtures, galleries, build tests, and performance gates remain implementation
requirements.

## First review — FAIL

The first reviewer accepted the shared compiler authority, logical fragment
components, non-executing package metadata direction, selected Akbura lessons,
and finite Avalonia-native utility scope. It found three high and four medium
issues:

- theme construction was incorrectly treated as installation;
- manifest staging/promotion did not define a complete build transaction;
- cross-catalog precedence ignored host installation order;
- referenced source hashes could not prove source freshness;
- reader ownership was ambiguous between internal tooling and a public package
  interface;
- missing and invalid manifests had conflicting diagnostic behavior; and
- roadmap dependencies omitted 008a for 009a/009b.

It also noted that the prior Plan 009/009a and 009b PASS records predated these
amendments.

## First resolution

- Theme metadata now requires a semantically resolved direct
  `Application.Styles.Add(new Theme())`; bare construction is inactive.
- Plan 008a stages one resource before `CoreCompile`, validates/promotes only
  after successful output, fingerprints the PE, preserves signing, and keeps
  staged data invisible to local tooling.
- Package-catalog installation order is explicitly host-controlled. Plans 009a
  and 009b test both representative orders and value restoration without
  claiming a portable package winner.
- Source-staleness comparison is local/open-source only. Referenced packages use
  PE identity/resource/fingerprint checks.
- `Lucent.Compiler` owns the internal schema, serializer, reader, validation, and
  result model; consumers use compiler/LSP queries rather than an
  application-facing reader.
- Missing manifests are silent. Malformed, incompatible, duplicate, or
  identity-mismatched Lucent manifests emit one project-generation diagnostic
  and contribute no entries.
- Roadmap dependency rows now match every plan.

## Second review — FAIL

The second fresh reviewer confirmed those fixes but found one blocker and two
medium issues:

- pre-compile identity derivation did not account for source/generated assembly
  attributes and signing modes used by the actual C# compilation;
- repeated in-node and multi-target builds could retain duplicate generated
  resource items without a deterministic replace/clean contract; and
- Plan 009b still used a few ambiguous “Plan 009 completion metadata” phrases
  that could recreate a second metadata shape.

It also recommended making the manifest's evolution policy concrete rather than
merely calling it versioned.

## Second resolution

- Identity is derived from the same exact Roslyn/MSBuild compilation inputs,
  source/generated assembly attributes, and signing options used by
  `CoreCompile`; unsupported parity stops with one build diagnostic.
- Staging uses one deterministic configuration/TFM/RID path, replaces the prior
  generated `EmbeddedResource`, declares incremental inputs/outputs and `Clean`,
  and tests repeated in-node and multi-target builds plus unsigned, strong,
  delay, and public signing.
- Every utility metadata reference now names Plan 008a manifest class entries
  consumed through Plan 009's existing catalog/cache.
- The manifest uses deterministic UTF-8 JSON with `formatMajor`, `formatMinor`,
  `minimumReaderMinor`, and `producerVersion`. Breaking changes increment major;
  additive optional changes increment minor; readers ignore unknown optional
  fields only when the minimum-reader contract permits it. Format changes retain
  golden and old/new reader-writer compatibility fixtures. Arbitrary extension
  bags remain prohibited.

## Final review — PASS

The final fresh reviewer found no blocker, high, or medium issue. It confirmed:

- one internal compiler-owned manifest schema/reader and immutable class
  catalog;
- deterministic, signed, isolated, failure-safe build staging and promotion;
- explicit and evolvable format compatibility rules;
- correct local/reference hash and missing/invalid diagnostic behavior;
- direct theme/style installation detection without execution;
- host-controlled package catalog ordering and state-value restoration;
- consistent 009b metadata wording and decoded state-class flow;
- acyclic roadmap dependencies and correctly assigned release gates; and
- preservation of Lucent's logical component, native Avalonia, no-runtime-CSS,
  no-hooks, and no-source-embedding direction.

## User grilling decisions

The post-review grilling confirmed these product choices:

- 008a blocks all of Plan 009, including theme/gallery work;
- manifest version one contains style catalogs only, not speculative public
  component signatures;
- inability to derive exact pre-compile assembly identity fails the build rather
  than silently omitting metadata;
- final-root/state shorthand remains deferred;
- manifest reading remains compiler-internal;
- readers preserve same-major compatibility through explicit major/minor/
  minimum-reader negotiation;
- application catalog conflicts follow host installation order;
- 008a closes release-used native gaps, while an explicit tested Avalonia C#
  escape counts as support;
- a broad release-critical gap is split into its own plan rather than expanding
  008a; and
- public `[TemplateContent]` receives a bounded zero-parameter, one-native-root,
  no-component-capture form now, with explicit C# fallback if public Avalonia
  APIs are insufficient.

## Post-grilling reviews

Fresh review of that template decision initially failed because the plan omitted
`TemplateContentAttribute.TemplateResultType`, left resource/binding forms
underspecified, and did not define the zero-parameter parser/recovery contract.
The plan now validates writable public `IDeferredContent` properties and result
type assignability, specifies `template PropertyName() { ... }`, and lists every
accepted/rejected parse and semantic form.

Further fresh reviews rejected broad “static C# value” and conversion wording.
The accepted generated body now allows only standard non-user-defined direct
literal/constant/enum conversions and one exact
`new Avalonia.Data.Binding("Path")` exception. That binding requires a resolved
public Avalonia styled/direct property and accessible `AvaloniaProperty`
identifier, then lowers through public `AvaloniaObject.Bind`. Constructor/Parse
conveniences, ordinary static members, CLR-only binding targets, resources,
markup extensions, captures, and every other expression use diagnostics plus the
explicit-C# fallback.

The final fresh reviewer found no blocker, high, or medium issue in the corrected
grammar, conversion, binding, result-type, lifecycle, fallback, manifest, or
dependency contracts.

## Residual risk

Implementation still has to prove identity/signing parity and public Avalonia
style precedence on the named fixtures. The plans stop rather than adding PE
post-processing, private Avalonia APIs, runtime conflict arbitration, or a
second metadata system if those proofs fail.
