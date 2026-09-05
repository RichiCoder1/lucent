# Proposed `.lui` composition

> **All code in this document is proposed design syntax. It does not compile with the current `.lui` language or current Lucent APIs.** Names and exact signatures are inputs to issues #71, #73, #74, #77, #78, and #79. The sketches show the authoring experience required by the application; they do not preselect a layout engine or expand `.lui` into a general imperative language.

## Application shell

```lui
// PROPOSED — unsupported syntax and APIs
public component LinksAndNotesShell(
    LinksAndNotesState app,
    EditorSessionStore sessions,
    AppCommands commands)
{
    <CommandScope bindings={commands.Bindings}>
        <ResponsiveContainer name="notes.shell">
            <Column style={ShellStyle}>
                <CaptureBar draft={app.Capture} add={commands.Add} />

                <Responsive when={constraints => constraints.Width >= 1060}>
                    <Grid columns="184, 320, minmax(482, 1fr)" style={ThreePaneStyle}>
                        <Navigation state={app.Navigation} gridColumn={0} />
                        <ItemCollection state={app.Collection} gridColumn={1} />
                        <ItemEditor session={sessions.For(app.SelectedId)} gridColumn={2} />
                    </Grid>
                </Responsive>

                <Responsive when={constraints => constraints.Width is >= 840 and < 1060}>
                    <Grid columns="64, 300, minmax(426, 1fr)" style={ThreePaneStyle}>
                        <NavigationRail state={app.Navigation} gridColumn={0} />
                        <ItemCollection state={app.Collection} gridColumn={1} />
                        <ItemEditor session={sessions.For(app.SelectedId)} gridColumn={2} />
                    </Grid>
                </Responsive>

                <Responsive when={constraints => constraints.Width < 840}>
                    <CompactWorkspace
                        route={app.CompactRoute}
                        collection={app.Collection}
                        session={sessions.For(app.SelectedId)}
                        back={commands.Back} />
                </Responsive>
            </Column>
        </ResponsiveContainer>
    </CommandScope>
}
```

This sketch deliberately passes the same application-owned session through each arrangement. Responsive branches control layout, paint, input, hit-test, and semantic participation. They do not use routing-only `Visible`, and they do not create a new session when the active branch changes.

The shell needs one coherent content-forwarding rule: `CommandScope` and `ResponsiveContainer` must accept default content without depending on a parameter named `content`. The nested Grid children need placement metadata that does not require wrapper elements solely for markup grammar.

## Collection and current-item rows

```lui
// PROPOSED — unsupported syntax and APIs
public component ItemCollection(CollectionState collection) {
    <Grid rows="auto, auto, minmax(0, 1fr)" style={CollectionStyle}>
        <CollectionHeader state={collection} gridRow={0} />
        <SearchField
            query={collection.Query}
            change={collection.SetQuery}
            command={AppCommand.Find}
            gridRow={1} />

        if (collection.Status is CollectionStatus.Loading) {
            <CollectionLoading gridRow={2} />
        } else if (collection.Status is CollectionStatus.Failed error) {
            <CollectionError error={error} retry={collection.Retry} gridRow={2} />
        } else if (collection.Items.Count == 0) {
            <CollectionEmpty query={collection.Query} gridRow={2} />
        } else {
            <VirtualizedList
                source={() => collection.Items}
                key={item => item.Id}
                row={item => ItemRow(collection.Current(item))}
                rowHeight={() => 68}
                selection={collection.Selection}
                viewport={collection.ViewportSession}
                gridRow={2}
                label={collection.AccessibleLabel} />
        }
    </Grid>
}

public component ItemRow(CurrentItem<LinkNoteSummary> current) {
    <Selectable
        selected={() => current.Value.Id == current.Owner.SelectedId}
        activate={() => current.Owner.Open(current.Value.Id)}
        style={ItemRowStyle}>
        <Column>
            <Text text={() => current.Value.Title} style={TwoLineTitleStyle} />
            <Row style={MetadataStyle}>
                <ItemKindIcon kind={() => current.Value.Kind} />
                <Text text={() => current.Value.MatchContext} />
                <SaveAdornment state={() => current.Value.SaveState} />
            </Row>
        </Column>
    </Selectable>
}
```

`CurrentItem<T>` is a placeholder name for the retained same-key update seam from issue #70. A realized row must read the latest immutable payload without remounting, losing focus, or manually refreshing a region. `viewport` represents an owned list scroll/anchor session and must receive the cell's assigned viewport rather than the whole window.

## Editor session

```lui
// PROPOSED — unsupported syntax and APIs
public component ItemEditor(EditorSession? session) {
    if (session is null) {
        <EditorEmpty />
    } else {
        <Grid rows="auto, auto, minmax(0, 1fr), auto" style={EditorStyle}>
            <TextField
                session={session.Title}
                label="Title"
                gridRow={0}
                style={TitleFieldStyle} />

            if (session.Url is not null) {
                <LinkField value={session.Url} open={session.OpenLink} gridRow={1} />
            }

            <MultilineTextEditor
                session={session.Body}
                label="Notes"
                wrap={TextWrap.WordWithGraphemeFallback}
                scroll={session.BodyViewport}
                gridRow={2}
                style={BodyEditorStyle} />

            <EditorFooter
                save={session.SaveStatus}
                archive={session.Archive}
                retry={session.RetrySave}
                gridRow={3} />
        </Grid>
    }
}
```

The editor takes a specific owned session contract rather than a general element handle or two-way binding escape hatch. The session separates portable draft/caret/selection/undo state from mounted Windows IME resources. The multiline control consumes constrained paragraph layout and supplies logical text ranges, caret and selection geometry, scrolling, input, and accessibility.

## Commands

```lui
// PROPOSED — unsupported syntax and APIs
public static AppCommands CreateCommands(LinksAndNotesState app) => new([
    Command.Bind(AppCommand.Capture, KeyChord.Ctrl("N"), app.FocusCapture),
    Command.Bind(AppCommand.Find, KeyChord.Ctrl("F"), app.FocusSearch),
    Command.Bind(AppCommand.Save, KeyChord.Ctrl("S"), app.SaveActive),
    Command.Bind(AppCommand.OpenLink, KeyChord.Ctrl("Enter"), app.OpenActiveLink),
    Command.Bind(AppCommand.Back, KeyChord.Alt("Left"), app.Back),
]);
```

The binding table is intentionally ordinary typed C# supplied to `.lui`. The required markup feature is composition around a command scope, not a new expression language. Command enablement, conflict diagnostics, focus routing, cancellation, and error behavior belong to the reusable command contract. UIA action invocation remains a separate adapter path that reaches the same application command.

## Styling and presentation sketch

```lui
// PROPOSED — properties named below do not all exist today
style ItemRowStyle {
    Padding: Insets.Symmetric(12, 10);
    MinHeight: 68;
    BorderBottom: Border.Hairline(Token.Brush.Divider);
    Background: Token.Brush.Surface;

    when Selected {
        Background: Token.Brush.Selection;
    }

    when Hover {
        Background: Token.Brush.Hover;
    }

    when FocusVisible {
        FocusRing: FocusRing.Inset(Token.Brush.Focus, 2);
    }
}

style BodyEditorStyle {
    MinWidth: 0;
    MinHeight: 0;
    Padding: Insets.Symmetric(20, 16);
    Clip: Clip.Rounded(Token.Radius.Editor);
    Border: Border.Uniform(Token.Brush.FieldBorder, 1);
    Typography: Token.Type.EditorBody;
}
```

Exact property families should follow Lucent's typed Style/Behavior boundary. Focus, selection, semantics, and action ownership remain behaviors even when styles respond to their states. Borders and clipping must share geometry with painting, input hit testing, focus rings, and semantic bounds.

