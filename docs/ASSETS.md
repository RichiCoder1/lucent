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

Image rendering and owned loading are tracked by [#146](https://github.com/RichiCoder1/lucent/issues/146); SVG adapter policy and compatibility by [#147](https://github.com/RichiCoder1/lucent/issues/147). Declaring SVG content does not certify browser SVG conformance or enable external file/network loading.
