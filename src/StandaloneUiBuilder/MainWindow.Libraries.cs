using System.Windows;
using AutomationProperties = System.Windows.Automation.AutomationProperties;
using System.Windows.Controls;
using System.Windows.Input;
using StandaloneUiBuilder.Core;

namespace StandaloneUiBuilder;

/// <summary>
/// Control libraries in the main window: the inspector's Library section, which edits a
/// library control's values, and Project > Libraries.
/// </summary>
public partial class MainWindow
{
    /// <summary>A field of the Library section: the property it edits.</summary>
    private sealed record LibraryField(LibraryProperty Property);

    /// <summary>The library control and properties the Library section's fields were built for.</summary>
    private (Guid Id, LibraryControl Library)? libraryFieldsFor;

    private const string DefaultChoice = "(library default)";

    private Preview.LibraryPreview? libraryPreview;
    private readonly System.Windows.Threading.DispatcherTimer previewRedraw = new() { Interval = TimeSpan.FromMilliseconds(80) };

    /// <summary>
    /// WPF and Windows Forms library controls are drawn as they look, by the preview host; the
    /// canvas is redrawn as drawings arrive (a few at once make one redraw).
    /// </summary>
    private void SetUpLibraryPreview()
    {
        libraryPreview = new Preview.LibraryPreview(() => editor.Document, Dispatcher);
        Design.ControlFactory.LibraryRenderer = libraryPreview.Draw;
        Design.ControlFactory.LibraryNote = control => editor.Document.Platform is ProjectPlatform.Wpf or ProjectPlatform.WinForms
            ? libraryPreview.ErrorFor(control) is { } error ? $"Not drawn: {error}" : null
            : $"The builder draws {editor.Document.Platform.DisplayName()} controls as boxes; the exported app shows the real one.";
        previewRedraw.Tick += (_, _) =>
        {
            // Not in the middle of a drag, which a redraw would end.
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                return;
            }

            previewRedraw.Stop();
            RenderSurface();
        };
        libraryPreview.Updated += (_, _) =>
        {
            previewRedraw.Stop();
            previewRedraw.Start();
        };
    }

    /// <summary>
    /// Shows the Library section for a library control: a field for each property it offers,
    /// empty (or the library default) until the design sets a value.
    /// </summary>
    private void RefreshLibraryInspector(ControlDocument control)
    {
        var library = control.Type == ControlType.Custom ? LibraryValues.Find(editor.Document, control.Properties.LibraryType) : null;
        LibraryPropertiesPanel.Visibility = Show(control.Type == ControlType.Custom);
        if (control.Type != ControlType.Custom)
        {
            libraryFieldsFor = null;
            LibraryPropertiesPanel.Children.Clear();
            return;
        }

        var package = LibraryValues.PackageOf(editor.Document, control.Properties.LibraryType);
        LibraryInfoText.Text = library is null
            ? $"{control.Properties.LibraryType}, from a library the project no longer has. Add it again in Project > Libraries."
            : $"{library.TypeName}, from {package!.Id} {package.Version}. Empty fields use the library's defaults.";
        if (library is null)
        {
            libraryFieldsFor = null;
            LibraryPropertiesPanel.Children.Clear();
            return;
        }

        if (libraryFieldsFor is not { } built || built.Id != control.Id || !ReferenceEquals(built.Library, library))
        {
            BuildLibraryFields(control, library);
        }

        var settings = control.Properties.LibrarySettings ?? [];
        refreshingInspector = true;
        foreach (var element in LibraryPropertiesPanel.Children.OfType<DockPanel>().Select(row => row.Children[^1]))
        {
            var field = (LibraryField)((FrameworkElement)element).Tag;
            var value = settings.FirstOrDefault(s => s.Name == field.Property.Name)?.Value;
            switch (element)
            {
                case TextBox box:
                    SetField(box, value ?? "");
                    break;
                case ComboBox choice:
                    choice.SelectedItem = value ?? DefaultChoice;
                    break;
            }
        }

        refreshingInspector = false;
    }

    private void BuildLibraryFields(ControlDocument control, LibraryControl library)
    {
        foreach (var box in LibraryPropertiesPanel.Children.OfType<DockPanel>().Select(r => r.Children[^1]).OfType<TextBox>())
        {
            SetFieldError(box, null);
        }

        LibraryPropertiesPanel.Children.Clear();
        libraryFieldsFor = (control.Id, library);
        var properties = (library.TypeParameters ?? []).Select(t => new LibraryProperty { Name = t, Type = "System.Type" }).Concat(library.Properties);
        foreach (var property in properties)
        {
            var row = new DockPanel { Style = (Style)FindResource("FieldRow"), ToolTip = Describe(property) };
            FrameworkElement editorElement;
            if (property.Kind is LibraryValueKind.Flag or LibraryValueKind.Choice)
            {
                var choices = new List<string> { DefaultChoice };
                choices.AddRange(property.Kind == LibraryValueKind.Flag ? ["True", "False"] : property.Choices ?? []);
                var combo = new ComboBox { ItemsSource = choices, Tag = new LibraryField(property) };
                combo.SelectionChanged += LibraryChoice_SelectionChanged;
                editorElement = combo;
            }
            else
            {
                var box = new TextBox { Style = (Style)FindResource("FieldBox"), Tag = new LibraryField(property) };
                box.LostKeyboardFocus += (_, _) => CommitField(box);
                box.KeyDown += InspectorField_KeyDown;
                editorElement = box;
            }

            AutomationProperties.SetName(editorElement, property.Name);
            AutomationProperties.SetAutomationId(editorElement, "Library" + property.Name);
            var label = new Label { Content = property.Name, Target = editorElement, Style = (Style)FindResource("FieldLabel") };
            row.Children.Add(label);
            row.Children.Add(editorElement);
            LibraryPropertiesPanel.Children.Add(row);
        }

        if (!properties.Any())
        {
            LibraryPropertiesPanel.Children.Add(new TextBlock
            {
                Text = "The builder found no simple properties to set; set others in your own code.",
                Style = (Style)FindResource("MutedText"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private static string Describe(LibraryProperty property) => property.Kind switch
    {
        LibraryValueKind.Text => $"{property.Name}: text.",
        LibraryValueKind.Whole => $"{property.Name}: a whole number ({property.Type}).",
        LibraryValueKind.Number => $"{property.Name}: a number ({property.Type}).",
        LibraryValueKind.Flag => $"{property.Name}: on or off.",
        LibraryValueKind.TypeArgument => $"{property.Name}: the type the component works with, such as string, int or DateTime.",
        _ => $"{property.Name}: one of {property.Type}'s values.",
    };

    /// <summary>Applies a Library section text field; returns true if the box was one.</summary>
    private bool CommitLibraryField(TextBox box)
    {
        if (box.Tag is not LibraryField field)
        {
            return false;
        }

        if (inspectedId is { } id)
        {
            var error = editor.SetLibrarySetting(id, field.Property.Name, box.Text);
            SetFieldError(box, error);
            if (error is null)
            {
                RefreshInspector();
            }
        }

        return true;
    }

    private void LibraryChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingInspector && inspectedId is { } id && sender is ComboBox { Tag: LibraryField field, SelectedItem: string choice })
        {
            anchorError = editor.SetLibrarySetting(id, field.Property.Name, choice == DefaultChoice ? "" : choice);
            UpdateInspectorErrors();
        }
    }

    private void Libraries_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !isPreview;

    /// <summary>Project > Libraries: add, update or remove the project's control libraries, and set licence keys.</summary>
    private void Libraries_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CommitFocusedField();
        if (editor.Document.Platform == ProjectPlatform.Any)
        {
            var answer = MessageBox.Show(this,
                "Control libraries exist for one platform only, and this project is for any platform. Choose its platform now?",
                AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes || NewProjectWindow.Choose(this, ProjectPlatform.Wpf, title: "Choose the project's platform") is not { } platform
                || platform == ProjectPlatform.Any)
            {
                return;
            }

            editor.SetPlatform(platform);
        }

        LibrariesWindow.Show(this, editor);
    }

    /// <summary>
    /// Changing the platform drops the libraries and the controls placed from them, since they
    /// exist on their platform only: the user confirms first.
    /// </summary>
    private bool ConfirmPlatformChange(ProjectPlatform platform)
    {
        if (platform == editor.Document.Platform || editor.Document.Libraries is not { Count: > 0 } libraries)
        {
            return true;
        }

        var controls = editor.LibraryControlCount;
        var what = $"{libraries.Count} librar{(libraries.Count == 1 ? "y" : "ies")}" + (controls > 0 ? $" and the {controls} control{(controls == 1 ? "" : "s")} placed from them" : "");
        return MessageBox.Show(this,
            $"The project's libraries are for {editor.Document.Platform.DisplayName()}. Changing to {platform.DisplayName()} removes {what}. Undo puts them back.{Environment.NewLine}{Environment.NewLine}Change the platform?",
            AppTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }
}
