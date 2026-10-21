using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RecipeEditor.Gui;

/// <summary>Game icons as WPF images: PNG made by the core's IconCache in the background, kept in memory once loaded.</summary>
public static class IconLoader
{
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(4);

    public static Task<ImageSource?> Get(string texture) => Loaded.GetOrAdd(texture, t => Task.Run(async () =>
    {
        var cache = Session.Icons;
        if (cache == null) return null;
        await Gate.WaitAsync();
        try
        {
            var file = cache.Png(t);
            if (file == null) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze();
            return (ImageSource?)bmp;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }));

    public static void Clear() => Loaded.Clear();
}

/// <summary>An item / dish icon from the game, or its first letter until (or unless) the icon loads.</summary>
public sealed class IconView : Grid
{
    public static readonly DependencyProperty TextureProperty = DependencyProperty.Register(nameof(Texture), typeof(string), typeof(IconView),
        new PropertyMetadata(null, (d, _) => ((IconView)d).Load()));
    public static readonly DependencyProperty FallbackProperty = DependencyProperty.Register(nameof(Fallback), typeof(string), typeof(IconView),
        new PropertyMetadata("", (d, e) => ((IconView)d)._letter.Text = (string)e.NewValue));
    public static readonly DependencyProperty LetterSizeProperty = DependencyProperty.Register(nameof(LetterSize), typeof(double), typeof(IconView),
        new PropertyMetadata(16.0, (d, e) => ((IconView)d)._letter.FontSize = (double)e.NewValue));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _letter = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };

    public string? Texture { get => (string?)GetValue(TextureProperty); set => SetValue(TextureProperty, value); }
    public string Fallback { get => (string)GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }
    public double LetterSize { get => (double)GetValue(LetterSizeProperty); set => SetValue(LetterSizeProperty, value); }

    public IconView()
    {
        _letter.SetResourceReference(TextBlock.FontFamilyProperty, "CondFont");
        _letter.SetResourceReference(TextBlock.ForegroundProperty, "Text2");
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_letter);
        Children.Add(_image);
    }

    private async void Load()
    {
        var texture = Texture;
        _image.Source = null;
        _letter.Visibility = Visibility.Visible;
        if (string.IsNullOrEmpty(texture)) return;
        var src = await IconLoader.Get(texture);
        if (texture != Texture) return; // reused for another item meanwhile
        _image.Source = src;
        _letter.Visibility = src == null ? Visibility.Visible : Visibility.Collapsed;
    }
}
