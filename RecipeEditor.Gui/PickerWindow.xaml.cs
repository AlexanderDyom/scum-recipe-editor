using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RecipeEditor.Core.Game;

namespace RecipeEditor.Gui;

public sealed class PickerItem(ItemInfo item) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string AutoName => "item:" + Item.ClassName;

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

    public ItemInfo Item { get; } = item;
    public string Name => Item.Name;
    public string ClassName => Item.ClassName;
    /// <summary>Second line of a cell: what kind of variant it is for a liquid or a container (they share names), else the class.</summary>
    public string Sub => Item.IsLiquid ? Loc.T("SubLiquid") : Item.Holds != null ? Loc.T("SubContainer") : Item.ClassName;
    public string? Icon => Item.Icon;
    public string Initial => Item.Name.Length > 0 ? char.ToUpper(Item.Name[0]).ToString() : "?";
    public string Tip => $"{Item.Name} ({Item.ClassName})" + (Item.Description.Length > 0 ? $"\n{Item.Description}" : "");
    public string Search { get; } = $"{item.Name}\n{item.ClassName}\n{item.Class}".ToLowerInvariant();
}

/// <summary>The item picker: an item for a slot (a variant, or the first variant of a new slot), with search and categories.</summary>
public partial class PickerWindow : Window
{
    private List<PickerItem> _all = [];
    private ItemCategory? _category;
    private readonly string? _countsAs;
    private bool _inedibleLoaded;
    private List<PickerItem> _shown = [];
    private PickerItem? _selected;
    private int _columns;
    private const double Cell = 108; // 100 + margin

    public ItemInfo? Result { get; private set; }

    /// <summary>
    /// <paramref name="countsAs"/>: how the new variant will count (whole / uses), shown with the chosen item.
    /// </summary>
    public PickerWindow(Window owner, string heading, string okText, string? countsAs = null)
    {
        _countsAs = countsAs;
        Owner = owner;
        InitializeComponent();
        Native.DarkTitleBar(this);
        Heading.Text = heading;
        OkButton.Content = okText;
        AddChip(null, Loc.T("CatAll"), true);
        foreach (var c in Enum.GetValues<ItemCategory>().Where(c => c != ItemCategory.Other)) AddChip(c, Loc.T("Cat" + c), false);
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            var task = Session.Catalog;
            if (task == null) return;
            if (!task.IsCompleted) ShowMessage(Loc.T("PickerLoading"));
            try { _all = (await task).Select(i => new PickerItem(i)).ToList(); }
            catch (Exception ex) { ShowMessage(ex.Message); return; }
            Refresh();
            // the rest of the game's items come later (a couple of thousand): added when read
            if (Session.Inedible is not { } rest) return;
            try
            {
                var more = await rest;
                var have = _all.Select(p => p.Item.Class).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _all.AddRange(more.Where(i => !have.Contains(i.Class)).Select(i => new PickerItem(i)));
                _inedibleLoaded = true;
                Refresh();
            }
            catch (Exception) { _inedibleLoaded = true; }
        };
    }

    private void AddChip(ItemCategory? c, string text, bool on)
    {
        var chip = new RadioButton
        {
            Content = text, GroupName = "cat", IsChecked = on, FontSize = 12,
            Style = (Style)FindResource("Chip"),
        };
        chip.Checked += (_, _) =>
        {
            _category = c;
            Refresh();
        };
        Chips.Children.Add(chip);
    }

    private void ShowMessage(string text)
    {
        Message.Text = text;
        Message.Visibility = Visibility.Visible;
    }

    private void Refresh()
    {
        var q = SearchBox.Text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var items = _shown = _all.Where(i => (_category == null || i.Item.Category == _category) && q.All(i.Search.Contains)).ToList();
        Layout(force: true);
        if (items.Count == 0) ShowMessage(Loc.T(_category == ItemCategory.Inedible && !_inedibleLoaded ? "InedibleLoading" : "NothingFound"));
        else Message.Visibility = Visibility.Collapsed;
        UpdateInfo();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    /// <summary>Cells into rows as wide as the list.</summary>
    private void Layout(bool force = false)
    {
        var width = Rows.ActualWidth - Rows.Padding.Left - Rows.Padding.Right - SystemParameters.VerticalScrollBarWidth;
        var columns = Math.Max(1, (int)(width / Cell));
        if (!force && columns == _columns) return;
        _columns = columns;
        Rows.ItemsSource = _shown.Chunk(columns).ToList();
    }

    private void Rows_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PickerItem p) return;
        if (_selected != null) _selected.Selected = false;
        _selected = p;
        p.Selected = true;
        UpdateInfo();
    }

    private void Cell_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        Cell_Click(sender, e);
        Ok_Click(sender, e);
    }

    private void UpdateInfo()
    {
        var p = _selected != null && _shown.Contains(_selected) ? _selected : null;
        OkButton.IsEnabled = p != null;
        Info.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Info.TextWrapping = TextWrapping.NoWrap;
        if (p == null) { Info.Text = Loc.F("PickerCount", _shown.Count); return; }
        // no picture of its own (isopropyl alcohol as a liquid, waters...): the game shows some other icon in the slot
        if (p.Icon == null)
        {
            Info.Text = Loc.F("NoIconWarning", p.Name);
            Info.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
            Info.TextWrapping = TextWrapping.Wrap;
            return;
        }
        var parts = new List<string> { p.Name };
        if (p.Item.Weight is > 0 and var w) parts.Add(Loc.F("Grams", Math.Round(w)));
        parts.Add(p.Item.IsLiquid ? Loc.T("CountsAsLiquid")
            : p.Item.Holds != null ? Loc.T("CountsContainer") // a bottle: goes whole, with what is in it
            : _countsAs ?? Loc.T("CountsWhole"));
        Info.Text = string.Join(" · ", parts);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } p || !_shown.Contains(p)) return;
        Result = p.Item;
        DialogResult = true;
    }
}
