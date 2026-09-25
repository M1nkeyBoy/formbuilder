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

The builder reads versions 1 and 2 and always saves version 2. A version 1 builder rejects
version 2 files with a clear message instead of silently dropping anchors.

## Schema version 2

```json
{
  "schemaVersion": 2,
  "projectId": "9e9608a0-1ab6-4dd4-8da0-592260982971",
  "name": "Customer form",
  "screen": {
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
  }
}
```

### Project

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | integer | Required. `2` when saved by this builder; `1` is still read. |
| `projectId` | GUID string | Required. Stable for the life of the project. |
| `name` | string | Written as the file name (without extension) on save. |
| `screen` | object | Required. The single screen in a prototype project. |

### Screen

| Field | Type | Default | Notes |
|---|---|---|---|
| `id` | string | `"main"` | |
| `name` | string | `"Main"` | |
| `width`, `height` | integer | 800, 600 | Design size in DIPs; must be positive. |
| `gridSize` | integer | 10 | Grid spacing in DIPs; must be positive. |
| `controls` | array | empty | Draw order: later controls are drawn on top. |

### Control

| Field | Type | Notes |
|---|---|---|
| `id` | GUID string | Required, unique within the screen, never empty. |
| `type` | string | Required. One of `Label`, `Button`, `TextBox`, `CheckBox`, `ComboBox`. |
| `name` | string | Required. Letter or underscore first, then letters, digits or underscores. Unique within the screen, ignoring case. |
| `x`, `y` | integer | DIPs from the screen's top-left corner; not negative. |
| `width`, `height` | integer | DIPs; at least the type's minimum size. The control must fit inside the screen. |
| `properties` | object | Type-specific values, below. |
| `anchor` | array of strings | Screen edges the control follows when the window is resized: any of `left`, `top`, `right`, `bottom`, with at least one of left/right and one of top/bottom. Optional; defaults to `["left", "top"]`. |

Minimum sizes: Label 20 × 16, Button 30 × 20, TextBox 30 × 20, CheckBox 20 × 16,
ComboBox 40 × 20.

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
| ComboBox | | | ✓ (in display order) |

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
