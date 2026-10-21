using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Project;
using RecipeEditor.Core.Recipe;

namespace RecipeEditor.Gui;

/// <summary>One use of a product: in which recipe, main or optional, added by the mod or the game's.</summary>
internal sealed record IngredientUse(RecipeEntry Recipe, string Group, bool ByMod);

/// <summary>Ingredients: every product the recipes use (game and mod), where it is used. View only for now.</summary>
public partial class MainWindow
{
    private Dictionary<string, List<IngredientUse>> _uses = new(StringComparer.OrdinalIgnoreCase);
    private string? _ingSelected;
    private ItemCategory? _ingCategory;

    /// <summary>Products by item class (or liquid class), with the recipes that use them as the mod has them now.</summary>
    private void IndexIngredients()
    {
        _uses = new(StringComparer.OrdinalIgnoreCase);
        if (_catalog == null || _doc == null) return;
        foreach (var e in _catalog.Entries)
        {
            var state = _doc.State(e.Id);
            var game = _doc.GameState(e.Id);
            if (state == null) continue;
            foreach (var g in Groups.All)
                foreach (var slot in state.Live(g))
                {
                    var gameSlot = slot.IsNew ? null : game?.Slots[g].FirstOrDefault(x => x.Origin == slot.Origin);
                    foreach (var o in slot.Options)
                    {
                        var key = o.Item ?? o.Resource;
                        if (key == null) continue;
                        var byMod = e.IsNew || gameSlot == null || !gameSlot.Options.Any(x => (x.Item ?? x.Resource) == key);
                        var list = _uses.TryGetValue(key, out var l) ? l : _uses[key] = [];
                        if (!list.Any(u => u.Recipe.Id == e.Id)) list.Add(new IngredientUse(e, g, byMod));
                    }
                }
        }
    }

    private ItemInfo Info(string key) =>
        _game!.Items.TryGetValue(key, out var i) ? i : new ItemInfo(key, _game.ItemName(key), "", null, GameData.CategoryOf(key));

    private void ShowIngredients()
    {
        if (_game == null) return;
        IndexIngredients();
        var all = _uses.Keys.Select(Info).ToList();
        IngChips.Children.Clear();
        AddIngChip(null, Loc.F("ChipAll", all.Count));
        foreach (var c in all.Select(i => i.Category).Distinct().Order()) AddIngChip(c, Loc.T("Cat" + c));
        FillIngredientList();
    }

    private void AddIngChip(ItemCategory? c, string text)
    {
        var chip = new RadioButton { Content = text, GroupName = "ingcat", Style = (Style)FindResource("Chip"), FontSize = 12, IsChecked = _ingCategory == c };
        chip.Checked += (_, _) => { _ingCategory = c; FillIngredientList(); };
        IngChips.Children.Add(chip);
    }

    private void IngSearch_TextChanged(object sender, TextChangedEventArgs e) => FillIngredientList();

    private void FillIngredientList()
    {
        IngList.Children.Clear();
        var q = IngSearch.Text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var items = _uses.Keys.Select(Info)
            .Where(i => _ingCategory == null || i.Category == _ingCategory)
            .Where(i => q.All(w => i.Name.ToLowerInvariant().Contains(w) || i.ClassName.ToLowerInvariant().Contains(w)))
            .OrderBy(i => i.Category).ThenBy(i => i.Name, StringComparer.CurrentCulture).ToList();
        foreach (var group in items.GroupBy(i => i.Category))
        {
            var head = new DockPanel { Margin = new Thickness(14, 10, 14, 4) };
            var count = new TextBlock { Text = group.Count().ToString(), Foreground = Res("Muted"), FontSize = 12 };
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(new TextBlock { Text = Loc.T("Cat" + group.Key), Foreground = Res("Muted"), FontSize = 12 });
            IngList.Children.Add(head);
            foreach (var i in group)
            {
                var uses = _uses[i.Class];
                var mod = uses.Count(u => u.ByMod);
                var line = Loc.P("UsedInRecipes", uses.Count) + (mod > 0 ? " · " + Loc.P("FromMod", mod) : "");
                IngList.Children.Add(ListRow(_game!.ItemIcon(i.Class), i.Name, line, null, i.Class == _ingSelected, $"ing:{i.ClassName}", () =>
                {
                    _ingSelected = i.Class;
                    FillIngredientList();
                }));
            }
        }
        if (items.Count == 0) IngList.Children.Add(new TextBlock { Text = Loc.T("NothingFound"), Foreground = Res("Muted"), Margin = new Thickness(14, 12, 14, 0) });
        if (_ingSelected == null || !_uses.ContainsKey(_ingSelected)) _ingSelected = items.FirstOrDefault()?.Class;
        ShowIngredientDetail();
    }

    /// <summary>A row of the left lists (recipes look): icon, name, a muted line, an optional badge; click selects.</summary>
    private Button ListRow(string? icon, string name, string line, UIElement? badge, bool selected, string automation, Action click)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(5), Background = Res("Slot"),
            Child = new IconView { Width = 30, Height = 30, Texture = icon, Fallback = name.Length > 0 ? name[..1].ToUpper() : "?" },
        });
        var text = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, FontSize = 13, Foreground = Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (line.Length > 0) text.Children.Add(new TextBlock { Text = line, FontSize = 11, Foreground = Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (badge is FrameworkElement b)
        {
            b.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(b, 2);
            g.Children.Add(b);
        }
        var row = new Border { Padding = new Thickness(14, 6, 14, 6), Background = selected ? Res("AccentBg") : Brushes.Transparent, Child = g };
        var button = new Button { Content = row, Style = (Style)FindResource("Bare"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutoName(button, automation);
        button.Click += (_, _) => click();
        return button;
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void ShowIngredientDetail()
    {
        IngDetail.Children.Clear();
        if (_ingSelected == null) return;
        var i = Info(_ingSelected);
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(new Border
        {
            Width = 96, Height = 96, CornerRadius = new CornerRadius(6), Background = Res("Slot"), BorderBrush = Res("SlotEdge"), BorderThickness = new Thickness(1),
            Child = new IconView { Width = 76, Height = 76, LetterSize = 36, Texture = _game!.ItemIcon(i.Class), Fallback = i.Name.Length > 0 ? i.Name[..1].ToUpper() : "?" },
        });
        var info = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        info.Children.Add(new TextBlock { Text = i.Name, FontFamily = (FontFamily)FindResource("CondFont"), FontWeight = FontWeights.SemiBold, FontSize = 24 });
        info.Children.Add(new TextBlock { Text = $"{i.ClassName} · {i.Folder}", FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(0, 2, 0, 12) });
        var facts = new Grid();
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = new List<(string, string)>();
        if (i.Weight is > 0 and var w) rows.Add((Loc.T("FactWeight"), Loc.F("Grams", Math.Round(w))));
        if (i.Uses is { } u) rows.Add((Loc.T("FactUses"), u.ToString()));
        rows.Add((Loc.T("FactCategory"), Loc.T("Cat" + i.Category)));
        for (var r = 0; r < rows.Count; r++)
        {
            facts.RowDefinitions.Add(new RowDefinition());
            var k = new TextBlock { Text = rows[r].Item1, Foreground = Res("Text2"), FontSize = 13, Margin = new Thickness(0, 0, 16, 6) };
            var v = new TextBlock { Text = rows[r].Item2, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(k, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            facts.Children.Add(k);
            facts.Children.Add(v);
        }
        info.Children.Add(facts);
        Grid.SetColumn(info, 1);
        head.Children.Add(info);
        IngDetail.Children.Add(head);
        if (i.Description.Length > 0)
            IngDetail.Children.Add(new TextBlock { Text = i.Description, Foreground = Res("Text2"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left });

        IngDetail.Children.Add(new TextBlock { Text = Loc.T("UsedInTitle"), Foreground = Res("Text2"), FontSize = 12, Margin = new Thickness(0, 18, 0, 6) });
        var box = new StackPanel();
        foreach (var use in _uses[_ingSelected].OrderBy(x => x.Recipe.Name, StringComparer.CurrentCulture))
        {
            var r = use.Recipe;
            var icon = _doc!.ItemIcon(_doc.State(r.Id)?.Result) ?? _doc.ItemIcon(_doc.Base(r.Id)?.Result);
            var line = Loc.T(use.Group == Groups.Main ? "RoleMain" : "RoleOptional") + (use.ByMod ? " · " + Loc.T("AddedByMod") : "");
            var badge = use.ByMod ? Badge(Loc.T("BadgeMod"), "AccentBg", "AccentText") : null;
            if (badge != null) { badge.Height = 22; badge.Margin = new Thickness(0); }
            var row = ListRow(icon, r.Name, line, badge, false, $"use:{r.Id}", () => OpenRecipe(r.Id));
            box.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
        }
        IngDetail.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = box });
    }

    /// <summary>Back to the recipes with this one selected.</summary>
    private void OpenRecipe(string id)
    {
        _selectedId = id;
        SearchBox.Text = "";
        ChipAll.IsChecked = true;
        NavRecipes.IsChecked = true;
        var row = Rows.FirstOrDefault(x => x.Entry.Id == id);
        if (row != null)
        {
            RecipeList.SelectedItem = row;
            RecipeList.ScrollIntoView(row);
        }
    }

    /// <summary>Own ingredients come later: say so.</summary>
    private void NewIngredient_Click(object sender, RoutedEventArgs e) =>
        Dialog.Show(this, Loc.T("NewIngredientSoonTitle"), Loc.T("NewIngredientSoonText"), Loc.T("Understood"), icon: "");
}
