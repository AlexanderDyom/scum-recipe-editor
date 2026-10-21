using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Settings;
using ScumModKit.Game;

namespace RecipeEditor.Gui;

/// <summary>
/// First start: game folder (client / server found in Steam) and the AES key, which is required. The key is checked on the
/// game's paks as soon as it is typed; "Continue" opens only after "key works, recipes: 113, cookbooks: 9".
/// </summary>
public partial class SetupWindow : Window
{
    private readonly string? _client = GameLocator.FindInstall(InstallKind.Client);
    private readonly string? _server = GameLocator.FindInstall(InstallKind.Server);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _loading = true;
    private int _check;
    private (int Recipes, int Books)? _counts;

    public AppSettings Result { get; }

    /// <summary>The game opened with the checked key; the caller takes it over after "Continue".</summary>
    public GameProvider? Game { get; private set; }

    public SetupWindow(AppSettings settings, string? prefillKey, bool settingsMode)
    {
        Result = settings;
        InitializeComponent();
        Native.DarkTitleBar(this);
        LanguageList.Fill(LangBox, settings.Language);
        if (settingsMode)
        {
            TitleText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[SettingsTitle]") { Source = Loc.Instance });
            ContinueButton.SetBinding(ContentProperty, new System.Windows.Data.Binding("[Save]") { Source = Loc.Instance });
        }

        var kind = settings.Kind;
        if (settings.GameFolder == null && _client == null && _server != null) kind = InstallKind.Server;
        (kind == InstallKind.Server ? ServerCard : ClientCard).IsChecked = true;
        PathBox.Text = settings.GameFolder ?? (kind == InstallKind.Server ? _server : _client) ?? "";
        KeyBox.Password = prefillKey ?? settings.AesKey ?? "";
        _loading = false;
        _debounce.Tick += (_, _) => { _debounce.Stop(); CheckKey(); };
        Refresh();
        Loc.Instance.PropertyChanged += OnLanguage;
        Closed += (_, _) =>
        {
            Loc.Instance.PropertyChanged -= OnLanguage;
            if (DialogResult != true) Game?.Dispose();
        };
        if (KeyBox.Password.Length > 0) CheckKey();
        else ShowKeyState();
    }

    private void OnLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Refresh();
        ShowKeyState();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void Refresh()
    {
        ClientFound.Text = _client != null ? "✓ " + Loc.T("FoundInSteam") : Loc.T("NotFound");
        ClientFound.Foreground = Res(_client != null ? "Ok" : "Muted");
        ServerFound.Text = _server != null ? "✓ " + Loc.T("FoundInSteam") : Loc.T("NotFound");
        ServerFound.Foreground = Res(_server != null ? "Ok" : "Muted");
        ShowPath();
    }

    private void ShowPath()
    {
        var paks = GameLocator.ResolvePaks(PathBox.Text);
        if (paks != null)
        {
            var mods = Path.Combine(paks, "~mods");
            var n = Directory.Exists(mods) ? Directory.EnumerateFiles(mods, "*.pak").Count(f => !Path.GetFileName(f).Contains("ModsRegistry", StringComparison.OrdinalIgnoreCase)) : 0;
            PathStatus.Text = "✓ " + Loc.F("PaksFound", n);
            PathStatus.Foreground = Res("Ok");
        }
        else
        {
            var empty = string.IsNullOrWhiteSpace(PathBox.Text);
            PathStatus.Text = empty ? Loc.T("PathEmpty") : Loc.T("PaksNotFound");
            PathStatus.Foreground = Res(empty ? "Muted" : "Danger");
        }
    }

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var found = ServerCard.IsChecked == true ? _server : _client;
        if (found != null) PathBox.Text = found;
    }

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        ShowPath();
        Restart();
    }

    private void KeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        KeyHint.Visibility = KeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_loading) Restart();
    }

    /// <summary>The key or the folder changed: forget the last check and check again after a short pause.</summary>
    private void Restart()
    {
        _check++;
        _counts = null;
        Game?.Dispose();
        Game = null;
        ShowKeyState();
        _debounce.Stop();
        _debounce.Start();
    }

    private string? _keyError;
    private bool _checking;

    private void ShowKeyState()
    {
        var empty = KeyBox.Password.Trim().Length == 0;
        KeyHint.Visibility = KeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyBox.BorderBrush = Res(_counts != null ? "Line" : empty || _keyError != null ? "Danger" : "Line");
        KeyCallout.Visibility = _counts == null ? Visibility.Visible : Visibility.Collapsed;
        ContinueButton.IsEnabled = _counts != null && Game != null;
        if (_checking)
        {
            KeyStatus.Text = Loc.T("KeyChecking");
            KeyStatus.Foreground = Res("Text2");
        }
        else if (_counts is { } c)
        {
            KeyStatus.Text = "✓ " + Loc.F("KeyWorks", c.Recipes, c.Books);
            KeyStatus.Foreground = Res("Ok");
        }
        else if (_keyError != null)
        {
            KeyStatus.Text = _keyError;
            KeyStatus.Foreground = Res("Danger");
        }
        KeyStatus.Visibility = _checking || _counts != null || _keyError != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Opens the paks with the key in the background (a second or two) and counts recipes and cookbooks.</summary>
    private async void CheckKey()
    {
        var id = ++_check;
        _keyError = null;
        _counts = null;
        var key = KeyBox.Password.Trim();
        var folder = PathBox.Text;
        if (key.Length == 0 || GameLocator.ResolvePaks(folder) == null) { ShowKeyState(); return; }
        try { GameProvider.NormalizeKey(key); }
        catch (FormatException)
        {
            _keyError = Loc.T("KeyBadFormat");
            ShowKeyState();
            return;
        }
        _checking = true;
        ShowKeyState();
        GameProvider? game = null;
        (int, int)? counts = null;
        string? error = null;
        try
        {
            (game, counts) = await Task.Run(() =>
            {
                var g = GameProvider.Open(folder, key);
                try { return (g, GameData.Count(g)); }
                catch { g.Dispose(); throw; }
            });
        }
        catch (Exception)
        {
            error = Loc.T("KeyWrong");
        }
        if (id != _check)
        {
            game?.Dispose(); // the key or folder changed meanwhile
            return;
        }
        _checking = false;
        Game = game;
        _counts = counts;
        _keyError = error;
        ShowKeyState();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("BrowseTitle") };
        if (Directory.Exists(PathBox.Text)) dlg.InitialDirectory = PathBox.Text;
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FolderName;
    }

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangBox.SelectedItem is LangOption o)
        {
            Result.Language = o.Code;
            Loc.Instance.Set(o.Code);
        }
    }

    private void WhereKey_Click(object sender, RoutedEventArgs e) =>
        Dialog.Show(this, Loc.T("WhereKeyTitle"), Loc.T("WhereKeyText"), Loc.T("Close"), icon: "");

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (Game == null || _counts == null) return;
        Result.GameFolder = GameLocator.InstallRoot(Game.Paks);
        Result.Kind = ServerCard.IsChecked == true ? InstallKind.Server : InstallKind.Client;
        Result.AesKey = KeyBox.Password.Trim();
        DialogResult = true;
    }
}

/// <summary>A language choice: "auto" = the system language. Ai = machine translation, shown with an "AI" mark.</summary>
public sealed record LangOption(string Code, string Text, bool Ai)
{
    public Visibility AiVisibility => Ai ? Visibility.Visible : Visibility.Collapsed;
}

internal static class LanguageList
{
    public static List<LangOption> Options()
    {
        var sys = Loc.SystemLanguage();
        var list = new List<LangOption> { new("auto", Loc.F("LangSystem", Loc.Languages.First(l => l.Code == sys).Native), Loc.IsAi(sys)) };
        list.AddRange(Loc.Languages.Select(l => new LangOption(l.Code, l.Native, l.Ai)));
        return list;
    }

    public static void Fill(ComboBox box, string selected)
    {
        var options = Options();
        box.ItemsSource = options;
        box.SelectedItem = options.FirstOrDefault(o => o.Code == (string.IsNullOrEmpty(selected) ? "auto" : selected)) ?? options[0];
    }
}
