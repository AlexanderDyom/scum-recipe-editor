using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Project;
using RecipeEditor.Core.Recipe;

namespace RecipeEditor.Gui;

/// <summary>A row of the recipe list; its badge follows the edits.</summary>
public sealed class RecipeRowView(RecipeEntry entry, GameData game, ProjectDocument doc, string groupTitle) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public RecipeEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Initial => Entry.Name.Length > 0 ? char.ToUpper(Entry.Name[0], Loc.Instance.Culture).ToString() : "?";
    public string GroupTitle { get; } = groupTitle;

    /// <summary>Icon of the dish it makes (a new recipe's clone has none in the game: its template's dish).</summary>
    public string? Icon => doc.ItemIcon(doc.State(Entry.Id)?.Result) ?? doc.ItemIcon(doc.Base(Entry.Id)?.Result);

    /// <summary>Differs from the game now (saved or not).</summary>
    public bool IsChanged => !Entry.IsNew && doc.State(Entry.Id) is { } s && doc.GameState(Entry.Id) is { } g && !Editing.SameWithMarks(s, g);

    public string Line
    {
        get
        {
            if (Entry.Group == RecipeGroupKind.Hidden) return Loc.T("HiddenLine");
            var parts = new List<string>();
            if (Entry.Group == RecipeGroupKind.New)
            {
                parts.AddRange(Entry.BookNames);
                if (Entry.BookNames.Count == 0 && Entry.KnownByDefault) parts.Add(Loc.T("KnownLine"));
            }
            if (doc.State(Entry.Id) is { } r)
            {
                if (r.Utilities.Count > 0) parts.Add(game.UtilityName(r.Utilities[0]).ToLower(Loc.Instance.Culture));
                if (r.CookingTime is { } t) parts.Add(Format.Time(t));
            }
            return string.Join(" · ", parts);
        }
    }

    public string? Tip => Entry.Group == RecipeGroupKind.Hidden ? Loc.T("HiddenTip") : null;

    public string Badge => Entry.IsNew ? Loc.T("BadgeNewRecipe") : IsChanged ? Loc.T("BadgeChanged") : Entry.HiddenInGame ? Loc.T("BadgeHidden") : "";
    public Visibility BadgeVisibility => Badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Brush BadgeBg => Res(Entry.IsNew ? "AccentBg" : IsChanged ? "WarnBg" : "Surface2");
    public Brush BadgeFg => Res(Entry.IsNew ? "AccentText" : IsChanged ? "Warn" : "Muted");
    public Brush NameBrush => Res(Entry.Group == RecipeGroupKind.Hidden ? "Muted" : "Text");

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}

internal static class Format
{
    /// <summary>40 с, 4 мин, 26,7 мин.</summary>
    public static string Time(double seconds)
    {
        var c = Loc.Instance.Culture;
        if (seconds < 60 || (seconds < 120 && seconds % 60 != 0)) return Loc.F("Seconds", Math.Round(seconds).ToString(c));
        return Loc.F("Minutes", Math.Round(seconds / 60, 1).ToString("0.#", c));
    }

    /// <summary>Amount of a variant as the cooking window shows it: ×1, 3 исп., 0,25 л.</summary>
    public static string Amount(SlotOption o)
    {
        var c = Loc.Instance.Culture;
        if (o.IsResource)
            return o.Liters < 1 ? Loc.F("Milliliters", Math.Round(o.Liters * 1000).ToString(c)) : Loc.F("Liters", Math.Round(o.Liters, 3).ToString("0.###", c));
        return o.Whole ? Loc.F("Whole", o.Usage) : Loc.F("Uses", o.Usage);
    }

    public static string Number(double v) => v.ToString("0.###", Loc.Instance.Culture);

    /// <summary>"20", "20,5" or "20.5" -> 20.5; null when empty; NaN when not a number.</summary>
    public static double? Parse(string text)
    {
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0) return null;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : double.NaN;
    }
}

/// <summary>Comparisons the editor needs beyond RecipeState.SameAs.</summary>
internal static class Editing
{
    /// <summary>Same recipe and the same slots struck out (what the editor shows).</summary>
    public static bool SameWithMarks(RecipeState a, RecipeState b) =>
        a.SameAs(b) && Groups.All.All(g => a.Slots[g].Count(s => s.Removed) == b.Slots[g].Count(s => s.Removed));

    /// <summary>A kept game slot differs from the game's one.</summary>
    public static bool Changed(Slot s, Slot? game) => game != null && !s.IsNew && !s.Removed && (!s.SameAs(game) || s.Title != game.Title);
}

/// <summary>The main window: rail, recipe list (groups, chips, search) and the recipe editor with the slot inspector.</summary>
public partial class MainWindow : Window
{
    private readonly RecipeProject _project;
    private GameData? _game;
    private ProjectDocument? _doc;
    private RecipeCatalog? _catalog;
    private ICollectionView? _view;
    private string? _selectedId;
    private RecipeRowView? _row;
    private RecipeState? _state;
    private RecipeState? _gameState;
    private (string Group, Slot Slot)? _slot;
    private bool _filling;
    /// <summary>Variant of the selected slot whose amount the inspector shows.</summary>
    private int _variant;

    private bool ReadOnly => _project.ReadOnly;

    public MainWindow(RecipeProject project)
    {
        _project = project;
        InitializeComponent();
        Native.DarkTitleBar(this);
        _selectedId = project.Editor.SelectedRecipe;
        Title = $"{project.Name} — SCUM Recipe Editor";
        ProjectName.Text = project.Name;
        ProjectPak.Text = project.Pak + ".pak";
        ReadOnlyBadge.Visibility = project.ReadOnly ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.Visibility = project.ReadOnly ? Visibility.Collapsed : Visibility.Visible;
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Save()), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(Undo), Key.Z, ModifierKeys.Control));
        Session.Changed += OnSession;
        Loc.Instance.PropertyChanged += OnLanguage;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            Session.Changed -= OnSession;
            Loc.Instance.PropertyChanged -= OnLanguage;
        };
        ShowProjectStatus();
        Load();
    }

    private void OnSession() => Load();

    private void OnLanguage(object? s, PropertyChangedEventArgs e)
    {
        ShowProjectStatus();
        if (NavRecipes.IsChecked != true) ShowSection();
    }

    private void ShowProjectStatus()
    {
        var dirty = _doc?.IsDirty == true;
        string icon, color, text;
        if (ReadOnly) (icon, color, text) = ("", "Warn", "ReadOnlyStatus");
        else if (dirty) (icon, color, text) = ("", "Warn", "UnsavedStatus");
        else if (Session.Paks != null && _project.InstalledPak(Session.Paks) is { } pak && pak.LastWriteTime < File.GetLastWriteTime(_project.ModkitPath))
            (icon, color, text) = ("", "Warn", "NotBuiltStatus");
        else (icon, color, text) = ("", "Ok", Session.Paks != null && _project.InstalledPak(Session.Paks) != null ? "BuiltStatus" : "SavedStatus");
        ProjectStatusIcon.Text = icon;
        ProjectStatusIcon.Foreground = (Brush)FindResource(color);
        ProjectStatus.Text = Loc.T(text);
        SaveButton.IsEnabled = dirty && !ReadOnly;
        BuildButton.IsEnabled = !ReadOnly && _doc != null;
        NewRecipeButton.IsEnabled = !ReadOnly && _doc != null;
        BuildButton.ToolTip = ReadOnly ? Loc.T("BuildReadOnlyTip") : null;
        Title = $"{(dirty ? "● " : "")}{_project.Name} — SCUM Recipe Editor";
    }

    /// <summary>Waits for the game's data (read in the background after the key check) and fills the list.</summary>
    private async void Load()
    {
        var task = Session.Data;
        if (task == null) return;
        if (!task.IsCompleted) ShowListMessage(Loc.T("Loading"), "");
        try
        {
            var game = await task;
            if (!ReferenceEquals(task, Session.Data)) return; // reloaded meanwhile
            var old = _doc;
            _game = game;
            _doc = new ProjectDocument(_project, game);
            if (old != null) _doc.TakeEditsFrom(old); // a language change keeps unsaved edits
            Fill();
            // the item catalog adds icons and names of items no stock recipe uses (e.g. crackers in a mod's slot)
            Session.Catalog?.ContinueWith(_ => Dispatcher.BeginInvoke(() =>
            {
                foreach (var r in Rows) r.Refresh();
                if (_slot == null) Render();
            }));
        }
        catch (Exception ex)
        {
            Session.Errors++;
            ShowListMessage(Loc.T("LoadFailed"), ex.Message);
        }
    }

    private void Fill()
    {
        if (_game == null || _doc == null) return;
        _catalog = RecipeCatalog.Build(_game, _project, id => _doc.State(id)?.Name); // unsaved names of new recipes too
        var titles = _catalog.Groups.ToDictionary(g => (g.Kind, g.Book ?? ""), g => g.Kind switch
        {
            RecipeGroupKind.New => Loc.T("GroupNew"),
            RecipeGroupKind.Known => Loc.T("GroupKnown"),
            RecipeGroupKind.Hidden => Loc.T("GroupHidden"),
            _ => g.Title,
        });
        var rows = _catalog.Entries.Select(e => new RecipeRowView(e, _game, _doc, titles[(e.Group, e.GroupBook ?? "")])).ToList();
        var source = (CollectionViewSource)FindResource("Recipes");
        source.Source = rows;
        _view = source.View;
        _view.Filter = Filter;
        UpdateChips();
        RecipeList.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == _selectedId) ?? rows.FirstOrDefault();
        RecipeList.ScrollIntoView(RecipeList.SelectedItem);
        UpdateListMessage();
        ShowProjectStatus();
    }

    private IEnumerable<RecipeRowView> Rows => (IEnumerable<RecipeRowView>?)((CollectionViewSource)FindResource("Recipes")).Source ?? [];

    private void UpdateChips()
    {
        var rows = Rows.ToList();
        ChipAll.Content = Loc.F("ChipAll", rows.Count);
        ChipChanged.Content = Loc.F("ChipChanged", rows.Count(r => r.IsChanged));
        ChipNew.Content = Loc.F("ChipNew", rows.Count(r => r.Entry.IsNew));
        ChipHidden.Content = Loc.F("ChipHidden", rows.Count(r => r.Entry.HiddenInGame));
        ChipNew.Visibility = _project.NewRecipes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool Filter(object o)
    {
        var r = (RecipeRowView)o;
        if (ChipChanged.IsChecked == true && !r.IsChanged) return false;
        if (ChipNew.IsChecked == true && !r.Entry.IsNew) return false;
        if (ChipHidden.IsChecked == true && !r.Entry.HiddenInGame) return false;
        return r.Entry.Matches(SearchBox.Text);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refilter();

    private void Chip_Checked(object sender, RoutedEventArgs e) => Refilter();

    private void Refilter()
    {
        if (_view == null) return;
        _view.Refresh();
        if (RecipeList.SelectedItem == null && !_view.IsEmpty)
        {
            _view.MoveCurrentToFirst();
            RecipeList.SelectedItem = _view.CurrentItem;
        }
        UpdateListMessage();
    }

    private void UpdateListMessage()
    {
        if (_view != null && _view.IsEmpty) ShowListMessage(Loc.T("NothingFound"), Loc.T("NothingFoundText"));
        else ListMessage.Visibility = Visibility.Collapsed;
    }

    private void ShowListMessage(string title, string text)
    {
        ListMessageTitle.Text = title;
        ListMessageText.Text = text;
        ListMessageText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ListMessage.Visibility = Visibility.Visible;
    }

    private void RecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipeList.SelectedItem is RecipeRowView row)
        {
            _selectedId = row.Entry.Id;
            ShowRecipe(row);
        }
        else if (_view == null || _view.IsEmpty)
        {
            Editor.Visibility = Visibility.Collapsed;
            PickRecipe.Visibility = Visibility.Visible;
        }
    }

    // ------------------------------------------------------------------ recipe editor

    private void ShowRecipe(RecipeRowView row)
    {
        _row = row;
        _state = _doc!.State(row.Entry.Id);
        _gameState = _doc.GameState(row.Entry.Id);
        _slot = null;
        Editor.Visibility = Visibility.Visible;
        PickRecipe.Visibility = Visibility.Collapsed;
        if (_state == null) return;
        _filling = true;
        TimeBox.Text = _state.CookingTime is { } t ? Format.Number(t) : "";
        TempBox.Text = _state.CookingTemperature is { } c ? Format.Number(c) : "";
        TimeBox.IsReadOnly = TempBox.IsReadOnly = ReadOnly;
        TimeBox.BorderBrush = TempBox.BorderBrush = (Brush)FindResource("Line");
        _filling = false;
        Render();
    }

    /// <summary>Redraws everything that depends on the recipe; the time and temperature boxes keep what is typed.</summary>
    private void Render(bool inspector = true)
    {
        if (_state == null || _row == null || _game == null) return;
        var e = _row.Entry;
        var b = _doc!.Base(e.Id);
        DishIcon.Texture = _row.Icon;
        DishIcon.Fallback = _row.Initial;
        var iconEditable = e.IsNew && !ReadOnly;
        DishIconBox.Cursor = iconEditable ? Cursors.Hand : null;
        DishIconBox.ToolTip = iconEditable ? Loc.T("ChooseIcon") : null;
        RecipeName.Text = e.Name;
        var result = IngredientOption.ShortClass(_state.Result ?? b?.Result);
        RecipeSub.Text = e.IsNew ? $"{e.Id} · {Loc.F("NewFrom", b?.Id ?? "—")}" : Loc.F("ResultLine", e.Id, result.Length > 0 ? result : "—");

        var changes = Diff();
        RecipeMeta.Children.Clear();
        foreach (var bk in e.BookNames) RecipeMeta.Children.Add(Chip("", bk));
        if (e.KnownByDefault) RecipeMeta.Children.Add(Chip("", Loc.T("KnownByDefaultChip")));
        foreach (var cat in _doc.Base(e.Id)?.Categories ?? []) RecipeMeta.Children.Add(Chip("\uE7C3", CategoryName(cat)));
        if (e.IsNew) RecipeMeta.Children.Add(Badge(Loc.T("BadgeNewRecipe"), "AccentBg", "AccentText"));
        else if (e.HiddenInGame) RecipeMeta.Children.Add(Badge(Loc.T("BadgeHidden"), "Surface2", "Muted", Loc.T("HiddenTip")));
        if (changes.Count > 0) RecipeMeta.Children.Add(Badge(Loc.F("ChangedCount", changes.Count), "WarnBg", "Warn"));
        ResetButton.IsEnabled = !ReadOnly && changes.Count > 0;

        Utilities.Children.Clear();
        // the chosen ones (on), the usual ones (pan, pot, oven) to click on, "+" for the rest
        var common = new[] { "Cooking.Utility.Pan", "Cooking.Utility.Pot", "Cooking.Utility.Oven" };
        var all = AllUtilities().ToList();
        foreach (var tag in all.Where(t => _state.Utilities.Contains(t) || common.Contains(t)))
        {
            var chip = new CheckBox { Content = _game.UtilityName(tag), IsChecked = _state.Utilities.Contains(tag), Style = (Style)FindResource("ChipToggle"), IsEnabled = !ReadOnly };
            chip.Click += (_, _) => ToggleUtility(tag, chip.IsChecked == true);
            Utilities.Children.Add(chip);
        }
        var rest = all.Where(t => !_state.Utilities.Contains(t) && !common.Contains(t)).ToList();
        if (!ReadOnly && rest.Count > 0)
        {
            var plus = new Button { Content = "+", Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), FontSize = 12, ToolTip = Loc.T("AddUtility") };
            var menu = new ContextMenu();
            foreach (var tag in rest)
            {
                var item = new MenuItem { Header = _game.UtilityName(tag) };
                item.Click += (_, _) => ToggleUtility(tag, true);
                menu.Items.Add(item);
            }
            plus.ContextMenu = menu;
            plus.Click += (_, _) => { menu.PlacementTarget = plus; menu.IsOpen = true; };
            AutoName(plus, "addutility");
            Utilities.Children.Add(plus);
        }
        var gt = _gameState?.CookingTime;
        TimeNote.Text = (gt is { } g ? Loc.F("InGame", Loc.F("Seconds", Format.Number(Math.Round(g)))) + " · " : "") + Loc.T("SkillNote");
        TimeBox.Tag = gt == null ? Loc.T("ByDefault") : "";
        TempNote.Text = _gameState?.CookingTemperature is { } gtemp ? Loc.F("InGame", Loc.F("Celsius", Format.Number(gtemp))) : Loc.T("NotSetInRecipe");

        Slots(MainSlots, Groups.Main);
        Slots(OptionalSlots, Groups.Optional);
        RecipeDescription.Text = (_state.Description ?? b?.Description ?? "").Trim();
        RecipeDescription.Visibility = RecipeDescription.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (inspector) RenderInspector();
        RenderChanges(changes);
        _row.Refresh();
        UpdateChips();
        ShowProjectStatus();
    }

    /// <summary>Every place a recipe of the game can be cooked (the tags recipes use).</summary>
    private IEnumerable<string> AllUtilities() =>
        _game!.Recipes.SelectMany(r => r.Utilities).Concat(_state!.Utilities).Distinct().OrderBy(t => _game.UtilityName(t), StringComparer.CurrentCulture);

    private void ToggleUtility(string tag, bool on)
    {
        Remember("utility");
        if (on && !_state!.Utilities.Contains(tag)) _state.Utilities.Add(tag);
        if (!on) _state!.Utilities.Remove(tag);
        if (_state!.Utilities.Count == 0) _state.Utilities.Add(tag); // a recipe is cooked somewhere
        Render();
    }

    private void TimeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _state == null || ReadOnly) return;
        var v = Format.Parse(TimeBox.Text);
        var ok = v is not double.NaN && (v != null || _gameState?.CookingTime == null);
        TimeBox.BorderBrush = (Brush)FindResource(ok ? "Line" : "Danger");
        if (!ok) return;
        Remember("time");
        _state.CookingTime = v;
        Render(inspector: false);
    }

    private void TempBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _state == null || ReadOnly) return;
        var v = Format.Parse(TempBox.Text);
        var ok = v is not double.NaN && (v != null || _gameState?.CookingTemperature == null);
        TempBox.BorderBrush = (Brush)FindResource(ok ? "Line" : "Danger");
        if (!ok) return;
        Remember("temperature");
        _state.CookingTemperature = v;
        Render(inspector: false);
    }

    private Slot? GameSlot(string group, Slot s) => s.IsNew ? null : _gameState?.Slots[group].FirstOrDefault(g => g.Origin == s.Origin);

    private string ItemName(string? item) => _game!.ItemName(item);

    private static void AutoName(FrameworkElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);

    /// <summary>
    /// Inspector of a new recipe with no slot chosen: its dish's icon (as the template's, or chosen) and model (the
    /// template's for now), as the cover plate of an own cookbook.
    /// </summary>
    private void RecipeIconPlate()
    {
        var dish = _state?.Result;
        if (dish == null || _doc?.TemplateClass(dish) is not { } template) return;
        var own = _doc.HasOwnIcon(dish);
        Inspector.Children.Add(Label(Loc.T("IconAndModel")));
        var plate = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        plate.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        plate.ColumnDefinitions.Add(new ColumnDefinition());
        plate.Children.Add(IconBox(_doc.ItemIcon(dish), _row!.Initial, ReadOnly ? null : PickRecipeIcon, "recipeicon"));
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Loc.F("ModelAs", _game!.ItemName(template)), FontSize = 13, TextWrapping = TextWrapping.Wrap });
        if (own) text.Children.Add(new TextBlock { Text = Loc.T("OwnIconChosen"), FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        if (!ReadOnly)
        {
            var pick = new Button { Content = Loc.T("ChooseIcon"), Style = (Style)FindResource("Link"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            pick.Click += (_, _) => PickRecipeIcon();
            AutoName(pick, "chooseicon");
            text.Children.Add(pick);
            if (own)
            {
                var reset = new Button { Content = Loc.T("IconAsModel"), Style = (Style)FindResource("Link"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
                reset.Click += (_, _) => SetRecipeIcon(null);
                AutoName(reset, "reseticon");
                text.Children.Add(reset);
            }
        }
        Grid.SetColumn(text, 1);
        plate.Children.Add(text);
        Inspector.Children.Add(plate);
        Inspector.Children.Add(new Border
        {
            Style = (Style)FindResource("Callout"), Background = Res("AccentBg"), Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock { Text = Loc.T("OwnModelSoon"), Foreground = Res("AccentText"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
        });
    }

    /// <summary>Icon of a new recipe's dish: ready dishes, other food, every icon of the game.</summary>
    private void PickRecipeIcon()
    {
        var dish = _state?.Result;
        if (dish == null || _doc?.TemplateClass(dish) == null || ReadOnly) return;
        var icon = ChooseIcon(Loc.T("RecipeIconTitle"), [IconTab.Dishes(), IconTab.Food(), IconTab.All()], _doc.ItemIcon(dish));
        if (icon != null) SetRecipeIcon(icon);
    }

    private void SetRecipeIcon(string? icon)
    {
        if (_state?.Result is not { } dish || _doc == null) return;
        _doc.SetIcon(dish, icon);
        _row?.Refresh();
        Render();
        ShowProjectStatus();
        ScheduleAutoSave();
    }

    private void DishIcon_Click(object sender, MouseButtonEventArgs e)
    {
        if (_row?.Entry.IsNew == true) PickRecipeIcon();
    }

    /// <summary>The icon picker: the chosen texture, or null when cancelled.</summary>
    private string? ChooseIcon(string heading, IReadOnlyList<IconTab> tabs, string? current)
    {
        var w = new IconPickerWindow(this, heading, tabs, current);
        return w.ShowDialog() == true ? w.Result : null;
    }

    /// <summary>A 64 px icon tile (cover of a book, icon of a new dish); with <paramref name="click"/> it opens the icon picker.</summary>
    private FrameworkElement IconBox(string? texture, string fallback, Action? click, string automation)
    {
        var grid = new Grid();
        grid.Children.Add(new IconView { Width = 56, Height = 56, Texture = texture, Fallback = fallback });
        var box = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(5), Background = Res("Slot"), Child = grid };
        if (click == null) return box;
        box.BorderBrush = Res("SlotEdge");
        box.BorderThickness = new Thickness(1);
        // a pencil in the corner: the icon can be changed
        grid.Children.Add(new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Background = Res("Surface2"), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, 2),
            Child = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10, Foreground = Res("Text2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var b = new Button { Content = box, Style = (Style)FindResource("Bare"), ToolTip = Loc.T("ChooseIcon"), VerticalAlignment = VerticalAlignment.Top };
        b.Click += (_, _) => click();
        AutoName(b, automation);
        return b;
    }

    /// <summary>A clickable tile (keyboard and UI automation reach it): the panel inside a bare button.</summary>
    private Button Tile(StackPanel content, string name, Action click)
    {
        var b = new Button { Content = content, Style = (Style)FindResource("Bare"), Margin = content.Margin, ToolTip = content.ToolTip };
        content.Margin = new Thickness(0);
        content.ToolTip = null;
        AutoName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Slots like the game's cooking window: icon, amount top-left, number of variants top-right, title below.</summary>
    private void Slots(WrapPanel panel, string group)
    {
        panel.Children.Clear();
        foreach (var s in _state!.Slots[group].Where(x => !HiddenTemplateSlot(x)))
        {
            var first = s.Options.FirstOrDefault();
            var tile = new Grid();
            tile.Children.Add(new IconView
            {
                Width = 46, Height = 46, LetterSize = 20, Texture = VariantIcon(first),
                Fallback = s.Title.Length > 0 ? char.ToUpper(s.Title[0]).ToString() : "?", Opacity = s.Removed ? 0.35 : 1,
            });
            if (first != null)
                tile.Children.Add(new TextBlock
                {
                    Text = Format.Amount(first), FontSize = 11, Foreground = (Brush)FindResource("Text"),
                    Margin = new Thickness(6, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                });
            if (s.Options.Count > 1)
                tile.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x33)), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(0, 4, 5, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = s.Options.Count.ToString(), FontSize = 10, Foreground = (Brush)FindResource("Text2") },
                });
            string? corner = s.Removed ? null : s.IsNew ? "Accent" : Editing.Changed(s, GameSlot(group, s)) ? "Warn" : null;
            if (corner != null)
                tile.Children.Add(new Polygon
                {
                    Points = [new Point(12, 0), new Point(12, 12), new Point(0, 12)], Fill = (Brush)FindResource(corner),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                });
            var selected = _slot is { } sel && ReferenceEquals(sel.Slot, s);
            var border = new Border
            {
                Width = 84, Height = 76, CornerRadius = new CornerRadius(3), Child = tile,
                Background = s.Removed ? Brushes.Transparent : (Brush)FindResource("Slot"),
                BorderBrush = selected ? (Brush)FindResource("Warn") : s.Removed ? (Brush)FindResource("RemovedEdge") : Brushes.Transparent,
                BorderThickness = new Thickness(selected ? 2 : 1),
            };
            var names = s.Options.Select(o => o.IsResource ? s.Title : ItemName(o.Item)).Distinct().ToList();
            var slot = new StackPanel { Width = 84, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = Loc.F("VariantsTip", string.Join(", ", names)) };
            slot.Children.Add(border);
            slot.Children.Add(new TextBlock
            {
                Text = s.Title, FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)FindResource(s.Removed ? "Muted" : "Text"), TextDecorations = s.Removed ? TextDecorations.Strikethrough : null,
            });
            panel.Children.Add(Tile(slot, $"slot:{group}:{s.Title}", () =>
            {
                if (_slot is not { } cur || !ReferenceEquals(cur.Slot, s)) _variant = 0;
                _slot = (group, s);
                Render();
            }));
        }
        if (!ReadOnly)
        {
            var add = new StackPanel { Width = 84, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = Loc.T("AddSlotTip") };
            add.Children.Add(new Border
            {
                Width = 84, Height = 76, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("LineStrong"),
                Child = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 18, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center },
            });
            add.Children.Add(new TextBlock { Text = Loc.T("Slot"), FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(Tile(add, $"addslot:{group}", () => AddSlot(group)));
        }
    }

    private void AddSlot(string group)
    {
        var item = Pick(Loc.T("PickNewSlot"), Loc.T("AddSlot"));
        if (item == null) return;
        Remember("slot");
        var s = new Slot { Title = item.Name, TitleSource = item.Name, TitleEdited = true, IsResource = item.IsLiquid, Options = [NewVariant(item, null)] };
        _state!.Slots[group].Add(s);
        _slot = (group, s);
        _variant = 0;
        Render();
    }

    private ItemInfo? Pick(string heading, string ok, string? countsAs = null)
    {
        var w = new PickerWindow(this, heading, ok, countsAs);
        return w.ShowDialog() == true ? w.Result : null;
    }

    // ------------------------------------------------------------------ inspector

    private void RenderInspector()
    {
        Inspector.Children.Clear();
        if (_slot is not { } sel || !_state!.Slots[sel.Group].Contains(sel.Slot))
        {
            _slot = null;
            if (_row?.Entry.IsNew == true) RecipeIconPlate();
            Inspector.Children.Add(Small(Loc.T(ReadOnly ? "PickSlotView" : "PickSlot")));
            return;
        }
        var (group, s) = sel;
        var gameSlot = GameSlot(group, s);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var badge = s.Removed ? Badge(Loc.T("BadgeRemovedSlot"), "DangerBg", "Danger") : s.IsNew ? Badge(Loc.T("BadgeNewSlot"), "AccentBg", "AccentText")
            : Editing.Changed(s, gameSlot) ? Badge(Loc.T("BadgeChanged"), "WarnBg", "Warn") : null;
        if (badge != null)
        {
            badge.Margin = new Thickness(8, 0, 0, 0);
            badge.Height = 22;
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
        }
        head.Children.Add(new TextBlock { Text = Loc.F("SlotTitle", s.Title), FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        Inspector.Children.Add(head);

        if (s.Removed)
        {
            Inspector.Children.Add(Small(Loc.T("SlotRemovedText")));
            if (!ReadOnly) Inspector.Children.Add(Button("", Loc.T("RestoreSlot"), "Btn", () => { Remember("restore"); s.Removed = false; Render(); }, 10));
            return;
        }

        // title
        Inspector.Children.Add(Label(Loc.T("SlotCaption")));
        var title = new TextBox { Text = s.Title, Style = (Style)FindResource("HintField"), FontSize = 13, Padding = new Thickness(8, 4, 8, 4), IsReadOnly = ReadOnly };
        AutoName(title, "slotcaption");
        title.TextChanged += (_, _) =>
        {
            var t = title.Text.Trim();
            if (t.Length == 0) return;
            Remember("title");
            if (gameSlot != null && t == gameSlot.Title)
            {
                s.Title = gameSlot.Title;
                s.TitleSource = gameSlot.TitleSource;
                s.TitleEdited = false;
            }
            else
            {
                s.Title = s.TitleSource = t;
                s.TitleEdited = true;
            }
            Render(inspector: false);
        };
        Inspector.Children.Add(title);
        if (s.TitleEdited) Inspector.Children.Add(Small(Loc.T("CaptionVerbatim"), 4));

        // variants: click one to set its own amount
        Inspector.Children.Add(Label(Loc.T("Variants"), 14));
        if (_variant >= s.Options.Count) _variant = 0;
        var vars = new WrapPanel();
        for (var i = 0; i < s.Options.Count; i++)
        {
            var o = s.Options[i];
            var index = i;
            var t = new Grid();
            var name = VariantName(s, o);
            t.Children.Add(new IconView { Width = 34, Height = 34, Texture = VariantIcon(o), Fallback = o.IsResource ? "≈" : name[..1] });
            t.Children.Add(new TextBlock
            {
                Text = Format.Amount(o), FontSize = 10, Foreground = (Brush)FindResource("Text"), Margin = new Thickness(4, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            });
            if (!ReadOnly && s.Options.Count > 1)
            {
                var x = new TextBlock
                {
                    Text = "", Style = (Style)FindResource("Icon"), FontSize = 9, Foreground = (Brush)FindResource("Muted"), Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 4, 0), ToolTip = Loc.T("RemoveVariant"),
                };
                x.PreviewMouseLeftButtonDown += (_, ev) => // before the tile button takes the click
                {
                    ev.Handled = true;
                    Remember("variant");
                    s.Options.RemoveAt(index);
                    VariantsChanged(s);
                };
                t.Children.Add(x);
            }
            var v = new StackPanel { Width = 58, ToolTip = $"{name} · {Format.Amount(o)}" };
            v.Children.Add(new Border
            {
                Width = 58, Height = 52, CornerRadius = new CornerRadius(3), Background = (Brush)FindResource("Slot"), Child = t,
                BorderBrush = (Brush)FindResource(index == _variant ? "Warn" : "Slot"), BorderThickness = new Thickness(index == _variant ? 2 : 0),
            });
            v.Children.Add(new TextBlock
            {
                Text = name, FontSize = 11, Foreground = (Brush)FindResource("Text2"),
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0),
            });
            v.Margin = new Thickness(0, 0, 6, 6);
            vars.Children.Add(Tile(v, $"variant:{index}", () =>
            {
                _variant = index;
                RenderInspector();
            }));
        }
        if (!ReadOnly)
        {
            var add = new StackPanel { Width = 58, Margin = new Thickness(0, 0, 6, 6), Background = Brushes.Transparent, ToolTip = Loc.T("AddVariantTip") };
            add.Children.Add(new Border
            {
                Width = 58, Height = 52, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("LineStrong"),
                Child = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 14, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center },
            });
            add.Children.Add(new TextBlock { Text = Loc.T("Add"), FontSize = 11, Foreground = (Brush)FindResource("Muted"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
            vars.Children.Add(Tile(add, "addvariant", () => AddVariant(s)));
        }
        Inspector.Children.Add(vars);

        // amount of the selected variant: whole / uses and how many, or litres; nutrition
        var sel0 = s.Options[_variant];
        Inspector.Children.Add(Label(s.Options.Count > 1 ? Loc.F("AmountOf", VariantName(s, sel0)) : Loc.T("Amount"), 10));
        if (!sel0.IsResource)
        {
            var seg = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var whole in new[] { true, false })
            {
                var rb = new RadioButton
                {
                    Content = Loc.T(whole ? "WholeMode" : "UsesMode"), GroupName = "amount", IsChecked = sel0.Whole == whole,
                    Style = (Style)FindResource("Chip"), FontSize = 12, IsEnabled = !ReadOnly,
                };
                rb.Checked += (_, _) =>
                {
                    if (sel0.Whole == whole) return;
                    Remember("whole");
                    sel0.Whole = whole;
                    Render();
                };
                seg.Children.Add(rb);
            }
            Inspector.Children.Add(seg);
        }
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(sel0.IsResource
            ? NumberField(Loc.T("AmountVolume"), Format.Number(Math.Round(sel0.Liters * 1000)), Loc.T("UnitMl"), "liters", v =>
            {
                if (v is not { } n || n <= 0 || n > 100000) return false;
                Remember("amount");
                sel0.Liters = Math.Round(n / 1000, 4);
                return true;
            })
            : NumberField(Loc.T("Amount"), sel0.Usage.ToString(), Loc.T(sel0.Whole ? "UnitPieces" : "UnitUses"), "amount", v =>
            {
                if (v is not { } n || n < 1 || n != Math.Floor(n) || n > 1000) return false;
                Remember("amount");
                sel0.Usage = (int)n;
                return true;
            }));
        var nut = NumberField(Loc.T("Nutrient"), Format.Number(sel0.Nutrient * 100), "%", "nutrient", v =>
        {
            if (v is not { } n || n > 1000) return false;
            Remember("nutrient");
            sel0.Nutrient = Math.Round(n / 100, 4);
            return true;
        });
        Grid.SetColumn(nut, 2);
        row.Children.Add(nut);
        Inspector.Children.Add(row);
        Inspector.Children.Add(Small(Loc.T(sel0.IsResource ? "LiquidNote" : "NutrientNote"), 4));
        if (!ReadOnly && s.Options.Count > 1)
            Inspector.Children.Add(Button("", Loc.T("AmountToAll"), "Btn", () =>
            {
                Remember("toall");
                foreach (var o in s.Options.Where(o => o.IsResource == sel0.IsResource))
                {
                    o.Whole = sel0.Whole;
                    o.Usage = sel0.Usage;
                    o.Liters = sel0.IsResource ? sel0.Liters : o.Liters;
                    o.Nutrient = sel0.Nutrient;
                }
                Render();
            }, 6));

        if (!ReadOnly)
        {
            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(Button("", Loc.T("RemoveSlot"), "DangerBtn", () =>
            {
                Remember("remove");
                if (s.IsNew) _state!.Slots[group].Remove(s);
                else s.Removed = true;
                Render();
            }));
            var other = group == Groups.Main ? Groups.Optional : Groups.Main;
            buttons.Children.Add(Button(null, Loc.T(other == Groups.Optional ? "ToOptional" : "ToMain"), "Btn", () => Move(group, s, other)));
            Inspector.Children.Add(buttons);
        }
    }

    /// <summary>Icon of a variant: the item's, or for a liquid in any container the liquid's own (ResourceImage).</summary>
    private string? VariantIcon(SlotOption? o) => o == null ? null : _game!.ItemIcon(o.Item) ?? _game.ItemIcon(o.Resource);

    /// <summary>The item's name, or the liquid's (any container), as the game calls them.</summary>
    private string VariantName(Slot s, SlotOption o) =>
        o.IsResource ? (o.Item != null ? $"{ItemName(o.Resource)} ({ItemName(o.Item)})" : ItemName(o.Resource)) : ItemName(o.Item);

    /// <summary>After adding or removing variants: a slot is a liquid slot when all its variants are liquids (as modkit sets it).</summary>
    private void VariantsChanged(Slot s)
    {
        s.IsResource = s.Options.Count > 0 && s.Options.All(o => o.IsResource);
        if (_variant >= s.Options.Count) _variant = Math.Max(0, s.Options.Count - 1);
        Render();
    }

    /// <summary>A new variant from the picker: an item, or a liquid in any container.</summary>
    private static SlotOption NewVariant(ItemInfo item, Slot? like)
    {
        if (item.IsLiquid)
        {
            var tpl = like?.Options.FirstOrDefault(o => o.IsResource);
            return new SlotOption { Resource = item.Class, Whole = true, Usage = 1, Liters = tpl?.Liters is > 0 and var l ? l : 0.25, Nutrient = tpl?.Nutrient ?? 1 };
        }
        var t = like?.Options.FirstOrDefault(o => !o.IsResource);
        return new SlotOption { Item = item.Class, Whole = t?.Whole ?? true, Usage = t?.Usage ?? 1, Liters = 1, Nutrient = t?.Nutrient ?? 1 };
    }

    private void AddVariant(Slot s)
    {
        var tpl0 = s.Options.FirstOrDefault(o => !o.IsResource);
        var item = Pick(Loc.F("PickVariant", s.Title), Loc.T("AddVariant"), Loc.T(tpl0 is { Whole: false } ? "CountsUses" : "CountsWhole"));
        if (item == null) return;
        Remember("variant");
        s.Options.Add(NewVariant(item, s));
        _variant = s.Options.Count - 1;
        VariantsChanged(s);
    }

    /// <summary>Main ⇄ optional: a game slot is struck out where it was and added to the other group.</summary>
    private void Move(string from, Slot s, string to)
    {
        Remember("move");
        var copy = new Slot { Title = s.Title, TitleSource = s.Title, TitleEdited = true, IsResource = s.IsResource, Options = s.Options.Select(o => o.Clone()).ToList() };
        if (s.IsNew) _state!.Slots[from].Remove(s);
        else s.Removed = true;
        _state!.Slots[to].Add(copy);
        _slot = (to, copy);
        Render();
    }

    private void RenderChanges(List<(char Kind, string Text)> changes)
    {
        Changes.Children.Clear();
        if (changes.Count == 0)
        {
            Changes.Children.Add(Small(Loc.T("NoRecipeChanges")));
            return;
        }
        foreach (var (kind, text) in changes)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            line.Children.Add(new TextBlock { Text = kind.ToString(), Width = 14, FontSize = 12, Foreground = (Brush)FindResource(kind == '+' ? "Ok" : kind == '−' ? "Danger" : "Warn") });
            line.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Text") });
            Changes.Children.Add(line);
        }
    }

    /// <summary>What differs from the game: − removed, + added, ~ changed.</summary>
    private List<(char, string)> Diff()
    {
        var list = new List<(char, string)>();
        var s = _state!;
        var g = _gameState;
        if (g == null) return list;
        string T(double? v) => v is { } x ? Format.Time(x) : Loc.T("ByDefault");
        string C(double? v) => v is { } x ? Loc.F("Celsius", Format.Number(x)) : Loc.T("ByDefault");
        if (!Nullable.Equals(s.CookingTime, g.CookingTime)) list.Add(('~', $"{Loc.T("Time")} {T(g.CookingTime)} → {T(s.CookingTime)}"));
        if (!Nullable.Equals(s.CookingTemperature, g.CookingTemperature)) list.Add(('~', $"{Loc.T("Temperature")} {C(g.CookingTemperature)} → {C(s.CookingTemperature)}"));
        foreach (var t in s.Utilities.Except(g.Utilities)) list.Add(('+', _game!.UtilityName(t)));
        foreach (var t in g.Utilities.Except(s.Utilities)) list.Add(('−', _game!.UtilityName(t)));
        foreach (var group in Groups.All)
        {
            var suffix = group == Groups.Optional ? " " + Loc.T("OptionalShort") : "";
            foreach (var x in s.Slots[group].Where(x => !HiddenTemplateSlot(x)))
            {
                var amount = x.Options.FirstOrDefault() is { } f ? " · " + Format.Amount(f) : "";
                if (x.Removed) list.Add(('−', x.Title + amount + suffix));
                else if (x.IsNew) list.Add(('+', x.Title + amount + suffix));
                else if (GameSlot(group, x) is { } gs)
                {
                    if (x.Title != gs.Title) list.Add(('~', $"«{gs.Title}» → «{x.Title}»"));
                    if (x.Options.Count != gs.Options.Count || x.Options.Zip(gs.Options).Any(p => p.First.Item != p.Second.Item || p.First.Resource != p.Second.Resource))
                        list.Add(('~', x.Options.Count == gs.Options.Count ? Loc.F("DiffVariantsReplaced", x.Title) : Loc.F("DiffVariants", x.Title, gs.Options.Count, x.Options.Count)));
                    else
                    {
                        if (x.Options.Zip(gs.Options).Any(p => p.First.Usage != p.Second.Usage || p.First.Whole != p.Second.Whole || Math.Abs(p.First.Liters - p.Second.Liters) > 1e-6 && p.First.IsResource))
                            list.Add(('~', $"{x.Title}: {string.Join(" / ", gs.Options.Select(Format.Amount))} → {string.Join(" / ", x.Options.Select(Format.Amount))}"));
                        if (x.Options.Zip(gs.Options).Any(p => Math.Abs(p.First.Nutrient - p.Second.Nutrient) > 1e-6))
                            list.Add(('~', Loc.F("DiffNutrient", x.Title, Format.Number(gs.Options[0].Nutrient * 100), Format.Number(x.Options[0].Nutrient * 100))));
                    }
                }
            }
        }
        return list;
    }

    // ------------------------------------------------------------------ small builders

    private Border Chip(string? icon, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) panel.Children.Add(new TextBlock { Text = icon, Style = (Style)FindResource("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border
        {
            BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6), Child = panel,
        };
        TextElement.SetForeground(b, (Brush)FindResource("Text2"));
        return b;
    }

    private Border Badge(string text, string bg, string fg, string? tip = null) => new()
    {
        Style = (Style)FindResource("Badge"), Background = (Brush)FindResource(bg), Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6),
        Child = new TextBlock { Text = text, Foreground = (Brush)FindResource(fg), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, ToolTip = tip,
    };

    private TextBlock Label(string text, double top = 0) =>
        new() { Text = text, Foreground = (Brush)FindResource("Text2"), FontSize = 12, Margin = new Thickness(0, top, 0, 6) };

    private TextBlock Small(string text, double top = 0) =>
        new() { Text = text, Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };

    private Button Button(string? icon, string text, string style, Action click, double top = 0)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) panel.Children.Add(new TextBlock { Text = icon, Style = (Style)FindResource("Icon"), FontSize = 13, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12 });
        var b = new Button { Content = panel, Style = (Style)FindResource(style), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, top, 6, 6) };
        AutoName(b, text);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A labelled number box; <paramref name="apply"/> returns false for a value it refuses (red border).</summary>
    private StackPanel NumberField(string label, string value, string unit, string automation, Func<double?, bool> apply)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis });
        var box = new TextBox { Text = value, Style = (Style)FindResource("HintField"), FontSize = 13, Padding = new Thickness(8, 4, 34, 4), IsReadOnly = ReadOnly };
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(new TextBlock { Text = unit, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsHitTestVisible = false });
        AutoName(box, automation);
        box.TextChanged += (_, _) =>
        {
            var v = Format.Parse(box.Text);
            var ok = v is not double.NaN && apply(v);
            box.BorderBrush = (Brush)FindResource(ok ? "Line" : "Danger");
            if (ok) Render(inspector: false);
        };
        p.Children.Add(field);
        return p;
    }

    // ------------------------------------------------------------------ save, reset

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_row == null || ReadOnly) return;
        Remember("reset");
        _doc!.Reset(_row.Entry.Id);
        ShowRecipe(_row);
    }

    // ------------------------------------------------------------------ autosave

    private System.Windows.Threading.DispatcherTimer? _autoSave;

    /// <summary>With autosave on (settings), the project is saved a moment after the last edit.</summary>
    private void ScheduleAutoSave()
    {
        if (!App.Settings.AutoSave || ReadOnly) return;
        _autoSave ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _autoSave.Stop();
        _autoSave.Tick -= AutoSaveTick;
        _autoSave.Tick += AutoSaveTick;
        _autoSave.Start();
    }

    private void AutoSaveTick(object? sender, EventArgs e)
    {
        _autoSave!.Stop();
        if (_doc?.IsDirty == true) Save();
    }

    // ------------------------------------------------------------------ undo

    private readonly Stack<(string Id, RecipeState State)> _undo = new();
    private string? _lastKey;
    private DateTime _lastTime;

    /// <summary>
    /// Keeps a copy of the recipe before an edit. Typing in one field counts as one edit (same key within 2 s).
    /// </summary>
    private void Remember(string key)
    {
        if (_state == null || ReadOnly) return;
        var now = DateTime.Now;
        var sameTyping = key == _lastKey && (now - _lastTime).TotalSeconds < 2 && _undo.Count > 0 && _undo.Peek().Id == _state.Id;
        _lastKey = key;
        _lastTime = now;
        if (sameTyping) return;
        _undo.Push((_state.Id, _state.Clone()));
        UndoButton.IsEnabled = true;
        ScheduleAutoSave();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Undo()
    {
        if (ReadOnly || _undo.Count == 0 || _doc == null) return;
        var (id, state) = _undo.Pop();
        _lastKey = null;
        _doc.Set(state);
        UndoButton.IsEnabled = _undo.Count > 0;
        var row = Rows.FirstOrDefault(r => r.Entry.Id == id);
        if (row == null) return;
        if (!ReferenceEquals(RecipeList.SelectedItem, row)) RecipeList.SelectedItem = row; // shows it
        else ShowRecipe(row);
        RecipeList.ScrollIntoView(row);
    }

    /// <summary>Cooking.Recipe.Cakes -> "Выпечка и десерты": the game has no translation of its categories, ours are in Strings.</summary>
    private static string CategoryName(string tag)
    {
        var key = "Cat_" + tag.Split('.').Last().Replace("/", "");
        var text = Loc.T(key);
        return text == key ? tag.Split('.').Last() : text;
    }

    /// <summary>A new recipe made "from scratch" does not show the slots of the recipe it was cloned from.</summary>
    private bool HiddenTemplateSlot(Slot s) => _row?.Entry.IsNew == true && s.Removed && !s.IsNew;

    /// <summary>"New recipe": from scratch or a copy of a game recipe (window), then it is selected to fill in.</summary>
    private void NewRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || ReadOnly) return;
        var w = new NewRecipeWindow(this, _doc, ChooseRecipe);
        if (w.ShowDialog() != true || w.CreatedId == null) return;
        _selectedId = w.CreatedId;
        SearchBox.Text = "";
        ChipAll.IsChecked = true;
        Fill();
        ShowProjectStatus();
        ScheduleAutoSave();
    }

    /// <summary>The dish model comes in a later version: say so instead of doing nothing.</summary>
    private void Model_Click(object sender, RoutedEventArgs e) =>
        Dialog.Show(this, Loc.T("ModelSoonTitle"), Loc.T("ModelSoonText"), Loc.T("Close"), icon: "");

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    /// <summary>Saves unsaved edits, then the build window (projects made by hand, like Food_Mod, build with their own script).</summary>
    private void Build_Click(object sender, RoutedEventArgs e) => OpenBuild();

    private void NavBuild_Click(object sender, RoutedEventArgs e)
    {
        var back = _beforeSettings ?? NavRecipes;
        OpenBuild();
        back.IsChecked = true;
    }

    private void OpenBuild()
    {
        if (_doc == null || ReadOnly || App.Settings.AesKey == null || Session.Paks == null) return;
        if (!Save()) return;
        new BuildWindow(this, _doc).ShowDialog();
        ShowProjectStatus();
    }

    /// <summary>Writes the changed recipes into modkit.json; false when it could not.</summary>
    private bool Save()
    {
        if (_doc == null || ReadOnly || !_doc.IsDirty) return true;
        try
        {
            _doc.Save();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException)
        {
            Session.Errors++;
            Session.NotifyChanged();
            Dialog.Show(this, Loc.T("SaveFailedTitle"), ex.Message, Loc.T("Close"), icon: "");
            return false;
        }
        Render();
        return true;
    }

    private bool _confirmed;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_confirmed && !ConfirmLeave()) e.Cancel = true;
    }

    /// <summary>Unsaved edits: save or drop them; false when saving failed (stay).</summary>
    private bool ConfirmLeave()
    {
        if (_doc == null || ReadOnly || !_doc.IsDirty) return true;
        return !Dialog.Show(this, Loc.T("UnsavedTitle"), Loc.T("UnsavedText"), Loc.T("Save"), Loc.T("DontSave"), icon: "") || Save();
    }

    // ------------------------------------------------------------------ navigation

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        ShowSection();
    }

    private void ShowSection()
    {
        var recipes = NavRecipes.IsChecked == true;
        var books = NavBooks.IsChecked == true;
        var ingredients = NavIngredients.IsChecked == true;
        ListPane.Visibility = recipes ? Visibility.Visible : Visibility.Collapsed;
        EditorPane.Visibility = recipes ? Visibility.Visible : Visibility.Collapsed;
        BooksPane.Visibility = books ? Visibility.Visible : Visibility.Collapsed;
        IngredientsPane.Visibility = ingredients ? Visibility.Visible : Visibility.Collapsed;
        if (books) ShowBooks();
        if (ingredients) ShowIngredients();
        if (recipes && _row != null) { Fill(); }
    }

    private RadioButton? _beforeSettings;

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        var back = _beforeSettings ?? NavRecipes;
        App.OpenSettings(this);
        back.IsChecked = true;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        // remember the section to return to after the settings button (a radio button in the same rail)
        _beforeSettings = new[] { NavRecipes, NavBooks, NavIngredients, NavBuild }.FirstOrDefault(n => n.IsChecked == true);
        base.OnPreviewMouseDown(e);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.OpenSettings(this);

    private void Projects_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmLeave()) return;
        _confirmed = true;
        new ProjectsWindow().Show();
        Close();
    }
}

internal sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
