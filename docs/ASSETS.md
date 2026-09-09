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

Image and Icon share PNG/JPEG preparation and rendering ([#146](https://github.com/RichiCoder1/lucent/issues/146)):

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

`Fit` supports Contain, Cover, Fill and None. Layout dimensions, padding, clipping, corner radius and opacity use the ordinary style properties. Decode resolution follows the final arranged box, scale and zoom in buckets; intrinsic desired size remains independent from decoded pixels. Replacing content clears unrelated old artwork immediately. A larger rendition of the same content may keep the smaller ready image until replacement. Loading and expected failure keep the box with a fixed neutral placeholder. There is no automatic retry, crossfade, frame timer or network access.

Windows hosts and the Skia headless harness configure the raster preparer. Lower-level consumers install `composition.ConfigureImages(new ImageCache(preparer, limits))` before mounting images. The composition owns this application cache; popup compositions borrow it. `IImagePreparer` isolates decoding from the renderer and leaves room for the static-vector adapter. SVG assets currently report UnsupportedFormat through the raster preparer.

For custom loading/error composition, `ImageCache.Acquire(scope, source, rendition)` returns a read-only load handle with status, error, owner-thread `Changed`, explicit `Retry()` and `AcquireLease()`. Scope disposal releases that consumer; other consumers continue. Preload reports Ready, Failed, Canceled or BudgetDeclined and does not guarantee retention. A held lease is distinct from preload. Ready cache hits attach synchronously; cold reads and codec work run on bounded workers and publish through the owner queue.

Prepared raster data is premultiplied sRGB RGBA with orientation applied once. Native renderer handles remain in the adapter. `RetainedScene` and `HeadlessSnapshot` now own image leases and must be disposed when released; `scene.Retain()` creates an independently owned snapshot. Removing a component or disposing its cache cannot free pixels still held by a retained frame. Cache limits bound source/encoded/output size, work concurrency and retained/temporary allocations; expected corrupt data and budget exhaustion become explicit load errors. Unexpected provider/adapter exceptions retain the application's fatal-error behavior.

Defaults allow four active preparations and 128 queued requests, with separate 128 MiB budgets for unleased cached data, live leased data and temporary preparation reservations. Encoded sources and individual prepared outputs have 64 MiB limits. The Skia renderer separately retains at most 256 native raster copies within 32 MiB; a larger single copy uses a bounded 64 MiB transient path. Large sources can be declined even for a small requested thumbnail because codec working memory depends on source dimensions. These are resource-accounting limits, not a promise that total process memory equals the pixel counters.

JPEG admission currently reserves a conservative eight bytes per original source pixel for codec working data, in addition to decode intermediates. Progressive/CMYK measurements and more selective admission are tracked in [#154](https://github.com/RichiCoder1/lucent/issues/154).
