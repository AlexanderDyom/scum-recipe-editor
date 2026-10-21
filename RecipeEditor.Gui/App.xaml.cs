using System.IO;
using System.Windows;
using System.Windows.Threading;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Settings;
using RecipeEditor.Core.Project;

namespace RecipeEditor.Gui;

/// <summary>
/// Start: setup (game folder + required AES key) when needed, then the projects window. Command line:
/// <c>ScumRecipeEditor.exe [project folder | modkit.json] [--aes-file key.txt]</c> — a project to open right away, and a
/// file whose first line is the key to put into the setup window (it is never shown or logged).
/// </summary>
public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>The editor's page on GitHub: releases, language files, issues.</summary>
    public const string ProjectPage = "https://github.com/AlexanderDyom/scum-recipe-editor";
    /// <summary>The version users see (0.1.0b: beta), from InformationalVersion.</summary>
    public static string Version =>
        typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false) is [System.Reflection.AssemblyInformationalVersionAttribute a, ..]
            ? a.InformationalVersion : typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private string? _keyFile;
    private string? _openProject;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--aes-file" && i + 1 < e.Args.Length) _keyFile = e.Args[++i];
            else if (!e.Args[i].StartsWith("--")) _openProject = e.Args[i];
        }
        Settings = AppSettings.Load();
        if (Settings.UseMergerSettings)
        {
            // one setup for both programs: the game folder and key ScumRegistryMerger already has (same Windows user)
            var (folder, key) = MergerLink.ReadMergerSettings();
            if (Settings.GameFolder == null && folder != null) Settings.GameFolder = folder;
            if (Settings.AesKey == null && key != null) Settings.AesKey = key;
        }
        Loc.Instance.Set(Settings.Language);

        if (!TryStartSession() && !RunSetup(null, settingsMode: false)) { Shutdown(); return; }

        RecipeProject? project = null;
        if (_openProject != null)
        {
            try
            {
                project = RecipeProject.Open(_openProject);
                Settings.Remember(project.Folder);
                Settings.Save();
            }
            catch (Exception ex)
            {
                Dialog.Show(null, Loc.T("ErrorTitle"), ex.Message, Loc.T("Close"));
            }
        }
        Session.RefreshMerger(Settings.CheckUpdates);
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        if (project != null) new MainWindow(project).Show();
        else new ProjectsWindow().Show();
    }

    /// <summary>Opens the game with the stored key (no window) when the settings are complete and the key still works.</summary>
    private bool TryStartSession()
    {
        if (!Settings.IsConfigured || _keyFile != null) return false;
        try
        {
            Session.Start(GameProvider.Open(Settings.GameFolder!, Settings.AesKey!));
            return true;
        }
        catch (Exception)
        {
            return false; // game updated or key wrong: the setup window says why
        }
    }

    /// <summary>The setup window (first start) or the same window as settings; true when the user confirmed.</summary>
    public bool RunSetup(Window? owner, bool settingsMode)
    {
        string? prefill = null;
        if (_keyFile != null)
        {
            try { prefill = File.ReadLines(_keyFile).FirstOrDefault()?.Trim(); }
            catch (IOException) { /* no file: type the key */ }
            _keyFile = null;
        }
        var language = Settings.Language;
        var w = new SetupWindow(Settings.Clone(), prefill, settingsMode);
        if (owner != null && owner.IsVisible)
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        if (w.ShowDialog() != true)
        {
            Loc.Instance.Set(Settings.Language); // the window may have previewed another language
            return false;
        }
        Settings = w.Result;
        Settings.Save();
        Loc.Instance.Set(Settings.Language);
        Session.Start(w.Game!);
        return true;
    }

    /// <summary>The settings window; true when saved.</summary>
    public static bool OpenSettings(Window owner, string section = "SecGame") => new SettingsWindow(owner, section).ShowDialog() == true;

    /// <summary>Settings saved in the settings window.</summary>
    public static void Apply(AppSettings s)
    {
        Settings = s;
        Settings.Save();
        Loc.Instance.Set(Settings.Language);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialog.Show(MainWindow, Loc.T("ErrorTitle"), e.Exception.Message, Loc.T("Close"));
        e.Handled = true;
    }
}
