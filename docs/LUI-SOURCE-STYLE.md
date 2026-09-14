# .lui source style

Status: approved for implementation, 2026-09-14. The owner confirmed shared understanding
after the [design interview](plans/lui-style-and-tooling.md), including content-placement
semantic exceptions, and requested handoff to Implementation.
The [implementation plan](plans/lui-formatting-and-linting.md) defines the delivery work.
The shared formatter, linter and editor implement this policy. See the
[formatting guide](LUI-FORMATTING.md) for commands, configuration, semantic
exceptions and explicit fixes; ordinary formatting never applies lint fixes.

Source style concerns how .lui code is written. Lucent Style declarations continue to
mean typed visual/layout assignments.

## Formatting

Use one canonical layout across .lui structure and embedded C# declarations, statements
and expressions. Formatting preserves behavior, comments, documentation, string values
and meaningful text. It never silently sorts attributes, assignments or declarations.

- Indent with four spaces.
- Aim for 100 columns. This is a soft target: preserving meaningful content takes priority.
- Put opening braces on the same line for components, named styles, control flow and
  local functions. This is the .lui source policy, including its C# regions.
- Keep short opening tags and parameter lists compact. When a list wraps, use one
  attribute or parameter per line and put the closing delimiter on its own line.
- Keep short elements with one text or expression child inline. Expand longer elements
  without changing the content's meaning.
- Keep a single short inline style assignment compact. Expand multiple assignments
  to one per line, even when the compact form would fit the width.
- Preserve at most one intentional blank line between groups inside a body. Normalize
  excess blank lines and separate top-level declarations consistently, while preserving
  documentation/comment attachment and declaration order.
- Require exactly one blank line between a component's declarations/methods and its UI
  recipe. Insert it even when the author omitted it. This includes a recipe beginning
  with structural control flow. Keep recipe-attached comments with the recipe, placing
  the separator before them; do not force an empty section when no declarations exist.

```csharp
public component SaveAction(Action save) {
    <Button onInvoke={save}>Save</Button>
}
```

Separate component state and methods from the UI recipe:

```csharp
public component Counter() {
    int count = 0;
    void Increment() {
        count++;
    }

    <Button onInvoke={Increment}>{() => $"Count: {count}"}</Button>
}
```

The accepted expanded-tag shape is:

```csharp
<Button
    onInvoke={Save}
    style={ActionStyle}
    name="document.save"
>
    Save document
</Button>
```

Wrapping chooses a layout based on the complete construct's width, not the number of
attributes alone. The expanded specimen illustrates the shape, not a rule to expand
that exact short example in isolation.

Inline style expressions follow their own accepted multi-assignment rule:

```csharp
style={BaseStyle with { Padding: 12; }}

style={BaseStyle with {
    Padding: 12;
    Spacing: 8;
}}
```

## Configuration

Projects may override indentation, line width and line endings through .editorconfig.
The defaults are four spaces and a 100-column soft target. Brace and wrapping rules are
canonical; they do not gain project-specific switches. Exact configuration precedence
across CLI/editor and fallback line-ending behavior are specified in the tooling plan.

Lint configuration is separate from these formatting knobs. Declaration placement can
be configured and enforced as component-first or styles-first. No declaration-order rule
is enabled by default.

## File organization

Prefer namespace/imports, then the main component, then its named styles. This puts the UI
structure first for a reader. This is a recommendation, not an enforced default. Teams may
choose component-first or styles-first and enforce that choice through the optional
declaration-order lint rule. Styles-first is a supported project convention.

The formatter preserves declaration order. Keep relative style order intact because style
initialization can be meaningful. A reordering fix must be explicit and preserve style
order and documentation/comment attachment; it is not part of ordinary formatting.

## Children and default content

Write children and default content between the opening and closing tags, rather than
assigning the default-content parameter as an attribute, wherever the forms preserve
the same meaning. This is an enforced authoring rule with narrow semantic exceptions.

```csharp
<Button onInvoke={Save}>Save</Button>
<Text>{() => displayName}</Text>
```

The rule follows the resolved parameter's default-content metadata, not its spelling:
it also applies when that parameter is named children, label or something else. An
unrelated parameter named content and other named slots remain ordinary attributes.

Retain an explicit default-content attribute when moving it into the body changes
binding, construction-time versus live behavior, evaluation order, or direct collection
forwarding. Do not replace an explicit empty/null value with omitted content. A lint fix
must prove the conversion preserves meaning; ordinary formatting does not move content
between attributes and bodies. Explicit live readers remain explicit live readers.

## Linting

Default lint diagnostics cover objective problems and the explicitly selected canonical
default-content placement rule above. Subjective design advice, such as extracting a
complex inline expression into a helper, remains optional. Enabled lints default to
warnings and honor .editorconfig and the project's warnings-as-errors policy. Optional
organizational rules report only when the project selects a policy.

Editor code actions and explicit CLI fix mode may apply a small set of proven
behavior-preserving fixes. Formatting alone never applies lint fixes. Unstable keys need
an explanation and an authored choice; a fixer must not invent a replacement identity.

## Local exceptions

Allow a formatter-ignore directive for the next complete element or declaration. Lint
suppression is scoped and rule-specific; source suppressions carry a reason. Exact marker
syntax and attachment rules are proposed in the tooling contract. General
formatter-off/on regions are outside the first release.

## Incomplete files and adoption

The first formatter leaves malformed files unchanged and reports that formatting is
unavailable. A check must fail for malformed/unformattable input; unchanged text is not
automatically evidence that a file is clean. Safe partial formatting is deferred.

Editor formatting and the CLI share one policy. Format-on-save follows the user's editor
setting; builds never rewrite source. Adoption starts with preservation tests, followed
by separate formatting-only migration changes, and then CI enforcement.
