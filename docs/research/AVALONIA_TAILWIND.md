# Avalonia and Tailwind-like utility styling

## Executive summary

No official Avalonia Tailwind integration was found, and Tailwind itself emits
browser CSS, not Avalonia styles. The ecosystem has useful adjacent work, but
not a stable, drop-in utility-class package for ordinary Avalonia/Lucent apps.
Akbura's experimental AKCSS is the closest technical precedent and already
demonstrates type-aware utilities, property-level conflict resolution,
composable per-side spacing, state/theme variants, and top-level breakpoints.
It is part of a separate UI language/compiler, however, rather than a reusable
Avalonia style package. The lowest-risk Lucent path is to study those solutions
but build a deliberately finite utility catalog on Plans 009/009a. A useful
first package is a **M** effort (about 15--25 engineer-days after those
foundations); a faithful Tailwind port is an explicit decline.

Facts below are marked **Verified** when directly supported by the linked
primary source. Recommendations and estimates are **Inference**.

## 1. Existing projects, packages, and tools

### Akbura / AKCSS

**Verified.** [Akbura](https://github.com/Asaicraft/Akbura) describes itself as
an experimental declarative .NET/Avalonia language and compiler with typed
styling through AKCSS, warns that syntax and APIs may change, and demonstrates
utilities directly on markup (`w-10`, `h-4`, `p-3`). Its
[utility documentation](https://asaicraft.github.io/Akbura/akcss/utilities)
documents Tailwind-inspired built-ins for size, margin/padding, grid placement,
spacing, opacity, colors, typography, borders, radii, and shadows, backed by
dynamic Avalonia resources. Its
[variant documentation](https://asaicraft.github.io/Akbura/akcss/utility-variants)
uses property-level conflict resolution and observable markup-extension
conditions; the
[built-in variants](https://asaicraft.github.io/Akbura/akcss/built-in-utility-variants)
cover Avalonia interaction/theme/state signals and top-level-width breakpoints,
while explicitly declining misleading Tailwind selectors and container-query
approximations.

AKCSS also demonstrates an important Avalonia-specific cost: because `Padding`
and `Margin` are each one `Thickness` property, ordinary `p-3` and `pt-2` style
setters cannot compose. Its
[spacing implementation](https://asaicraft.github.io/Akbura/akcss/spacing)
uses four attached side properties plus a runtime combiner to preserve untouched
sides. The repository had no GitHub releases when consulted and its README calls
the project experimental. GitHub identifies its license as MIT, but the checked
in license notice still contains `[year] [fullname]` placeholders; copying code
would require a license/attribution review rather than relying on the badge.

**Inference.** AKCSS is the best architecture reference found, especially for
conflict and spacing semantics, but taking an Akbura dependency would import a
second language/compiler/runtime model and violate Lucent's existing seams.

### Avalonia.Acss

**Verified.** [Avalonia.Acss](https://github.com/TheExiledCat/Avalonia.Acss) is a
CSS-inspired Avalonia styling library. Its README describes `.acss` selectors
such as element and class selectors and a work-in-progress implementation; it
is not a Tailwind port. The repository states MIT licensing and says it was not
yet published on NuGet. The repository page reported seven commits, no
published releases, one open issue, and a latest README update dated 2025-01-13
when consulted (the [releases page](https://github.com/TheExiledCat/Avalonia.Acss/releases)
has no release artifact). These facts indicate an experiment rather than a
dependency with a release/compatibility contract.

**Inference.** Its selector/file idea is worth studying, but Lucent should not
depend on it: no package version or active release stream was verified.

### Flowery.NET

**Verified.** [Flowery.NET](https://github.com/tobitege/Flowery.NET) is a native
C#/Avalonia component library inspired by DaisyUI (which is a Tailwind-based
web component library), not a utility-class compiler. Its README advertises
80+ controls, themes, runtime theme switching, localization, and platform
gallery applications. The repository's [MIT license](https://github.com/tobitege/Flowery.NET/blob/avalonia12/LICENSE)
permits reuse subject to the usual notice; its `avalonia12` README documents
package version `3.1.0` and Avalonia 12/.NET 10 targets. The repository
[changelog](https://github.com/tobitege/Flowery.NET/blob/avalonia12/CHANGELOG.md)
records 3.1.0 and 3.0.0 entries. This is active design-system/component
precedent, but its controls/templates are outside Lucent's compiler seam.

**Inference.** Reuse should mean visual/token comparison only, not taking a
component framework dependency. The target-framework and Avalonia-major
coupling also make it unsuitable as a generic utility foundation.

### Nlnet.Avalonia.Css

**Verified.** [Nlnet.Avalonia.Css](https://github.com/liwuqingxin/Avalonia.Css)
is an MIT-licensed Avalonia-specific CSS/runtime styling system with dynamic and
hot loading, syntax extensions, resources, behaviors, and a Fluent theme. Its
README explicitly says it does not follow standard CSS. The inspected core
project targets .NET 6 and references Avalonia; the repository's latest commit
when consulted was 2025-07-23. It is not a Tailwind utility catalog.

**Inference.** Its runtime parser/hot-loading architecture conflicts with
Lucent's compile-time typed CSS and no-runtime-parser contract. It is ecosystem
evidence, not a dependency candidate.

### tw2x (tailwind2xaml)

**Verified.** [NuGet `tw2x` 1.0.3](https://www.nuget.org/packages/tw2x/1.0.3)
is a .NET tool that converts Tailwind theme colors to XAML and documents an
Avalonia target. It generates resources (especially colors), not `Classes`
utilities, selectors, states, or runtime class matching. The package reports no
dependencies. The retrieved package evidence did not establish a clear license,
so it is **not** a dependency recommendation.

**Inference.** Its color-resource conversion is a possible behavior reference
for Shadcn OKLCH, but Lucent's existing compile-time color path and Plan 009's
dependency gate are safer.

### Official Avalonia styling

**Verified.** Avalonia's official [Styles documentation](https://docs.avaloniaui.net/docs/styling/styles)
defines styles in `Styles` collections, scoped on controls/windows or globally
on `Application.Styles`. [Selector syntax](https://docs.avaloniaui.net/docs/styling/style-selector-syntax)
supports type, class, name, descendant/direct-child, negation, derived-type,
pseudo-class, `:nth-child`, and template selectors. [Style classes](https://docs.avaloniaui.net/docs/styling/style-classes)
are whitespace-delimited labels assigned through `Classes`, including bound
conditional classes. [Themes](https://docs.avaloniaui.net/docs/styling/themes)
and `DynamicResource` are the supported theme/resource model. This is
CSS-shaped matching, but it is not browser CSS and has no Tailwind utility
catalog.

## 2. Official support and boundary

**Verified.** Tailwind's official docs describe a compiler that scans source
for classes and generates browser CSS ([utility classes](https://tailwindcss.com/docs/styling-with-utility-classes),
[class detection](https://tailwindcss.com/docs/detecting-classes-in-source-files)).
Its official repository identifies Tailwind CSS as MIT licensed
([license](https://github.com/tailwindlabs/tailwindcss/blob/main/LICENSE)); the
repository page currently reports release `v4.3.3`. Tailwind documentation
does not define Avalonia output, Avalonia selectors, AXAML, or Avalonia
resources. Avalonia's official docs define the native styling model above;
they do not claim Tailwind support.

**Inference.** Tailwind can be an input vocabulary/reference, not a runtime or
build dependency. Lucent must not imply that Tailwind configuration, generated
browser CSS, or Tailwind IntelliSense is compatible with Avalonia.

## 3. Technical mapping for Lucent

| Tailwind concept | Avalonia-compatible lowering | Completion/catalog implication |
|---|---|---|
| `p-4`, `m-2`, `gap-2` | A class selector such as `.p-4` with typed setters (`Margin`, `Padding`, or a documented layout property). `gap` only maps where the chosen Avalonia panel/property actually supports it. Per-side/axis utilities cannot compose correctly as ordinary setters because Avalonia stores each margin/padding as one `Thickness`; either defer them or add a small reviewed side-composition primitive. | Immutable entries include name, applicable type, origin, definition, and detail, matching Plan 009's `StyleClassEntry`. |
| `text-sm`, `font-bold`, `text-center` | `FontSize`, `FontWeight`, and `TextAlignment` setters, generally typed to `TextBlock`/content controls. | Rank typed entries ahead of untyped entries; do not promise browser inheritance where Avalonia does not inherit the property. |
| `bg-*`, `text-*`, `border-*`, ring tokens | `Background`, `Foreground`, `BorderBrush`, `BorderThickness`, and focus-ring styles, using namespaced `Shadcn.*` resources and `DynamicResource` for theme changes. | Theme manifest entries are versioned, reviewed, and independent of compiler/runtime assemblies. |
| `rounded-*`, `shadow-*`, opacity | Avalonia `CornerRadius`, `BoxShadow`/supported shadow seam, and `Opacity` only on controls/properties where semantics are acceptable. | Unsupported property/type combinations are omitted or diagnosed at compile time. |
| `hover:`, `focus:`, `disabled:`, `checked:`, `selected:` | Compound selectors such as a literal utility class plus `:pointerover`, `:focus-visible`, or `:disabled`; Avalonia documents these pseudo-classes and their control ownership. Exact Tailwind names such as `hover:bg-primary` additionally require escaped-colon class parsing/generation, which Lucent does not currently support. | Variant tokens are finite catalog entries. Do not fabricate states a control does not expose. |
| `dark:` | `ThemeVariant`/theme dictionaries plus `DynamicResource`, or a reviewed theme class only if application state explicitly supplies it. | Shadcn light/dark resources remain ordinary Avalonia and compiler-independent, per `docs/SHADCN_THEME.md`. |
| `md:`/responsive utilities | A bounded Avalonia `ContainerQuery`/style resource mapping, only as a separate opt-in feature; not a viewport/media-query translation. Official [responsive layout docs](https://docs.avaloniaui.net/docs/layout/responsive-layouts) describe container queries and other native approaches. | Exclude from smallest subset; a future catalog needs explicit container ownership and deterministic semantics. |
| arbitrary values (`w-[117px]`) | No direct general mapping. A future typed parser could allow a very small allowlist, but must lower to validated Avalonia values at compile time. | Do not generate unbounded metadata or accept arbitrary expressions. |

The compiler owns parsing, validation, lowering, diagnostics, and catalog
creation. LSP consumes immutable snapshots only; completion performs no file
I/O, MSBuild evaluation, assembly loading, or network access. Plan 009a's
`LucentStyle` install is explicit through `Application.Styles`; Plan 012
packages only the generated/proven surfaces. This preserves the requested
seams and avoids a second parser or executable theme discovery.

## 4. Smallest viable subset and estimate

**Inference: smallest useful release (M, 15--25 engineer-days).** Assumptions:
Plan 009 already supplies the typed CSS frontend, class-catalog contract,
immutable LSP cache, Shadcn theme project, and tests; Avalonia's public APIs
are sufficient; one engineer owns implementation and one reviewer performs
gallery/accessibility/package review.

1. **Finite vocabulary and generator (3--5 days):** utility specification,
   canonical rule order, spacing/sizing/typography/semantic-color/border/radius
   entries, exact applicable control types, and generated styles/catalog.
2. **Avalonia semantic gaps (4--7 days):** decide whether v1 excludes per-side
   spacing or adds a small side-composition primitive; prove conflicts and class
   order; add escaped-colon support if exact `hover:*` names are accepted.
3. **States and completion/package integration (3--5 days):** finite supported
   pseudo-class variants, Shadcn dynamic resources, Plan 009 metadata, Plan 009a
   explicit library installation, and no request-path I/O.
4. **Evidence and package hardening (5--8 days):** utility gallery,
accessibility/applicability/precedence tests, package-consumer fixture,
documentation, and Plan 012 packaging. Existing Plan 009/009a tests support
   the lower bound; a runtime side composer or template exceptions push toward
   25 days.

A deliberately smaller **v0** that permits only unprefixed, whole-property
utilities (`p-*`, `m-*`, fixed `w-*`/`h-*`, typography, semantic colors,
border/radius, opacity/visibility) is roughly **5--10 engineer-days** after Plans
009/009a. It is useful as a feasibility probe but should not be marketed as a
Tailwind-compatible subset.

An AKCSS-like broader engine with composable per-side values, reactive
breakpoints, dark/state variants, property-level conflict arbitration, and a
larger utility inventory is approximately **30--50 engineer-days (L/XL)** after
Plans 009/009a, before long-term compatibility maintenance. Full Tailwind parity
is not usefully bounded: browser flex/grid, media/container queries, arbitrary
values/variants, plugins, preflight, transforms/filters, and pseudo-elements
would either remain false friends or require unrelated runtime/layout systems.

Recommended v1 classes should cover whole-property spacing/dimensions, text
appearance/alignment, background/foreground/border/radius, opacity/visibility,
and a finite set of real Avalonia state variants. Per-side spacing belongs only
if its composition primitive is accepted. Explicitly exclude grid/flex behavior
that has no one-property Avalonia equivalent, arbitrary values, responsive
prefixes, container/viewport variants, custom selector variants,
animation/transition utilities, filters/transforms, browser reset/preflight,
pseudo-elements, and JavaScript/plugin-generated utilities.

## 5. Key incompatibilities and risks

* **Browser layout is not Avalonia layout.** Tailwind utilities target CSS box,
  flex, grid, intrinsic sizing, and browser inheritance. Avalonia has panels,
  logical/visual trees, DIP measurement, control themes, and styled properties;
  equal names do not guarantee equal measurement or rendering.
* **Responsive/media/container variants differ.** Tailwind's [responsive design](https://tailwindcss.com/docs/responsive-design)
  is mobile-first viewport breakpoints. Avalonia's container queries are
  ancestor-container based and have different lifecycle/scope semantics. A
  blind `md:` translation would be incorrect.
* **Arbitrary values and class explosion.** Tailwind explicitly supports
  arbitrary values and variants ([custom styles](https://tailwindcss.com/docs/adding-custom-styles));
  compiling every possible value produces unbounded styles/catalog entries.
  Static finite manifests and compile-time validation are required.
* **Specificity and order are different.** Avalonia resolves selector
  activators and style priority; its docs advise general-before-specific and
  later declaration behavior. Utility ordering cannot simply copy generated
  browser CSS ordering. Plan 009a must prove adjacent-over-global precedence
  against public Avalonia behavior rather than assume it.
* **Atomic CSS properties do not imply atomic Avalonia fields.** Tailwind's
  `px-*`, `pt-*`, and `pb-*` compose because browsers own separate logical/side
  declarations. Avalonia exposes one `Thickness` value. Ordinary utility styles
  overwrite each other unless Lucent omits side utilities or owns a bounded
  side-composition mechanism; AKCSS confirms this is real implementation work.
* **Tailwind variant spelling is not currently a Lucent selector identifier.**
  Lucent's selector grammar accepts letters, digits, `_`, and `-` in class
  names; it interprets `:` as a pseudo-class delimiter. Exact tokens such as
  `hover:bg-primary` need escaped class-name syntax and tests through Lucent,
  generated Avalonia selectors, `Classes`, and completion replacement ranges.
* **Pseudo-classes are control-defined.** Tailwind states are browser pseudo
  classes and selectors. Avalonia exposes control-specific pseudo-classes such
  as `:pointerover`, `:pressed`, `:focus-visible`, `:checked`, and `:error`;
  templates may own the actual visual element. Some utilities therefore need
  typed selectors or a reviewed template exception.
* **Templates are a hard boundary.** Styling a `Button` property is not the
  same as styling its template's presenter, chrome, or popup. Avalonia supports
  `/template/` selectors, but relying on internal parts is version-sensitive.
  Do not promise full Tailwind component parity.
* **Dark mode is not a browser class by default.** Avalonia theme variants and
  resource dictionaries are the native mechanism. A `dark` class would only
  work if the application deliberately supplies and maintains that class.
* **Runtime scanning is unsafe and unnecessary.** Tailwind's source scanning
  is a build-time web workflow and misses dynamically assembled class strings
  ([detection limitations](https://tailwindcss.com/docs/detecting-classes-in-source-files)).
  Lucent should compile known CSS and explicit global style inputs; never scan
  or parse CSS on the runtime/request path.

## 6. Recommendation

**Adapt the model; build the small subset; decline a Tailwind port.** Reuse
Avalonia's public `Styles`, selectors, classes, pseudo-classes, themes, and
resources. Adapt Tailwind's ergonomic finite vocabulary and variant naming only
where a one-to-one Avalonia property/state mapping is verified. Build it inside
Lucent's existing compiler/catalog seam, ship a separate opt-in package, and
keep `Lucent.Themes.Shadcn` ordinary Avalonia as Plans 009/009a require. Do not
take dependencies on Akbura/AKCSS, Avalonia.Acss, Nlnet.Avalonia.Css,
Flowery.NET, or tw2x without a new review of version compatibility, license
metadata, maintenance evidence, and architectural fit.

The package should use a name such as `Lucent.Styles.Utilities` until the exact
compatibility surface and any Tailwind naming/trademark implications are
reviewed. Installation can reuse Plan 009a's explicit generated/library style
seam; completion can reuse Plan 009 metadata. Do not run Tailwind, scan source at
runtime, or accept `tailwind.config.*` as though browser configuration were
portable.

### Roadmap placement

**Inference.** Do not expand the reviewed Plan 009 or 009a scopes again. If the
v0 gallery proves useful, add a bounded Plan 009b after 009a:

1. Plan 009 supplies Shadcn semantic resources, theme-aware class metadata, and
   literal `Class:` completion.
2. Plan 009a supplies explicit app/library global-style installation and
   metadata-only completion.
3. Plan 009b generates and tests the finite utility package, including any
   accepted escaped-name or side-composition primitive.
4. Plan 012 packages it only after the utility gallery and consumer fixture
   pass; the default template should not install it unless utility-first styling
   becomes an accepted product default.

This keeps an optional authoring vocabulary out of the compiler/theme critical
path and gives the package an independent removal/versioning boundary.

## Searches and sources consulted

Searches: `Avalonia Tailwind CSS utility classes Avalonia`; `Avalonia Tailwind
package`; `Avalonia CSS styling Tailwind`; official Avalonia styling/selectors/
responsive documentation; official Tailwind utility/state/responsive/class
detection documentation; and repository/package searches for Avalonia.Acss,
Akbura/AKCSS, Nlnet.Avalonia.Css, Flowery.NET, and tw2x.

Kept primary sources: [Avalonia styles](https://docs.avaloniaui.net/docs/styling/styles),
[Avalonia selectors](https://docs.avaloniaui.net/docs/styling/style-selector-syntax),
[Avalonia classes](https://docs.avaloniaui.net/docs/styling/style-classes),
[Avalonia themes](https://docs.avaloniaui.net/docs/styling/themes),
[Avalonia responsive layouts](https://docs.avaloniaui.net/docs/layout/responsive-layouts),
[Tailwind utility classes](https://tailwindcss.com/docs/styling-with-utility-classes),
[Tailwind states](https://tailwindcss.com/docs/hover-focus-and-other-states),
[Tailwind class detection](https://tailwindcss.com/docs/detecting-classes-in-source-files),
[Tailwind MIT license](https://github.com/tailwindlabs/tailwindcss/blob/main/LICENSE),
[Akbura](https://github.com/Asaicraft/Akbura),
[AKCSS utilities](https://asaicraft.github.io/Akbura/akcss/utilities),
[AKCSS variants](https://asaicraft.github.io/Akbura/akcss/utility-variants),
[AKCSS built-in variants](https://asaicraft.github.io/Akbura/akcss/built-in-utility-variants),
[AKCSS spacing](https://asaicraft.github.io/Akbura/akcss/spacing),
[Avalonia.Acss](https://github.com/TheExiledCat/Avalonia.Acss),
[Nlnet.Avalonia.Css](https://github.com/liwuqingxin/Avalonia.Css),
[Flowery.NET](https://github.com/tobitege/Flowery.NET), its [license](https://github.com/tobitege/Flowery.NET/blob/avalonia12/LICENSE),
[changelog](https://github.com/tobitege/Flowery.NET/blob/avalonia12/CHANGELOG.md),
and [tw2x NuGet](https://www.nuget.org/packages/tw2x/1.0.3). Search-result
summaries and general blog/commentary were dropped.

## Gaps, risks, and approval decisions

The public evidence does not establish a maintained Avalonia utility package,
complete Avalonia property coverage, or a clear permissive license for tw2x.
Akbura is active and technically relevant, but remains explicitly experimental,
has no GitHub releases, and its MIT notice contains unresolved attribution
placeholders. Exact repository/package status can change; recheck identities at
implementation time. Before implementation, approval is needed for the finite
class vocabulary, exact variant spelling, supported Avalonia major version,
whether per-side composition is worth a runtime primitive, and the accepted
precedence/template tests. No new dependency is recommended by this note.
