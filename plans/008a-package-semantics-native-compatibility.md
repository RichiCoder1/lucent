# Plan 008a: Lock package semantics and native compatibility

> **Executor instructions**: Add one versioned, non-executable metadata seam for
> compiled Lucent artifacts and one executable native-compatibility matrix. Reuse
> the shared compiler semantics and public PE/resource APIs; do not embed private
> source by default, load target assemblies, add XAML syntax, or turn Lucent
> components into Avalonia controls.
>
> **Drift check**:
> `git diff --stat 556ce31..HEAD -- build src editors tests docs plans`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: plans 007a, 008
- **Category**: package semantics / native interop / tooling
- **Planned at**: commit `556ce31`, 2026-08-23
- **Research**:
  [`docs/research/AKBURA_LEARNINGS.md`](../docs/research/AKBURA_LEARNINGS.md)

## Why this matters

Plan 008 makes one project's compiler and language server correct, bounded, and
fast. Plans 009–009b then need package-owned theme, global-style, and utility
metadata that tooling can inspect without executing referenced assemblies. If
each plan invents its own metadata shape, identity rules, reader, and cache, the
package seam becomes three shallow modules before the first release.

Akbura demonstrates a useful alternative: emit versioned semantic metadata with
the compiled artifact and inspect it without loading the assembly. Lucent needs
a narrower form tailored to interfaces it has actually accepted. The same
pre-package checkpoint should make native Avalonia interoperability an explicit
tested contract rather than a broad promise that raw C# is available.

Plan 008's native-C# matrix measures editor behavior for supported authoring
contexts. This plan reuses those fixtures where practical but owns a different
question: whether generated/runtime Lucent behavior projects honestly onto
public Avalonia properties, resources, templates, controls, styles, and
lifetimes. Do not create a second editor-parity suite.

## Decisions

### One Lucent module manifest

Define one internal versioned `LucentModuleManifest` schema and serializer. A
successful build writes the manifest deterministically, embeds it under one
stable resource name in the output assembly, and retains the intermediate file
under `obj` for build tests and diagnostics. Referenced manifests are read from
PE resources through public metadata APIs; the compiler and LSP never call
`Assembly.Load`, run a module initializer, instantiate a generated type, or scan
runtime objects.

`Lucent.Compiler` owns the internal schema, serializer, PE reader, validation,
and immutable result model. The compiler/MSBuild adapter asks the compiler for
manifest bytes; the LSP asks the compiler project-context seam for normalized
catalog entries. Neither adapter owns a parser/reader, and application code gets
no public manifest reader or writer API. Package-consumer tests exercise the
compiler/LSP query surface against restored assemblies rather than calling the
reader directly.

Use deterministic UTF-8 JSON with a small version envelope:

- `formatMajor` changes when a required field is removed, renamed, retyped, or
  changes meaning;
- `formatMinor` changes for additive optional fields/sections;
- `minimumReaderMinor` changes when a producer uses additive semantics that an
  older reader must not ignore; and
- `producerVersion` records the Lucent compiler version for diagnostics but does
  not replace format negotiation.

Before Lucent declares a released compatibility baseline, a reader accepts only
its exact supported major/minor and does not carry old-writer normalization or
legacy fixtures. Unknown JSON fields remain harmlessly ignored as ordinary
serializer behavior, not as a compatibility promise. It rejects any other
major/minor or a `minimumReaderMinor` above its supported minor with one bounded
incompatible-format diagnostic. Required fields stay required. Once Lucent
explicitly declares a stable versioning contract, minor additive compatibility
may be introduced only when the bridge is trivial and reviewed. Do not add
arbitrary extension dictionaries; new top-level sections need an implemented
producer and consumer plus a format decision in the same change.

Version one contains only metadata consumed by accepted plans:

- manifest format version and producing Lucent compiler version;
- exact output assembly identity fields available at build time: name, version,
  culture, and public-key token;
- installable style-catalog type metadata names;
- immutable style-class entries with name, optional applicable native type,
  origin, definition identity when available, and human-readable detail; and
- stable logical source identities and content hashes for local navigation and
  open/local stale-data rejection, without source contents.

The manifest has an enumerated style-catalog section. A later format may add an
enumerated public-component section when Lucent has a real component-library/
package use case. Version one does not serialize private fields, state,
generated implementation members, arbitrary CLR metadata, or speculative
extension bags.

The assembly identity in the manifest must agree with the containing PE
metadata. A mismatch, unsupported newer format, malformed resource, duplicate
catalog type, duplicate class entry, or invalid source hash makes that manifest
unavailable and produces one bounded project/build diagnostic. It never falls
back to executing the assembly.

For a referenced package, source hashes are opaque identities because the
consumer does not have the producer's source. Validate their encoding and use
them for navigation only when matching source is independently available. A
reference becomes stale or invalid through PE identity/resource/fingerprint
checks, not by comparing unavailable source files. Live/local sources may compare
their current content hashes with the last successful manifest.

### One build transaction

Integrate the resource without post-processing the assembly:

1. After successful Lucent generation and before `CoreCompile`, compute the
   expected assembly identity from the exact evaluated SDK/MSBuild compiler inputs,
   including `SignAssembly`, key, public-sign/delay-sign options and the selected target
   framework. Source- or generator-owned identity attributes are accepted only
   when post-`CoreCompile` PE verification proves they agree; disagreement fails
   the build with one actionable diagnostic and emits no promoted manifest.
2. Atomically replace one deterministic staging path under the configuration,
   target-framework, and runtime-identifier-specific intermediate directory.
   Before `CoreCompile`, remove any prior generated manifest resource item and
   add exactly that path once as an `EmbeddedResource` with the stable logical
   name so the normal compiler embeds it before signing.
3. After `CoreCompile` succeeds, reopen the produced PE through the shared reader,
   verify its actual identity and embedded manifest, compute the PE fingerprint,
   and atomically promote a last-successful intermediate record containing the
   manifest bytes plus that PE fingerprint.
4. On Lucent generation or `CoreCompile` failure, never promote the staged
   manifest. Local tooling ignores staged files and uses live source semantics
   plus the last-successful record; referenced tooling reads only the resource
   from the resolved PE and keys its cache by PE fingerprint.

This ordering keeps signing intact and prevents a newer failed-build `obj`
candidate from being paired with an older output assembly.

The staging target declares the exact Lucent/CSS/style-catalog inputs plus the
C# identity-affecting compile/options inputs and manifest tool version; its
output is the deterministic staged path. The promotion target depends on
successful `CoreCompile` and declares the PE plus staged manifest as inputs and
the last-successful manifest/fingerprint record as output. `Clean` removes
staged and promoted records. Repeated in-node builds must leave one generated
resource item, and each target framework/runtime identifier owns isolated paths.

### One style-class metadata contract

Move the generic class metadata shape needed by Plans 009–009b to the shared
compiler seam in this plan. It is equivalent to:

```csharp
StyleClassEntry(
    string Name,
    string? ApplicableType,
    StyleClassOrigin Origin,
    SourceIdentity? Definition,
    string Detail);
```

`SourceIdentity` is a logical path/hash/span tuple when source is present. It is
not an embedded source file. Producers may supply live entries from an open
project or serialized entries from a manifest; consumers see one immutable
catalog and do not know which parser, package, or theme produced it.

The current project's open `.lui`/CSS documents remain authoritative over its
last successful manifest. The manifest is the package/reference seam, not a
replacement for Plan 008's live semantic snapshot and not a new runtime
registry.

### A bounded native-compatibility matrix

Add one executable matrix covering the existing Lucent-to-Avalonia seams:

- ordinary and attached styled properties;
- routed and CLR events;
- direct Lucent expressions and explicit native compiled bindings;
- static and dynamic resource values through supported C# and CSS forms;
- native child/content metadata and the accepted item-template subset;
- custom and third-party Avalonia controls resolved from project references;
- exact single-root host mounting, multiple-root composition, lifetime ownership,
  disposal, and automation behavior; and
- adjacent/global/theme style priority and value restoration where the relevant
  style plans add those sources.

The matrix records `supported`, `bounded subset`, or `escape through explicit
Avalonia C#`, with one fixture or source reference for each claim. An explicit,
tested Avalonia C# expression counts as supported compatibility; Lucent syntax is
required only when the escape is materially unclear or cannot preserve the
native behavior. Unsupported XAML syntax is not a failure.

Close gaps exercised by the examples, Workbench, Plans 009–009b, or Plan 012's
clean package consumer. Other gaps remain honestly classified. If a release-used
gap requires a broad new syntax/runtime module, stop, design it as a separate
plan, and add that dependency only when the release cannot use the explicit C#
escape. Do not let the compatibility matrix silently turn 008a into a general
XAML or control framework.

### Bounded public `TemplateContent`

Close one concrete native gap in this plan. When semantic resolution finds a
writable public property marked `Avalonia.Metadata.TemplateContentAttribute`
whose property type can accept `Avalonia.Controls.IDeferredContent`, allow the
existing template-property grammar with an empty parameter list:

```csharp
ThirdPartyControl {
    template DeferredBody() {
        Border {
            Width: 8;
        }
    }
}
```

The exact grammar alternatives are:

```text
template Identifier '(' Type Identifier ')' block  // existing item template
template Identifier '(' ')' block                  // bounded TemplateContent
```

The zero-parameter form is valid only for the resolved public
`[TemplateContent]`/`IDeferredContent` property contract. The one-parameter form
keeps the existing typed item-template semantics; the two forms do not infer or
share data contexts.

The compiler generates an `IDeferredContent` implementation whose
`Build(IServiceProvider)` creates and returns one fresh native control root per
call. Resolve the property and attribute through the existing project-aware
native symbol seam; do not maintain a control/property allowlist. When
`TemplateContentAttribute.TemplateResultType` is non-null, the generated root's
static native type must be assignable to that result type. An attribute whose
result type is not a native control is outside the bounded generated form and
uses the explicit-C# fallback.

The body may contain one native control root and only these property-value forms:

- non-interpolated literals with a standard, non-user-defined C# identity,
  reference, implicit numeric, or constant conversion to the resolved target
  property type; and
- named `const` fields and enum members, recognized semantically as compile-time
  symbols, with the same standard non-user-defined conversions.

This form does not use Lucent's constructor/`Parse` target-type conveniences and
does not allow static fields/properties except the compile-time `const`/enum
symbols above, because ordinary static access can execute a type initializer or
getter. Values such as brushes, thicknesses, or custom objects that need
construction use the explicit-C# fallback.

The one construction exception is exactly `new Avalonia.Data.Binding("Path")`,
where `Path` is a non-empty, non-interpolated string literal. Avalonia 12.1.1
does not expose a public eager binding-path parser, and its public `Binding`
constructor stores malformed paths without validating them, so Lucent does not
claim compile-time path validation; Avalonia validates the path when the binding
is attached. No object initializer or derived/custom `BindingBase` type is
accepted. The compiler semantically recognizes this form only when the receiving
assignment resolves to a public Avalonia styled/direct property with an
accessible static `AvaloniaProperty` identifier. It emits a dedicated public
`AvaloniaObject.Bind(targetProperty, binding)` call on the realized root, so
inherited `DataContext` owns observation. A CLR-only property,
missing/inaccessible Avalonia property identifier, or incompatible binding target
uses a diagnostic and the explicit-C# fallback. This exception does not route
through Lucent `binding(...)` or the ordinary property-value lowerer.

All other C# expressions are rejected in the bounded form, including object
construction, method invocation, interpolation, lambdas, `await`, instance/static
member access, and every other `BindingBase` expression. The initial generated
form does not support static/dynamic resource lookup or markup extensions; use
the explicit-C# `IDeferredContent` fallback when those are required.

The body may not contain Lucent component invocations, events, slots, structural
regions, nested templates, or reads/writes of component parameters, fields,
`State<T>`, or `Computed<T>`. Generated deferred content owns no
`ComponentOwner`, subscriptions beyond Avalonia-owned bindings on the realized
root, or captured declaring-component lifetime; Avalonia/the receiving control
owns each realized native root.

Use only the public `TemplateContentAttribute` and `IDeferredContent.Build`
contract. Do not emulate XamlIl service-provider internals or promise
`ControlTemplate`, `DataTemplate`, markup-extension, name-scope, or template-part
parity. If the public contract cannot implement this bounded form, keep the
matrix row supported through explicit Avalonia C#, omit the syntax/generator,
and record the evidence. A broader public template module still requires a
separate plan.

### Keep the focused-format lesson deferred

Akbura's final-root component form is worth a later parser prototype, but it is
not required by packaging, native compatibility, or Plans 009–009b. This plan
does not add filename-inferred components, implicit `Render()`, bare reactive
state reads/writes, XML markup, `bind`/`out` parameters, hooks, commands, or
dependency injection. Those would change Lucent's author-facing language and
need their own evidence and review rather than hitching a ride on metadata work.

## Scope

**In scope**:

- Define the manifest schema, format-version policy, deterministic serializer,
  resource name, and shared reader.
- Emit and embed a manifest from successful builds without changing runtime
  startup.
- Read referenced manifests through public PE/resource APIs with exact identity,
  malformed/newer-format, duplicate, fingerprint, and cancellation tests; limit
  source-staleness comparison to live/local source.
- Define the shared immutable style-class entry/catalog contract used by Plans
  009–009b.
- Add app/library/reference fixtures proving metadata survives clean build,
  project reference, pack, and restore without repository paths or assembly
  execution.
- Publish the native-compatibility matrix and executable evidence for accepted
  seams.
- Add the bounded public `[TemplateContent]` lowering and diagnostics above, or
  record the explicit-C# fallback if public Avalonia APIs cannot support it.
- Update architecture, styling/tooling, build, and package-boundary docs.

**Out of scope**:

- Public component-library metadata, cross-package Lucent component invocation,
  source embedding or SourceLink policy, runtime reflection, or a public manifest
  extensibility interface.
- Theme values/classes, OKLCH, `Class:` completion, global styles, utilities, or
  example redesign; Plans 009–010 own those consumers.
- New XAML/AXAML syntax, generalized markup extensions, template support beyond
  the exact bounded public `[TemplateContent]` form, implicit component adapters,
  generated `Control` subclasses, hooks, commands, or DI syntax.
- Replacing Plan 008's project snapshot, source maps, LSP cache, or live CSS
  parser with serialized metadata.

## Steps

### 1. Lock the failing package and compatibility contracts

Add temporary producer/consumer fixtures before implementation. The producer
declares one installable style catalog and representative class entries. The
consumer references it as a project and as a local package, then inspects
metadata without loading the output assembly. Add malformed, duplicate,
identity-mismatch, unsupported-major/minor, newer-required-minor, unknown-field,
invalid-hash-encoding, PE-fingerprint, cancellation, and missing-manifest cases.
Add local/open-source stale-hash cases separately; referenced packages do not
claim unverifiable source freshness. Add source-owned assembly version/culture,
unsigned, strong-signed, delay-signed, public-signed, repeated in-node rebuild,
and two-target-framework identity/resource fixtures.

Create the native-compatibility table from current executable behavior. Link
every supported claim to a focused existing or new fixture and mark genuine gaps
without expanding syntax to make the table look complete.

**Verify**: package/reference metadata tests fail because no Lucent manifest
exists; the compatibility table distinguishes existing support, bounded support,
and explicit Avalonia escape paths.

### 2. Emit one deterministic manifest

Build the smallest schema/serializer and implement the pre-`CoreCompile` stage
and post-success promotion transaction above. Derive identity from the same
Roslyn compilation/options and generated compile inputs that Plan 008 validates
against design-time MSBuild, not a second assembly-attribute parser. Write
atomically, replace the one generated resource item, avoid rewriting identical
promoted content, embed one staged resource before signing, and never promote it
after failed Lucent generation or failed `CoreCompile`. Normalize logical paths
consistently with Plan 008's URI/source-map rules and hash source identity
deterministically.

Keep style entries producer-neutral. The fixture may produce entries directly;
do not implement Shadcn, native-theme, global-style, or utility producers here.

**Verify**: clean/repeated builds produce byte-identical manifests; source order
does not change canonical output; renamed/deleted inputs cannot survive the next
successful manifest; failed Lucent generation and failed `CoreCompile` leave the
last-successful promoted record unchanged; signed outputs validate without PE
post-processing; actual PE identity and fingerprint match the promoted record;
source-owned identity attributes, unsigned/signing modes, repeated in-node
builds, and multiple target frameworks produce exactly one isolated resource per
output.

### 3. Add the shared non-executing reader

Read local intermediate manifests and referenced assembly resources through one
module. Validate format and containing assembly identity before publishing an
immutable snapshot. Integrate it at Plan 008's project-generation seam so one
generation owns one bounded referenced-manifest set; completion handlers never
open assemblies or files.

Return no metadata for missing manifests. Report malformed, unsupported-version,
duplicate, or identity-mismatched Lucent manifests once per project generation,
not once per editor request. Cache by resolved reference identity and PE
fingerprint, evict with the owning project generation, and preserve
cancellation/stale-publication rules.

**Verify**: project and packaged references yield the same entries without
assembly execution; missing/malformed/newer manifests are bounded; no
request-path I/O is introduced; Plan 008's latency, allocation, memory, and
generation budgets remain accepted.

### 4. Close release-used native gaps and publish the contract

Run the matrix fixtures across the supported seams and document exact syntax,
native owner, generated behavior, lifecycle ownership, and escape hatch. Close
release-used gaps with the smallest direct Lucent support or an explicit tested
Avalonia C# escape. Split any required broad module into its own reviewed plan
rather than expanding this one implicitly.

Add a custom referenced control with a public `[TemplateContent]`
`IDeferredContent` property. Test semantic attribute/type discovery, one fresh
native root per `Build` call, directly converted literal/constant/enum values,
the one allowed inherited-`DataContext` `new Binding("Path")` path, no
declaring-component capture,
and no owner/subscription retention beyond the realized control's native
binding.

Add parse/recovery and semantic diagnostics for missing `()`, non-empty
parameters on a `[TemplateContent]` property, zero parameters on an item-template
property, duplicate template assignment, missing/empty body, nonpublic/read-only/
unmarked/incompatible property types, incompatible/non-control
`TemplateResultType`, zero/multiple roots, resource/markup-extension/
`binding(...)` use, element/relative/name-scope/provider binding sources, events,
components, state/parameter/field capture, slots, structural regions, nested
templates, object construction, invocation, interpolation, lambdas, `await`,
instance/static-member access, constructor/`Parse` target conversion, binding
object initializers, empty/interpolated binding paths, and any `BindingBase` form
other than the exact exception above. Add a binding diagnostic/fallback fixture
for CLR-only properties and missing/inaccessible Avalonia property identifiers,
and conversion diagnostics for user-defined implicit operators and ordinary
static members while accepting compile-time `const`/enum symbols. Do not depend
on XamlIl service-provider types. If the bounded generated form fails on public
APIs, replace these syntax tests with one explicit-C# fixture and document the
fallback; do not use private APIs.

Record style precedence rows as later plans add theme/global/utility sources;
this plan establishes the matrix and baseline, while Plans 009–009b own their
new runtime evidence.

**Verify**: every release-used row works directly or through its tested explicit
C# escape; every other supported row has executable evidence; every
bounded/escape row names the limit; the public `[TemplateContent]` decision has
the fixture evidence above; no row claims general XAML compatibility or hides a
wrapper control.

### 5. Hand off the package consumers

Update Plan 009 to publish Shadcn and reviewed native-theme entries through this
manifest/catalog contract. Update Plan 009a's generated global-style type and
Plan 009b's utility package to emit the same manifest section. Update Plan 012
to verify the embedded manifest from clean packaged consumers.

**Verify**: the amended plans define no second style metadata schema, reader,
runtime registry, assembly activation rule, or completion cache.

## Done criteria

- [x] One versioned deterministic manifest is embedded in successful Lucent
      outputs and available as an intermediate build artifact.
- [x] Project/package consumers inspect referenced metadata through public
      PE/resource APIs without loading or executing the target assembly.
- [x] Major/minor/minimum-reader compatibility, manifest identity,
      malformed/newer formats, duplicates, local stale hashes, PE fingerprints,
      cancellation, failed Lucent/`CoreCompile` generation, signing modes,
      repeated/multi-target staging, promotion, and cache eviction have
      executable tests.
- [x] A fixture producer/consumer proves the immutable style-class contract and
      referenced-manifest reader, and Plans 009–009b are amended to use them
      when those dependent plans execute.
- [x] The native-compatibility matrix gives executable evidence or an explicit
      bounded/escape classification for every listed seam.
- [x] Every release-used matrix gap is closed directly or through a tested
      explicit Avalonia C# escape; any required broad module is split into a
      separate reviewed dependency.
- [x] Public `[TemplateContent]` properties either support the bounded one-native-
      root/no-capture lowering through `IDeferredContent`, including
      `TemplateResultType` validation and the bounded raw-binding rule, or have a
      documented, tested explicit-C# fallback with no XamlIl/private API
      dependency.
- [x] No source embedding, speculative public component schema, runtime registry,
      XAML syntax, wrapper-control model, hooks, or reactive style runtime is
      introduced.
- [x] Plan 008's accepted semantic, latency, allocation, memory, cancellation,
      and generation contracts remain green.

## STOP conditions

- Reading package metadata requires `Assembly.Load`, target code execution,
  runtime reflection, or network access.
- The manifest starts serializing implementation details or private source to
  serve a hypothetical consumer.
- Build ordering cannot guarantee that failed generation leaves the last good
  artifact visible without publishing partial/new metadata.
- Manifest embedding requires post-processing a compiled/signed PE or local
  tooling can observe an unpromoted staged manifest.
- Exact identity cannot be derived from the same source/generated attributes and
  compilation/signing options used by `CoreCompile`, or repeated/multi-target
  builds cannot guarantee exactly one isolated manifest resource.
- Manifest reading introduces completion-path I/O or an unbounded cache outside
  Plan 008's project-generation ownership.
- Closing a compatibility-matrix gap requires a new language feature, general
  XAML processor, broad template framework, or component-as-control architecture;
  split a release-critical module instead of expanding 008a.

## Maintenance notes

The manifest is a package/tooling adapter over the shared compiler interface,
not a second semantic model. Live source remains authoritative while it is open;
runtime Avalonia behavior remains authoritative after installation.

Add a manifest section only when a released producer and consumer both exist.
One producer with no consumer is speculative metadata; one consumer with a
private reader is a duplicate seam.
