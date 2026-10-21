using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RecipeEditor.Core.Project;

namespace RecipeEditor.Gui;

/// <summary>A row of the project list.</summary>
public sealed class ProjectRowView(RecipeProject project, string? paks)
{
    public RecipeProject Project { get; } = project;
    public string Name => Project.Name;
    public string FileLine => $"{Project.Pak}.pak · {Project.Folder}";

    public string Stats
    {
        get
        {
            var parts = new List<string>();
            var changed = Project.ChangedRecipes.Count();
            if (changed > 0) parts.Add(Loc.F("StatChanged", changed));
            if (Project.NewRecipes.Count > 0) parts.Add(Loc.F("StatNew", Project.NewRecipes.Count));
            return parts.Count > 0 ? string.Join(" · ", parts) : Loc.T("StatEmpty");
        }
    }

    public string Stats2 => Project.OwnBooks.Count > 0 ? Loc.F("StatBooks", Project.OwnBooks.Count) : "";

    private FileInfo? Pak => Project.InstalledPak(paks);

    public string Badge => Project.ReadOnly ? Loc.T("BadgeReadOnly") : Pak != null ? Loc.T("BadgeBuilt") : Loc.T("BadgeNotBuilt");
    public string? BadgeTip => Project.ReadOnly ? Loc.T("ReadOnlyTip") : null;
    public Brush BadgeBg => Res(Project.ReadOnly ? "WarnBg" : Pak != null ? "OkBg" : "Surface2");
    public Brush BadgeFg => Res(Project.ReadOnly ? "Warn" : Pak != null ? "Ok" : "Muted");

    /// <summary>When the installed pak was built, else when modkit.json last changed.</summary>
    public string Date => FriendlyDate(Pak?.LastWriteTime ?? File.GetLastWriteTime(Project.ModkitPath));

    public static string FriendlyDate(DateTime t)
    {
        var c = Loc.Instance.Culture;
        if (t.Date == DateTime.Today) return Loc.F("Today", t.ToString("HH:mm", c));
        if (t.Date == DateTime.Today.AddDays(-1)) return Loc.F("Yesterday", t.ToString("HH:mm", c));
        return t.ToString(t.Year == DateTime.Today.Year ? "dd.MM" : "dd.MM.yyyy", c);
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}

/// <summary>Projects (open / open a folder) and a new project (name -> pakchunkNNN-Name_P, first free number).</summary>
public partial class ProjectsWindow : Window
{
    private List<RecipeProject> _projects = [];
    private string? _root;

    public ProjectsWindow()
    {
        InitializeComponent();
        Native.DarkTitleBar(this);
        _root = App.Settings.NewProjectsRoot;
        Reload();
        Loc.Instance.PropertyChanged += OnLanguage;
        Closed += (_, _) => Loc.Instance.PropertyChanged -= OnLanguage;
    }

    private void OnLanguage(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Reload();

    private void Reload()
    {
        var selected = (List.SelectedItem as ProjectRowView)?.Project.Folder;
        _projects = ProjectList.Load(App.Settings);
        var rows = _projects.Select(p => new ProjectRowView(p, Session.Paks)).ToList();
        List.ItemsSource = rows;
        List.SelectedItem = rows.FirstOrDefault(r => r.Project.Folder == selected) ?? rows.FirstOrDefault();
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var kind = App.Settings.Kind == ScumModKit.Game.InstallKind.Server ? Loc.T("Server") : Loc.T("Client");
        GameLine.Text = $"{kind} · {App.Settings.GameFolder}";
        UpdateOpenButton();
        UpdateNew();
    }

    private void UpdateOpenButton()
    {
        var row = List.SelectedItem as ProjectRowView;
        OpenButton.IsEnabled = row != null;
        OpenButton.Content = row != null ? Loc.F("OpenProject", row.Name) : Loc.F("OpenProject", "").Trim();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOpenButton();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(List, d) is ListBoxItem { DataContext: ProjectRowView row })
            OpenProject(row.Project);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is ProjectRowView row) OpenProject(row.Project);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("OpenFolderTitle") };
        if (dlg.ShowDialog(this) != true) return;
        if (!File.Exists(Path.Combine(dlg.FolderName, RecipeProject.ModkitFile)))
        {
            Dialog.Show(this, Loc.T("OpenFolder").TrimEnd('…'), Loc.T("NotAProject"), Loc.T("Close"), icon: "");
            return;
        }
        OpenProject(RecipeProject.Open(dlg.FolderName));
    }

    private void OpenProject(RecipeProject project)
    {
        App.Settings.Remember(project.Folder);
        App.Settings.Save();
        var main = new MainWindow(project);
        main.Show();
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (App.OpenSettings(this)) Reload();
    }

    // ------------------------------------------------------------------ new project

    private int _number;

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateNew();

    private string NewFolder => Path.Combine(_root ?? App.Settings.NewProjectsRoot, NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "…");

    private void UpdateNew()
    {
        var name = NameBox.Text.Trim();
        _number = RecipeProject.FirstFreeNumber(Session.Paks, _projects);
        FolderBox.Text = NewFolder;
        // the project's own folder name is the useful end of the path
        Dispatcher.BeginInvoke(() => FolderBox.ScrollToHorizontalOffset(double.MaxValue), System.Windows.Threading.DispatcherPriority.Loaded);
        Derived.Inlines.Clear();
        string? error = null;
        if (name.Length > 0 && !RecipeProject.IsValidName(name)) error = Loc.T("NameInvalid");
        else if (_projects.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) error = Loc.T("NameTaken");
        else if (name.Length > 0 && File.Exists(Path.Combine(NewFolder, RecipeProject.ModkitFile))) error = Loc.T("FolderTaken");

        Derived.Inlines.Add(new Run(Loc.T("ModFile") + " "));
        var code = new Border
        {
            Background = (Brush)FindResource("Surface2"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
            Child = new TextBlock
            {
                Text = RecipeProject.PakName(name.Length > 0 && error == null ? name : "<…>", _number) + ".pak",
                Foreground = (Brush)FindResource("Text"), FontSize = 12,
            },
        };
        Derived.Inlines.Add(new InlineUIContainer(code) { BaselineAlignment = BaselineAlignment.Center });
        Derived.Inlines.Add(new LineBreak());
        if (error != null) Derived.Inlines.Add(new Run(error) { Foreground = (Brush)FindResource("Danger") });
        else Derived.Inlines.Add(new Run(Loc.F("NumberNote", _number)));
        NameBox.BorderBrush = (Brush)FindResource(error != null ? "Danger" : "Line");
        CreateButton.IsEnabled = name.Length > 0 && error == null;
    }

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("ProjectFolderTitle") };
        if (Directory.Exists(_root)) dlg.InitialDirectory = _root;
        if (dlg.ShowDialog(this) != true) return;
        _root = dlg.FolderName;
        App.Settings.ProjectsRoot = _root;
        App.Settings.Save();
        UpdateNew();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var project = RecipeProject.Create(NewFolder, NameBox.Text.Trim(), _number, DescriptionBox.Text);
        OpenProject(project);
    }
}
