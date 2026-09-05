# Links and notes experience design

Status: proposed design input for [issue #69](https://github.com/RichiCoder1/lucent/issues/69). This directory describes intended product behavior and representative authoring shapes. It does not claim that the proposed `.lui` APIs or visual capabilities exist today.

## Product job and scope

The application is a fast, local place to capture a URL or thought, add a plain-text note, find it again, open its link, and archive it. The primary user is at a Windows desktop and returns frequently enough that keyboard paths, stable editing state, and trustworthy saves matter more than onboarding or decorative chrome.

The first complete loop is:

1. Paste a URL or enter a thought in the capture field.
2. Create an inbox item and move focus into its title.
3. Edit the title and note while saves happen in the background.
4. Retrieve the item from the inbox or local search.
5. Open its URL when present or archive it.
6. Close and reopen the application without losing accepted work.

Initial scope includes paste or explicit entry, plain text, inbox, archive, and local search across title, URL, and note text. Drag and drop, global capture, browser integration, tags, rich text, synchronization, and collaboration remain outside this design.

## Chosen experience direction

The shell is a continuous working surface divided by quiet rules, rather than a dashboard of cards. A narrow capture strip is always the first command surface. The inbox is dense enough to scan but gives titles two lines when needed. The editor uses the available space as a calm reading and writing canvas. Selection, focus, save status, and errors carry the visual emphasis.

Use the Windows system text family and Lucent theme tokens. The visual hierarchy needs four type roles: application/navigation, list metadata, item title, and editor body. It needs hairline dividers, a selected-row fill, a visible keyboard focus ring distinct from selection, and one restrained accent for primary actions and active navigation. No third-party font, icon set, or image asset is selected here.

The signature interaction is uninterrupted capture-to-edit: after a capture, the item appears selected in the inbox and its title is ready for correction, with the original URL or thought already safe in the owned editor session.

## Information model

Each item presents:

- stable identity;
- kind: link or standalone note;
- title, which may initially be derived from the captured text;
- optional URL;
- plain-text body;
- inbox or archived state;
- created and last-edited timestamps;
- current durable revision and current editor-session revision.

The UI owns selection, active query, active collection, pane arrangement, and per-item editor sessions. The storage service owns durable records and ordered writes. Mounted controls consume those objects; they do not own the only copy of a draft.

## Representative content

Design and implementation checks should use all of these fixtures rather than repeated short placeholders:

| Fixture | Content | Design pressure |
| --- | --- | --- |
| Short link | `SQLite is transactional` with a normal documentation URL and a two-sentence note | Common scan/edit path |
| Standalone thought | Untitled capture promoted to `Questions for the storage spike` | No URL, title derivation, keyboard capture |
| Long article | 148-character title, 420-character URL with a long path/query, 8,000-character note with headings represented as plain text | Wrapping, row height, editor scrolling |
| Long note | 20,000 characters across short and long paragraphs, blank lines, emoji, combining marks, Arabic and Hebrew samples | Paragraph layout, caret movement, sustained editing |
| Similar results | Ten titles sharing the same prefix and matching in different fields | Search context and unambiguous selection |

List rows clamp the title to two lines and show one metadata line; they never change height based on the full note. The editor shows the complete title, URL, and body. Long URLs wrap at useful punctuation with grapheme-safe fallback rather than forcing horizontal scrolling.

## Documents in this design

- [Responsive layouts](layouts.md) defines the wide, medium, and compact arrangements and the content-derived thresholds.
- [Interaction and states](interaction-and-states.md) defines focus, keyboard, editing, save, error, loading, empty, and long-content behavior.
- [Proposed `.lui` composition](proposed-lui.md) shows representative future authoring shapes and labels every unsupported capability.
- [Framework requirements](framework-requirements.md) maps the design to reusable Lucent work and existing implementation tickets.

## Open owner decisions

The application name, external repository shape, and publication license remain owner decisions. They do not change the interaction model in this directory, but they must be resolved before external publication. Icon assets also need a selected source and provenance entry in `CREDITS.md` before adoption.

