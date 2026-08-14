---
status: accepted
---

# Make named slot supply explicit and single-site

Lucent declares a named slot with `slot name;`, supplies it with `slot name { ... }`, and places it with `yield name;`; unnamed trailing content continues to supply the implicit `children` slot. We chose the explicit call-site keyword over a bare `name { ... }` block to avoid collisions with renderable symbols and properties. Initially each slot has at most one syntactic yield site, which may be conditional but not repeated, so identity and lifetime remain deterministic; required and repeatable slots may be added later as explicit features.
