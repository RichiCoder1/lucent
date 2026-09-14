# .lui formatting references

Primary-source review, 2026-09-14. The owner requested XAML/React and Vue/Prettier as
comparative examples. These findings inform the design; they are not evidence of a
working .lui formatter or permission to copy another language's whitespace semantics.

## Prettier Vue/HTML and JSX

Source pinned to Prettier commit
[884c2d6a7df1e97523f494dd4908f55a23e7df47](https://github.com/prettier/prettier/commit/884c2d6a7df1e97523f494dd4908f55a23e7df47).
No source was copied, installed or executed.

- A grouped document attempts flat layout; breakable line separators become spaces or
  newlines together. This matches compact lists that expand to one item per line.
  `fill` instead packs multiple items onto wrapped lines, which is a different policy.
  Avoid unbounded alternative layouts: deeply nested conditional groups can be expensive.
  [Document primitives](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/commands.md#group)
- The accepted .lui wrapping rule resembles ordinary grouped wrapping. Prettier's
  `singleAttributePerLine: true` instead forces multiple attributes to break even when
  they fit. Vue SFC root-block exemptions are language-specific and should not be copied.
  [HTML/Vue attributes](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-html/print/tag.js#L276-L295),
  [JSX attributes](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-js/print/jsx.js#L644-L676)
- Model the break before a closing delimiter separately from list-item separators.
  Line comments can force a break so the delimiter cannot become comment content.
  [JSX closing delimiters](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-js/print/jsx.js#L679-L714)
- Comments participate in layout. HTML retains their relative source positions, and
  line-suffix boundaries flush comments before crossing an embedded-language boundary.
  These mechanisms are relevant to .lui parameter trivia and trailing-expression comments.
  [Source order](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-html/print/tag.js#L244-L284),
  [Line-suffix boundary](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/commands.md#linesuffixboundary)
- Text sensitivity is an explicit input to layout. HTML tracks meaningful leading/trailing
  spaces; JSX has its own transformations. .lui must preserve its scalar-content semantics,
  rather than inherit CSS display lookups or JSX whitespace transformations.
  [HTML marker handling](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-html/print/tag.js#L124-L207),
  [JSX text](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-js/print/jsx.js#L311-L368)
- Opening tags and their children can use separate groups. A single interpolation can
  indent conditionally when the opening tag breaks, without forcing every descendant
  into the same layout decision.
  [Element groups](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-html/print/element.js#L31-L120)
- Print width is a target. JSX has special cases for single-line versus multiline string
  attributes. Test .lui's indivisible string/text exceptions explicitly.
  [Print width](https://prettier.io/docs/options#print-width),
  [JSX string attributes](https://github.com/prettier/prettier/blob/884c2d6a7df1e97523f494dd4908f55a23e7df47/src/language-js/print/jsx.js#L617-L648)

## XAML

Microsoft documents competing attribute-wrapping conventions and notes significant
whitespace. Its detailed Visual Studio 2017 options page separately describes width,
first-attribute placement and blank lines; that page is historical, not a current universal
XAML standard. Use the familiar visual alternatives as examples, while retaining .lui's
own value/text semantics.
[Current XAML syntax guidance](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-syntax-guide#tips-and-tricks-notes-on-style),
[Historical formatter options](https://learn.microsoft.com/en-us/previous-versions/visualstudio/visual-studio-2017/ide/reference/options-text-editor-xaml-formatting?view=vs-2017).

## C# formatting machinery

The existing repository C# formatter is CSharpier. Its documented small configuration
surface covers width, indentation and line endings, and its public options do not provide
a brace-style switch. Do not assume it directly emits the accepted same-line .lui brace
policy. It remains a useful reference for a canonical format and whole-file layout.
[CSharpier configuration](https://github.com/belav/csharpier/blob/main/Src/Website/docs/Configuration.md),
[Public options](https://github.com/belav/csharpier/blob/main/Src/CSharpier.Core/PublicAPI.Shipped.txt).

Roslyn exposes syntax-node formatting and separate options for method, control-block,
type and other brace locations. Its tokens retain leading/trailing trivia. This makes it
a candidate for C#-aware preservation and spacing, but the .lui printer still needs
coordinated width decisions and exact embedded-island boundaries. Current public API
documentation is not proof of compatibility with the eventual pinned Lucent package set.
[Formatting API](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/Portable/PublicAPI.Shipped.txt),
[C# formatting options](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/CSharp/Portable/PublicAPI.Shipped.txt),
[Syntax trivia](https://github.com/dotnet/roslyn/wiki/Roslyn-Overview).

## Design implications to prove

Retain lossless source/trivia boundaries; build grouped layout documents for .lui structure;
coordinate embedded C# formatting with the containing width/indentation and same-line
brace policy; flush comments before delimiters; and preserve meaningful text using Lucent
rules. Keep this work in compiler/developer tooling, with one policy shared by CLI/editor.
No Node, browser, or formatting engine belongs in a published Lucent application.
