using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RecipeEditor.Core.Project;

namespace RecipeEditor.Gui;

/// <summary>
/// "New recipe": from scratch (an empty recipe to fill) or a copy of a game recipe; an English name for now, a
/// description, and where players learn it (a cookbook of the mod or of the game, or known from the start).
/// </summary>
public sealed class NewRecipeWindow : Window
{
    private readonly ProjectDocument _doc;
    private readonly Func<IReadOnlyCollection<string>, string?> _pickRecipe;
    private readonly RadioButton _scratch, _copy;
    private readonly TextBlock _copyName = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox _name, _description;
    private readonly TextBlock _ids, _error;
    private readonly ComboBox _learn = new() { FontSize = 12 };
    private readonly Button _create;
    private string? _copyOf;
    private string? _icon;
    private readonly IconView _iconView = new() { Width = 56, Height = 56, LetterSize = 26 };
    private readonly TextBlock _iconNote = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    public string? CreatedId { get; private set; }

    private Brush Res(string key) => (Brush)FindResource(key);

    public NewRecipeWindow(Window owner, ProjectDocument doc, Func<IReadOnlyCollection<string>, string?> pickRecipe)
    {
        Owner = owner;
        _doc = doc;
        _pickRecipe = pickRecipe;
        Style = (Style)FindResource("AppWindow");
        Title = Loc.T("NewRecipeTitle");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Native.DarkTitleBar(this);

        var root = new StackPanel { Margin = new Thickness(22, 16, 22, 18) };
        root.Children.Add(new TextBlock { Text = Loc.T("NewRecipeTitle"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });

        // from scratch / copy of a game recipe
        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
        _scratch = Card("", Loc.T("FromScratch"), Loc.T("FromScratchDesc"), true, new Thickness(0, 0, 5, 0));
        _copy = Card("", Loc.T("CopyOfGame"), Loc.T("CopyOfGameDesc"), false, new Thickness(5, 0, 0, 0));
        cards.Children.Add(_scratch);
        cards.Children.Add(_copy);
        root.Children.Add(cards);
        var chooseRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var choose = new Button { Content = Loc.T("ChooseRecipe"), Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 5, 10, 5) };
        System.Windows.Automation.AutomationProperties.SetName(choose, "chooserecipe");
        choose.Click += (_, _) =>
        {
            var id = _pickRecipe(_doc.Project.NewRecipes.ToList());
            if (id == null) return;
            _copyOf = id;
            _copy.IsChecked = true;
            Refresh();
        };
        DockPanel.SetDock(choose, Dock.Right);
        chooseRow.Children.Add(choose);
        chooseRow.Children.Add(_copyName);
        root.Children.Add(chooseRow);

        // icon (the dish's) beside the English name
        var nameRow = new Grid();
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition());
        var iconButton = new Button
        {
            Style = (Style)FindResource("Bare"), VerticalAlignment = VerticalAlignment.Top, ToolTip = Loc.T("ChooseIcon"),
            Content = new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(5), Background = Res("Slot"), BorderBrush = Res("SlotEdge"), BorderThickness = new Thickness(1),
                Child = new Grid { Children = { _iconView, new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Background = Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, 2),
                    Child = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10, Foreground = Res("Text2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                } } },
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(iconButton, "recipeicon");
        iconButton.Click += (_, _) => PickIcon();
        nameRow.Children.Add(iconButton);
        var nameCol = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(nameCol, 1);
        nameRow.Children.Add(nameCol);
        root.Children.Add(nameRow);

        // English name and description
        nameCol.Children.Add(Label(Loc.T("RecipeNameEn")));
        _name = new TextBox { Style = (Style)FindResource("HintField"), FontSize = 13, Padding = new Thickness(8, 4, 8, 4), Tag = "Crazy Salad" };
        System.Windows.Automation.AutomationProperties.SetName(_name, "recipename");
        nameCol.Children.Add(_name);
        _ids = new TextBlock { FontSize = 11, Foreground = Res("Muted"), Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap };
        nameCol.Children.Add(_ids);
        _iconNote.Foreground = Res("Muted");
        nameCol.Children.Add(_iconNote);
        root.Children.Add(Label(Loc.T("RecipeDescriptionEn"), 12));
        _description = new TextBox { Style = (Style)FindResource("HintField"), FontSize = 13, Padding = new Thickness(8, 4, 8, 4), Tag = Loc.T("OptionalHint2") };
        root.Children.Add(_description);
        root.Children.Add(new TextBlock { Text = Loc.T("EnglishOnlyNote"), Style = (Style)FindResource("Small"), Margin = new Thickness(0, 6, 0, 0) });

        // where it is learnt
        root.Children.Add(Label(Loc.T("LearnFrom"), 14));
        foreach (var (key, text) in LearnOptions()) _learn.Items.Add(new ComboBoxItem { Content = text, Tag = key });
        _learn.SelectedIndex = 0;
        root.Children.Add(_learn);
        var warn = new Border
        {
            Style = (Style)FindResource("Callout"), Background = Res("WarnBg"), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed,
            Child = new TextBlock { Text = Loc.T("KnownWarning"), Foreground = Res("Warn"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
        };
        root.Children.Add(warn);
        _learn.SelectionChanged += (_, _) => warn.Visibility = (_learn.SelectedItem as ComboBoxItem)?.Tag as string == "known" ? Visibility.Visible : Visibility.Collapsed;

        _error = new TextBlock { FontSize = 12, Foreground = Res("Danger"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        root.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = Loc.T("Cancel"), Style = (Style)FindResource("Btn"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        _create = new Button { Content = Loc.T("CreateRecipe"), Style = (Style)FindResource("Primary"), IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetName(_create, Loc.T("CreateRecipe"));
        _create.Click += (_, _) => Create();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_create);
        root.Children.Add(buttons);
        Content = root;

        _name.TextChanged += (_, _) => Refresh();
        _scratch.Checked += (_, _) => Refresh();
        _copy.Checked += (_, _) => Refresh();
        Loaded += (_, _) => _name.Focus();
        Refresh();
    }

    private TextBlock Label(string text, double top = 0) =>
        new() { Text = text, Foreground = Res("Text2"), FontSize = 12, Margin = new Thickness(0, top, 0, 6) };

    private RadioButton Card(string glyph, string title, string description, bool on, Thickness margin)
    {
        var p = new StackPanel();
        var h = new StackPanel { Orientation = Orientation.Horizontal };
        h.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 14, Margin = new Thickness(0, 0, 8, 0) });
        h.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        p.Children.Add(h);
        p.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var r = new RadioButton { Content = p, Style = (Style)FindResource("Card"), GroupName = "kind", IsChecked = on, Margin = margin };
        System.Windows.Automation.AutomationProperties.SetName(r, title);
        return r;
    }

    /// <summary>Own cookbooks first, then the game's, then "known from the start".</summary>
    private IEnumerable<(string Key, string Text)> LearnOptions()
    {
        foreach (var b in _doc.Cooking.OwnBooks.Keys) yield return (b, Loc.F("LearnOwnBook", _doc.OwnBookTexts(b).Name));
        foreach (var b in _doc.Game.Books) yield return (b.Id, b.Name);
        yield return ("known", Loc.T("GroupKnown"));
    }

    /// <summary>The dish the new recipe's dish copies (its model; its icon unless one is chosen).</summary>
    private string? TemplateDish()
    {
        var template = _scratch.IsChecked == true ? _doc.ScratchTemplate() : _copyOf;
        return template == null ? null : _doc.Game.Recipe(template)?.Result;
    }

    private void PickIcon()
    {
        var w = new IconPickerWindow(this, Loc.T("RecipeIconTitle"), [IconTab.Dishes(), IconTab.Food(), IconTab.All()], _icon ?? _doc.Game.ItemIcon(TemplateDish()));
        if (w.ShowDialog() != true) return;
        _icon = w.Result;
        Refresh();
    }

    private void Refresh()
    {
        var dish = TemplateDish();
        _iconView.Texture = _icon ?? _doc.Game.ItemIcon(dish);
        _iconView.Fallback = "?";
        _iconNote.Text = _icon != null ? Loc.T("OwnIconChosen") + " · " + Loc.F("ModelAs", _doc.Game.ItemName(dish))
            : dish != null ? Loc.F("IconModelAs", _doc.Game.ItemName(dish)) : "";
        _copyName.Text = _copyOf != null ? _doc.Game.Recipe(_copyOf)?.Name ?? _copyOf : Loc.T("NoRecipeChosen");
        _copyName.Foreground = Res(_copyOf != null ? "Text" : "Muted");
        var name = _name.Text.Trim();
        var ok = ProjectDocument.IsEnglishName(name);
        _name.BorderBrush = Res(name.Length == 0 || ok ? "Line" : "Danger");
        var key = ok ? ProjectDocument.NameKey(name) : "…";
        _ids.Text = Loc.F("NewRecipeIds", $"Cook_Dish_{_doc.Project.Name}_{key}", $"Dish_{_doc.Project.Name}_{key}");
        _create.IsEnabled = ok && (_scratch.IsChecked == true || _copyOf != null);
    }

    private void Create()
    {
        try
        {
            var template = _scratch.IsChecked == true ? _doc.ScratchTemplate() : _copyOf!;
            var learn = (_learn.SelectedItem as ComboBoxItem)?.Tag as string ?? "known";
            CreatedId = _doc.CreateRecipe(template, _name.Text.Trim(), _description.Text.Trim(), _scratch.IsChecked == true, learn, _icon);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _error.Text = ex.Message;
            _error.Visibility = Visibility.Visible;
        }
    }
}
