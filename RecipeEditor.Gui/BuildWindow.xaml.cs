using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RecipeEditor.Core.Build;
using RecipeEditor.Core.Project;
using RecipeEditor.Core.Settings;

namespace RecipeEditor.Gui;

/// <summary>
/// The build window: what goes into the mod, where it goes (~mods + shared registry, or a folder), the steps as they run and
/// #SpawnItem commands to test it. The project is saved before (the caller does it).
/// </summary>
public partial class BuildWindow : Window
{
    private readonly ProjectDocument _doc;
    private readonly DispatcherTimer _gameCheck = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, (TextBlock Icon, TextBlock Text, TextBlock Result, TextBlock Detail)> _rows = [];
    private bool _running;

    private RecipeProject Project => _doc.Project;
    private string Paks => Session.Paks!;

    public BuildWindow(Window owner, ProjectDocument doc)
    {
        Owner = owner;
        _doc = doc;
        InitializeComponent();
        Native.DarkTitleBar(this);
        Title = Loc.F("BuildTitle", Project.Name);
        PakName.Text = Project.Pak + ".pak";
        ModsPath.Text = "→ " + Path.Combine("SCUM", "Content", "Paks", "~mods");
        FolderBox.Text = Path.Combine(Project.Folder, "release");
        FolderHint.Text = Loc.F("FolderHint", Project.Pak);
        FillSummary();
        InstallBox.IsChecked = App.Settings.InstallByDefault;
        RegistryBox.Click += (_, _) => _registryTouched = true;
        Session.Changed += ShowMerger;
        Closed += (_, _) => Session.Changed -= ShowMerger;
        ShowMerger();
        _gameCheck.Tick += (_, _) => UpdateGameState();
        _gameCheck.Start();
        Closed += (_, _) => _gameCheck.Stop();
        Closing += (_, e) => e.Cancel = _running;
        UpdateGameState();
    }

    private void FillSummary()
    {
        var c = Project.Modkit.Cooking;
        var dishes = Project.Modkit.Clones.Count(x => x.New?.Contains("/Items/Food/", StringComparison.OrdinalIgnoreCase) == true);
        var cells = new (int N, string Key)[]
        {
            (Project.ChangedRecipes.Count(), "SumChanged"), (Project.NewRecipes.Count, "SumNewRecipes"), (dishes, "SumNewDishes"),
            (Project.OwnBooks.Count, "SumOwnBooks"), (c?.Books.Count ?? 0, "SumGameBooks"),
        };
        for (var i = 0; i < cells.Length; i++)
        {
            var p = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            p.Children.Add(new TextBlock { Text = cells[i].N.ToString(), FontFamily = (FontFamily)FindResource("CondFont"), FontWeight = FontWeights.SemiBold, FontSize = 22 });
            p.Children.Add(new TextBlock { Text = Loc.P(cells[i].Key, cells[i].N), FontSize = 12, Foreground = (Brush)FindResource("Text2"), TextWrapping = TextWrapping.Wrap });
            Summary.Children.Add(new Border
            {
                BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, i < cells.Length - 1 ? 1 : 0, 0),
                Child = p,
            });
        }
    }

    private bool Install => InstallBox.IsChecked == true;

    private void Install_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        FolderPanel.Visibility = Install ? Visibility.Collapsed : Visibility.Visible;
        ShowMerger();
        OpenFolderText.Text = Loc.T(Install ? "OpenModsFolder" : "OpenReleaseFolder");
        UpdateGameState();
    }

    private void UpdateGameState()
    {
        var running = Install && ProjectBuilder.GameRunning();
        GameRunningBanner.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        BuildButton.IsEnabled = !_running && !running;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("SaveToFolder") };
        if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text;
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Install ? Path.Combine(Paks, "~mods") : FolderBox.Text;
        if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void CopySpawn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SpawnText.Text);
            CopyText.Text = Loc.T("Copied");
        }
        catch (Exception)
        {
            // clipboard busy: the commands stay visible to copy by hand
        }
    }

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        var key = App.Settings.AesKey;
        if (key == null || Session.Paks == null) return;
        _running = true;
        UpdateGameState();
        Steps.Children.Clear();
        _rows.Clear();
        SpawnPanel.Visibility = Visibility.Collapsed;
        var request = new BuildRequest
        {
            Paks = Paks, AesKey = key, Install = Install, OutputFolder = Install ? null : FolderBox.Text,
            UpdateRegistry = RegistryBox.IsChecked == true, MergerCli = Session.MergerCli,
            AlsoPaks = Install && AlsoClientBox.Visibility == Visibility.Visible && AlsoClientBox.IsChecked == true ? ClientPaks : null,
        };
        var progress = new Progress<BuildStep>(ShowStep);
        var project = RecipeProject.Open(Project.Folder); // what is saved on disk
        BuildReport report;
        try
        {
            report = await Task.Run(() => ProjectBuilder.Run(project, request, progress));
        }
        finally
        {
            _running = false;
        }
        if (report.Mods.Any(m => !m.Included)) Session.Warnings++;
        if (report.Pak == null) Session.Errors++;
        Session.NotifyChanged();
        BuildButtonText.Text = Loc.T("BuildAgain");
        UpdateGameState();
        var spawn = ProjectBuilder.SpawnCommands(_doc);
        if (report.Pak != null && spawn.Count > 0)
        {
            SpawnText.Text = string.Join("\n", spawn);
            CopyText.Text = Loc.T("CopyCommands");
            SpawnPanel.Visibility = Visibility.Visible;
        }
    }

    private void ShowStep(BuildStep s)
    {
        if (!_rows.TryGetValue(s.Key, out var row))
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Style = (Style)FindResource("Icon"), FontSize = 14 };
            var text = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var result = new TextBlock { FontSize = 12, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var detail = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 2, 0, 4), Visibility = Visibility.Collapsed };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(result, 2);
            grid.Children.Add(icon);
            grid.Children.Add(text);
            grid.Children.Add(result);
            var block = new StackPanel();
            block.Children.Add(grid);
            block.Children.Add(detail);
            Steps.Children.Add(new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 6), Child = block });
            row = (icon, text, result, detail);
            _rows[s.Key] = row;
        }
        var (glyph, color) = s.State switch
        {
            StepState.Running => ("", "Text2"),
            StepState.Ok => ("", "Ok"),
            StepState.Warning => ("", "Warn"),
            _ => ("", "Danger"),
        };
        row.Icon.Text = glyph;
        row.Icon.Foreground = (Brush)FindResource(color);
        row.Text.Text = StepText(s);
        row.Result.Text = StepResult(s);
        row.Text.ToolTip = s.Key == "registry" && !string.IsNullOrEmpty(s.Result) ? s.Result : null;
        var showDetail = s.State is StepState.Failed or StepState.Warning && !string.IsNullOrEmpty(s.Detail);
        row.Detail.Text = showDetail ? s.Detail : "";
        row.Detail.Foreground = (Brush)FindResource(s.State == StepState.Failed ? "Danger" : "Warn");
        row.Detail.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        if (s.Key == "registry")
            RegistryHelp.Visibility = s.State is StepState.Warning or StepState.Failed && Session.MergerExe != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string StepText(BuildStep s) => (s.Text switch
    {
        "StepRegistryOk" or "StepRegistryWarn" => Loc.F(s.Text, s.Result is { Length: > 0 } mods ? mods.Split(", ").Length : 0),
        _ => Loc.T(s.Text),
    }) + (s.Key.EndsWith("-client", StringComparison.Ordinal) ? Loc.T("StepClientSuffix") : "");

    private static string StepResult(BuildStep s) => s.Key.Replace("-client", "") switch
    {
        "clones" or "edits" when s.State == StepState.Ok => Loc.P("PackagesCount", int.Parse(s.Result ?? "0")),
        "check" when s.State != StepState.Running => Loc.P("ProblemsCount", int.Parse(s.Result ?? "0")),
        "install" when s.State == StepState.Ok => FormatSize(long.Parse(s.Result ?? "0")),
        "registry" when s.State == StepState.Ok => "pakchunk999-ModsRegistry",
        _ => "",
    };

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? Loc.F("SizeMB", (bytes / 1048576.0).ToString("0.0", Loc.Instance.Culture)) : Loc.F("SizeKB", Math.Max(1, bytes / 1024));

    // ------------------------------------------------------------------ ScumRegistryMerger

    /// <summary>Installed: its version (and a newer one on GitHub). Missing: "not installed" and a download button.</summary>
    private void ShowMerger()
    {
        var installed = Session.MergerCli != null;
        RegistryBox.IsEnabled = Install && installed && !_running;
        if (!RegistryBox.IsEnabled) RegistryBox.IsChecked = false;
        else if (!_registryTouched) RegistryBox.IsChecked = true;
        MergerState.Text = installed
            ? Loc.F("MergerInstalled", Session.MergerVersion?.ToString(3) ?? "?")
            : Loc.T("MergerMissing");
        MergerState.Foreground = (Brush)FindResource(installed ? "Text2" : "Warn");
        var latest = Session.MergerLatest;
        MergerNewBadge.Visibility = installed && Session.MergerUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        MergerNewText.Text = latest != null ? Loc.F("MergerOnGitHub", latest.Version.ToString(3)) : "";
        MergerActionText.Text = Loc.T(installed ? "MergerUpdate" : "MergerDownload");
        MergerActionButton.Visibility = !installed || Session.MergerUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        MergerActionButton.IsEnabled = !_downloading;
        RegistryPanel.Visibility = Install ? Visibility.Visible : Visibility.Collapsed;
        ShowServer();
        OpenMergerButton.Visibility = Session.MergerExe != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _registryTouched;

    /// <summary>The SCUM client's Paks when the mod is for SCUM Server and the client is installed here, else null.</summary>
    private static string? ClientPaks => App.Settings.Kind == ScumModKit.Game.InstallKind.Server
        ? ScumModKit.Game.GameLocator.ResolvePaks(ScumModKit.Game.GameLocator.FindInstall(ScumModKit.Game.InstallKind.Client)) : null;

    /// <summary>Building for SCUM Server: say what the players need, offer to install into the SCUM client too.</summary>
    private void ShowServer()
    {
        var server = App.Settings.Kind == ScumModKit.Game.InstallKind.Server;
        ServerPanel.Visibility = server ? Visibility.Visible : Visibility.Collapsed;
        if (!server) return;
        ServerText.Text = Loc.T(ProjectBuilder.ServerAlone(Project) ? "ServerAloneOk" : "ServerNeedsClients");
        AlsoClientBox.Visibility = Install && ClientPaks != null ? Visibility.Visible : Visibility.Collapsed;
    }
    private bool _downloading;

    /// <summary>Downloads ScumRegistryMerger (window app + console part) from its latest GitHub release, or updates it.</summary>
    private async void MergerAction_Click(object sender, RoutedEventArgs e)
    {
        _downloading = true;
        var text = MergerActionText.Text;
        MergerActionText.Text = Loc.T("Downloading");
        ShowMerger();
        try
        {
            var release = Session.MergerLatest ?? await MergerLink.LatestAsync()
                          ?? throw new InvalidOperationException(Loc.T("MergerNoRelease"));
            await MergerLink.InstallAsync(release, Session.MergerExe);
            if (App.Settings.MergerPath == null && Session.MergerExe == null)
            {
                App.Settings.MergerPath = Path.Combine(MergerLink.InstallFolder, MergerLink.ExeName);
                App.Settings.Save();
            }
            Session.RefreshMerger(checkOnline: true);
        }
        catch (Exception ex)
        {
            Dialog.Show(this, Loc.T("MergerDownloadFailed"), ex.Message, Loc.T("Close"), icon: "\uE7BA");
        }
        finally
        {
            _downloading = false;
            MergerActionText.Text = text;
            ShowMerger();
        }
    }

    private void MergerPage_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(MergerLink.PageUrl) { UseShellExecute = true });

    /// <summary>The ScumRegistryMerger window: every mod's state (e.g. built for another game version), its own settings.</summary>
    private void OpenMerger_Click(object sender, RoutedEventArgs e)
    {
        if (Session.MergerExe is not { } exe) return;
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
        catch (Exception ex) { Dialog.Show(this, Loc.T("OpenMerger"), ex.Message, Loc.T("Close"), icon: ""); }
    }
}
