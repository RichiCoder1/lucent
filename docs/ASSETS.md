# Packaged assets

`Lucent.Lui.Sdk` generates typed references and embeds explicitly declared artwork and binary content. This is the packaged-asset foundation from [#145](https://github.com/RichiCoder1/lucent/issues/145). The [accepted design](https://github.com/RichiCoder1/lucent/issues/144#issuecomment-5592602334) also includes Image/Icon rendering, SVG preparation, stock controls and window artwork; those later slices are not implied by a generated asset reference.

## Declare content

In a project using `Lucent.Lui.Sdk`, opt in each asset explicitly:

```xml
<ItemGroup>
  <LucentAsset Include="Artwork\brand.png" Path="images/brand.png" />
  <LucentAsset Include="Artwork\empty-inbox.svg" Path="images/empty-inbox.svg" />
  <LucentAsset Include="Data\example.bin" Path="data/example.bin" Kind="Binary" />
</ItemGroup>
```

`Include` locates build input. `Path` is its stable logical identity inside the declaring library's catalog. Path defaults to the project-relative input path; exported library assets should specify it explicitly so moving source files does not change their public identity. Separators normalize to `/`; identities remain ordinal and case-sensitive. Traversal, absolute paths, ambiguous paths and case-fold collisions are errors.

Image declarations infer PNG, JPEG or SVG from content and expose an `ImageSource`. Binary declarations expose an `AssetReference`. Describing an image does not open its bytes, decode it or allocate a native resource. Intrinsic metadata is available before preparation.

For raster artwork, optional `Density="2"` describes two encoded pixels per logical unit. Dimensions account for EXIF orientation before dividing by that density; embedded print-resolution tags do not change layout. SVG metadata uses its root dimension attributes or `viewBox`, with a 300-DIP desired width and proportional height for viewBox-only artwork. Percentage axes remain viewport-relative metadata, separate from the finite desired size used during layout. Unsupported or invalid dimension values produce a build diagnostic. Metadata extraction does not decode pixels or certify the full SVG body; SVG preparation owns the supported styling and rendering policy.

## Use typed accessors

The examples generate `Assets.Images.Brand`, `Assets.Images.EmptyInbox` and `Assets.Data.Example` under the project's `RootNamespace`. Path segments become nested PascalCase names and the final extension is removed. An explicit `Accessor="Brand"` or `Accessor="Logos.Brand"` overrides that member path. Ambiguous member/type names are diagnosed rather than assigned numeric suffixes.

```xml
<PropertyGroup>
  <LucentAssetAccessorNamespace>Example.App.Resources</LucentAssetAccessorNamespace>
  <LucentAssetAccessorClass>Artwork</LucentAssetAccessorClass>
</PropertyGroup>
<ItemGroup>
  <LucentAsset Include="Artwork\brand.png" Path="images/brand.png"
               Accessor="Brand" AccessorNamespace="Example.Branding" />
</ItemGroup>
```

That declaration produces `Example.Branding.Artwork.Brand`. Namespace and accessor choices are independent of domain/path identity. A per-item namespace override does not change the project-configured root class.

The catalog domain defaults to the final `AssemblyName`; set `LucentAssetDomain` when a library needs an identity independent of its assembly name. Domain/path collisions with different content are build errors, including between referenced libraries.

Accessors are ordinary C# symbols in both C# and `.lui`; there is no URI parser or magic string conversion. Their generated source participates in semantic compilation. For example, a `.lui` expression can read `Assets.Images.Brand.Metadata.Width` without loading the image.

## Ownership and packaging

Each retained provider packages all its declared payloads in its assembly. Project and package references use the declaring library's generated accessors; applications do not reconstruct assembly names or copy loose artwork directories. No per-use payload trimming is promised.

An asset reference describes a SHA-256 revision, encoded byte length and format. Its explicit `OpenRead()` call transfers a fresh stream to the caller, which must dispose it. This is IO: it does not belong in rendering or measurement. Custom providers must return the declared content; `OpenRead()` checks readable/seekable stream contracts but does not hash or decode the bytes. Image preparation is responsible for content verification and byte/decode budgets.

Generated inventory under the project's intermediate `lucent-assets` directory records domain/path, hashes, sizes, formats and provenance. It supports build/package diagnostics without runtime assembly scanning. Missing declared files fail the build. A changed source, removed declaration or changed generator must invalidate the corresponding generated output.

SVG adapter policy and compatibility are tracked by [#147](https://github.com/RichiCoder1/lucent/issues/147). Declaring SVG content does not certify browser SVG conformance or enable external file/network loading.

## Image and Icon

Image and Icon share PNG/JPEG and static SVG preparation and rendering ([#146](https://github.com/RichiCoder1/lucent/issues/146)):

```csharp
public component Artwork() {
    <Column>
        <Image source={Assets.Images.Brand} alternativeText="Application logo" />
        <Icon source={Assets.Images.Brand} />
    </Column>
}

style Thumbnail {
    Width: 80;
    Height: 80;
    Fit: ImageFit.Cover;
    ColorMode: ImageColorMode.Source;
    ImageZoom: 1;
}
```

Image preserves source colors and requires a nonempty `alternativeText` or `decorative={true}`. Icon uses the same source and loader, defaults to a centered 16-DIP box and monochrome inherited `TextColor`, and is decorative unless given a nonempty `label`. A meaningful image has one noninteractive Image semantic node; it does not create a focus target. Both components accept typed source/name readers. Static invalid accessibility intent is diagnosed as `LUI2022`; dynamic values are checked when read.

## Optional Lucide pack and stock buttons

Reference `Lucent.Icons.Lucide` when an application wants the finite stock icon set. The package contains typed embedded `ImageSource` values such as `LucideIcons.Search`, `LucideIcons.RefreshCw`, `LucideIcons.NotebookPen` and `LucideIcons.Trash`. It pins Lucide 1.43.0 at commit `ba95e4c988b1e1b39cf5544e73b25a74b76816ee`; its packaged `lucide-icons.json` records every included icon and hash. Applications receive the selected SVG bytes and require no Node installation or upstream importer. Adding the package includes the complete selected set; per-use payload trimming is not promised.

Maintainers update the selection with `src/Lucent.Icons.Lucide/tools/Import-Lucide.ps1`. The importer accepts only a Lucide Git checkout whose `HEAD` exactly matches `lucide-selection.json`, validates canonical 24px `currentColor` artwork, removes no unselected files implicitly, and regenerates typed accessors plus the hashed package inventory.

Use a leading icon on a text button or a labeled icon-only button from C# or `.lui`:

```csharp
Components.Button("Refresh", LucideIcons.RefreshCw, Refresh);
Components.IconButton(LucideIcons.Ellipsis, "More actions", ShowMenu);
```

```lui
<Button leadingIcon={LucideIcons.ArrowLeft} onInvoke={GoBack}>Back</Button>
<IconButton source={LucideIcons.Ellipsis} label="More actions" onInvoke={ShowMenu} />
```

The leading glyph is decorative and shares its button's label, focus target, enabled state and invocation. `IconButton` requires a nonempty label and exposes the root as the single button semantic node. Both use inherited `TextColor` through the ordinary monochrome Icon/Image pipeline.

`Fit` supports Contain, Cover, Fill and None. Layout dimensions, padding, clipping, corner radius and opacity use the ordinary style properties. Decode resolution follows the final arranged box, scale and zoom in buckets; intrinsic desired size remains independent from decoded pixels. Replacing content clears unrelated old artwork immediately. A larger rendition of the same content may keep the smaller ready image until replacement. Loading and expected failure keep the box with a fixed neutral placeholder. There is no automatic retry, crossfade, frame timer or network access.

Windows hosts and the Skia headless harness configure the shared Skia preparer. Lower-level consumers install `composition.ConfigureImages(new ImageCache(preparer, limits))` before mounting images. The composition owns this application cache; popup compositions borrow it. `IImagePreparer` isolates preparation from the renderer; the built-in adapter supports rasters and the Secure Static SVG subset described below.

For custom loading/error composition, `ImageCache.Acquire(scope, source, rendition)` returns a read-only load handle with status, error, owner-thread `Changed`, explicit `Retry()` and `AcquireLease()`. Scope disposal releases that consumer; other consumers continue. Preload reports Ready, Failed, Canceled or BudgetDeclined and does not guarantee retention. A held lease is distinct from preload. Ready cache hits attach synchronously; cold reads and codec work run on bounded workers and publish through the owner queue.

Prepared raster data is premultiplied sRGB RGBA with orientation applied once. Native renderer handles remain in the adapter. `RetainedScene` and `HeadlessSnapshot` now own image leases and must be disposed when released; `scene.Retain()` creates an independently owned snapshot. Removing a component or disposing its cache cannot free pixels still held by a retained frame. Cache limits bound source/encoded/output size, work concurrency and retained/temporary allocations; expected corrupt data and budget exhaustion become explicit load errors. Unexpected provider/adapter exceptions retain the application's fatal-error behavior.

Defaults allow four active preparations and 128 queued requests, with separate 128 MiB budgets for unleased cached data, live leased data and temporary preparation reservations. Encoded sources and individual prepared outputs have 64 MiB limits. The Skia renderer separately retains at most 256 native raster copies within 32 MiB; a larger single copy uses a bounded 64 MiB transient path. Large sources can be declined even for a small requested thumbnail because codec working memory depends on source dimensions. These are resource-accounting limits, not a promise that total process memory equals the pixel counters.

JPEG admission recognizes standard three-component 4:4:4, 4:2:2 and 4:2:0 headers. Interleaved baseline input uses the pinned codec's strip-memory estimate; progressive input also reserves full-image coefficient memory. Sequential multi-scan, CMYK/YCCK, malformed and unrecognized headers retain the conservative eight-bytes-per-source-pixel fallback. Decode intermediates and fixed codec overhead are charged separately. See [memory evidence and reproduction](ASSET-MEMORY-EVIDENCE.md) for estimates, measurements and their limits.

## Secure Static SVG

`SkiaImagePreparer` also prepares static SVG with Svg.Skia **5.2.3**, using the same `ImageSource`, cache, Image and Icon surfaces. It selects SecureStatic/SameDocumentAndDataOnly explicitly after validating the exact hashed bytes. No global parser flags are changed. Each preparation owns its parser and native picture; draws/disposal are serialized per prepared vector. No animation controller or recurring frame work is installed.

The initial finite support matrix includes shapes, paths, groups, symbols/use, gradients, clips, masks, simple static type/class/id styles, and Gaussian blur/offset/color-matrix/blend/composite/flood/merge filters. Same-document references must exist, have the appropriate resource type, and be acyclic. File/network resources, entities/DTDs, imported CSS, scripts, animation, foreignObject, nested SVG viewports, nested SVG data, unknown elements/properties and unsupported context-paint filter inputs fail with `ImageLoadException`; they cannot silently disappear from an otherwise valid image. This is a bounded static subset, not browser SVG conformance. Only base64 PNG/JPEG embedded images are accepted, and their actual decode is checked.

Root dimensions use the generated logical metadata, including absolute units and percentage axes. A viewBox-only source uses the catalog's 300-DIP default width and aspect ratio. The prepared picture is independent of the first raster bucket; viewport-relative layout and final scaling remain the Image component's responsibility. Source mode preserves authored colors and defaults inherited `currentColor` to black when the root does not specify a color. Monochrome uses the picture's alpha coverage and inherited `TextColor` without reparsing.

SVG text requires explicit font bytes: construct `new SkiaImagePreparer(fontBytes)` and use `font-family="Lucent SVG"` with normal weight/style. The preparer copies the bytes (maximum 8 MiB), exposes their SHA-256 as `SvgFontIdentity`, and performs no system-font discovery. Outlined artwork needs no font configuration. The selected font and processing policy are immutable within the preparer/cache environment; caches are never shared between different preparers. `IImagePreparer.GetCacheRendition` permits this adapter to reuse one vector across scale changes while raster adapters keep size-specific output. Font bytes and document state are never process-global mutable settings.

Additional limits: 2 MiB encoded SVG; depth 32; 4,096 XML elements; 64 attributes per element; 64 KiB per ordinary attribute; 256 KiB aggregate path data; 16,384 expanded elements; 32 declared/64 expanded filter primitives; 4,096 logical units per intrinsic axis; blur deviation at most 64; numeric magnitude at most 1,000,000; percentage dimensions up to 200%; embedded raster input up to approximately 1 MiB each and 4 megapixels after reference expansion. DOM, path, filter and raster costs are charged to the existing temporary/output/cache/lease budgets before third-party preparation. Cost estimates are conservative accounting, not exact native process-memory ceilings. Filters retain their intrinsic prepared representation; this does not promise browser-quality filter resampling at arbitrary magnification.

Renderer contracts cover positive pixels, pinned-font text, gradients/masks/clips/filters, source/tint at 100/150/200%, vector cache/lease ownership, and negative resource/XML/budget fixtures. The font fixture is Abel Regular, OFL-1.1, pinned to Google Fonts commit `3b99d83d2625944fc0b8bd328d793fa819b92381`, SHA-256 `8809dcad25318225052f88333e208c5aad4adcb7b2c934c135735ec19aa410b4`; its license ships with the test fixture. Package/NativeAOT results and exact source identity are recorded in #147.


The first filter adapter also rejects transforms combined with filters and filter/use expansion. It conservatively bounds untransformed filter geometry and its working envelope; unsupported combinations are explicit errors instead of potentially unbounded intermediate surfaces. Filter inputs must name SourceGraphic, SourceAlpha, or a previously declared result. The pinned text font must contain every required visible glyph; missing glyphs do not fall back to installed fonts.

## Windows application artwork

An executable can declare one default application artwork source. The item must be an image and
the project must have `OutputType` `Exe` or `WinExe`; a referenced library cannot register an
application default, and multiple defaults in one executable are errors:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
</PropertyGroup>
<ItemGroup>
  <LucentAsset Include="Artwork\application.svg"
               Path="application/application.svg"
               ApplicationIcon="true" />
</ItemGroup>
```

Without `IconFile`, the SDK uses the shared Skia artwork policy to generate PNG-backed ICO
entries at 16, 24, 32, 48, 64, 128 and 256 pixels. It also packages those finite PNG renditions
and emits a static `ApplicationIconDefault` registration, so the Windows host can install the
same rendered pixels before the first visible presentation. The generated `application.ico`
becomes the .NET application icon input and is retained in the final NativeAOT apphost resources.

An authored ICO may provide optical renditions beside the source:

```xml
<LucentAsset Include="Artwork\application.svg"
             Path="application/application.svg"
             ApplicationIcon="true"
             IconFile="Artwork\application-optical.ico" />
```

`IconFile` is copied byte-for-byte to the generated apphost ICO after every directory entry is
decoded and checked. Entries must be square, unique in size, non-overlapping and complete. PNG
entries and uncompressed 32-bit DIB entries are supported; malformed, truncated or unsupported
entries fail the build. Decoded optical renditions are packaged as PNG `ImageSource` values for
the runtime default, while the supplied ICO remains the executable's exact optical resource.

`WindowsWindowOptions.Icon` supplies a per-window `ImageSource` override. When it is omitted,
the current executable's generated default is used. Source preparation runs on a bounded worker;
SDL surface creation, alternate-image registration and `SDL_SetWindowIcon` remain on the window
owner thread. A configured icon keeps the SDL window hidden until preparation and installation
finish. An expected image failure reports the error and continues without an icon; a close or quit
while the hidden startup work is pending cancels the preparation and tears down its resources
without a late window mutation. Installed SDL surfaces and alternates live through the window's
process lifetime and are released during host teardown.
