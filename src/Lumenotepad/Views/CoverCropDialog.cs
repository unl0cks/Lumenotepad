using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

public static class CoverCropDialog
{
    private const double FrameW = 392, FrameH = 264;

    public static async Task<string?> Show(Window owner, string imagePath)
    {
        Bitmap bmp;
        try { bmp = new Bitmap(imagePath); }
        catch { return null; }

        try
        {
            double imgW = bmp.PixelSize.Width, imgH = bmp.PixelSize.Height;
            double baseScale = Math.Max(FrameW / imgW, FrameH / imgH);
            double zoom = 1;
            double Scale() => baseScale * zoom;
            double offX = 0, offY = 0;
            void Clamp()
            {
                offX = Math.Min(0, Math.Max(FrameW - imgW * Scale(), offX));
                offY = Math.Min(0, Math.Max(FrameH - imgH * Scale(), offY));
            }
            offX = (FrameW - imgW * Scale()) / 2;
            offY = (FrameH - imgH * Scale()) / 2;

            var img = new Image
            {
                Source = bmp, Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            void Apply()
            {
                img.Width = imgW * Scale();
                img.Height = imgH * Scale();
                img.Margin = new Thickness(offX, offY, 0, 0);
            }
            Apply();

            var frame = new Border
            {
                Width = FrameW, Height = FrameH, ClipToBounds = true,
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.Parse("#40FFFFFF")),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.Parse("#26000000")),
                Child = img,
                Cursor = Platform.AdaptiveCursors.For(StandardCursorType.SizeAll),
            };

            var slider = new Slider { Minimum = 1, Maximum = 4, Value = 1, Width = FrameW, Margin = new Thickness(0, 6, 0, 0) };
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != Slider.ValueProperty) return;
                double old = Scale();
                zoom = slider.Value;

                double cx = (FrameW / 2 - offX) / old, cy = (FrameH / 2 - offY) / old;
                offX = FrameW / 2 - cx * Scale();
                offY = FrameH / 2 - cy * Scale();
                Clamp();
                Apply();
            };

            bool panning = false;
            Point start = default;
            (double X, double Y) origin = default;
            frame.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed) return;
                panning = true;
                start = e.GetPosition(frame);
                origin = (offX, offY);
                e.Pointer.Capture(frame);
                e.Handled = true;
            };
            frame.PointerMoved += (_, e) =>
            {
                if (!panning) return;
                var p = e.GetPosition(frame);
                offX = origin.X + p.X - start.X;
                offY = origin.Y + p.Y - start.Y;
                Clamp();
                Apply();
                e.Handled = true;
            };
            frame.PointerReleased += (_, e) => { panning = false; e.Pointer.Capture(null); e.Handled = true; };
            frame.PointerWheelChanged += (_, e) =>
            {
                slider.Value = Math.Clamp(slider.Value + e.Delta.Y * 0.25, 1, 4);
                e.Handled = true;
            };

            var t = Services.ThemeManager.Current;
            var win = new Window
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.Parse(t.WindowBackground)),
            };
            win.Opened += (_, _) => WinChrome.RoundCorners(win, true);
            Services.ThemeManager.ConfigureDialogChrome(win);

            var lumenTheme = Application.Current?.FindResource("LumenButton") as Avalonia.Styling.ControlTheme;
            string quietTop = t.DarkChrome ? "#33FFFFFF" : "#12000000";
            string quietBottom = t.DarkChrome ? "#1AFFFFFF" : "#0A000000";
            var cancel = new Button
            {
                Content = "Cancel", FontSize = 12.5, Padding = new Thickness(16, 7), Theme = lumenTheme,
                Foreground = new SolidColorBrush(Color.Parse(t.TextPrimary)), CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse(quietTop), 0),
                        new GradientStop(Color.Parse(quietBottom), 1),
                    },
                },
                BorderBrush = new SolidColorBrush(Color.Parse(t.DarkChrome ? "#40FFFFFF" : "#2E000000")),
            };
            var confirm = new Button
            {
                Content = "Use this crop", FontSize = 12.5, Padding = new Thickness(16, 7), Theme = lumenTheme,
                CornerRadius = new CornerRadius(8),
            };
            cancel.Click += (_, _) => win.Close(false);
            confirm.Click += (_, _) => win.Close(true);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0),
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);

            var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
            stack.Children.Add(new TextBlock
            {
                Text = "Position the cover", FontSize = 15, FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(t.TextPrimary)),
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Drag to move · scroll or use the slider to zoom",
                FontSize = 12, Foreground = new SolidColorBrush(Color.Parse(t.TextSecondary)),
                Margin = new Thickness(0, 6, 0, 12),
            });
            stack.Children.Add(frame);
            stack.Children.Add(slider);
            stack.Children.Add(buttons);

            win.Content = stack;
            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) win.Close(false);
                else if (e.Key == Key.Enter) win.Close(true);
            };

            var ok = await win.ShowDialog<bool?>(owner);
            if (ok != true) return null;

            double s = Scale();
            var src = new Rect(-offX / s, -offY / s, FrameW / s, FrameH / s);
            int outW = (int)Math.Clamp(src.Width, 392, 1176);
            int outH = (int)Math.Round(outW * (FrameH / FrameW));
            var rtb = new RenderTargetBitmap(new PixelSize(outW, outH), new Vector(96, 96));
            using (var ctx = rtb.CreateDrawingContext())
                ctx.DrawImage(bmp, src, new Rect(0, 0, outW, outH));
            var tmp = Path.Combine(Path.GetTempPath(), "lumenotepad-cover-" + Guid.NewGuid().ToString("N") + ".png");
            rtb.Save(tmp);
            return tmp;
        }
        finally
        {
            bmp.Dispose();
        }
    }
}
