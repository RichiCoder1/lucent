# Language model

Lucent source uses the working `.lui` extension. It has C# semantics and the .NET type system, plus syntax for declarative UI that ordinary Roslyn C# does not parse.

The extension and syntax are working choices until the frontend and tooling prove them. The semantic principles in this document are more important than the exact punctuation.

## Vocabulary

Lucent should keep its custom vocabulary small:

```text
component
slot
yield
context
using context
```

Normal C# remains responsible for namespaces, `using`, parameters, fields, methods, named arguments, lambdas, records, `with`, generics, expressions, and control flow. Lucent adds UI declarations, keyed UI iteration, slots, and contextually typed construction where they materially improve UI code.

## Components

A block-bodied component is a compiler-owned, class-like logical instance. It may contain ordinary C# fields and methods and must contain exactly one `Fragment Render()` method:

```csharp
namespace MyApp.Components;

component UserCard(User user, bool compact = false)
{
    Fragment Render() =>
        Card {
            class: compact ? "user-card compact" : "user-card";
            Text(user.Name);
            Text(user.Email);
        };
}
```

The declaration does not require a framework base class, an `override`, or an Avalonia control wrapper. The compiler may generate a sealed implementation type, but authors compose components through Lucent invocation syntax rather than `new` or manual calls to `Render()`.

Component parameters are current read-only inputs, not mutable state. When a parent updates an invocation with the same logical identity, new input values are visible to dependent render computations while the component instance and its state members survive.

`Render()` is the declarative boundary. It describes a fragment from current inputs, state, slots, and context:

```text
inputs + state + slots + context
    -> Render()
    -> Fragment
```

Its invocation count is not observable. The compiler may lower the method into control creation, direct property updates, event wiring, and structural regions rather than invoking it as a conventional virtual method. Render code must not mutate state, perform I/O, subscribe to services, or otherwise depend on being run a particular number of times. Constructing descriptions and event callbacks is allowed.

Ordinary locals remain ordinary C# values:

```csharp
Fragment Render()
{
    var label = $"Count: {count.Value}";

    return Text(label);
}
```

`label` is recomputed render data, not a hidden reactive cell. The compiler may propagate the visible `count.Value` dependency into a direct property update; when it cannot safely specialize an expression, correctness comes from reevaluating the owning computation or structural region.

A stateless component may use an expression body:

```csharp
component Badge(string text) =>
    Text {
        class: "badge";
        text: text;
    };
```

This is shorthand for the same `Fragment Render()` contract, not a second component model. The exact literal syntax for an explicit zero- or multiple-root fragment remains a working choice.

Controls and components share composition syntax but not an implementation model. A native control resolves to its real Avalonia type and members, while a component may produce zero, one, or many controls without becoming a heavyweight control itself. Nested content on a native control follows Avalonia's content metadata; trailing content supplied to a Lucent component remains a Lucent slot.

Native scalar content may be written explicitly or with trailing literal
content when the control has an unambiguous scalar content route:

```csharp
Button { Content: "Add task"; }
Button { "Add task"; }
```

Both forms target Avalonia's `ContentControl.Content`; the explicit spelling is
available when floating text would be less clear. Explicit content and nested
implicit content cannot be combined.

The language rule is that literal conveniences are selected from the resolved
target property type, not from a control or property-name table. The current
proof of concept implements that rule for `Thickness` and `CornerRadius`:

```csharp
Border {
    Padding: 24;
    CornerRadius: 8;
}

Border {
    Padding: (16, 8);
}
```

Those recognized values lower to typed `Thickness` and `CornerRadius`
construction, while the same numeric literal remains numeric for a property
such as `Width`. Explicit C# construction always remains available. General
target-type conversion beyond those two framework primitives, including string
parsing for enums, brushes, colors, and `GridLength`, remains deferred.

Native events are always visibly C# lambdas. A zero-argument lambda ignores the
delegate arguments; a two-argument lambda receives them:

```csharp
Button {
    Click: () => AddTask();
}

TextBox {
    TextChanged: (sender, e) => {
        draft.Update(sender.Text ?? "");
    };
}
```

The compiler resolves the actual Avalonia delegate, generates its concrete
signature, and narrows `sender` to the control type before running the body.
A bare statement block is rejected because it hides both the function boundary
and its arguments. One-argument event lambdas are also rejected as ambiguous.

## UI declarations and control flow

A declaration such as `Column { ... }` is not object-initializer shorthand. It gives the compiler a semantic view of node identity, properties, events, children, dependencies, CSS classes, and lifetime.

```csharp
Fragment Render() =>
    Column {
        if (user is null) {
            LoginPrompt();
        }
        else {
            UserCard(user);
        }

        foreach (var item in items) keyed by item.Id {
            ItemRow(item);
        }
    };
```

Normal C# conditionals and loops express structural UI. `keyed by` adds the logical identity needed to preserve item state through insertion, deletion, and movement.

The initial executable keyed-loop subset requires one native control root
inside the loop and a dedicated `Panel` or `ItemsControl` child region. Existing
keys retain their native controls, changed item values refresh row properties,
new keys create rows, removed keys dispose their subscriptions, and source
order determines native child order. Nested structural control flow remains
deferred.

Complete C# expressions remain valid in property values, arguments, event callbacks, conditions, and other defined C# positions. The frontend must preserve their C# meaning rather than silently reinterpret them as Lucent declarations.

## Children and named slots

Every composable component has an implicit `children` slot. A trailing UI block supplies it:

```csharp
Card(title: "Profile") {
    Avatar(user.Avatar);
    Text(user.Name);
}
```

The component places that content explicitly inside `Render()`:

```csharp
component Card(string? title = null)
{
    Fragment Render() =>
        Column {
            if (title is not null) {
                Text(title);
            }

            yield children;
        };
}
```

Named slots cover semantic regions such as actions, headers, footers, icons, or commands:

```csharp
component Dialog(string title)
{
    slot actions;

    Fragment Render() =>
        Column {
            Text(title);
            yield children;

            Row {
                yield actions;
            }
        };
}
```

```csharp
Dialog("Delete project?") {
    Text("This action cannot be undone.");

    slot actions {
        Button("Cancel", onClick: close);
        Button("Delete", onClick: delete);
    }
}
```

Slots contain declarative renderable content, not already-created controls. A named slot is declared with `slot name;`, supplied with `slot name { ... }`, and placed with `yield name;`. Unnamed trailing content supplies the implicit `children` slot. Unknown or duplicate named-slot supplies are compile errors, while an omitted optional slot is an empty fragment.

Initially, each slot has at most one syntactic yield site. That site may be conditional, so the supplied subtree does not exist while it is not yielded, but it may not appear twice or inside a repeated region. Supplied content captures the caller's lexical values and observes tree context at its yield site. Required, typed, and repeatable slots are deferred.

## Context

Context carries data through the rendered component tree without turning it into a global service locator:

```csharp
context Theme = darkTheme;
PreviewPane();
```

```csharp
using context Theme;
```

Nested providers shadow outer values. Content observes the context where a component yields it, so a component can establish context for its supplied children.

The exact naming and type-inference rules are deferred. Lucent may integrate context with `Microsoft.Extensions.DependencyInjection`, but it should not create a competing application-service container.

## Parameters, options, and contextual construction

Component APIs should distinguish four roles:

```text
parameters -> essential data and behavior
options    -> secondary configuration
slots      -> caller-provided UI
context    -> ambient tree-scoped values
```

Lucent does not implicitly promote members of an options record into component arguments. The API boundary stays visible:

```csharp
Button(
    "Delete",
    options: {
        intent: ButtonIntent.Danger,
        size: ButtonSize.Small,
    }
);
```

A bare construction expression is valid only when the expected .NET type is statically known and unambiguous. It lowers to construction of that type. Lucent does not infer anonymous property bags, guess a shape, or implicitly merge objects. Normal C# construction and `with` expressions remain available.

## State members

Persistent component-owned state is an explicit member:

```csharp
component Counter(int initial = 0)
{
    private readonly State<int> count = new(initial);

    Fragment Render() =>
        Text($"Count: {count.Value}");

    private void Increment() =>
        count.Update(value => value + 1);
}
```

A state member's initializer runs once when its logical component instance is mounted. Later input changes do not reinitialize it. State survives render reevaluation and keyed movement, and is disposed when its owning component instance is removed.

`State<T>.Value` is a compiler-visible reactive read. `Update` schedules invalidation through Lucent's scheduler, and the compiler updates only dependent properties or structural regions where possible. An ordinary mutable field is not reactive merely because `Render()` reads it.

The baseline semantic surface is:

```csharp
State<T>
state.Value
state.Update(value)
state.Update(current => next)
```

Concise member sugar remains a working candidate:

```csharp
state int count = initial;
```

If adopted, it must mean exactly an instance-owned `State<int>` member with one-time initialization. It must not introduce hook ordering, hidden local persistence, or different lifetime rules.

## Reusable state and effects

Class-shaped components remove the need for order-sensitive hooks as the basic state mechanism. Reusable behavior may eventually be expressed through ordinary owned helper objects, a dedicated state-composition facility, or narrowly defined compiler support. No custom `hook` declaration syntax is accepted yet.

Effects, subscriptions, and asynchronous work cannot run inside `Render()`. Their eventual API must make ownership, scheduling, cancellation, error routing, and cleanup visible. Dependency arrays should not be required when dependencies can be derived reliably, but the compiler must not invent dependencies hidden behind opaque code.

## Language design test

Before adding syntax, ask:

1. Is it clearer than normal C# for this UI problem?
2. Is any hidden construction, merging, subscription, identity, or lifetime obvious?
3. Can the compiler understand it statically?
4. Can completion, hover, and diagnostics explain it?
5. Does it materially improve authoring?
6. Can it lower efficiently?

If normal C# is already clear, keep normal C#.
