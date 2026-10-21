using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Project;

namespace RecipeEditor.Gui;

/// <summary>An icon to choose: a game texture with the name of what it shows (an item, a book, or the texture's own name).</summary>
public sealed class IconChoice(string texture, string name, string? item = null) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Texture { get; } = texture;
    public string Name { get; } = name;
    /// <summary>Short texture name (ICO_Tomato_Salad_Item).</summary>
    public string TextureName { get; } = texture[(texture.LastIndexOf('.') + 1)..];
    public string Tip => item != null ? $"{Name}\n{TextureName}" : TextureName;
    public string AutoName => "icon:" + TextureName;
    public string Search { get; } = $"{name}\n{texture}".ToLowerInvariant();

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
/// The icon of a new dish or an own cookbook: game textures by tabs (e.g. ready dishes / food / all icons), search,
/// double-click or "Choose". Only the visible rows are made: "all icons" holds about two thousand.
/// </summary>
public partial class IconPickerWindow : Window
{
    private const double Cell = 108; // 100 + margin
    private readonly IReadOnlyList<IconTab> _tabs;
    private List<IconChoice> _all = [];
    private List<IconChoice> _shown = [];
    private IconChoice? _selected;
    private int _columns;
    private int _load; // the tab being loaded (a quicker click wins)

    public string? Result { get; private set; }

    public IconPickerWindow(Window owner, string heading, IReadOnlyList<IconTab> tabs, string? current)
    {
        Owner = owner;
        _tabs = tabs;
        Result = current;
        InitializeComponent();
        Native.DarkTitleBar(this);
        Heading.Text = heading;
        for (var i = 0; i < tabs.Count; i++)
        {
            var n = i;
            var chip = new RadioButton { Content = tabs[i].Title, GroupName = "tab", IsChecked = i == 0, FontSize = 12, Style = (Style)FindResource("Chip") };
            chip.Checked += (_, _) => ShowTab(n);
            Chips.Children.Add(chip);
        }
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            ShowTab(0);
        };
    }

    private async void ShowTab(int n)
    {
        var ticket = ++_load;
        _all = [];
        Rows.ItemsSource = null;
        ShowMessage(Loc.T("PickerLoading"));
        List<IconChoice> list;
        try { list = await _tabs[n].Load(); }
        catch (Exception ex) { ShowMessage(ex.Message); return; }
        if (ticket != _load) return;
        _all = list;
        // the current icon stays chosen when the tab has it
        var keep = _selected?.Texture ?? Result;
        _selected = keep == null ? null : _all.FirstOrDefault(c => c.Texture.Equals(keep, StringComparison.OrdinalIgnoreCase));
        foreach (var c in _all) c.Selected = ReferenceEquals(c, _selected);
        Refresh();
        if (_selected != null) ScrollTo(_selected);
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
        if (_selected != null) _selected.Selected = false;
        _selected = c;
        c.Selected = true;
        UpdateInfo();
    }

    private void Cell_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        Cell_Click(sender, e);
        Ok_Click(sender, e);
    }

    private void UpdateInfo()
    {
        OkButton.IsEnabled = _selected != null;
        Info.Text = _selected == null ? Loc.F("IconCount", _shown.Count) : $"{_selected.Name} · {_selected.TextureName}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        Result = _selected.Texture;
        DialogResult = true;
    }
}
