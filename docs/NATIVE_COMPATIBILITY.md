# Native compatibility matrix

Lucent projects use public Avalonia APIs; they do not attempt XAML parity.

| Seam | Status | Evidence / escape hatch |
| --- | --- | --- |
| Styled and attached properties | supported | GeneralCompilerTests.Native_controls_properties_content_and_events_are_lowered_directly; ComponentCompositionTests.Attached_properties_lower_to_static_setters |
| Routed and CLR events | supported | GeneralCompilerTests.Native_click_event_is_hooked_by_exact_event_name; NativeCompatibilityMatrixTests.Generated_clr_event_invokes_and_unsubscribes_on_dispose |
| Direct expressions and compiled bindings | supported | CompilerTests.Compiled_binding_generation_matches_checked_in_snapshot; GeneralCompilerTests.Native_compiled_item_binding_uses_inherited_data_context |
| Static and dynamic resources | supported | NativeCompatibilityMatrixTests.Lucent_css_resource_compiles_and_tracks_native_resource_changes; NativeCompatibilityMatrixTests.Explicit_csharp_static_resource_uses_resource_dictionary |
| Native child/content metadata and ItemTemplate | bounded subset | GeneralCompilerTests.Typed_item_template_lowers_native_fragment_and_item_scope; GeneralCompilerTests.Item_template_requires_exactly_one_native_root |
| Referenced custom and third-party controls | supported | NativeCompatibilityMatrixTests.Referenced_assembly_control_resolves_property_and_event_metadata |
| Exact root mounting, fragments, lifetime, and automation | supported | NativeCompatibilityMatrixTests.Generated_clr_event_invokes_and_unsubscribes_on_dispose; GeneralCompilerTests.Indirect_and_structural_roots_do_not_emit_approximate_mount_root; AccessibilityTests.Shown_shell_palette_and_settings_have_exact_accessibility_contract |
| TemplateContent and IDeferredContent | bounded subset | GeneralCompilerTests.Template_content_emits_fresh_public_deferred_content_without_component_capture; NativeCompatibilityMatrixTests.Explicit_csharp_template_escape_builds_richer_content |
| Adjacent, global, and theme precedence/value restoration | supported | NativeCompatibilityMatrixTests.Native_style_order_restores_previous_and_local_values; NativeCompatibilityMatrixTests.Global_catalog_order_is_host_controlled_and_adjacent_wins |
| Optional finite utility catalog | supported | NativeCompatibilityMatrixTests.Utility_catalog_uses_canonical_order_not_class_token_order; NativeCompatibilityMatrixTests.Utility_catalog_host_order_is_observable_and_local_values_win; GeneralCompilerTests.Escaped_state_class_selectors_preserve_decoded_names_and_exact_source_spans |

`resource("key")` is the supported Lucent CSS dynamic-resource form. Static
resource lookup and richer native resource composition remain explicit Avalonia
C# (`ResourceDictionary`/`Style` APIs); no XAML syntax is implied.

`ItemTemplate` is one typed native root and excludes component invocation,
events, and structural regions. `TemplateContent` is one native root with
literals, `const`/enum values, and the documented `new Binding("Path")`
exception. The executable C# escape fixture supplies a richer two-child
`IDeferredContent`; use that public Avalonia contract for resources, markup
extensions, name scopes, or other richer templates.

Avalonia 12.1.1 exposes no public eager binding-path parser. The bounded
`new Binding("Path")` form therefore leaves path validation to Avalonia when the
binding attaches.

`LucentStyle` catalogs are explicit `Application.Styles` entries. An adjacent
component style wins over a global catalog for the same property. Between two
global catalogs, the observed winner follows host `Styles.Add` order; packages
must not claim a portable cross-catalog precedence. Removing a later style
restores the earlier style, and a local value still wins. This is not a claim of
general XAML, `ControlTemplate`, markup-extension, or runtime wrapper support.
