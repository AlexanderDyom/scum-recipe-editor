using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Settings;
using ScumModKit.Game;

namespace RecipeEditor.Gui;

/// <summary>
/// Settings: sections on the left (game, AES key, projects, build, ScumRegistryMerger, language, updates, about), the
/// chosen one on the right. Changes apply on "Save"; a new game folder or key is checked on the game's paks first.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly string _startLanguage;
    private string _section;
    private string? _key;
    private bool _keyChanged;
    private GameProvider? _checkedGame;
    private string? _checkedFor;

    private static readonly (string Key, string Glyph)[] Sections =
    [
        ("SecGame", ""), ("SecKey", ""), ("SecProjects", ""), ("SecBuild", ""),
        ("SecMerger", ""), ("SecLanguage", ""), ("SecUpdates", ""), ("SecAbout", ""),
    ];

    public SettingsWindow(Window owner, string section = "SecGame")
    {
        Owner = owner;
        _s = App.Settings.Clone();
        _startLanguage = _s.Language;
        _section = section;
        InitializeComponent();
        Native.DarkTitleBar(this);
        Title = Loc.T("SettingsWindowTitle");
        foreach (var (key, glyph) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15, Width = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = key == "SecMerger" ? "ScumRegistryMerger" : Loc.T(key), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var rb = new RadioButton { Content = row, GroupName = "sec", Style = (Style)FindResource("Nav"), IsChecked = key == section };
            System.Windows.Automation.AutomationProperties.SetName(rb, key);
            rb.Checked += (_, _) => { _section = key; ShowSection(); };
            Nav.Children.Add(rb);
        }
        Session.Changed += OnSession;
        Closed += (_, _) =>
        {
            Session.Changed -= OnSession;
            if (DialogResult != true)
            {
                Loc.Instance.Set(_startLanguage);
                if (!ReferenceEquals(_checkedGame, Session.Game)) _checkedGame?.Dispose();
            }
        };
        ShowSection();
    }

    private void OnSession()
    {
        if (_section is "SecMerger" or "SecUpdates") ShowSection();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void ShowSection()
    {
        Pane.Children.Clear();
        Pane.Children.Add(new TextBlock { Text = _section == "SecMerger" ? "ScumRegistryMerger" : Loc.T(_section), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        switch (_section)
        {
            case "SecGame": GameSection(); break;
            case "SecKey": KeySection(); break;
            case "SecProjects": ProjectsSection(); break;
            case "SecBuild": BuildSection(); break;
            case "SecMerger": MergerSection(); break;
            case "SecLanguage": LanguageSection(); break;
            case "SecUpdates": UpdatesSection(); break;
            case "SecAbout": AboutSection(); break;
        }
    }

    // ------------------------------------------------------------------ building blocks (groups and lines)

    private TextBlock Lead(string text) =>
        new() { Text = text, Style = (Style)FindResource("Small"), Margin = new Thickness(0, 0, 0, 14), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };

    private StackPanel Group()
    {
        var inner = new StackPanel();
        Pane.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 14), Child = inner });
        return inner;
    }

    /// <summary>A line of a group: title and description on the left, a control on the right.</summary>
    private void Line(StackPanel group, string title, string? description, UIElement? control)
    {
        if (group.Children.Count > 0) group.Children.Add(new Border { Height = 1, Background = Res("Line") });
        var g = new Grid { Margin = new Thickness(14, 11, 14, 11) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = title, FontSize = 13 });
        if (description != null) left.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        g.Children.Add(left);
        if (control is FrameworkElement fe)
        {
            fe.Margin = new Thickness(14, 0, 0, 0);
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 1);
            g.Children.Add(fe);
        }
        group.Children.Add(g);
    }

    private CheckBox Toggle(bool on, Action<bool> set, string name)
    {
        var t = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = on };
        System.Windows.Automation.AutomationProperties.SetName(t, name);
        t.Click += (_, _) => set(t.IsChecked == true);
        return t;
    }

    private Button Btn(string? glyph, string text, string style, Action click)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph != null) p.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(new TextBlock { Text = text, FontSize = 12 });
        var b = new Button { Content = p, Style = (Style)FindResource(style), Padding = new Thickness(10, 5, 10, 5) };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        return b;
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    // ------------------------------------------------------------------ sections

    private void GameSection()
    {
        Pane.Children.Add(Lead(Loc.T("SecGameLead")));
        var client = GameLocator.FindInstall(InstallKind.Client);
        var server = GameLocator.FindInstall(InstallKind.Server);
        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
        var path = new TextBox { Text = _s.GameFolder ?? "", Style = (Style)FindResource("Field"), FontSize = 13 };
        var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        foreach (var (kind, found, glyph, title) in new[] { (InstallKind.Client, client, "", "Client"), (InstallKind.Server, server, "", "Server") })
        {
            var p = new StackPanel();
            var h = new StackPanel { Orientation = Orientation.Horizontal };
            h.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 8, 0) });
            h.Children.Add(new TextBlock { Text = Loc.T(title), FontSize = 14 });
            p.Children.Add(h);
            p.Children.Add(new TextBlock { Text = found != null ? "✓ " + Loc.T("FoundInSteam") : Loc.T("NotFound"), FontSize = 12, Foreground = Res(found != null ? "Ok" : "Muted"), Margin = new Thickness(0, 3, 0, 0) });
            var card = new RadioButton { Content = p, Style = (Style)FindResource("Card"), GroupName = "kind", IsChecked = _s.Kind == kind, Margin = new Thickness(kind == InstallKind.Client ? 0 : 5, 0, kind == InstallKind.Client ? 5 : 0, 0) };
            card.Checked += (_, _) =>
            {
                _s.Kind = kind;
                if (found != null) path.Text = found;
            };
            cards.Children.Add(card);
        }
        Pane.Children.Add(cards);
        var row = new DockPanel();
        var browse = Btn("", Loc.T("Browse"), "Btn", () =>
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("BrowseTitle") };
            if (Directory.Exists(path.Text)) dlg.InitialDirectory = path.Text;
            if (dlg.ShowDialog(this) == true) path.Text = dlg.FolderName;
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(path);
        Pane.Children.Add(row);
        Pane.Children.Add(status);
        void ShowPath()
        {
            var paks = GameLocator.ResolvePaks(path.Text);
            if (paks != null)
            {
                var mods = Path.Combine(paks, "~mods");
                var n = Directory.Exists(mods) ? Directory.EnumerateFiles(mods, "*.pak").Count(f => !Path.GetFileName(f).Contains("ModsRegistry", StringComparison.OrdinalIgnoreCase)) : 0;
                status.Text = "✓ " + Loc.F("PaksFound", n);
                status.Foreground = Res("Ok");
                _s.GameFolder = GameLocator.InstallRoot(paks);
            }
            else
            {
                status.Text = Loc.T(string.IsNullOrWhiteSpace(path.Text) ? "PathEmpty" : "PaksNotFound");
                status.Foreground = Res("Danger");
            }
        }
        path.TextChanged += (_, _) => ShowPath();
        ShowPath();
        // a server's textures are stripped: icons and models may come from the client (only for the editor's view)
        if (client != null)
        {
            var g = Group();
            Line(g, Loc.T("SetVisualsFromClient"), Loc.T("SetVisualsFromClientDesc"), Toggle(_s.VisualsFromClient == true, v => _s.VisualsFromClient = v, "visualsfromclient"));
        }
    }

    private void KeySection()
    {
        Pane.Children.Add(Lead(Loc.T("KeyRequired")));
        var box = new PasswordBox { Style = (Style)FindResource("KeyField"), FontSize = 13, Padding = new Thickness(8, 7, 8, 7), Password = _key ?? _s.AesKey ?? "" };
        System.Windows.Automation.AutomationProperties.SetName(box, "aeskey");
        var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        box.PasswordChanged += (_, _) =>
        {
            _key = box.Password.Trim();
            _keyChanged = true;
            status.Text = "";
        };
        var row = new DockPanel();
        var check = Btn("", Loc.T("CheckKey"), "Btn", async () =>
        {
            status.Text = Loc.T("KeyChecking");
            status.Foreground = Res("Text2");
            var r = await CheckKeyAsync(box.Password.Trim());
            status.Text = r.Ok ? "✓ " + Loc.F("KeyWorks", r.Recipes, r.Books) : r.Error;
            status.Foreground = Res(r.Ok ? "Ok" : "Danger");
        });
        check.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(check, Dock.Right);
        row.Children.Add(check);
        row.Children.Add(box);
        Pane.Children.Add(row);
        Pane.Children.Add(status);
        Pane.Children.Add(new TextBlock { Text = Loc.T("KeyStoredHint"), Style = (Style)FindResource("Small"), Margin = new Thickness(0, 10, 0, 0) });
        var where = new Button { Content = Loc.T("WhereKey"), Style = (Style)FindResource("Link"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        where.Click += (_, _) => Dialog.Show(this, Loc.T("WhereKeyTitle"), Loc.T("WhereKeyText"), Loc.T("Close"), icon: "");
        Pane.Children.Add(where);
    }

    /// <summary>Opens the game's paks with the key (the folder of the "Game" section) and counts recipes and cookbooks.</summary>
    private async Task<(bool Ok, int Recipes, int Books, string Error)> CheckKeyAsync(string key)
    {
        try { GameProvider.NormalizeKey(key); }
        catch (FormatException) { return (false, 0, 0, Loc.T("KeyBadFormat")); }
        var folder = _s.GameFolder ?? "";
        try
        {
            var (game, counts) = await Task.Run(() =>
            {
                var g = GameProvider.Open(folder, key);
                try { return (g, GameData.Count(g)); }
                catch { g.Dispose(); throw; }
            });
            if (!ReferenceEquals(_checkedGame, Session.Game)) _checkedGame?.Dispose();
            _checkedGame = game;
            _checkedFor = folder + "|" + key;
            return (true, counts.Recipes, counts.Books, "");
        }
        catch (Exception)
        {
            return (false, 0, 0, Loc.T("KeyWrong"));
        }
    }

    private void ProjectsSection()
    {
        var g = Group();
        var folder = new TextBlock { Text = _s.NewProjectsRoot, FontSize = 12, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap };
        var change = Btn("", Loc.T("Change"), "Btn", () =>
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("ProjectFolderTitle") };
            if (Directory.Exists(_s.NewProjectsRoot)) dlg.InitialDirectory = _s.NewProjectsRoot;
            if (dlg.ShowDialog(this) != true) return;
            _s.ProjectsRoot = dlg.FolderName;
            ShowSection();
        });
        Line(g, Loc.T("SetProjectsFolder"), _s.NewProjectsRoot, change);
        Line(g, Loc.T("SetAutoSave"), Loc.T("SetAutoSaveDesc"), Toggle(_s.AutoSave, v => _s.AutoSave = v, "autosave"));
    }

    private void BuildSection()
    {
        var g = Group();
        var number = new TextBox { Text = _s.FirstPakNumber.ToString(), Style = (Style)FindResource("HintField"), Width = 90, FontSize = 13, Padding = new Thickness(8, 4, 8, 4) };
        number.TextChanged += (_, _) =>
        {
            var ok = int.TryParse(number.Text, out var n) && n is >= 1 and < 999;
            number.BorderBrush = Res(ok ? "Line" : "Danger");
            if (ok) _s.FirstPakNumber = n;
        };
        Line(g, Loc.T("SetFirstNumber"), Loc.T("SetFirstNumberDesc"), number);
        Line(g, Loc.T("SetInstallDefault"), Loc.T("SetInstallDefaultDesc"), Toggle(_s.InstallByDefault, v => _s.InstallByDefault = v, "installdefault"));
    }

    private void MergerSection()
    {
        Pane.Children.Add(Lead(Loc.T("SecMergerLead")));
        var g = Group();
        var exe = MergerLink.Find(_s.MergerPath);
        var change = Btn(null, Loc.T("Change"), "Btn", () =>
        {
            var dlg = new OpenFileDialog { Filter = "ScumRegistryMerger.exe|ScumRegistryMerger.exe", Title = "ScumRegistryMerger" };
            if (dlg.ShowDialog(this) != true) return;
            _s.MergerPath = dlg.FileName;
            App.Settings.MergerPath = dlg.FileName; // so the session finds it right away
            Session.RefreshMerger(checkOnline: false);
            ShowSection();
        });
        Line(g, Loc.T("SetMergerProgram"), exe != null ? exe + " — " + Loc.T(_s.MergerPath != null ? "SetMergerChosen" : "SetMergerFound") : Loc.T("MergerMissing"), change);

        var ver = new StackPanel { Orientation = Orientation.Horizontal };
        if (Session.MergerVersion is { } v)
        {
            ver.Children.Add(new TextBlock { Text = v.ToString(3), FontFamily = (FontFamily)FindResource("CondFont"), FontWeight = FontWeights.SemiBold, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            ver.Children.Add(new TextBlock { Text = Loc.T("SetInstalledWord"), Style = (Style)FindResource("Small"), Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        if (Session.MergerLatest is { } latest && (Session.MergerVersion == null || latest.Version > Session.MergerVersion))
        {
            ver.Children.Add(new Border
            {
                Style = (Style)FindResource("Badge"), Background = Res("WarnBg"), Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = Loc.F("SetAvailable", latest.Version.ToString(3)), Foreground = Res("Warn"), FontSize = 11 },
            });
        }
        if (Session.MergerVersion == null || Session.MergerUpdateAvailable)
            ver.Children.Add(Btn("", Loc.T(Session.MergerVersion == null ? "MergerDownload" : "MergerUpdate"), "Primary", DownloadMerger));
        var checkedText = Session.MergerChecked is { } t ? Loc.F("SetChecked", ProjectRowView.FriendlyDate(t)) : Loc.T("SetNotChecked");
        Line(g, Loc.T("SetVersion"), checkedText, ver);
        Line(g, Loc.T("SetCheckUpdates"), Loc.T("SetCheckUpdatesDesc"), Toggle(_s.CheckUpdates, v2 => _s.CheckUpdates = v2, "checkupdates"));
        Line(g, Loc.T("SetUseMergerSettings"), Loc.T("SetUseMergerSettingsDesc"), Toggle(_s.UseMergerSettings, v2 => _s.UseMergerSettings = v2, "usemergersettings"));

        var row = new WrapPanel();
        var open = Btn("", Loc.T("SetOpenMerger"), "Btn", () => { if (Session.MergerExe != null) Open(Session.MergerExe); });
        open.IsEnabled = Session.MergerExe != null;
        open.Margin = new Thickness(0, 0, 12, 0);
        row.Children.Add(open);
        var link = new Button { Content = "github.com/AlexanderDyom/scum-registry-merger", Style = (Style)FindResource("Link"), VerticalAlignment = VerticalAlignment.Center };
        link.Click += (_, _) => Open(MergerLink.PageUrl);
        row.Children.Add(link);
        Pane.Children.Add(row);
        var recheck = new Button { Content = Loc.T("SetCheckNow"), Style = (Style)FindResource("Link"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        recheck.Click += (_, _) => Session.RefreshMerger(checkOnline: true);
        Pane.Children.Add(recheck);
    }

    private async void DownloadMerger()
    {
        try
        {
            var release = Session.MergerLatest ?? await MergerLink.LatestAsync() ?? throw new InvalidOperationException(Loc.T("MergerNoRelease"));
            var exe = await MergerLink.InstallAsync(release, Session.MergerExe);
            if (Session.MergerExe == null) { _s.MergerPath = exe; App.Settings.MergerPath = exe; }
            Session.RefreshMerger(checkOnline: true);
        }
        catch (Exception ex)
        {
            Dialog.Show(this, Loc.T("MergerDownloadFailed"), ex.Message, Loc.T("Close"), icon: "");
        }
    }

    private void LanguageSection()
    {
        var wrap = new WrapPanel();
        foreach (var o in LanguageList.Options())
        {
            var chip = new RadioButton { Content = o.Text + (o.Ai ? "  AI" : ""), Style = (Style)FindResource("Chip"), GroupName = "lang", FontSize = 12, IsChecked = _s.Language == o.Code || (string.IsNullOrEmpty(_s.Language) && o.Code == "auto") };
            chip.Checked += (_, _) =>
            {
                _s.Language = o.Code;
                Loc.Instance.Set(o.Code); // preview; Cancel goes back
                ShowSection();
            };
            wrap.Children.Add(chip);
        }
        Pane.Children.Add(wrap);
        Pane.Children.Add(new TextBlock { Text = Loc.T("SetLanguageNote"), Style = (Style)FindResource("Small"), Margin = new Thickness(0, 10, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left });
    }

    private void UpdatesSection()
    {
        var g = Group();
        Line(g, Loc.T("SetEditorVersion"), Loc.T("SetEditorReleases"), new TextBlock { Text = App.Version, FontFamily = (FontFamily)FindResource("CondFont"), FontWeight = FontWeights.SemiBold, FontSize = 18 });
        // the latest release on GitHub: "you have the latest" or "update to X" (downloads, closes, starts the new one)
        var latest = Session.EditorLatest;
        UIElement action;
        if (Session.EditorUpdateAvailable)
        {
            action = Btn("", Loc.F("UpdateEditorTo", latest!.Tag.TrimStart('v')), "Primary", async () =>
            {
                if (!Dialog.Show(this, Loc.F("UpdateEditorTo", latest.Tag.TrimStart('v')), Loc.T("UpdateEditorText"), Loc.T("UpdateEditorYes"), Loc.T("Cancel"), icon: "")) return;
                await Session.UpdateEditorAsync();
            });
        }
        else action = new TextBlock { Text = Loc.T(latest == null ? "EditorNotChecked" : "EditorUpToDate"), Foreground = Res("Muted"), FontSize = 12 };
        Line(g, Loc.T("SetEditorLatest"), latest == null ? null : latest.Tag, action);
        var page = Btn("", Loc.T("EditorReleasesPage"), "Btn", () => Open(App.ProjectPage + "/releases"));
        Line(g, Loc.T("EditorReleases"), null, page);
        Line(g, Loc.T("SetCheckUpdates"), Loc.T("SetCheckUpdatesDesc"), Toggle(_s.CheckUpdates, v => _s.CheckUpdates = v, "checkupdates2"));
        var check = Btn("", Loc.T("CheckNow"), "Btn", () => Session.RefreshMerger(checkOnline: true));
        Line(g, Loc.T("CheckNowTitle"), null, check);
    }

    private void AboutSection()
    {
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = Loc.T("AboutAuthor"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        box.Children.Add(new TextBlock { Text = Loc.T("AboutText"), Foreground = Res("Text2"), TextWrapping = TextWrapping.Wrap, LineHeight = 19, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left });
        box.Children.Add(new TextBlock { Text = Loc.F("AboutVersion", App.Version), Style = (Style)FindResource("Small"), Margin = new Thickness(0, 8, 0, 0) });
        box.Children.Add(new TextBlock { Text = Loc.F("AboutData", AppSettings.Folder), FontSize = 12, Foreground = Res("Muted") });
        var page = Btn("", Loc.T("AboutPage"), "Btn", () => Open(App.ProjectPage));
        page.ToolTip = App.ProjectPage;
        page.Margin = new Thickness(0, 10, 0, 0);
        page.HorizontalAlignment = HorizontalAlignment.Left;
        box.Children.Add(page);
        var grid = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 20, Foreground = Res("Text2") });
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        Pane.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = grid });
    }

    // ------------------------------------------------------------------ save

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = _keyChanged ? _key : App.Settings.AesKey;
        var gameChanged = !string.Equals(_s.GameFolder, App.Settings.GameFolder, StringComparison.OrdinalIgnoreCase) || _keyChanged;
        if (gameChanged)
        {
            // the game must open with the key before anything is stored
            if (string.IsNullOrEmpty(key) || GameLocator.ResolvePaks(_s.GameFolder) == null)
            {
                Dialog.Show(this, Loc.T("SettingsGameTitle"), Loc.T("SettingsGameText"), Loc.T("Close"), icon: "");
                return;
            }
            if (_checkedFor != (_s.GameFolder ?? "") + "|" + key)
            {
                SaveButton.IsEnabled = false;
                var r = await CheckKeyAsync(key);
                SaveButton.IsEnabled = true;
                if (!r.Ok)
                {
                    Dialog.Show(this, Loc.T("SettingsGameTitle"), r.Error, Loc.T("Close"), icon: "");
                    return;
                }
            }
            _s.AesKey = key;
        }
        var languageChanged = _s.Language != _startLanguage;
        var visualsChanged = _s.VisualsFromClient != App.Settings.VisualsFromClient || _s.Kind != App.Settings.Kind;
        App.Apply(_s);
        if (gameChanged && _checkedGame != null) Session.Start(_checkedGame);
        else if (visualsChanged) Session.OpenVisuals();
        else if (languageChanged) Session.Reload();
        Session.RefreshMerger(checkOnline: false);
        Session.AskClientVisuals(this); // switched to SCUM Server just now
        DialogResult = true;
    }
}
