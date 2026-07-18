using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lumenotepad.Views;

/// <summary>Continuous fake frost for menus. Windows' DWM only offers a few fixed blur levels, so a
/// truly smooth 0–100 strength can't come from the OS — instead the menu paints its OWN backdrop: a
/// snapshot of the owner window's content under the popup, gaussian-blurred (SkiaSharp) at exactly
/// the requested strength, with the theme's menu tint flattened in. The snapshot is taken when the
/// popup opens — the popup is a separate window, so it can never capture itself. Any part of a menu
/// hanging OUTSIDE the owner window falls back to the translucent tint alone.</summary>
public static class MenuFrost
{
    /// <summary>Build the frosted backdrop brush for a popup, or null when it can't (no owner
    /// geometry, zero-size popup, capture failure) — the caller then falls back to DWM blur.</summary>
    public static IBrush? TryBackdrop(TopLevel popupTl, Window owner, int pct)
    {
        try
        {
            if (pct <= 0) return null;
            double scale = owner.RenderScaling;
            var ownerOrigin = owner.PointToScreen(new Point(0, 0));
            var popupOrigin = popupTl.PointToScreen(new Point(0, 0));
            double ox = (popupOrigin.X - ownerOrigin.X) / scale;   // popup origin in owner logical coords
            double oy = (popupOrigin.Y - ownerOrigin.Y) / scale;
            double pw = popupTl.ClientSize.Width, ph = popupTl.ClientSize.Height;
            if (pw < 1 || ph < 1 || owner.ClientSize.Width < 1 || owner.ClientSize.Height < 1) return null;

            int Px(double v) => (int)Math.Round(v * scale);

            // 1. Snapshot the whole owner window at native pixels.
            using var full = new RenderTargetBitmap(
                new PixelSize(Math.Max(1, Px(owner.ClientSize.Width)), Math.Max(1, Px(owner.ClientSize.Height))),
                new Vector(96 * scale, 96 * scale));
            full.Render(owner);
            using var ms = new MemoryStream();
            full.Save(ms);
            ms.Position = 0;
            using var sk = SkiaSharp.SKBitmap.Decode(ms);
            if (sk is null) return null;

            // 2. Blur the region under the popup (with margin so the blur doesn't fade at the edges)
            //    at a CONTINUOUS sigma from the preference.
            float sigma = 1.5f + pct / 100f * 20f;
            int m = (int)Math.Ceiling(sigma * 3);
            var want = new SkiaSharp.SKRectI(Px(ox) - m, Px(oy) - m, Px(ox + pw) + m, Px(oy + ph) + m);
            var have = SkiaSharp.SKRectI.Intersect(want, new SkiaSharp.SKRectI(0, 0, sk.Width, sk.Height));

            var info = new SkiaSharp.SKImageInfo(Math.Max(1, Px(pw)), Math.Max(1, Px(ph)));
            using var surface = SkiaSharp.SKSurface.Create(info);
            if (surface is null) return null;
            var c = surface.Canvas;
            c.Clear(SkiaSharp.SKColors.Transparent);
            if (!have.IsEmpty)
            {
                using var region = new SkiaSharp.SKBitmap();
                if (sk.ExtractSubset(region, have))
                {
                    using var paint = new SkiaSharp.SKPaint
                    {
                        ImageFilter = SkiaSharp.SKImageFilter.CreateBlur(sigma, sigma),
                    };
                    c.DrawBitmap(region, have.Left - Px(ox), have.Top - Px(oy), paint);
                }
            }

            // 3. Flatten the theme's menu tint over the blur — one opaque-ish backdrop image.
            var tint = Color.Parse(Services.ThemeManager.Current.MenuBackground);
            c.DrawColor(new SkiaSharp.SKColor(tint.R, tint.G, tint.B, tint.A));

            using var img = surface.Snapshot();
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var outMs = new MemoryStream();
            data.SaveTo(outMs);
            outMs.Position = 0;
            return new ImageBrush(new Bitmap(outMs)) { Stretch = Stretch.Fill };
        }
        catch { return null; }
    }
}
