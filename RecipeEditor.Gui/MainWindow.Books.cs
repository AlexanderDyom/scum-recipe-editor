using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RecipeEditor.Core.Project;

namespace RecipeEditor.Gui;

/// <summary>
/// Books: the three ways a recipe becomes learnable — own cookbooks (clones of a stock book), the game's cookbooks
/// (the mod can add recipes) and the "known from the start" list (stays with existing characters for good).
/// </summary>
public partial class MainWindow
{
    private const string Known = "known";
    private string? _bookSel;

    private static string BookClass(string id) => $"{ProjectDocument.BookFolder}/{id}.{id}_C";

    private void ShowBooks()
    {
        if (_doc == null || _game == null) return;
        NewBookButton.IsEnabled = !ReadOnly;
        BookList.Children.Clear();
        var own = _doc.Cooking.OwnBooks.Keys.ToList();
        _bookSel ??= own.Count > 0 ? "own:" + own[0] : "game:" + _game.Books[0].Id;
        Head(BookList, Loc.T("GroupOwnBooks"), own.Count);
        foreach (var b in own)
        {
            var (name, _) = _doc.OwnBookTexts(b);
            var template = _doc.OwnBookTemplate(b);
            BookList.Children.Add(ListRow(_doc.ItemIcon(BookClass(b)), name, b,
                Count(_doc.Cooking.OwnBooks[b].Count), _bookSel == "own:" + b, $"book:{b}", () => SelectBook("own:" + b)));
        }
        if (own.Count == 0) BookList.Children.Add(new TextBlock { Text = Loc.T("NoOwnBooks"), Foreground = Res("Muted"), FontSize = 12, Margin = new Thickness(14, 2, 14, 6), TextWrapping = TextWrapping.Wrap });
        Head(BookList, Loc.T("GroupGameBooks"), _game.Books.Count);
        foreach (var b in _game.Books)
        {
            var added = _doc.Cooking.Books.GetValueOrDefault(b.Id)?.Count ?? 0;
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            if (added > 0) right.Children.Add(new Border { Style = (Style)FindResource("Badge"), Background = Res("WarnBg"), Margin = new Thickness(0, 0, 8, 0), Child = new TextBlock { Text = "+" + added, Foreground = Res("Warn"), FontSize = 11 } });
            right.Children.Add(Count(_doc.BookRecipes(b.Id).Count()));
            BookList.Children.Add(ListRow(_game.ItemIcon(BookClass(b.Id)), b.Name, "", right, _bookSel == "game:" + b.Id, $"book:{b.Id}", () => SelectBook("game:" + b.Id)));
        }
        Head(BookList, Loc.T("GroupNoBook"), null);
        BookList.Children.Add(ListRow(null, Loc.T("GroupKnown"), "", Count(_doc.KnownRecipes.Count()), _bookSel == Known, "book:known", () => SelectBook(Known)));
        ShowBookDetail();
    }

    private TextBlock Count(int n) => new() { Text = n.ToString(), Foreground = Res("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    private void Head(Panel list, string title, int? count)
    {
        var head = new DockPanel { Margin = new Thickness(14, 12, 14, 4) };
        if (count != null)
        {
            var c = new TextBlock { Text = count.ToString(), Foreground = Res("Muted"), FontSize = 12 };
            DockPanel.SetDock(c, Dock.Right);
            head.Children.Add(c);
        }
        head.Children.Add(new TextBlock { Text = title, Foreground = Res("Muted"), FontSize = 12 });
        list.Children.Add(head);
    }

    private void SelectBook(string key)
    {
        _bookSel = key;
        ShowBooks();
    }

    /// <summary>After a change of the lists: mark the project, refresh this section, save when autosave is on.</summary>
    private void BooksChanged()
    {
        ShowBooks();
        ShowProjectStatus();
        ScheduleAutoSave();
    }

    private void ShowBookDetail()
    {
        BookDetail.Children.Clear();
        BookDetail.ColumnDefinitions.Clear();
        BookDetail.ColumnDefinitions.Add(new ColumnDefinition());
        BookDetail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        var form = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        var side = new StackPanel();
        Grid.SetColumn(side, 1);
        BookDetail.Children.Add(form);
        BookDetail.Children.Add(side);
        if (_bookSel == null || _doc == null || _game == null) return;

        if (_bookSel == Known)
        {
            BookTitle(form, Loc.T("GroupKnown"), null);
            form.Children.Add(new Border
            {
                Style = (Style)FindResource("Callout"), Background = Res("WarnBg"), Margin = new Thickness(0, 0, 0, 14),
                Child = new TextBlock { Text = Loc.T("KnownWarning"), Foreground = Res("Warn"), FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18 },
            });
            RecipeBox(form, _doc.KnownRecipes.ToList(), r => !_game.KnownByDefault.Contains(r, StringComparer.OrdinalIgnoreCase),
                r => { _doc.RemoveKnown(r); BooksChanged(); }, r => { _doc.AddKnown(r); BooksChanged(); }, Known);
            Side(side, null, null);
            return;
        }

        var own = _bookSel.StartsWith("own:");
        var id = _bookSel[(_bookSel.IndexOf(':') + 1)..];
        if (own)
        {
            if (!_doc.Cooking.OwnBooks.ContainsKey(id)) { _bookSel = null; return; }
            var (name, description) = _doc.OwnBookTexts(id);
            BookTitle(form, name, Badge(Loc.T("BadgeOwnBook"), "AccentBg", "AccentText"));
            form.Children.Add(Label(Loc.T("BookName")));
            var nameBox = new TextBox { Text = name, Style = (Style)FindResource("HintField"), FontSize = 13, Padding = new Thickness(8, 4, 8, 4), IsReadOnly = ReadOnly };
            AutoName(nameBox, "bookname");
            form.Children.Add(nameBox);
            form.Children.Add(Small(Loc.T("EnglishOnlyNote"), 4));
            form.Children.Add(Label(Loc.T("BookDescription"), 12));
            var descBox = new TextBox
            {
                Text = description, Style = (Style)FindResource("Field"), FontSize = 13, Height = 58, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top, IsReadOnly = ReadOnly,
            };
            form.Children.Add(descBox);
            void SaveTexts()
            {
                var ok = ProjectDocument.IsEnglishName(nameBox.Text.Trim());
                nameBox.BorderBrush = Res(ok ? "Line" : "Danger");
                if (!ok) return;
                _doc.SetOwnBookTexts(id, nameBox.Text.Trim(), descBox.Text.Trim());
                ShowProjectStatus();
                ScheduleAutoSave();
            }
            nameBox.LostFocus += (_, _) => { SaveTexts(); ShowBooks(); };
            descBox.LostFocus += (_, _) => SaveTexts();
            form.Children.Add(Label(Loc.T("BookRecipes"), 14));
            var recipes = _doc.Cooking.OwnBooks[id];
            RecipeBox(form, [.. recipes], _ => true, r => { _doc.RemoveFromBook(id, r); BooksChanged(); }, r => { _doc.AddToBook(id, r); BooksChanged(); }, id);
            if (recipes.Count == 0) form.Children.Add(new TextBlock { Text = Loc.T("EmptyBookWarning"), Foreground = Res("Warn"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
            if (!ReadOnly)
                form.Children.Add(Button("", Loc.T("DeleteBook"), "DangerBtn", () =>
                {
                    if (!Dialog.Show(this, Loc.T("DeleteBookTitle"), Loc.F("DeleteBookText", name), Loc.T("DeleteBook"), Loc.T("Cancel"), icon: "")) return;
                    _doc.DeleteOwnBook(id);
                    _bookSel = null;
                    BooksChanged();
                }, 16));
            Side(side, id, _doc.OwnBookTemplate(id));
        }
        else
        {
            var book = _game.Book(id)!;
            BookTitle(form, book.Name, Badge(Loc.T("BadgeGameBook"), "Surface2", "Text2"));
            if (book.Description.Length > 0) form.Children.Add(new TextBlock { Text = book.Description, Foreground = Res("Text2"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            form.Children.Add(Label(Loc.T("BookRecipes")));
            RecipeBox(form, _doc.BookRecipes(id).ToList(), r => !book.Recipes.Contains(r, StringComparer.OrdinalIgnoreCase),
                r => { _doc.RemoveFromBook(id, r); BooksChanged(); }, r => { _doc.AddToBook(id, r); BooksChanged(); }, id);
            form.Children.Add(new TextBlock { Text = Loc.T("GameBookNote"), Foreground = Res("Muted"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
            Side(side, id, id);
        }
    }

    private void BookTitle(Panel form, string text, Border? badge)
    {
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (badge != null)
        {
            badge.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(badge, Dock.Right);
            d.Children.Add(badge);
        }
        d.Children.Add(new TextBlock { Text = text, FontFamily = (FontFamily)FindResource("CondFont"), FontWeight = FontWeights.SemiBold, FontSize = 24, TextTrimming = TextTrimming.CharacterEllipsis });
        form.Children.Add(d);
    }

    /// <summary>Recipes of a book (or of the known list) with "add" and, where the mod put them, "remove".</summary>
    private void RecipeBox(Panel form, List<string> recipes, Func<string, bool> removable, Action<string> remove, Action<string> add, string context)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (!ReadOnly)
        {
            var addButton = Button("", Loc.T("AddRecipe"), "Btn", () =>
            {
                var picked = ChooseRecipe(recipes);
                if (picked != null) add(picked);
            });
            addButton.Margin = new Thickness(0);
            DockPanel.SetDock(addButton, Dock.Right);
            head.Children.Add(addButton);
        }
        head.Children.Add(new TextBlock { Text = Loc.P("RecipesCount", recipes.Count), Foreground = Res("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        form.Children.Add(head);
        var box = new StackPanel();
        var known = _doc!.KnownRecipes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var r in recipes.Select(id => _catalog?.Entries.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } e ? (Id: id, Entry: e) : (Id: id, Entry: null))
                     .OrderBy(x => x.Entry?.Name ?? x.Id, StringComparer.CurrentCulture))
        {
            var name = r.Entry?.Name ?? r.Id;
            var icon = _doc!.ItemIcon(_doc.State(r.Id)?.Result) ?? _doc.ItemIcon(_doc.Base(r.Id)?.Result);
            var notes = new List<string>();
            if (_doc.State(r.Id)?.Utilities.FirstOrDefault() is { } u) notes.Add(_game.UtilityName(u).ToLower(Loc.Instance.Culture));
            if (r.Entry?.IsNew == true) notes.Add(Loc.T("NewInMod"));
            var otherBooks = (r.Entry?.BookNames ?? []).Where(b => context == Known || b != BookDisplayName(context)).ToList();
            if (context != Known && otherBooks.Count > 0) notes.Add(Loc.F("AlsoIn", otherBooks[0]));
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (context != Known && known.Contains(r.Id))
                right.Children.Add(new Border { Style = (Style)FindResource("Badge"), Background = Res("WarnBg"), Margin = new Thickness(0, 0, 8, 0), ToolTip = Loc.T("KnownBadgeTip"), Child = new TextBlock { Text = Loc.T("KnownBadge"), Foreground = Res("Warn"), FontSize = 11 } });
            if (removable(r.Id) && context != Known && _game.Book(context) != null)
                right.Children.Add(new Border { Style = (Style)FindResource("Badge"), Background = Res("AccentBg"), Margin = new Thickness(0, 0, 8, 0), Child = new TextBlock { Text = Loc.T("BadgeMod"), Foreground = Res("AccentText"), FontSize = 11 } });
            if (context == Known && removable(r.Id))
                right.Children.Add(new Border { Style = (Style)FindResource("Badge"), Background = Res("AccentBg"), Margin = new Thickness(0, 0, 8, 0), Child = new TextBlock { Text = Loc.T("BadgeMod"), Foreground = Res("AccentText"), FontSize = 11 } });
            if (!ReadOnly && removable(r.Id))
            {
                var x = new Button { Style = (Style)FindResource("IconBtn"), Padding = new Thickness(5), ToolTip = Loc.T("RemoveFromBook"), Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10 } };
                AutoName(x, $"remove:{r.Id}");
                var id = r.Id;
                x.Click += (_, _) => remove(id);
                right.Children.Add(x);
            }
            var row = ListRow(icon, name, string.Join(" · ", notes), right, false, $"bookrecipe:{r.Id}", () => OpenRecipe(r.Id));
            box.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
        }
        form.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = box });
    }

    private string BookDisplayName(string id) =>
        _doc!.Cooking.OwnBooks.ContainsKey(id) ? _doc.OwnBookTexts(id).Name : _game!.Book(id)?.Name ?? id;

    /// <summary>Right column: cover and model (own books: as another book, changeable), where it is found, spawn command.</summary>
    private void Side(Panel side, string? id, string? template)
    {
        if (id == null)
        {
            side.Children.Add(Small(Loc.T("KnownSide")));
            return;
        }
        var own = _doc!.Cooking.OwnBooks.ContainsKey(id);
        side.Children.Add(Label(Loc.T("CoverAndModel")));
        var cover = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        cover.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        cover.ColumnDefinitions.Add(new ColumnDefinition());
        void PickIcon()
        {
            var icon = ChooseIcon(Loc.T("BookIconTitle"), [IconTab.Books(_game!), IconTab.AllFood(), IconTab.All()], _doc.ItemIcon(BookClass(id)));
            if (icon == null) return;
            _doc.SetIcon(BookClass(id), icon);
            BooksChanged();
        }
        cover.Children.Add(IconBox(_doc.ItemIcon(BookClass(id)), "?", own && !ReadOnly ? PickIcon : null, "bookicon"));
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (own)
        {
            text.Children.Add(new TextBlock { Text = Loc.F("ModelAs", template != null ? _game!.Book(template)?.Name ?? template : "—"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            if (_doc.HasOwnIcon(BookClass(id))) text.Children.Add(new TextBlock { Text = Loc.T("OwnIconChosen"), FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(0, 2, 0, 0) });
            if (!ReadOnly)
            {
                var pick = new Button { Content = Loc.T("ChooseIcon"), Style = (Style)FindResource("Link"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                pick.Click += (_, _) => PickIcon();
                AutoName(pick, "chooseicon");
                text.Children.Add(pick);
                if (_doc.HasOwnIcon(BookClass(id)))
                {
                    var reset = new Button { Content = Loc.T("IconAsModel"), Style = (Style)FindResource("Link"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
                    reset.Click += (_, _) => { _doc.SetIcon(BookClass(id), null); BooksChanged(); };
                    AutoName(reset, "reseticon");
                    text.Children.Add(reset);
                }
            }
            if (!ReadOnly)
            {
                var choose = new Button { Content = Loc.T("ChooseOtherBook"), Style = (Style)FindResource("Link"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                var menu = new ContextMenu();
                foreach (var b in _game!.Books)
                {
                    var item = new MenuItem { Header = b.Name };
                    item.Click += (_, _) => { _doc.SetOwnBookTemplate(id, b.Id); BooksChanged(); };
                    menu.Items.Add(item);
                }
                choose.ContextMenu = menu;
                choose.Click += (_, _) => { menu.PlacementTarget = choose; menu.IsOpen = true; };
                AutoName(choose, "choosecover");
                text.Children.Add(choose);
            }
        }
        else text.Children.Add(new TextBlock { Text = id, FontSize = 12, Foreground = Res("Muted") });
        Grid.SetColumn(text, 1);
        cover.Children.Add(text);
        side.Children.Add(cover);
        if (own)
        {
            side.Children.Add(new Border
            {
                Style = (Style)FindResource("Callout"), Background = Res("AccentBg"), Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBlock { Text = Loc.T("OwnIconSoon"), Foreground = Res("AccentText"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
            });
            side.Children.Add(Label(Loc.T("FoundInWorld")));
            side.Children.Add(Small(Loc.T("NotFoundInWorld"), 0));
        }
        side.Children.Add(Label(Loc.T("SpawnForTest"), 14));
        var command = $"#SpawnItem {id} 1";
        var field = new DockPanel();
        var copy = new Button { Style = (Style)FindResource("IconBtn"), Padding = new Thickness(6), ToolTip = Loc.T("CopyCommands"), Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12 } };
        copy.Click += (_, _) => { try { Clipboard.SetText(command); } catch (Exception) { /* busy: select by hand */ } };
        AutoName(copy, "copyspawn");
        copy.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(copy, Dock.Right);
        field.Children.Add(copy);
        field.Children.Add(new TextBox { Text = command, Style = (Style)FindResource("Field"), IsReadOnly = true, FontSize = 12 });
        side.Children.Add(field);
    }

    private void NewBook_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || ReadOnly || _game == null) return;
        var id = _doc.CreateOwnBook($"{_project.Name.Replace('_', ' ')} Cookbook", "", _game.Books[0].Id, []); // English for now
        _bookSel = "own:" + id;
        BooksChanged();
    }

    /// <summary>A recipe to add (not one of <paramref name="except"/>): search by dish or ingredient, double-click or "Add".</summary>
    private string? ChooseRecipe(IReadOnlyCollection<string> except)
    {
        if (_catalog == null) return null;
        string? result = null;
        var w = new Window
        {
            Owner = this, Title = Loc.T("PickRecipeTitle"), Width = 520, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Style = (Style)FindResource("AppWindow"),
        };
        Native.DarkTitleBar(w);
        var dock = new DockPanel();
        var search = new TextBox { Style = (Style)FindResource("SearchField"), Tag = Loc.T("SearchHint"), Margin = new Thickness(14, 14, 14, 8) };
        DockPanel.SetDock(search, Dock.Top);
        dock.Children.Add(search);
        var list = new StackPanel();
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        void Fill()
        {
            list.Children.Clear();
            foreach (var e in _catalog.Entries.Where(e => !except.Contains(e.Id, StringComparer.OrdinalIgnoreCase) && e.Matches(search.Text)).Take(200))
            {
                var icon = _doc!.ItemIcon(_doc.State(e.Id)?.Result) ?? _doc.ItemIcon(_doc.Base(e.Id)?.Result);
                var line = e.Group == RecipeGroupKind.Hidden ? Loc.T("HiddenLine") : string.Join(" · ", e.BookNames.DefaultIfEmpty(e.KnownByDefault ? Loc.T("KnownLine") : ""));
                list.Children.Add(ListRow(icon, e.Name, line, null, false, $"pick:{e.Id}", () => { result = e.Id; w.DialogResult = true; }));
            }
        }
        search.TextChanged += (_, _) => Fill();
        w.Content = dock;
        w.Loaded += (_, _) => { Fill(); search.Focus(); };
        w.KeyDown += (_, ke) => { if (ke.Key == Key.Escape) w.DialogResult = false; };
        return w.ShowDialog() == true ? result : null;
    }
}
