// Lumenotepad app-icon generator (owner spec: a STANDALONE notebook + pencil — not a squircle
// badge — with the "lumen" lightbulb glowing in a folded page corner). Draws the master at 256px
// with SkiaSharp, resizes to every ICO size, and writes a multi-res .ico (PNG-compressed entries;
// fine on Windows 10/11) straight into the app's Assets. Run: dotnet run --project tools/icongen
using SkiaSharp;

const int Master = 256;
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

string root = FindRepoRoot();
string outIco = Path.Combine(root, "src", "Lumenotepad", "Assets", "lumenotepad.ico");
string outPng = Path.Combine(root, "assets", "lumenotepad-icon-256.png");
Directory.CreateDirectory(Path.GetDirectoryName(outIco)!);
Directory.CreateDirectory(Path.GetDirectoryName(outPng)!);

using var surface = SKSurface.Create(new SKImageInfo(Master, Master, SKColorType.Bgra8888, SKAlphaType.Premul));
Draw(surface.Canvas);
using var master = surface.Snapshot();

var pngs = new List<byte[]>();
foreach (int s in sizes)
{
    using var bmp = SKBitmap.FromImage(master);
    using var resized = bmp.Resize(new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul),
                                   new SKSamplingOptions(SKCubicResampler.Mitchell));
    using var img = SKImage.FromBitmap(resized);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    pngs.Add(data.ToArray());
}

WriteIco(outIco, sizes, pngs);
File.WriteAllBytes(outPng, pngs[^1]);
Console.WriteLine($"wrote {outIco} ({new FileInfo(outIco).Length} bytes) + {outPng}");

// ---- macOS iconset: re-draw at a 1024px master (vector code scales cleanly, no upsampling) and
// emit the PNG sizes tools/publish-macos.sh packs into lumenotepad.icns for the .app bundle. ----
string macDir = Path.Combine(root, "assets", "macos-iconset");
Directory.CreateDirectory(macDir);
using (var big = SKSurface.Create(new SKImageInfo(1024, 1024, SKColorType.Bgra8888, SKAlphaType.Premul)))
{
    big.Canvas.Scale(4f);          // Draw() works in 256-space; 4x = a true 1024 master
    Draw(big.Canvas);
    using var bigImg = big.Snapshot();
    foreach (int s in new[] { 16, 32, 64, 128, 256, 512, 1024 })
    {
        using var bmp = SKBitmap.FromImage(bigImg);
        using var resized = bmp.Resize(new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul),
                                       new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var img = SKImage.FromBitmap(resized);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(macDir, $"icon_{s}.png"), data.ToArray());
    }
}
Console.WriteLine($"wrote {macDir}/icon_{{16..1024}}.png");

static string FindRepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "Lumenotepad")))
        d = d.Parent!;
    return d?.FullName ?? throw new InvalidOperationException("repo root not found");
}

static void Draw(SKCanvas c)
{
    // LUMEN identity pass (owner feedback): the LIGHT is the hero. Dark-glass cover (the family's
    // #0B0C10 base), accent-blue rim light, and one BIG white-hot bulb glowing from the cover —
    // readable as a bulb at 32px and as a glowing mark even at 16px. The pencil turns sleek and
    // dark (no more generic yellow), eraser squared where it meets the ferrule.
    c.Clear(SKColors.Transparent);

    // Fill the frame AND centre it: the artwork spans y:30–234 (the pencil tip pokes below the
    // cover), so its true vertical centre is ~132, not 128 — scaling about 128 left more empty
    // space up top. Scale up 1.18× about the real content centre (128,132) so margins are even.
    c.Translate(128f, 128f);
    c.Scale(1.18f, 1.18f);
    c.Translate(-128f, -132f);

    var accent = new SKColor(0x4D, 0xA6, 0xFF);

    // soft drop shadow under everything
    using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true })
    {
        shadow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10);
        c.DrawRoundRect(new SKRect(52, 42, 212, 232), 26, 26, shadow);
    }

    // ---- dark-glass notebook cover, top-right corner genuinely CUT for the fold ----
    var cover = new SKRect(46, 30, 210, 226);
    var coverPath = new SKPath();
    coverPath.AddRoundRect(new SKRoundRect(cover, 26, 26));
    var cornerCut = new SKPath();
    cornerCut.MoveTo(152, 18); cornerCut.LineTo(222, 18); cornerCut.LineTo(222, 88); cornerCut.Close();
    using var cutCover = coverPath.Op(cornerCut, SKPathOp.Difference);
    using (var fill = new SKPaint { IsAntialias = true })
    {
        fill.Shader = SKShader.CreateLinearGradient(
            new SKPoint(cover.Left, cover.Top), new SKPoint(cover.Right, cover.Bottom),
            new[] { new SKColor(0x1C, 0x21, 0x2C), new SKColor(0x0B, 0x0C, 0x10) }, SKShaderTileMode.Clamp);
        c.DrawPath(cutCover, fill);
    }
    // accent rim light around the glass edge (the Lumen glow signature)
    using (var rim = new SKPaint
    {
        Color = accent.WithAlpha(0x66), IsAntialias = true,
        Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f,
    })
        c.DrawPath(cutCover, rim);
    // spine: a slim accent light-strip down the left edge
    using (var spine = new SKPaint { IsAntialias = true })
    {
        spine.Shader = SKShader.CreateLinearGradient(
            new SKPoint(56, 30), new SKPoint(56, 226),
            new[] { accent.WithAlpha(0xE6), new SKColor(0x2E, 0x6F, 0xC4) }, SKShaderTileMode.Clamp);
        c.DrawRoundRect(new SKRect(58, 46, 65, 210), 3.5f, 3.5f, spine);
    }

    // ---- folded corner: dark glass folding over, lit by the bulb below it ----
    using (var fold = new SKPaint { IsAntialias = true })
    {
        fold.Shader = SKShader.CreateLinearGradient(
            new SKPoint(152, 30), new SKPoint(210, 88),
            new[] { new SKColor(0x3A, 0x51, 0x74), new SKColor(0x20, 0x2A, 0x3C) }, SKShaderTileMode.Clamp);
        var tri = new SKPath();
        tri.MoveTo(152, 30); tri.LineTo(210, 88); tri.LineTo(152, 88); tri.Close();
        c.DrawPath(tri, fold);
    }
    using (var foldEdge = new SKPaint
    {
        Color = accent.WithAlpha(0x8C), IsAntialias = true,
        Style = SKPaintStyle.Stroke, StrokeWidth = 2, StrokeCap = SKStrokeCap.Round,
    })
        c.DrawLine(152, 30, 210, 88, foldEdge);

    // ---- THE bulb: big, white-hot core with an accent halo spilling over the dark glass ----
    var bulbAt = new SKPoint(122, 116);
    using (var halo = new SKPaint { IsAntialias = true })
    {
        halo.Shader = SKShader.CreateRadialGradient(bulbAt, 86,
            new[] { accent.WithAlpha(0x82), accent.WithAlpha(0x2E), accent.WithAlpha(0x00) },
            new float[] { 0, 0.45f, 1 }, SKShaderTileMode.Clamp);
        c.DrawCircle(bulbAt, 86, halo);
    }
    using (var globe = new SKPaint { IsAntialias = true })
    {
        globe.Shader = SKShader.CreateRadialGradient(new SKPoint(bulbAt.X - 6, bulbAt.Y - 8), 42,
            new[] { new SKColor(0xF4, 0xFA, 0xFF), new SKColor(0xBF, 0xE0, 0xFF), accent },
            new float[] { 0, 0.55f, 1 }, SKShaderTileMode.Clamp);
        c.DrawCircle(bulbAt, 34, globe);
    }
    // filament: a clear dark V inside the globe (what makes it read as a BULB, not a dot)
    using (var fil = new SKPaint
    {
        Color = new SKColor(0x14, 0x2A, 0x4A), IsAntialias = true,
        Style = SKPaintStyle.Stroke, StrokeWidth = 5.5f, StrokeCap = SKStrokeCap.Round,
    })
    {
        c.DrawLine(bulbAt.X - 11, bulbAt.Y - 4, bulbAt.X, bulbAt.Y + 10, fil);
        c.DrawLine(bulbAt.X + 11, bulbAt.Y - 4, bulbAt.X, bulbAt.Y + 10, fil);
    }
    // neck + base bars (screw cap)
    using (var neck = new SKPaint { Color = new SKColor(0x9A, 0xC4, 0xEE), IsAntialias = true })
    {
        var p = new SKPath();
        p.MoveTo(bulbAt.X - 15, bulbAt.Y + 31);
        p.LineTo(bulbAt.X + 15, bulbAt.Y + 31);
        p.LineTo(bulbAt.X + 11, bulbAt.Y + 44);
        p.LineTo(bulbAt.X - 11, bulbAt.Y + 44);
        p.Close();
        c.DrawPath(p, neck);
    }
    using (var cap = new SKPaint { Color = new SKColor(0x6E, 0x7C, 0x92), IsAntialias = true })
    {
        c.DrawRoundRect(new SKRect(bulbAt.X - 12, bulbAt.Y + 44, bulbAt.X + 12, bulbAt.Y + 50), 2, 2, cap);
        c.DrawRoundRect(new SKRect(bulbAt.X - 9, bulbAt.Y + 52, bulbAt.X + 9, bulbAt.Y + 58), 2, 2, cap);
    }

    // ---- sleek pencil: dark body, accent band, squared-on eraser ----
    c.Save();
    c.RotateDegrees(-38, 138, 186);
    float top = 173, bot = 199;                       // slimmer 26px body
    using (var wood = new SKPaint { Color = new SKColor(0xC9, 0xD4, 0xE4), IsAntialias = true })
    {
        var tip = new SKPath();
        tip.MoveTo(60, (top + bot) / 2); tip.LineTo(84, top); tip.LineTo(84, bot); tip.Close();
        c.DrawPath(tip, wood);
    }
    using (var graphite = new SKPaint { Color = new SKColor(0x10, 0x14, 0x1B), IsAntialias = true })
    {
        var lead = new SKPath();
        lead.MoveTo(60, (top + bot) / 2); lead.LineTo(69, top + 4.9f); lead.LineTo(69, bot - 4.9f); lead.Close();
        c.DrawPath(lead, graphite);
    }
    using (var body = new SKPaint { IsAntialias = true })
    {
        body.Shader = SKShader.CreateLinearGradient(
            new SKPoint(84, top), new SKPoint(84, bot),
            new[] { new SKColor(0x39, 0x41, 0x50), new SKColor(0x22, 0x27, 0x31) }, SKShaderTileMode.Clamp);
        c.DrawRect(new SKRect(84, top, 208, bot), body);
    }
    using (var band = new SKPaint { Color = accent, IsAntialias = true })
        c.DrawRect(new SKRect(196, top, 208, bot), band);       // accent band = the ferrule
    using (var eraser = new SKPaint { Color = new SKColor(0x8E, 0x9B, 0xAE), IsAntialias = true })
    {
        // FLAT against the band (owner: a rounded base read as detached) — outer corners only
        var p = new SKPath();
        p.MoveTo(208, top);
        p.LineTo(219, top); p.ArcTo(new SKRect(219, top, 229, top + 10), 270, 90, false);
        p.LineTo(229, bot - 5); p.ArcTo(new SKRect(219, bot - 10, 229, bot), 0, 90, false);
        p.LineTo(208, bot);
        p.Close();
        c.DrawPath(p, eraser);
    }
    c.Restore();
}

static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        int s = sizes[i];
        w.Write((byte)(s >= 256 ? 0 : s));
        w.Write((byte)(s >= 256 ? 0 : s));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(pngs[i].Length);
        w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var png in pngs) w.Write(png);
}
