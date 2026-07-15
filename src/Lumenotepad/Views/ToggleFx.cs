using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Lumenotepad.Views;

/// <summary>Slides the custom ToggleSwitch knob (Theme.axaml's replacement template). Styles can't
/// drive RenderTransform in this build and Fluent's built-in knob animation only worked sometimes
/// (owner report), so the knob is positioned here via Motion tweens from ONE global hook pair:
/// IsChecked changes slide it, TemplateApplied snaps it to its resting spot (no animation when a
/// window opens with toggles already on).</summary>
public static class ToggleFx
{
    // 42 track − 2×1px border − 2px left margin − 16px knob − 2px right slack = 20px of travel.
    private const double Travel = 20;

    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        ToggleSwitch.IsCheckedProperty.Changed.AddClassHandler<ToggleSwitch>((t, _) => Slide(t, animate: true));
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ToggleSwitch>((t, _) => Slide(t, animate: false));
    }

    private static void Slide(ToggleSwitch t, bool animate)
    {
        if (t.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "knob") is not { } knob)
            return;                                       // stock/other template — nothing to drive
        double to = t.IsChecked == true ? Travel : 0;
        // Tag remembers where the knob actually sits, so a rapid re-toggle animates from the truth.
        double from = knob.Tag is double d ? d : Travel - to;
        knob.Tag = to;
        if (!animate)
        {
            Motion.Stop(knob);
            if (to == 0) knob.ClearValue(Avalonia.Visual.RenderTransformProperty);
            else knob.RenderTransform = TransformOperations.Parse($"translate({to}px, 0px)");
            return;
        }
        // Rest at identity clears the transform (Motion.Tween does that itself when the target is 0).
        Motion.Tween(knob, from, 0, 1, to, 0, 1, 180);
    }
}
