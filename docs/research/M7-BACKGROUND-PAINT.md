# Research: M7 background paint (primary-source brief)

**Scope/date.** Read-only comparison of the documented public contracts, consulted 2026-08-29. “Fact” paragraphs report source behavior; “Recommendation” is Lucent design judgment. Current Lucent has `SceneProperties.Fill : Property<uint>` (`src/Lucent.Core/LayoutScene.cs:30`), emits one rectangular `PaintSceneNode` before text/children (`:268`), and `Arrangement.Clip` wraps the resulting subtree (`:280`); no border, radius, opacity, image, or gradient property exists.

ADR 0002 is the subsequent product decision. Because M7 explicitly ships both solid and linear-gradient paint, it adopts the concrete immutable union recommended below under the public name `Brush` and replaces `Fill` with `Background` during the unreleased API window.

## Summary
Background is not one portable concept. CSS and Flutter explicitly model ordered decoration layers; Avalonia/WPF/Compose/GPUI/Slint mostly expose one brush/fill for a box (with other modifiers/properties supplying additional decoration); QML separates color and gradient. All paint examples are box-local and do not participate in layout measurement. Subtree opacity and child clipping are separate concerns everywhere, although defaults differ.

**Recommendation:** keep `Fill` as the honest solid-color contract for this milestone. When gradients are actually shipped, replace it in the core scene model with a small immutable `Paint` value containing only `Solid(uint)` and `LinearGradient(stops, direction)` (or add that typed property during the unreleased compatibility window), and emit one paint node. Do not add image, arbitrary layer arrays, border/radius, or group opacity until each has renderer and scene semantics. Make clip and subtree opacity separate properties; neither is implied by a background paint.

## Findings

1. **CSS — layered background decoration.** `background-image` is a comma-separated list of images, with first listed layer on top; gradients are `<image>` values. Background color is beneath images, borders are painted above, and `background-clip`/`background-origin` control relation to border/padding/content boxes. A missing image falls back to the color. [MDN background-image](https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Properties/background-image) [MDN background-clip](https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Properties/background-clip). `border-radius` clips the background, not necessarily descendants; `opacity < 1` composites the entire element and descendants as a stacking context, while changing neither layout nor allocated box. [MDN opacity](https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Properties/opacity) [MDN border-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Properties/border-radius). Browser allocation, raster caching, and invalidation are implementation details, not a CSS contract. **Implication:** do not copy CSS's shorthand or unbounded layer list into core.

2. **Avalonia/WPF — brush on a box, decoration elsewhere.** Avalonia `Panel.Background` and `Border.Background` take `IBrush`; brushes include solid, linear/radial/conic gradients, and `ImageBrush`; `BorderBrush`, `BorderThickness`, and `CornerRadius` are separate. [Avalonia brushes](https://docs.avaloniaui.net/docs/graphics-animation/brushes) [Avalonia Border.Background](https://api-docs.avaloniaui.net/docs/P_Avalonia_Controls_Border_Background). WPF `Panel.Background` is a `Brush` and does not affect layout; WPF `UIElement.Opacity` affects descendants (nested values multiply), while brush alpha affects only that brush. [WPF Panel.Background](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.panel.background?view=windowsdesktop-10.0) [WPF UIElement.Opacity](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.opacity?view=windowsdesktop-10.0). Rounded/background clipping and child clipping are distinct control behavior. Brush/resource sharing is the normal allocation strategy, but exact caching is renderer/property-system behavior, not a portable guarantee.

3. **Jetpack Compose — ordered modifier decoration, one brush operation.** `Modifier.background(brush, shape, alpha)` draws one shape behind content; overloads support solid `Color` and `Brush`, with built-in linear/radial/sweep gradients. [Compose background API](https://developer.android.com/reference/kotlin/androidx/compose/foundation/background.modifier). A separate `graphicsLayer` applies opacity to the whole layer; default `CompositingStrategy.Auto` may allocate an offscreen buffer for alpha, while `ModulateAlpha` can avoid it when content does not overlap. [graphicsLayer](https://developer.android.com/reference/kotlin/androidx/compose/ui/graphics/graphicsLayer.modifier) [CompositingStrategy](https://developer.android.com/reference/kotlin/androidx/compose/ui/graphics/layer/CompositingStrategy). Shape clips the background operation; clipping descendants requires a clipping/drawing operation, not merely background. Modifiers do not change measurement unless a layout modifier is used. **Implication:** a brush plus explicit clip/group-opacity maps better than a CSS-like background object.

4. **Flutter — explicit immutable `Decoration` with fixed body order.** `BoxDecoration` is an immutable description: body paints bottom-to-top as color, gradient, image; border is above, shadow below. It supports rectangular/circular shape and border radius. [BoxDecoration](https://api.flutter.dev/flutter/painting/BoxDecoration-class.html). Decoration shape/radius does **not** clip the child; explicit `ClipRRect`/`ClipPath` is required and may cost performance. `BoxDecoration.isComplex` reports whether caching its painting may help; `createBoxPainter` creates a painter and `==`/`hashCode`/`lerp` support reuse and animation. [BoxDecoration.isComplex](https://api.flutter.dev/flutter/painting/BoxDecoration/isComplex.html) [DecorationImage](https://api.flutter.dev/flutter/painting/DecorationImage-class.html). `Opacity` composites a subtree for intermediate alpha and is relatively expensive. [Flutter Opacity](https://api.flutter.dev/flutter/widgets/Opacity-class.html). Decoration has no layout effect except `padding`/border insets when a layout widget elects to honor them.

5. **GPUI — `Background` is deliberately narrow, with gradients.** Current docs define `Background` as solid color or linear gradient; `.bg(...)` accepts it through `Into<Fill>`, and `Style::opacity` is separate from background alpha. [GPUI Background](https://docs.rs/gpui/latest/gpui/struct.Background.html) [GPUI Style](https://docs.rs/gpui/latest/gpui/struct.Style.html). Rounded quads can carry corner radius and border parameters in low-level `quad`; gradients are evaluated by the WGPU shader. [GPUI gradient example](https://github.com/zed-industries/zed/blob/main/crates/gpui/examples/gradient.rs) [GPUI shader](https://github.com/zed-industries/zed/blob/main/crates/gpui_wgpu/src/shaders.wgsl). This is a paint-phase value, not layout input. Retained elements/styles and GPU quad batching are implementation mechanisms; callers should not assume allocation or cache lifetime.

6. **QML — separate color/gradient plus explicit clip/layer.** Qt Quick `Rectangle.color` is a color and `Rectangle.gradient` is a separate `Gradient`; when both are set, gradient wins. [Qt Rectangle](https://doc.qt.io/qt-6.5/qml-qtquick-rectangle.html). `Item.clip` defaults false and clips own and child drawing only when enabled. Parent opacity applies individually to children and can cause overlap artifacts; `layer.enabled` provides a composited subtree when needed. [Qt Item 6.11.2](https://doc.qt.io/qt-6/qml-qtquick-item.html). Rectangle painting has no intrinsic layout impact; Qt may cache enabled layers, but cache memory/performance is explicitly a tradeoff.

7. **Slint — unified brush, explicit rectangle clip.** `Rectangle.background` is a `brush`, accepting a color or gradients (linear/radial/conic). [Slint Rectangle](https://docs.slint.dev/latest/docs/slint/reference/elements/rectangle/) [Slint colors/brushes](https://docs.slint.dev/latest/docs/slint/reference/colors-and-brushes/). `clip` defaults false; item `opacity` affects the item and child tree as a composited layer. [Slint common properties](https://docs.slint.dev/latest/docs/slint/reference/common/). Layouts use geometry/min/max/preferred sizing; background does not itself size an item. Compiler/renderer can choose storage and caching; no user-facing cache contract is promised.

## Cross-framework contract matrix

| System | Paint model | Solid/gradient/image | Shape/border/clip | Group opacity | Layout/cache signal |
|---|---|---|---|---|---|
| CSS | ordered, unbounded background-image layers + color | all; gradients are images | clip/origin + radius; border above; child overflow separate | whole element, stacking context | no layout effect; implementation-defined caching |
| Avalonia/WPF | one Brush property; border separate | solid/gradients/images | Border owns border/radius; child clip separate | element opacity vs brush alpha | no measure effect; resource/renderer caching |
| Compose | one background modifier per operation; modifiers order | solid/Brush gradients; image via other APIs | supplied Shape; clip separate | graphics layer; possible offscreen buffer | no measure effect; layer allocation explicit-ish |
| Flutter | immutable Decoration, fixed body layers | all three | decoration shape does not child-clip | Opacity buffer | no intrinsic layout; isComplex/BoxPainter cache hint |
| GPUI | one Background/Fill per style/quad | solid + linear gradient; images separate | quad radius/border params; clipping separate | style opacity separate | no layout effect; retained/GPU implementation |
| QML | Rectangle color or gradient property | solid/gradient; image item separate | clip false by default | layer needed for group compositing | no layout effect; layer cache tradeoff |
| Slint | one brush | solid + gradients; image separate element | clip false by default | item composited opacity | no layout effect; compiler/renderer-owned |

## Smallest honest Lucent contract

**Facts about current code:** `Fill` is a `uint`, defaults transparent, is resolved through the existing typed style mechanism, and produces at most one rectangle paint node. It currently cannot express alpha separately from color, gradient stops, image resource identity, border, radius, or group compositing.

**Recommendation (M7):**

- Keep `SceneProperties.Fill : Property<uint>` unchanged for the immediate solid path; it is honest and avoids a fake brush hierarchy.
- Define the future scene contract as **one box paint**, not “background layers”: solid plus a finite, validated linear gradient (ordered stops, finite positions/colors, deterministic direction). A concrete immutable value/union is sufficient; no interface, factory, or renderer callback.
- If gradients are implemented in M7, make a single typed `Paint` property replace/augment `Fill` only as an intentional unreleased API change. Preserve `Fill` only as a convenience setter if that costs no second resolution path; otherwise do not maintain aliases.
- Keep image, repeating/conic/radial gradients, blend modes, and layer arrays out until a real use case and renderer evidence exist. A future image should carry stable resource identity and fit/position semantics, not a filesystem path in core.
- Keep `Clip` (subtree overflow) and future `Opacity` (subtree compositing) separate from paint. Background shape clipping, child clipping, and border painting must be separately named and tested. Paint never contributes desired size/measurement.
- Cache by immutable paint/resource identity at the renderer boundary; do not expose cache objects or promise allocations. Gradient evaluation should be per draw or renderer-cached as appropriate; image decode/upload ownership belongs to the platform renderer.

## Version, commit, and license ledger (for `CREDITS.md`)

These are reference identities, not dependencies or copied code. Existing repository ledger already records the pinned commits for Avalonia, Compose, Flutter, GPUI, and Slint; retain those exact pins when adding this entry.

| Reference | Exact identity consulted | License/status |
|---|---|---|
| CSS Backgrounds & Borders | CSS Backgrounds and Borders Level 3 spec, [background-image](https://drafts.csswg.org/css-backgrounds/#background-image), consulted 2026-08-29; MDN pages modified 2026-07-21 where shown | W3C/CSSWG specification; MDN CC-BY-SA 2.5 (docs), code samples CC0 |
| Avalonia | Git commit `0442ba19098e6642185431c41c23f7138a270e0c` (repository ledger; Avalonia 12-era docs; prior implementation 12.1.1) | MIT |
| WPF | Windows desktop API view `windowsdesktop-10.0`, consulted 2026-08-29 | Microsoft documentation/platform contract; no source copied |
| Jetpack Compose | AndroidX commit `a3b352883a0709bc25f8217df1a526290f754d96` (repository ledger), API page last updated 2026-08-12 UTC | Apache-2.0 (AndroidX); docs under Android content license |
| Flutter | Flutter commit `53c174684f2fe66522393013f4b88518d7caa1ad` (repository ledger); API docs consulted 2026-08-29 | BSD-3-Clause |
| GPUI | Zed/GPUI commit `8166e3d7b8b42d8aaf4d4dee7fcd25ab4ec65105` (repository ledger); docs.rs latest page consulted 2026-08-29 | Apache-2.0 where file headers permit; verify before reuse |
| Qt Quick/QML | Qt documentation 6.11.2 (Item result), Qt 6.5.12 (Rectangle page), consulted 2026-08-29 | LGPL/GPL/commercial component terms; no dependency/source copied |
| Slint | commit `14c19d762af672fdc3934e4f490c1db97c20615f` (repository ledger); latest docs consulted 2026-08-29 | GPL-3.0/commercial component terms; no dependency/source copied |

## Gaps / risks

- Public API docs intentionally do not guarantee renderer allocation, batching, raster cache eviction, or GPU upload timing; Lucent must benchmark its chosen renderer rather than infer these from framework names.
- Exact current package versions of online “latest” docs are not always printed. The pinned repository identities above are the reproducibility anchors; record any newly selected implementation commit in `CREDITS.md` before adoption.
- No framework comparison establishes a universal image decoding, color-space, DPI, repeat, or failure policy. Those should remain out of M7’s solid/linear-gradient contract.

## Sources kept / dropped

**Kept:** official MDN/CSSWG, Microsoft Learn, Avalonia API/docs, Android Developers, Flutter API, docs.rs/official Zed source, Qt, and Slint links above because they directly specify API behavior. **Dropped:** blogs, tutorials, SEO summaries, and benchmark claims without primary implementation evidence; no such source is needed for the contract recommendation.
