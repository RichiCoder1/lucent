# Plan 009/009b Shadcn and utility gallery evidence

The Shadcn and utility light/dark images are deterministic 900×700 offscreen
captures. Regenerate all four from the repository root with:

```sh
dotnet run --project examples/shadcn-gallery/ShadcnGallery.csproj -- --capture
dotnet run --project examples/shadcn-gallery/ShadcnGallery.csproj -- --capture --dark
```

The capture path verifies every finite utility token and its computed native value, including
whole-value spacing/dimensions, `StackPanel` gaps, typography, Shadcn dynamic
resources, border/radius, opacity/visibility/clipping, alignment, and the
canonical same-property conflict (`p-4`, `w-48`, and `h-12` win regardless of
class-token order). It verifies hover/focus/focus-visible/disabled/checked and
selected utility controls, keyboard traversal, in-viewport evidence, non-color disabled opacity,
and focus-ring evidence. Runtime tests record deactivation/restoration and both host catalog
orders; a local value remains authoritative.

The capture path also verifies ten button treatments, including deterministic
hover, pressed, and focus-visible states; focused/invalid/disabled fields;
focused CheckBox, RadioButton, ComboBox, ListBox, Menu, and TabControl; and
list, menu, and tooltip controls before writing either PNG. It also verifies
the native-state markers and reopens the PNG to verify its dimensions. The
gallery shows committed semantic-token swatches, including `Shadcn.Input`, and
uses public native control states rather than template copies.

| capture | SHA-256 | review |
| --- | --- | --- |
| `utility-gallery-light.png` | `12f4bd2ff969570f64930213bf687383e1e0036f09a081c331838f64d6ea4fe6` | complete light utility section in viewport; computed mappings validated before capture |
| `utility-gallery-dark.png` | `a48a4e5a24af94d3d34227299a0991eee174f4d2ed1a2dd38e74b282008822be` | complete dark utility section in viewport; computed mappings validated before capture |
| `shadcn-gallery-light.png` | `9ad7a2cb0c1d79cb8dfa15cb3b7d0e5f1578b8d61663b10a6a5ff5a45aa939a6` | light native controls, keyboard focus and state evidence visible |
| `shadcn-gallery-dark.png` | `09ac16c0c4a9266ece4fd24212304e33c95b5a4e5034b7dae8f578d7bbfa86cc` | dark native controls, semantic resources and state evidence visible |
