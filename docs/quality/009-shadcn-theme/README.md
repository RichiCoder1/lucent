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
| `utility-gallery-light.png` | `e05ef674152f9201f04f007c5e9d40c9ae8557e018d57bef029506a393601ef3` | complete light utility section in viewport; computed mappings validated before capture |
| `utility-gallery-dark.png` | `4e8db080fa3814d925409f5ea0aaf439c8b90cdd3bcd6f773d903177e5053f82` | complete dark utility section in viewport; computed mappings validated before capture |
| `shadcn-gallery-light.png` | `0226e85fa2387d32e881bbd053a6f36fd4d436fa8327213697cd4bec3eea2008` | light native controls, keyboard focus and state evidence visible |
| `shadcn-gallery-dark.png` | `784c8c4136d25e9182319f4f0de5cdd33adacae55b23b641ef08f75ac7f8005f` | dark native controls, semantic resources and state evidence visible |
