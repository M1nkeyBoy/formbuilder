# Project file format (`.uibproj`)

A builder project is a UTF-8 JSON file with the `.uibproj` extension. It stores the design,
not generated application code. It is an internal prototype format: any change must be
recorded here, with a migration, before the code changes.

A complete example is in [`samples/customer-form.uibproj`](samples/customer-form.uibproj).

## Version history

| Version | Change | Migration when opening an older file |
|---|---|---|
| 1 | First prototype format. | — |
| 2 | Adds `anchor` to each control. | Every control gets `["left", "top"]`, which is how version 1 designs behaved. |
| 3 | Adds the `StackPanel` and `Grid` container types, with `children`, `row`, `column` and the container properties. | None: older files have no containers. |
| 4 | Adds `rowSpan` and `columnSpan` for controls inside a Grid. | None: older files have no spans (every control covers one cell). |
| 5 | Adds `rowSizes` and `columnSizes` to Grid properties. | None: older files have equal rows and columns. |
| 6 | Replaces the single `screen` with a list of `screens`. | The file's `screen` becomes the only entry in `screens`. Its name becomes `Main` if it is not an identifier (only possible in a hand-edited file). |
| 7 | Adds the types `RadioButton`, `ListBox`, `Slider`, `ProgressBar`, `DatePicker`, `PasswordBox` and the container `GroupBox`, and the properties `isMultiline`, `minimum`, `maximum` and `value`. | None: older files use none of them. |
| 8 | Adds `opensScreen` and `closesScreen` to Buttons. | None: older buttons do nothing but call their hook. |

The builder reads versions 1 to 8 and always saves version 8. An older builder rejects a
newer file with a clear message instead of silently dropping what it does not know.

## Schema version 8

```json
{
  "schemaVersion": 8,
  "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971",
  "name": "Customer form",
  "screens": [{
    "id": "main",
    "name": "Main",
    "width": 800,
    "height": 600,
    "gridSize": 10,
    "controls": [
      {
        "id": "9e651cb8-84b8-4140-a171-62075666768e",
        "type": "Button",
        "name": "SubmitButton",
        "x": 100,
        "y": 100,
        "width": 120,
        "height": 32,
        "properties": { "text": "Submit" },
        "anchor": ["top", "right"]
      }
    ]
  }]
}
```

### Project

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | integer | Required. `8` when saved by this builder; `1` to `7` are still read. |
| `projectId` | GUID string | Required. Stable for the life of the project. |
| `name` | string | Written as the file name (without extension) on save. |
| `screens` | array | Required, at least one. In order: the first is the main screen, which a generated application opens with. Versions 1 to 5 had a single `screen` object instead. |

### Screen

| Field | Type | Default | Notes |
|---|---|---|---|
| `id` | string | `"main"` | Required. Stable, unique within the project. New screens get a 32-character hex ID. |
| `name` | string | `"Main"` | Letter or underscore first, then letters, digits or underscores. Unique within the project, ignoring case. It names the screen's generated window (`SettingsWindow`, `SettingsForm`). |
| `width`, `height` | integer | 800, 600 | Design size in DIPs; must be positive. |
| `gridSize` | integer | 10 | Grid spacing in DIPs; must be positive. |
| `controls` | array | empty | Draw order: later controls are drawn on top. |

### Control

| Field | Type | Notes |
|---|---|---|
| `id` | GUID string | Required, unique within the project, never empty. |
| `type` | string | Required. One of `Label`, `Button`, `TextBox`, `PasswordBox`, `CheckBox`, `RadioButton`, `ComboBox`, `ListBox`, `Slider`, `ProgressBar`, `DatePicker`, or the containers `StackPanel`, `Grid` and `GroupBox`. |
| `name` | string | Required. Letter or underscore first, then letters, digits or underscores. Unique within its screen, ignoring case; other screens may reuse it. |
| `x`, `y` | integer | DIPs from the screen's top-left corner; not negative. |
| `width`, `height` | integer | DIPs; at least the type's minimum size. The control must fit inside the screen. |
| `properties` | object | Type-specific values, below. |
| `children` | array of controls | Containers only: the controls inside, in order. |
| `row`, `column` | integer | Only for a control inside a Grid: the cell it fills, counted from 0. |
| `rowSpan`, `columnSpan` | integer | Only for a control inside a Grid: how many rows and columns it covers, starting at its cell. Optional; omitted when 1. It must fit inside the grid. |
| `anchor` | array of strings | Screen edges the control follows when the window is resized: any of `left`, `top`, `right`, `bottom`, with at least one of left/right and one of top/bottom. Optional; defaults to `["left", "top"]`. |

Minimum sizes: Label 20 × 16, Button 30 × 20, TextBox and PasswordBox 30 × 20, CheckBox and
RadioButton 20 × 16, ComboBox 40 × 20, ListBox 40 × 30, Slider 40 × 20, ProgressBar 20 × 8,
DatePicker 80 × 20, StackPanel and Grid 20 × 20, GroupBox 40 × 40.

### Containers

A control inside a container has no position of its own; the container places it:

- **StackPanel** lines its children up in `children` order: top to bottom when `orientation`
  is `Vertical`, left to right when `Horizontal`, with `spacing` DIPs between them. Each
  child keeps its `height` (vertical) or `width` (horizontal) and stretches across the
  stack. Children that do not fit are clipped.
- **GroupBox** is a frame with its `text` as a title. It lines up its children exactly like a
  StackPanel, inside an area 8 DIPs in from its left, right and bottom edges and 20 DIPs
  down from its top, which leaves room for the frame and title.
- **Grid** divides itself into `rows` × `columns` cells. Fixed rows and columns get their size;
  the rest share the remaining space by weight (equally by default). If the fixed sizes do
  not fit, the shared ones get nothing and the overflow is clipped. Each child fills the cell
  at its `row` and `column`, extended over `rowSpan` rows and `columnSpan` columns.

A child's `x`, `y` and `anchor` are ignored and saved as `0` and the default. Its `width` and
`height` are kept, so it keeps its size if moved back onto the screen. Containers can be
nested. Names and IDs are unique across the whole screen, including inside containers.

### Anchors

Anchors describe what happens when a generated window is made larger than the design size:

- Anchored to **left** (or **top**) only: the control keeps its position.
- Anchored to **right** (or **bottom**) only: it keeps its distance from that edge, so it moves.
- Anchored to **both** left and right (or top and bottom): it keeps both distances, so it
  stretches.

### Type-specific properties

| Type | `text` (string) | `isChecked` (boolean) | `items` (array of strings) |
|---|---|---|---|
| Label | ✓ | | |
| Button | ✓ | | |
| TextBox | ✓ | | |
| CheckBox | ✓ | ✓ | |
| RadioButton | ✓ | ✓ | |
| ComboBox | | | ✓ (in display order) |
| ListBox | | | ✓ (in display order) |
| GroupBox | ✓ (the title) | | |

PasswordBox and DatePicker have no type-specific properties: a password is never stored, and
a DatePicker starts with no date chosen. RadioButtons in the same container (or directly on
the screen) are one group: choosing one in the editor clears the others.

A Button may have an action: `opensScreen`, the `id` of a screen it opens as a dialog over
its own, or `closesScreen: true` to close its own screen. Not both; the screen must exist.
Deleting a screen in the editor removes the buttons' links to it.

A TextBox may have `isMultiline: true` for several lines of text that wrap; it is omitted for
a single line.

Slider and ProgressBar have whole-number `minimum`, `maximum` and `value`: the minimum is less
than the maximum, the value lies between them, and all are within ±1,000,000.

| Type | `orientation` | `spacing` | `rows` | `columns` |
|---|---|---|---|---|
| StackPanel, GroupBox | `Vertical` or `Horizontal` | 0 to 200 DIPs | | |
| Grid | | | 1 to 20 | 1 to 20 |

A Grid may also have `rowSizes` and `columnSizes`: one string per row or column, in WPF's
notation. `"100"` is a fixed size in DIPs (1 to 10000). `"*"` or `"2*"` is a share of the space
left after the fixed rows or columns, by weight. Both are optional and omitted when every
size is `"*"`.

A missing supported property is read as `""`, `false` or `[]`. A property the type does not
support is ignored on load and not written on save.

## Not stored

The current selection, Design/Preview mode, undo history, window layout and any values typed
into controls in Preview are never written to the project file. Recovery drafts are stored
separately and do not use this file.

## Loading rules

- The file must be JSON with an integer `schemaVersion` of 1 or 2.
- A `schemaVersion` newer than the builder supports is rejected with a message; the builder
  never guesses how to read a future format.
- An unknown control `type` is rejected with a message naming the type and control.
- IDs, names, sizes, bounds and anchors are validated; every problem found is listed. An
  unknown anchor edge name is named in the message.
- A file that fails to load never replaces the project that is currently open.

## Saving rules

- The file is written to a temporary file in the same folder and then moved over the
  destination, so an interrupted save cannot leave a truncated project.
- Saving the same document again produces the same bytes.
- A project can be saved at any stage, including with no controls.
