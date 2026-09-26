# Standalone UI Builder: WPF design handoff

Implement the supplied `UiBuilderRedesign.xaml` appearance in the existing WPF application. It is a presentation reference, with sample content and named integration points, not a replacement for the existing editor logic. Preserve all current functionality and project formats.

## Integration

- Merge the resource brushes and styles into the existing window or a dedicated resource dictionary. Add the application's actual `x:Class` if using this as a compiled window. Keep the existing code-behind and view model; reconcile names rather than duplicating controls.
- Retain the native Windows title bar and window controls. The project header below it contains the project name, Save, and Export. Keep File/Edit/Format/Screen/Project/View commands available through the existing menu, a compact menu, or an overflow menu; do not remove commands absent from this reference.
- Replace `SampleArtboard` and its entire sample form with the existing designer surface, including its Canvas, selection adorners, resize handles, drag-and-drop, hit testing, snapping, keyboard navigation, scroll behavior, and preview renderer. The sample form is illustrative and must not be inserted into users' projects. Its display dimensions do not replace the actual 800 × 600 DIP screen model.
- Bind screen tabs to the actual screen collection and selection. This reference places the design surface below the tab strip; do not rely on the empty sample TabItem content as the designer host. Preserve add, rename, main-screen selection, and existing screen management actions.
- Populate the control library from the actual supported control types. The tile Tag values show the intended mapping. Wire selection, drag-and-drop, Enter insertion, and search to existing logic. Bind Layers to real screen controls and keep layer and canvas selection synchronized.
- Bind the inspector to the selected control, with type-appropriate fields. Group identity/content, layout, appearance, and interaction. Preserve all existing properties, including foreground/background colors, font weight, anchor flags, control-specific settings, and click actions. Hide irrelevant groups for each type. When selection is empty, show Screen settings.
- Preserve numeric validation, DIP units, canvas bounds, naming rules, undo/redo, dirty state, save/open/import, project themes, and shortcuts. The literal values in this reference are samples, not bindings or defaults to overwrite in existing projects.
- Wire Design/Preview, Save, Undo/Redo, Align/Arrange, Grid, and Fit to canvas to actual commands. Keep multi-selection alignment and tab-order tools available. Wire Export to a framework picker offering the currently supported WPF, WinForms, WinUI 3, .NET MAUI, and Blazor exporters.
- Use the existing theme system to replace the semantic DynamicResource brushes for dark mode. The supplied brush values are the light theme. The tiled DrawingBrush uses those same semantic resources.
- Retain native ComboBox behavior or integrate the application's tested ComboBox style. This reference intentionally keeps the native popup template. Replace the few Unicode symbols with the application's existing icon system if available; no third-party icon package is required.

## Layout and scope

Resizable three-column workspace: controls/layers, canvas, inspector. Both side panels have minimum widths and splitters; long content scrolls vertically. The desktop window has a 960 × 700 minimum. This is a native desktop layout, not the stacked small-screen web preview.

The XAML does not include export dialog logic, command bindings, converters, search filtering, persistence, or interaction code. Implement these using the existing app architecture. Avoid rebuilding the editor engine.

## Acceptance checks

Build and launch on the app's existing target framework. Verify control insertion/selection/resizing, screen switching, inspector edits, undo/redo, project save/reopen, preview, and each existing exporter. Check keyboard focus, accessible labels, validation, long names, 100%/150%/200% display scaling, window resizing, and both app themes. Compare the populated editor with the design reference after integrating the real surface.
