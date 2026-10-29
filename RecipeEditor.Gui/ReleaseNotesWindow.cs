using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RecipeEditor.Core.Settings;

namespace RecipeEditor.Gui;

/// <summary>
/// The editor's releases in a window, with the description written on GitHub: "Version X is available" at start
/// (update now / later) and "Updated to X" once at the first start of a new version.
/// </summary>
public static class ReleaseNotesWindow
{
    /// <summary>A newer release at start; true when the user chose "Update now".</summary>
    public static bool ShowUpdate(Window? owner, GitHubRelease latest) =>
        Show(owner, "", "AccentText", Loc.F("NewVersionTitle", latest.Tag.TrimStart('v')),
            Loc.F("NewVersionCaption", App.Version, latest.Published.ToLocalTime().ToString("d MMMM yyyy", Loc.Instance.Culture)),
            latest.Notes, latest.PageUrl, Loc.T("UpdateNow"), Loc.T("UpdateLater"), Loc.T("UpdateKeeps"));

    /// <summary>"What's new" of this version (its release, or null when GitHub could not be reached).</summary>
    public static void ShowWhatsNew(Window? owner, GitHubRelease? release, string? previous) =>
        Show(owner, "", "Ok", Loc.F("WhatsNewTitle", App.Version),
            previous != null ? Loc.F("WhatsNewCaptionFrom", previous) : Loc.T("WhatsNewCaption"),
            release?.Notes is { Length: > 0 } notes ? notes : Loc.T("WhatsNewOffline"),
            release?.PageUrl ?? App.ProjectPage + "/releases", Loc.T("GotIt"), null, null);

    private static bool Show(Window? owner, string icon, string iconBrush, string title, string caption, string notes, string page,
                             string primary, string? secondary, string? note)
    {
        var res = Application.Current.Resources;
        var w = new Window
        {
            Title = "SCUM Recipe Editor", SizeToContent = SizeToContent.Height, Width = 540, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = owner == null || !owner.IsVisible,
            Style = (Style)res["AppWindow"],
        };
        if (owner != null && owner.IsVisible) w.Owner = owner;
        Native.DarkTitleBar(w);

        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = icon, Style = (Style)res["Icon"], Foreground = (Brush)res[iconBrush], Margin = new Thickness(0, 0, 10, 0), FontSize = 20 });
        head.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock { Text = caption, Foreground = (Brush)res["Muted"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) });
        panel.Children.Add(new Border
        {
            Background = (Brush)res["Panel"], BorderBrush = (Brush)res["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Markdown(notes), Padding = new Thickness(14, 12, 14, 12) },
        });

        var links = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var open = new Button { Content = Loc.T("OpenRelease"), Style = (Style)res["Link"], VerticalAlignment = VerticalAlignment.Center };
        open.Click += (_, _) => Open(page);
        links.Children.Add(open);
        if (note != null)
        {
            var n = new TextBlock { Text = note, Foreground = (Brush)res["Muted"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(n, Dock.Right);
            links.Children.Add(n);
        }
        panel.Children.Add(links);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var result = false;
        if (secondary != null)
        {
            var b2 = new Button { Content = secondary, Style = (Style)res["Btn"], Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            b2.Click += (_, _) => w.Close();
            buttons.Children.Add(b2);
        }
        var b1 = new Button { Content = primary, Style = (Style)res["Primary"], IsDefault = true, IsCancel = secondary == null };
        b1.Click += (_, _) => { result = true; w.Close(); };
        buttons.Children.Add(b1);
        panel.Children.Add(buttons);
        w.Content = panel;
        w.ShowDialog();
        return result;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no browser: nothing to do */ }
    }

    // ------------------------------------------------------------------ the release description (GitHub Markdown)

    /// <summary>
    /// The part of Markdown release descriptions use: headings, paragraphs, "-" / "*" / "1." lists, **bold**, `code` and
    /// [links](https://...). Anything else stays as text.
    /// </summary>
    public static UIElement Markdown(string text)
    {
        var res = Application.Current.Resources;
        var panel = new StackPanel();
        var paragraph = new List<string>();

        void Flush()
        {
            if (paragraph.Count == 0) return;
            panel.Children.Add(Text(string.Join(" ", paragraph), new Thickness(0, 0, 0, 8)));
            paragraph.Clear();
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0 || Regex.IsMatch(line, @"^\s*(-{3,}|\*{3,}|_{3,})\s*$")) { Flush(); continue; }
            if (Regex.Match(line, @"^\s*#{1,6}\s+(.*)$") is { Success: true } h)
            {
                Flush();
                var t = Text(h.Groups[1].Value.TrimEnd('#', ' '), new Thickness(0, panel.Children.Count > 0 ? 6 : 0, 0, 6));
                t.Foreground = (Brush)res["AccentText"];
                t.FontWeight = FontWeights.SemiBold;
                panel.Children.Add(t);
                continue;
            }
            if (Regex.Match(line, @"^(\s*)([-*+]|\d+[.)])\s+(.*)$") is { Success: true } li)
            {
                Flush();
                var marker = li.Groups[2].Value.Length == 1 && "-*+".Contains(li.Groups[2].Value) ? "•" : li.Groups[2].Value;
                var row = new DockPanel { Margin = new Thickness(Math.Min(li.Groups[1].Value.Length, 8) * 6, 0, 0, 4) };
                var m = new TextBlock { Text = marker, Foreground = (Brush)res["Muted"], Width = marker == "•" ? 14 : 20 };
                DockPanel.SetDock(m, Dock.Left);
                row.Children.Add(m);
                row.Children.Add(Text(li.Groups[3].Value, new Thickness(0)));
                panel.Children.Add(row);
                continue;
            }
            // a continued list item (indented line right after it) joins that item
            if (paragraph.Count == 0 && raw.StartsWith("  ") && panel.Children.Count > 0 && panel.Children[^1] is DockPanel last
                && last.Children[^1] is TextBlock item)
            {
                AddInlines(item, " " + line.Trim());
                continue;
            }
            paragraph.Add(line.Trim());
        }
        Flush();
        if (panel.Children.Count > 0 && panel.Children[^1] is FrameworkElement end) end.Margin = new Thickness(end.Margin.Left, end.Margin.Top, 0, 0);
        return panel;
    }

    private static TextBlock Text(string text, Thickness margin)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = margin };
        AddInlines(t, text);
        return t;
    }

    private static readonly Regex InlineRx = new(@"\*\*(?<b>.+?)\*\*|__(?<b>.+?)__|`(?<c>[^`]+)`|\[(?<lt>[^\]]+)\]\((?<lu>https?://[^)\s]+)\)", RegexOptions.Compiled);

    private static void AddInlines(TextBlock t, string text)
    {
        var res = Application.Current.Resources;
        var at = 0;
        foreach (Match m in InlineRx.Matches(text))
        {
            if (m.Index > at) t.Inlines.Add(new Run(text[at..m.Index]));
            if (m.Groups["b"].Success) t.Inlines.Add(new Bold(new Run(m.Groups["b"].Value)));
            else if (m.Groups["c"].Success)
                t.Inlines.Add(new Run(m.Groups["c"].Value) { Background = (Brush)res["Surface2"], FontFamily = new FontFamily("Consolas"), FontSize = 12 });
            else
            {
                var url = m.Groups["lu"].Value;
                var link = new Hyperlink(new Run(m.Groups["lt"].Value)) { Foreground = (Brush)res["AccentText"], NavigateUri = new Uri(url) };
                link.RequestNavigate += (_, _) => Open(url);
                t.Inlines.Add(link);
            }
            at = m.Index + m.Length;
        }
        if (at < text.Length) t.Inlines.Add(new Run(text[at..]));
    }
}
