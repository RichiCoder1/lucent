# Review: Plan 007 example UX and CSS quality

## Verdict

**PASS after revision and design-authority re-review.** Fresh read-only
adversarial reviews found no remaining blocker or high-severity defect. The plan
preserves the four confirmed decisions: a cohesive visual family, broad but
bounded Avalonia styling parity, bounded Windows reference captures with native/
headless checks, and insertion as Plan 007 with tooling/package plans shifted to
008/009. A later review also verified incorporation of commit `6b98897`'s product
and visual authority.

## Findings addressed

1. **Capture count was contradictory.** The plan now commits exactly eight final
   captures; correction captures replace affected files rather than increasing
   the total.
2. **Scaling and state evidence was not reproducible.** The plan now fixes eight
   named profiles, logical dimensions, themes, states, Windows scaling, and exact
   launch commands. The eight committed captures split the named profiles across
   100%/200%; separate non-image validation runs every profile at both scales.
3. **Selector/component boundaries were ambiguous.** The plan now defines type,
   multi-class, name, descendant, child, and terminal pseudo-class syntax; maps
   names to native `Name`; and scopes traversal to each native styled root.
4. **Dynamic resource lowering was assumed.** `resource()` is now contingent on
   a passing Avalonia 12.1.1 compile/runtime spike and an explicit decision if
   public programmatic setters cannot retain theme-reactive resources.
5. **Broad parity lacked an auditable boundary.** The plan now contains the exact
   property inventory with native property, value kind, resource eligibility,
   and transition eligibility.
6. **Transition fallback could emit the wrong type.** The plan now names the five
   exact transition families and requires incompatible pairs to diagnose rather
   than fall back to `DoubleTransition`.

## Design-authority review

After `PRODUCT.md`, `DESIGN.md`, `design/tokens.css`, the Lucent marks, and the
approved dark-ribbon Workbench mock were committed, the previous verdict became
stale and Plan 007 was reviewed again.

- **Authority:** the committed product/design files now override duplicated plan
  prose; the obsolete violet/blue direction was replaced by Registration Overlay.
- **Token bridge:** `--lucent-*` maps deterministically to `Lucent.*`; one bounded
  test checks the four apps' resources/CSS against canonical light/dark values.
- **Focus:** an Avalonia 12.1.1 decision fixture must prove the 2 px teal outline
  and 2 px separation without replacing native templates or accessibility peers.
- **States and motion:** hover/no-lift, transient-only shadows, non-opacity-only
  disabled state, labeled warning/error state, and reduced-motion behavior are
  explicit implementation and review gates.
- **Marks and mock:** 24/32 px use color; 16/20 px prefer grayscale unless exact
  rendered evidence supports color. The approved mock governs composition, not
  fictional live-preview/dependency features.
- **Documentation:** Plan 012 consumes the updated authority for Blume's reading-mode
  visual translation.

The final fresh review passed with no blocker or high finding.

## Cross-plan checks

- Plan 006 remains the behavior/accessibility/lifecycle prerequisite.
- Plan 008 consumes Plan 007's typed CSS catalog and forbids an LSP-only schema.
- Plan 009 preserves Plan 007's adaptive and accessibility gates while proving
  the replacement theme; Plan 010 migrates the shell, and Plan 011 replaces
  Workbench placeholders with real project/compiler data.
- Repository search found no stale numbered tooling/package-plan filenames after
  roadmap insertions.

## Residual risks

- The exact Avalonia API for a theme-reactive dynamic resource inside a
  programmatically created `Setter` remains deliberately unresolved until the
  mandatory spike.
- Plan 006 is actively being implemented. Plan 007 must not begin until those
  contracts stabilize and its drift check passes.
