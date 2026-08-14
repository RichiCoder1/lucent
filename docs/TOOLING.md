# Tooling and developer experience

Tooling is part of Lucent's language design. A separate `.lui` source format is worthwhile only if the editor can recognize its structure and explain compiler decisions in Lucent terms.

## Shared frontend

The build compiler and language server should use the same parser, syntax tree, semantic model, diagnostics, and symbol representation:

```text
                    Lucent frontend
                   /               \
             build compiler      language server
```

Duplicating the parser or semantic rules would create drift exactly where the project needs trust. The initial frontend APIs should be designed for incremental document analysis even if the first build path is batch-oriented.

## Proof-of-concept language server

The proof of concept is not complete without basic editor support for `.lui`.

Required capabilities:

- document recognition;
- syntax and basic semantic diagnostics;
- component symbol discovery;
- component completion;
- go-to-definition for Lucent components;
- hover information;
- document symbols.

Useful follow-ups include Lucent-to-C# navigation, parameter and property completion, signature help, CSS class and token completion, references, rename, and reactive dependency inspection.

## Diagnostics

Diagnostics should describe the user's component and style model, not expose internal parser codes or generated C# accidents.

```text
The property `background` expects a Color or ColorToken,
but `spacing.large` is a LengthToken.

Button {
    background: spacing.large;
                ^^^^^^^^^^^^^
}
```

The diagnostic model needs source spans and concepts for components, render methods, slots, context, state members, keys, CSS, and Avalonia property projection. Generated-code diagnostics should map back to the responsible `.lui` or CSS expression whenever possible.

## Formatting and source mapping

The custom format needs a formatter early enough that syntax discussions are not distorted by hand-formatted examples. Formatting must preserve stable output and should be based on the shared syntax tree.

Generated C# should include deterministic names and source mappings. Developers need to debug application behavior without treating generated code as the primary authoring surface, while framework contributors still need generated output that is readable enough to inspect.

## Hot reload

Hot reload is desirable after the core proof of concept:

```text
edit component
    -> compile affected region
    -> patch implementation
    -> retain compatible component state
    -> update UI
```

Production-grade hot reload is not an initial requirement. The early component identity, state layout, generated-code boundaries, and source mapping should avoid making it impossible later.
