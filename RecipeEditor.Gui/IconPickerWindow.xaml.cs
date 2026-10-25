using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Models;
using RecipeEditor.Core.Project;

namespace RecipeEditor.Gui;

/// <summary>An icon to choose: a game texture with the name of what it shows (an item, a book, or the texture's own name).</summary>
public sealed class IconChoice(string texture, string name, string? item = null, DishModel? model = null) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Texture { get; } = texture;
    /// <summary>On the "3D model" tab: the dish whose model this cell shows.</summary>
    public DishModel? Model { get; } = model;
    public string Name { get; } = name;
    /// <summary>Short texture name (ICO_Tomato_Salad_Item).</summary>
    public string TextureName { get; } = texture[(texture.LastIndexOf('.') + 1)..];
    public string Tip => model != null ? $"{Name}\n{model.MeshName}" : item != null ? $"{Name}\n{TextureName}" : TextureName;
    public string AutoName => model != null ? $"model:{model.MeshName}:{name}" : "icon:" + TextureName;
    public string Search { get; } = $"{name}\n{texture}\n{model?.Mesh}".ToLowerInvariant();

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    /// <summary>Items with an icon, one choice per texture (the first item's name).</summary>
    public static List<IconChoice> OfItems(IEnumerable<ItemInfo> items) => items.Where(i => !string.IsNullOrEmpty(i.Icon))
        .GroupBy(i => i.Icon!, StringComparer.OrdinalIgnoreCase).Select(g => new IconChoice(g.Key, g.First().Name, g.First().Class)).ToList();

    /// <summary>Texture names as the game has them, without ICO_ / T_ and with spaces: "Tomato Salad Item".</summary>
    public static string Pretty(string texture)
    {
        var n = texture[(texture.LastIndexOf('.') + 1)..];
        foreach (var p in new[] { "ICO_", "T_" })
            if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) n = n[p.Length..];
        return n.Replace('_', ' ');
    }
}

/// <summary>A tab of the icon picker: its title and how to get its icons.</summary>
public sealed record IconTab(string Title, Func<Task<List<IconChoice>>> Load)
{
    private static async Task<List<ItemInfo>> Catalog() => Session.Catalog != null ? await Session.Catalog : [];

    /// <summary>Icons of ready dishes (Items/Food/CookedFood).</summary>
    public static IconTab Dishes() => new(Loc.T("IconTabDishes"), async () => IconChoice.OfItems((await Catalog()).Where(i => i.Category == ItemCategory.Dishes)));

    /// <summary>Icons of every other food: vegetables, fruit, meat and fish, dairy, canned, dry, liquids.</summary>
    public static IconTab Food() => new(Loc.T("IconTabFood"), async () =>
        IconChoice.OfItems((await Catalog()).Where(i => i.Category is not (ItemCategory.Dishes or ItemCategory.Inedible or ItemCategory.Other))));

    /// <summary>Icons of all food, ready dishes included.</summary>
    public static IconTab AllFood() => new(Loc.T("IconTabAllFood"), async () =>
        IconChoice.OfItems((await Catalog()).Where(i => i.Category is not (ItemCategory.Inedible or ItemCategory.Other))));

    /// <summary>Covers of the game's cookbooks.</summary>
    public static IconTab Books(GameData game) => new(Loc.T("IconTabBooks"), () => Task.FromResult(game.Books
        .Select(b => (Book: b, Icon: game.ItemIcon(ProjectDocument.ClassOf($"{ProjectDocument.BookFolder}/{b.Id}"))))
        .Where(b => b.Icon != null).GroupBy(b => b.Icon!, StringComparer.OrdinalIgnoreCase)
        .Select(g => new IconChoice(g.Key, g.First().Book.Name, g.First().Book.Id)).ToList()));

    /// <summary>Every icon texture of the game (the item icon folder and every item's icon), named by the texture.</summary>
    public static IconTab All() => new(Loc.T("IconTabAll"), async () =>
    {
        var named = (await Catalog()).Where(i => !string.IsNullOrEmpty(i.Icon)).GroupBy(i => i.Icon!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        var textures = Session.IconTextures != null ? await Session.IconTextures : [];
        return textures.Concat(named.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => new IconChoice(t, IconChoice.Pretty(t)))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    });
}


/// <summary>
/// The "3D model" page of the picker: the dish whose model is chosen now (class), what to take from it (the ready dish,
/// the look in the cookware), whether the cookware look can be taken here (else <see cref="CookNote"/> says why), and
/// read-only (just look: "Model…" of a game recipe).
/// </summary>
public sealed record ModelPick(string? Current, bool Dish = true, bool Cook = true, bool CookAvailable = true, string? CookNote = null, bool ReadOnly = false);

/// <summary>
/// The icon of a new dish or an own cookbook: game textures by tabs (e.g. ready dishes / food / all icons), search,
/// double-click or "Choose". Only the visible rows are made: "all icons" holds about two thousand. With a
/// <see cref="ModelPick"/> a second page "3D model of the dish" lists the game's dishes with a 3D view of each.
/// </summary>
public partial class IconPickerWindow : Window
{
    private const double Cell = 108; // 100 + margin
    private readonly IReadOnlyList<IconTab> _tabs;
    private readonly ModelPick? _pick;
    private List<IconChoice> _all = [];
    private List<IconChoice> _shown = [];
    private IconChoice? _selected;
    private IconChoice? _modelSelected;
    private int _columns;
    private int _load; // the tab being loaded (a quicker click wins)
    private int _shownModel; // the model being loaded (a quicker click wins)
    private int _tab;
    private bool _modelPage;
    private CheckBox? _dish, _cook;
    private RadioButton? _cookView;
    private CheckBox? _inVessel;

    /// <summary>The icon chosen (or the current one).</summary>
    public string? Result { get; private set; }
    /// <summary>The dish whose model was chosen (or the current one), and what to take from it.</summary>
    public string? ModelResult { get; private set; }
    public bool ModelDish { get; private set; }
    public bool ModelCook { get; private set; }

    public IconPickerWindow(Window owner, string heading, IReadOnlyList<IconTab> tabs, string? current, ModelPick? model = null, bool openModel = false)
    {
        Owner = owner;
        _tabs = tabs;
        _pick = model;
        Result = current;
        ModelResult = model?.Current;
        (ModelDish, ModelCook) = (model?.Dish ?? false, model?.Cook ?? false);
        InitializeComponent();
        Native.DarkTitleBar(this);
        Heading.Text = heading;
        if (model != null)
        {
            Width = 1040;
            Height = 760;
            MinWidth = 860;
            MinHeight = 560;
            Heading.Visibility = Visibility.Collapsed;
            TabBar.Visibility = tabs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildModelOptions();
        }
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            if (model != null && (openModel || tabs.Count == 0))
            {
                ModelTabHead.IsChecked = true;
                ShowPage(true);
            }
            else ShowPage(false);
        };
    }

    private void Page_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ShowPage(ReferenceEquals(sender, ModelTabHead));
    }

    /// <summary>The icon page (its tabs as chips) or the model page (ready dishes, 3D view on the right).</summary>
    private void ShowPage(bool model)
    {
        _modelPage = model;
        ModelColumn.Width = model ? new GridLength(400) : new GridLength(0);
        ModelPanel.Visibility = model ? Visibility.Visible : Visibility.Collapsed;
        Chips.Children.Clear();
        var titles = model ? [Loc.T("IconTabDishes")] : _tabs.Select(t => t.Title).ToList();
        for (var i = 0; i < titles.Count; i++)
        {
            var n = i;
            var chip = new RadioButton { Content = titles[i], GroupName = "tab", IsChecked = i == (model ? 0 : _tab), FontSize = 12, Style = (Style)FindResource("Chip") };
            chip.Checked += (_, _) => ShowTab(n);
            Chips.Children.Add(chip);
        }
        if (_pick?.ReadOnly == true)
        {
            OkButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = Loc.T("Close");
        }
        else OkButton.Content = Loc.T(model ? "ChooseModelOk" : "ChooseIconOk");
        System.Windows.Automation.AutomationProperties.SetName(OkButton, OkButton.Content as string ?? "");
        SearchBox.Tag = Loc.T(model ? "ModelSearch" : "IconSearch");
        ShowTab(model ? 0 : _tab);
    }

    private async void ShowTab(int n)
    {
        var ticket = ++_load;
        if (!_modelPage) _tab = n;
        var modelPage = _modelPage;
        _all = [];
        Rows.ItemsSource = null;
        ShowMessage(Loc.T("PickerLoading"));
        List<IconChoice> list;
        try { list = modelPage ? await ModelChoices() : await _tabs[n].Load(); }
        catch (Exception ex) { ShowMessage(ex.Message); return; }
        if (ticket != _load) return;
        _all = list;
        // the current choice stays chosen when the tab has it
        if (modelPage)
        {
            var keep = _modelSelected?.Model?.Dish ?? ModelResult;
            _modelSelected = keep == null ? null : _all.FirstOrDefault(c => c.Model!.Dish.Equals(keep, StringComparison.OrdinalIgnoreCase));
            foreach (var c in _all) c.Selected = ReferenceEquals(c, _modelSelected);
        }
        else
        {
            var keep = _selected?.Texture ?? Result;
            _selected = keep == null ? null : _all.FirstOrDefault(c => c.Texture.Equals(keep, StringComparison.OrdinalIgnoreCase));
            foreach (var c in _all) c.Selected = ReferenceEquals(c, _selected);
        }
        Refresh();
        var chosen = modelPage ? _modelSelected : _selected;
        if (chosen != null) ScrollTo(chosen);
        if (modelPage) ShowModel();
    }

    /// <summary>One cell per dish of the game: its icon and name, its model in the 3D view.</summary>
    private static async Task<List<IconChoice>> ModelChoices()
    {
        var models = await Session.DishModels;
        var data = Session.Data != null ? await Session.Data : null;
        return models.Select(m => new IconChoice(data?.ItemIcon(m.Dish) ?? "", data?.ItemName(m.Dish) ?? m.MeshName, m.Dish, m))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void ShowMessage(string text)
    {
        Message.Text = text;
        Message.Visibility = Visibility.Visible;
    }

    private void Refresh()
    {
        var q = SearchBox.Text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _shown = _all.Where(c => q.All(c.Search.Contains)).ToList();
        Layout(force: true);
        if (_shown.Count == 0) ShowMessage(Loc.T("NothingFound"));
        else Message.Visibility = Visibility.Collapsed;
        UpdateInfo();
    }

    /// <summary>Cells into rows as wide as the list.</summary>
    private void Layout(bool force = false)
    {
        var width = Rows.ActualWidth - Rows.Padding.Left - Rows.Padding.Right - SystemParameters.VerticalScrollBarWidth;
        var columns = Math.Max(1, (int)(width / Cell));
        if (!force && columns == _columns) return;
        _columns = columns;
        Rows.ItemsSource = _shown.Chunk(columns).ToList();
    }

    private void ScrollTo(IconChoice c)
    {
        var row = _shown.IndexOf(c) / Math.Max(1, _columns);
        Dispatcher.BeginInvoke(() => FindScroll(Rows)?.ScrollToVerticalOffset(Math.Max(0, (row - 1) * 126.0)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static ScrollViewer? FindScroll(DependencyObject d)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (c is ScrollViewer sv) return sv;
            if (FindScroll(c) is { } found) return found;
        }
        return null;
    }

    private void Rows_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IconChoice c) return;
        if (_modelPage)
        {
            if (_modelSelected != null) _modelSelected.Selected = false;
            _modelSelected = c;
        }
        else
        {
            if (_selected != null) _selected.Selected = false;
            _selected = c;
        }
        c.Selected = true;
        if (_modelPage) ShowModel();
        UpdateInfo();
    }

    private void Cell_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        Cell_Click(sender, e);
        if (_pick?.ReadOnly != true) Ok_Click(sender, e);
    }

    private void UpdateInfo()
    {
        if (_modelPage)
        {
            OkButton.IsEnabled = _modelSelected != null && (_dish?.IsChecked == true || _cook?.IsChecked == true);
            Info.Text = _modelSelected == null ? Loc.F("ModelCount", _shown.Count) : $"{_modelSelected.Name} · {_modelSelected.Model!.MeshName}";
        }
        else
        {
            OkButton.IsEnabled = _selected != null;
            Info.Text = _selected == null ? Loc.F("IconCount", _shown.Count) : $"{_selected.Name} · {_selected.TextureName}";
        }
    }

    // ------------------------------------------------------------------ the 3D view

    /// <summary>"Ready dish / in the cookware" over the view, and the switches of what to take below it.</summary>
    private void BuildModelOptions()
    {
        RadioButton ViewChip(string text, bool on)
        {
            var r = new RadioButton { Content = text, GroupName = "view", IsChecked = on, FontSize = 12, Style = (Style)FindResource("Chip"), Background = (System.Windows.Media.Brush)FindResource("Bg") };
            r.Checked += (_, _) => ShowModel();
            System.Windows.Automation.AutomationProperties.SetName(r, text);
            return r;
        }
        Viewer.TopLeft.Children.Add(ViewChip(Loc.T("ModelViewDish"), true));
        Viewer.TopLeft.Children.Add(_cookView = ViewChip(Loc.T("ModelViewCook"), false));
        // "in the cookware": the look alone, or inside the pot / pan as the game shows it
        _inVessel = new CheckBox { Content = Loc.T("ModelInVessel"), Style = (Style)FindResource("ChipToggle"), IsChecked = true, Visibility = Visibility.Collapsed };
        System.Windows.Automation.AutomationProperties.SetName(_inVessel, Loc.T("ModelInVessel"));
        _inVessel.Click += (_, _) => ShowModel();
        Viewer.TopLeft.Children.Add(_inVessel);
        if (_pick!.ReadOnly) return;

        CheckBox Switch(string text, string sub, bool on, bool enabled)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = text, Foreground = (System.Windows.Media.Brush)FindResource("Text"), FontSize = 13 });
            box.Children.Add(new TextBlock { Text = sub, Style = (Style)FindResource("Small"), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
            var c = new CheckBox { Content = box, IsChecked = on && enabled, IsEnabled = enabled, Style = (Style)FindResource("Toggle"), Margin = new Thickness(0, 0, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(c, text);
            c.Checked += (_, _) => UpdateInfo();
            c.Unchecked += (_, _) => UpdateInfo();
            return c;
        }
        ModelOptions.Children.Add(new TextBlock { Text = Loc.T("ModelTake"), Style = (Style)FindResource("H2") });
        ModelOptions.Children.Add(_dish = Switch(Loc.T("ModelTakeDish"), Loc.T("ModelTakeDishSub"), _pick.Dish, true));
        ModelOptions.Children.Add(_cook = Switch(Loc.T("ModelTakeCook"), Loc.T("ModelTakeCookSub"), _pick.Cook, _pick.CookAvailable));
        if (_pick.CookNote != null)
            ModelOptions.Children.Add(new TextBlock { Text = _pick.CookNote, Style = (Style)FindResource("Small"), Foreground = (System.Windows.Media.Brush)FindResource("Warn"), Margin = new Thickness(0, -4, 0, 0) });
    }

    /// <summary>Loads the chosen dish's model (ready dish or the cookware look) into the 3D view.</summary>
    private async void ShowModel()
    {
        var ticket = ++_shownModel;
        if (_modelSelected?.Model is not { } m || Session.Visuals is not { } game || Session.Icons is not { } icons)
        {
            Viewer.ShowMessage(Loc.T("ModelNone"));
            return;
        }
        if (_cook != null && _pick?.CookAvailable == true)
        {
            // a smoked dish has no cookware look to take
            _cook.IsEnabled = m.CookMesh != null;
            if (m.CookMesh == null) _cook.IsChecked = false;
        }
        var cook = _cookView?.IsChecked == true;
        if (cook && m.CookMesh == null)
        {
            Viewer.ShowMessage(Loc.T("ModelNoCook"));
            return;
        }
        var vesselTag = cook ? DishModels.CookwareFor(m.Utilities) : null;
        if (_inVessel != null) _inVessel.Visibility = vesselTag != null ? Visibility.Visible : Visibility.Collapsed;
        var inVessel = vesselTag != null && _inVessel?.IsChecked == true;
        Viewer.ShowMessage(Loc.T("PickerLoading"));
        var meshes = await Task.Run(() =>
        {
            var mesh = cook ? DishModels.LoadMesh(game, icons, m.CookMesh!, DishModels.CookMaterials(m)) : DishModels.LoadMesh(game, icons, m.Mesh, m.Materials);
            if (mesh == null) return null;
            if (!inVessel) return new List<MeshData> { mesh };
            // as the game places it: the recipe's own transform, else the vessel's spot; a flat quad becomes a disc of the vessel's size
            var vessel = DishModels.ReadCookware(game, vesselTag!);
            var vesselMesh = vessel == null ? null : DishModels.LoadMesh(game, icons, vessel.Mesh, vessel.Materials);
            if (vessel == null || vesselMesh == null) return [mesh];
            var food = mesh.IsFlatQuad ? mesh.AsDisc(vessel.Radius * 0.97) : mesh;
            return [vesselMesh, food.Transformed(m.CookPlacement ?? vessel.Spot)];
        });
        if (ticket != _shownModel) return;
        if (meshes == null) Viewer.ShowMessage(Loc.T("ModelUnreadable"));
        else Viewer.Show(meshes, $"{string.Join(" + ", meshes.Select(x => x.Name))}\n{Loc.F("ModelTriangles", meshes[^1].Triangles.ToString("N0"))}");
    }

    /// <summary>Takes what was chosen on both pages: the icon, and the model with what to take from it.</summary>
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_pick?.ReadOnly == true) return;
        if (_modelPage ? _modelSelected?.Model == null : _selected == null) return;
        if (_selected != null) Result = _selected.Texture;
        if (_modelSelected?.Model != null && _pick != null)
        {
            ModelResult = _modelSelected.Model.Dish;
            ModelDish = _dish?.IsChecked == true;
            ModelCook = _cook?.IsChecked == true;
        }
        DialogResult = true;
    }
}
