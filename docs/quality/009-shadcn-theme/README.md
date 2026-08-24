# Plan 009 Shadcn gallery evidence

`shadcn-gallery-light.png` and `shadcn-gallery-dark.png` are deterministic
900×700 offscreen captures. Regenerate them from the repository root with:

```sh
dotnet run --project examples/shadcn-gallery/ShadcnGallery.csproj -- --capture
dotnet run --project examples/shadcn-gallery/ShadcnGallery.csproj -- --capture --dark
```

The capture path verifies ten button treatments, including deterministic
hover, pressed, and focus-visible states; focused/invalid/disabled fields;
focused CheckBox, RadioButton, ComboBox, ListBox, Menu, and TabControl; and
list, menu, and tooltip controls before writing either PNG. It also verifies
the native-state markers and reopens the PNG to verify its dimensions. The
gallery shows committed semantic-token swatches, including `Shadcn.Input`, and
uses public native control states rather than template copies.
