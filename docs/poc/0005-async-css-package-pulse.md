# POC 0005: Package Pulse async, CSS, and motion slice

## Scope

Package Pulse combines the smallest useful vertical slices of three deferred
features:

- adjacent `.css` compilation into native Avalonia styles;
- typed Avalonia property transitions;
- owned task-returning `Computed<T>` values with stale content.

Run it with:

```powershell
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj
```

The package catalog is local and deterministic. Each query waits 850 ms. The
query `fail` produces an error; rapid query changes exercise cancellation and
late-result suppression.

## Proven behavior

- MSBuild and `lucentc` discover a CSS file beside its `.lui` source.
- CSS variables, type/class selectors, one Avalonia pseudo-class, common typed
  values, and transitions lower to native Avalonia objects.
- No CSS, color, or duration string is parsed at application runtime.
- `Computed<T>` starts owned work on mount, keeps its last committed value while
  pending, cancels replaced work, and ignores completions from old generations.
- Completions and errors commit through `Dispatcher.UIThread`.
- Component disposal cancels owned work and queued commits check disposal.
- Package rows retain keyed native controls while replacement results reconcile.

## Avalonia concessions

This is not browser CSS. `gap` maps to `StackPanel.Spacing`; CSS pixels become
Avalonia device-independent units; selectors target projected native controls;
and pseudo-classes are Avalonia's actual control states. A declaration is valid
only when its matched native control exposes the corresponding Avalonia
property.

Transitions use Avalonia's animation priority. Lucent generates typed
`BrushTransition`, `DoubleTransition`, `ThicknessTransition`, or
`CornerRadiusTransition` instances against the real target property.

## Deliberate limits

- Every state change currently refreshes every computed member. Symbol-derived
  dependencies should replace this once components contain several computations.
- `Computed<T>` requires an initial value and exposes `Value`, `IsPending`, and
  `ErrorMessage`. Structural loading/error boundaries remain the intended final
  interface.
- CSS supports no combinators, IDs, scoping, keyframes, transforms, media
  queries, enter/exit lifetime animation, or reduced-motion policy.
- CSS property applicability is ultimately checked by generated C#; diagnostics
  for malformed values currently point to the stylesheet but not the exact
  declaration.
- Visual screenshot automation is not part of this checkpoint.

## Verification

```powershell
dotnet test Lucent.sln --no-restore
dotnet build src/Lucent.Poc/Lucent.Poc.csproj --no-restore
```

The full suite passes, the POC builds without warnings, and a four-second launch
smoke confirms the desktop process remains running.
