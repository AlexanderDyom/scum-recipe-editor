using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RecipeEditor.Core.Models;

namespace RecipeEditor.Gui;

/// <summary>
/// A game mesh in 3D (WPF Viewport3D): orbit with the left button, pan with the right or middle one, zoom with the wheel,
/// double-click resets. A floor grid under the model; buttons for the view (perspective / top / front) and reset. UE units
/// (cm, Z up, left-handed) map to WPF as (X, Z, Y): Y up, the swap mirrors the handedness back.
/// </summary>
public sealed class ModelView : Grid
{
    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 40, NearPlaneDistance = 0.5, FarPlaneDistance = 5000 };
    private readonly ModelVisual3D _content = new();
    private readonly TextBlock _caption = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _message = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) };
    private Point3D _target;
    private double _yaw = 35, _pitch = 25, _distance = 60, _radius = 15;
    private Point _last;
    private MouseButton? _drag;

    /// <summary>Extra controls laid over the view's top-left corner (e.g. "ready dish / in the cookware").</summary>
    public StackPanel TopLeft { get; } = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };

    public ModelView()
    {
        Background = new RadialGradientBrush(Color.FromRgb(0x2E, 0x35, 0x3A), Color.FromRgb(0x16, 0x1A, 0x1C)) { Center = new Point(0.5, 0.62), GradientOrigin = new Point(0.5, 0.62), RadiusX = 0.8, RadiusY = 0.8 };
        _viewport.Camera = _camera;
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x70, 0x70, 0x70)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0xC8, 0xC4, 0xBC), new Vector3D(-0.4, -1, -0.6)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x50, 0x54, 0x5A), new Vector3D(0.6, -0.3, 0.8)));
        _viewport.Children.Add(new ModelVisual3D { Content = lights });
        _viewport.Children.Add(_content);
        Children.Add(_viewport);

        _caption.Foreground = (Brush)Application.Current.FindResource("Text2");
        _caption.Margin = new Thickness(10, 0, 12, 10);
        _caption.VerticalAlignment = VerticalAlignment.Bottom;
        Children.Add(_caption);
        _viewport.ToolTip = Loc.T("ModelViewHint"); // how to turn it: on hover, the corners stay for the model's name
        _message.Foreground = (Brush)Application.Current.FindResource("Muted");
        Children.Add(_message);
        Children.Add(TopLeft);

        var buttons = new StackPanel { Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(ViewButton("", Loc.T("ModelViewPerspective"), () => SetView(35, 25)));
        buttons.Children.Add(ViewButton("", Loc.T("ModelViewTop"), () => SetView(0, 89)));
        buttons.Children.Add(ViewButton("", Loc.T("ModelViewFront"), () => SetView(0, 2)));
        buttons.Children.Add(ViewButton("", Loc.T("ModelViewReset"), Reset));
        Children.Add(buttons);

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += (_, _) => { _drag = null; ReleaseMouseCapture(); };
        MouseWheel += (_, e) => { _distance = Math.Clamp(_distance * (e.Delta > 0 ? 0.88 : 1.14), _radius * 0.6, _radius * 20); UpdateCamera(); };
        UpdateCamera();
    }

    private Button ViewButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Style = (Style)Application.Current.FindResource("IconBtn"), Width = 32, Height = 32, Margin = new Thickness(0, 0, 0, 6), ToolTip = tip,
            Content = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("Icon"), FontSize = 15 },
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Shows a text instead of a model (loading, cannot be read, nothing chosen).</summary>
    public void ShowMessage(string text)
    {
        _content.Content = null;
        _caption.Text = "";
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
    }

    /// <summary>Shows a mesh; <paramref name="caption"/> goes under it (mesh name, triangles).</summary>
    public void Show(MeshData mesh, string caption) => Show([mesh], caption);

    /// <summary>Shows meshes together (a pot and what cooks in it), in their own places.</summary>
    public void Show(IReadOnlyList<MeshData> meshes, string caption)
    {
        var group = new Model3DGroup();
        var (min, max) = Bounds(meshes.SelectMany(m => m.Positions).ToArray());
        foreach (var mesh in meshes)
        foreach (var part in mesh.Parts)
        {
            var g = new MeshGeometry3D();
            var used = part.Indices.Distinct().ToArray();
            var map = new Dictionary<int, int>(used.Length);
            foreach (var i in used)
            {
                map[i] = g.Positions.Count;
                g.Positions.Add(new Point3D(mesh.Positions[i * 3], mesh.Positions[i * 3 + 2], mesh.Positions[i * 3 + 1]));
                g.Normals.Add(new Vector3D(mesh.Normals[i * 3], mesh.Normals[i * 3 + 2], mesh.Normals[i * 3 + 1]));
                g.TextureCoordinates.Add(new Point(mesh.Uvs[i * 2], mesh.Uvs[i * 2 + 1]));
            }
            foreach (var i in part.Indices) g.TriangleIndices.Add(map[i]);
            g.Freeze();
            var material = Material(part.TexturePng);
            group.Children.Add(new GeometryModel3D(g, material) { BackMaterial = material });
        }
        // the floor grid under the model, its size a round number of centimetres
        var center = new Point3D((min.X + max.X) / 2, (min.Z + max.Z) / 2, (min.Y + max.Y) / 2);
        var size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        group.Children.Add(Grid3D(center.X, min.Z, center.Z, Math.Max(10, Math.Ceiling(size * 1.6 / 10) * 10)));
        group.Freeze();
        _content.Content = group;
        _target = center;
        _radius = Math.Max(1, size / 2);
        _message.Visibility = Visibility.Collapsed;
        _caption.Text = caption;
        Reset();
    }

    private static Material Material(string? png)
    {
        if (png != null && System.IO.File.Exists(png))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(png);
                bmp.EndInit();
                bmp.Freeze();
                // UVs may go past 0..1: tile the texture
                var brush = new ImageBrush(bmp) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
                brush.Freeze();
                return new DiffuseMaterial(brush);
            }
            catch (Exception)
            {
                // an unreadable texture: plain grey below
            }
        }
        return new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x9A, 0x9E, 0x9C)));
    }

    /// <summary>Grid lines (thin quads) on the floor y, every <c>size / 10</c>, centred under the model.</summary>
    private static GeometryModel3D Grid3D(double cx, double y, double cz, double size)
    {
        var g = new MeshGeometry3D();
        var half = size / 2;
        var w = size / 400;
        for (var k = 0; k <= 10; k++)
        {
            var t = -half + k * size / 10;
            Quad(g, new Point3D(cx + t - w, y, cz - half), new Point3D(cx + t + w, y, cz - half), new Point3D(cx + t + w, y, cz + half), new Point3D(cx + t - w, y, cz + half));
            Quad(g, new Point3D(cx - half, y, cz + t - w), new Point3D(cx + half, y, cz + t - w), new Point3D(cx + half, y, cz + t + w), new Point3D(cx - half, y, cz + t + w));
        }
        g.Freeze();
        var m = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0x70, 0x6A, 0x74, 0x7A)));
        return new GeometryModel3D(g, m) { BackMaterial = m };
    }

    private static void Quad(MeshGeometry3D g, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        var i = g.Positions.Count;
        foreach (var p in new[] { a, b, c, d }) g.Positions.Add(p);
        foreach (var n in new[] { 0, 1, 2, 0, 2, 3 }) g.TriangleIndices.Add(i + n);
    }

    private static (Point3D Min, Point3D Max) Bounds(float[] p)
    {
        if (p.Length < 3) return (new Point3D(), new Point3D());
        double x0 = p[0], y0 = p[1], z0 = p[2], x1 = x0, y1 = y0, z1 = z0;
        for (var i = 0; i < p.Length; i += 3)
        {
            x0 = Math.Min(x0, p[i]); x1 = Math.Max(x1, p[i]);
            y0 = Math.Min(y0, p[i + 1]); y1 = Math.Max(y1, p[i + 1]);
            z0 = Math.Min(z0, p[i + 2]); z1 = Math.Max(z1, p[i + 2]);
        }
        return (new Point3D(x0, y0, z0), new Point3D(x1, y1, z1));
    }

    private void SetView(double yaw, double pitch)
    {
        (_yaw, _pitch) = (yaw, pitch);
        UpdateCamera();
    }

    private void Reset()
    {
        (_yaw, _pitch) = (35, 25);
        _distance = _radius * 4.6;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var dir = new Vector3D(Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));
        _camera.Position = _target + dir * _distance;
        _camera.LookDirection = -dir;
        _camera.UpDirection = _pitch > 88 ? new Vector3D(0, 0, -1) : new Vector3D(0, 1, 0);
        _camera.NearPlaneDistance = Math.Max(0.1, _distance / 100);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindButton(d)) return; // the view buttons
        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
        {
            Reset();
            return;
        }
        _drag = e.ChangedButton;
        _last = e.GetPosition(this);
        CaptureMouse();
    }

    private static bool FindButton(DependencyObject d)
    {
        for (var x = d; x != null; x = x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is Button) return true;
        return false;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == null) return;
        var p = e.GetPosition(this);
        var (dx, dy) = (p.X - _last.X, p.Y - _last.Y);
        _last = p;
        if (_drag == MouseButton.Left)
        {
            _yaw -= dx * 0.5;
            _pitch = Math.Clamp(_pitch + dy * 0.4, -10, 89);
        }
        else
        {
            // pan in the view plane, as far as the mouse moved at the target's distance
            var scale = _distance * Math.Tan(_camera.FieldOfView * Math.PI / 360) * 2 / Math.Max(1, ActualHeight);
            var right = Vector3D.CrossProduct(_camera.LookDirection, _camera.UpDirection);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, _camera.LookDirection);
            up.Normalize();
            _target += (-right * dx + up * dy) * scale;
        }
        UpdateCamera();
    }
}
