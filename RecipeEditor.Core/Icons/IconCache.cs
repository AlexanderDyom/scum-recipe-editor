// Icons of items and dishes straight from the user's game: the texture an item's _ES points to (GeneralPurposeIcon),
// decoded (BC7 / BC1 / BC3 / BGRA8) and written as a 128 px PNG into %LOCALAPPDATA%\ScumRecipeEditor\cache\icons\<game>.
// Nothing of the game ships with the editor; the cache folder changes when the game's paks change.
using System.Buffers.Binary;
using System.IO.Compression;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CUE4Parse.UE4.Assets.Exports.Texture;
using RecipeEditor.Core.Game;

namespace RecipeEditor.Core.Icons;

public sealed class IconCache
{
    public const int Size = 128;
    private readonly GameProvider _game;
    private readonly object _lock = new();

    public string Folder { get; }

    public IconCache(GameProvider game, string? root = null)
    {
        _game = game;
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRecipeEditor", "cache", "icons");
        Folder = Path.Combine(root, GameStamp(game.Paks));
    }

    /// <summary>Changes when the game updates: size and time of the base pak.</summary>
    private static string GameStamp(string paks)
    {
        var f = Directory.EnumerateFiles(paks, "pakchunk0-*.pak").Select(p => new FileInfo(p)).OrderBy(p => p.Name).FirstOrDefault();
        return f == null ? "game" : $"{f.Length:x}-{f.LastWriteTimeUtc.Ticks:x}";
    }

    /// <summary>PNG file of a texture (/Game/.../ICO_X.ICO_X), made on first use; null when it cannot be read.</summary>
    public string? Png(string? texture)
    {
        if (string.IsNullOrEmpty(texture) || texture == "None") return null;
        var package = texture.Split('.')[0];
        var file = Path.Combine(Folder, string.Join("_", package.Trim('/').Split('/').Skip(1)) + ".png");
        if (File.Exists(file)) return file;
        try
        {
            var path = package.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase) ? "SCUM/Content/" + package[6..] : package.TrimStart('/');
            UTexture2D? tex;
            lock (_lock) // CUE4Parse readers are not meant for parallel loads of one package
                tex = _game.Provider.TryLoadPackage(path, out var pkg) ? pkg.GetExports().OfType<UTexture2D>().FirstOrDefault() : null;
            if (tex?.PlatformData is not { Mips.Length: > 0 } pd) return null;
            var mip = pd.Mips.FirstOrDefault(m => m.BulkData?.Data != null && m.SizeX <= 512) ?? pd.Mips[0];
            var data = mip.BulkData?.Data;
            if (data == null) return null;
            var rgba = Decode(pd.PixelFormat.ToString(), data, mip.SizeX, mip.SizeY);
            if (rgba == null) return null;
            var (w, h) = (mip.SizeX, mip.SizeY);
            while (w > Size && h > 1 && w % 2 == 0 && h % 2 == 0) (rgba, w, h) = Half(rgba, w, h);
            Directory.CreateDirectory(Folder);
            var tmp = file + ".tmp";
            File.WriteAllBytes(tmp, Png(rgba, w, h));
            File.Move(tmp, file, true);
            return file;
        }
        catch (Exception)
        {
            return null; // a broken or unusual texture: the UI shows its letter instead
        }
    }

    /// <summary>RGBA8 pixels of a mip, or null for formats the editor does not decode.</summary>
    private static byte[]? Decode(string format, byte[] data, int w, int h)
    {
        CompressionFormat? bc = format switch
        {
            "PF_BC7" => CompressionFormat.Bc7, "PF_DXT1" => CompressionFormat.Bc1WithAlpha, "PF_DXT5" => CompressionFormat.Bc3,
            "PF_BC5" => CompressionFormat.Bc5, "PF_BC4" => CompressionFormat.Bc4, _ => null,
        };
        if (bc != null)
        {
            var px = new BcDecoder().DecodeRaw(data, w, h, bc.Value);
            var o = new byte[w * h * 4];
            for (var i = 0; i < px.Length; i++)
            {
                o[i * 4] = px[i].r;
                o[i * 4 + 1] = px[i].g;
                o[i * 4 + 2] = px[i].b;
                o[i * 4 + 3] = px[i].a;
            }
            return o;
        }
        if (format == "PF_B8G8R8A8" && data.Length >= w * h * 4)
        {
            var o = new byte[w * h * 4];
            for (var i = 0; i < w * h; i++)
            {
                o[i * 4] = data[i * 4 + 2];
                o[i * 4 + 1] = data[i * 4 + 1];
                o[i * 4 + 2] = data[i * 4];
                o[i * 4 + 3] = data[i * 4 + 3];
            }
            return o;
        }
        return null;
    }

    /// <summary>2x2 box filter with alpha-weighted colour (no dark fringes on transparent edges).</summary>
    private static (byte[], int, int) Half(byte[] s, int w, int h)
    {
        var (nw, nh) = (w / 2, h / 2);
        var d = new byte[nw * nh * 4];
        for (var y = 0; y < nh; y++)
            for (var x = 0; x < nw; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
                {
                    var i = ((y * 2 + dy) * w + x * 2 + dx) * 4;
                    var al = s[i + 3];
                    r += s[i] * al; g += s[i + 1] * al; b += s[i + 2] * al; a += al;
                }
                var o = (y * nw + x) * 4;
                if (a > 0) { d[o] = (byte)(r / a); d[o + 1] = (byte)(g / a); d[o + 2] = (byte)(b / a); }
                d[o + 3] = (byte)(a / 4);
            }
        return (d, nw, nh);
    }

    /// <summary>A plain RGBA PNG (filter 0 on every row).</summary>
    public static byte[] Png(byte[] rgba, int w, int h)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        Chunk(ms, "IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
                for (var y = 0; y < h; y++)
                {
                    z.WriteByte(0);
                    z.Write(rgba, y * w * 4, w * 4);
                }
            Chunk(ms, "IDAT", raw.ToArray());
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        s.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, System.IO.Hashing.Crc32.HashToUInt32(body));
        s.Write(crc);
    }
}
