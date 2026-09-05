# Framework requirements

This design should be implemented through reusable Lucent capabilities. Application policy stays in the links-and-notes application; layout, text, input, presentation, and ownership gaps belong in the framework.

## Required capability matrix

| Capability | Design use | Required contract | Primary ticket |
| --- | --- | --- | --- |
| Default content forwarding | Shell, command scope, responsive container, composed headers/footers | Consistent zero-or-more default content independent of parameter spelling; placement without grammar-only wrappers | #71 |
| Live retained row payload | Search/save changes in realized rows | Same-key immutable replacement reaches the row without remount, stale reads, focus loss, or manual region handles | #70 |
| Owned editor sessions | Same draft across wide/medium/compact | Hoistable draft, caret, selection, undo/redo, IME, and viewport state with explicit platform-resource transfer/cancellation | #73, #79 |
| Responsive participation | Arrangement branches | Container-owned logical constraints; explicit layout/paint/input/hit-test/semantic participation; deterministic focus transfer | #73 |
| Grid | Shell, collection, editor | Fixed/content/weighted tracks, min/max, gaps, placement, spanning, alignment, overflow, device rounding | #74, #77 |
| Flex-style Row/Column | Toolbars, metadata, actions | Shared grow/shrink/basis/wrap/gap/min/max model rather than a parallel convenience algorithm | #74, #77 |
| Constrained paragraphs | Wrapped rows, URLs, editor | Available width/height in requests; line/run metrics, logical ranges, caret/selection geometry, reusable painting | #74, #77 |
| Constrained virtualization | Inbox within a Grid cell | Realization from assigned cell viewport; fixed row height and stable scroll anchor through query/resize | #77 |
| Multiline editor | Plain-text note body | Wrapped input, selection, clipboard, undo/redo, caret navigation, IME, scrolling, and accessibility over an owned session | #79 |
| Wheel/trackpad routing | List and editor scrolling | Innermost eligible viewport targeting, clamping, nested bubbling, real Windows event delivery | #78 |
| Application commands | Capture/find/save/open/back | Typed command bindings available to `.lui`, deterministic conflict/enablement/focus behavior | #78 |
| Ordered durability | Capture/save/archive/close | Per-record ordering or revision-conditional writes; stale read rejection; durable completion and retry | #72, #76, #81 |
| Presentation primitives | Pane rules, fields, selected rows | Borders, clipping, composable surfaces, hover/pressed/focused/disabled/selected variants across theme and scale | #80 |
| Icons | Navigation, kind, open, archive | Typed accessible icon recipe; theme/scaling behavior; decorative semantics option; vetted asset provenance | #80 |

## Layout and text acceptance pressures

- Wrapped text desired height must be recomputed from the width assigned by Grid/Flex, and painting must use compatible shaped results.
- A scroll direction may provide unbounded measurement space, but the sibling direction still carries the assigned constraint.
- The collection's fixed-height virtualized list must realize against its actual pane viewport before painting and accessibility projection.
- Long text must not turn each grapheme cluster into repeated full-string scans or repeated fallback shaping. Characterize the representative 20,000-character fixture and a larger stress fixture while implementing the affected text path.
- Layout, scene projection, input hit testing, and semantic bounds must agree on clipping and responsive participation.
- Responsive branches must not repurpose `InputProperties.Visible`; that property currently routes input and does not describe layout, paint, or semantic participation.

## Component recipes required by the application

These are application-facing recipe names, not commitments to new Core primitive types:

- `CaptureBar`
- `Navigation` and `NavigationRail`
- `CollectionHeader`, `SearchField`, `ItemCollection`, and `ItemRow`
- `CollectionLoading`, `CollectionEmpty`, and `CollectionError`
- `ItemEditor`, `EditorEmpty`, `LinkField`, `MultilineTextEditor`, and `EditorFooter`
- `SaveStatus`, `InlineValidation`, and recoverable error strip
- `Icon` with accessible-label/decorative modes

Recipes should compose built-in primitives and behaviors where possible. A recipe becomes a new primitive only when text editing, platform input, accessibility, or lifecycle ownership requires an implementation boundary.

## Presentation inventory

The designed screens require:

- application, pane, field, hover, selected, and error surface brushes;
- primary, muted, link, focus, error, and disabled foreground brushes;
- hairline and field borders with scale-correct rounding;
- inset keyboard focus rings that remain visible under clipping;
- application/navigation, list title, metadata, editor title, editor body, and status typography roles;
- icons for Inbox, Archive, Link, Note, Open, Back, Search, Retry, and save error;
- stable loading placeholders and a bounded nonblocking saving indicator.

The initial font should use the Windows system family through theme tokens. No third-party visual asset is adopted by this design. Before implementation selects an icon source, record its origin, version, license, modifications, and distribution requirements in `CREDITS.md`. If icons are authored specifically for Lucent, record that provenance as well.

## Verification scenarios derived from the design

1. Render realistic short, long, empty, loading, no-results, save-failed, and startup-failed states at widths 480, 839, 840, 1,059, and 1,060 logical pixels.
2. Resize through both thresholds during an active multiline edit and verify draft, caret, selection, undo/redo, IME policy, editor scroll, list query, selection, and scroll anchor.
3. Run the complete capture/edit/search/open/archive/restart loop with keyboard only and through ordinary UI Automation.
4. Exercise 100%, 150%, and 200% scale with focus rings, long URLs, nested scrolling, and no one-pixel overflow.
5. Force reverse save completion and a save failure; verify reported durable revision, retry, negotiated close, and restart state.
6. Verify nonparticipating responsive branches are absent from layout, paint, pointer/keyboard routing, hit testing, and the semantic tree.
7. Verify the virtualized list realizes from its Grid cell viewport rather than the whole window and keeps the focused row available during keyboard navigation.

## Remaining design gaps

- Exact Grid track-sizing semantics and the internal layout-engine choice belong to issue #74.
- The application-owned rule that derives a title/body from non-URL capture needs product-copy validation during #80/#81.
- The multiline title treatment needs a component decision: expanding single-line field, or distinct display/edit states with a clear transition.
- Icon source and final theme token values await implementation-level visual exploration and provenance review.
- The maximum number of inactive editor sessions retained in memory needs an application policy informed by usage; eviction may occur only after durable save and must not affect the active session.
- Search result ranking and snippet rules need a small deterministic specification in #81; this design only fixes the visible context requirement.
