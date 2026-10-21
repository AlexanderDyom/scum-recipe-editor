using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RecipeEditor.Gui;

/// <summary>
/// The status bar at the bottom of every big window (like ScumStudio's): game paks, key, server,
/// ScumRegistryMerger and its newer version, check again, warnings / errors of this session, the editor's version.
/// </summary>
public sealed class GlobalStatus : Border
{
    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    public GlobalStatus()
    {
        Height = 26;
        Padding = new Thickness(12, 0, 12, 0);
        BorderThickness = new Thickness(0, 1, 0, 0);
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(BackgroundProperty, "StatusBg");
        TextElement.SetFontSize(this, 11.5);
        SetResourceReference(TextElement.ForegroundProperty, "Text2");
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_right, Dock.Right);
        dock.Children.Add(_right);
        dock.Children.Add(_left);
        Child = dock;
        Loaded += (_, _) =>
        {
            Session.Changed += Refresh;
            Session.StatusChanged += Refresh;
            Loc.Instance.PropertyChanged += OnLanguage;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            Session.Changed -= Refresh;
            Session.StatusChanged -= Refresh;
            Loc.Instance.PropertyChanged -= OnLanguage;
        };
    }

    private void OnLanguage(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        _left.Children.Clear();
        var data = Session.Data;
        if (Session.Game == null || data is { IsFaulted: true }) Add(Loc.T("StPaksError"), "Danger");
        else if (data is { IsCompletedSuccessfully: true }) Add(Loc.T("StPaks"), "Ok");
        else Add(Loc.T("StPaksLoading"), "Warn");
        if (App.Settings.AesKeyProtected != null) Add(Loc.T("StKey"), "Ok");
        Add(Loc.T(Session.ServerFound ? "StServer" : "StNoServer"), Session.ServerFound ? "Ok" : "Muted");
        if (Session.MergerCli != null)
        {
            var v = Session.MergerVersion?.ToString(3) ?? "?";
            if (Session.MergerUpdateAvailable) Add($"ScumRegistryMerger {v} → {Session.MergerLatest!.Version.ToString(3)}", "Warn", Loc.T("StMergerNewTip"));
            else Add($"ScumRegistryMerger {v}", "Ok");
        }
        else Add(Loc.T("StMergerMissing"), "Warn", Loc.T("StMergerMissingTip"));

        _right.Children.Clear();
        var refresh = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11 },
            Style = (Style)FindResource("Link"), Margin = new Thickness(0, 0, 14, 0), ToolTip = Loc.T("StRefreshTip"), VerticalAlignment = VerticalAlignment.Center,
        };
        refresh.Template = Plain();
        refresh.Click += (_, _) => Session.RefreshMerger(checkOnline: true);
        System.Windows.Automation.AutomationProperties.SetName(refresh, Loc.T("StRefreshTip"));
        _right.Children.Add(refresh);
        _right.Children.Add(Counter("", "Warn", Session.Warnings, Loc.T("StWarnings")));
        _right.Children.Add(Counter("", "Danger", Session.Errors, Loc.T("StErrors")));
        _right.Children.Add(new TextBlock { Text = Loc.F("VersionLine", App.Version), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
    }

    private static ControlTemplate Plain()
    {
        var t = new ControlTemplate(typeof(Button));
        var f = new FrameworkElementFactory(typeof(ContentPresenter));
        f.SetValue(BackgroundProperty, Brushes.Transparent);
        t.VisualTree = f;
        return t;
    }

    private StackPanel Counter(string glyph, string color, int n, string tip)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
        var g = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        g.SetResourceReference(TextBlock.ForegroundProperty, color);
        p.Children.Add(g);
        p.Children.Add(new TextBlock { Text = n.ToString(), VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    private void Add(string text, string dot, string? tip = null)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
        var e = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        e.SetResourceReference(Shape.FillProperty, dot);
        item.Children.Add(e);
        item.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        _left.Children.Add(item);
    }
}
