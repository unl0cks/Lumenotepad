using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Lumenotepad.Views;

public static class ToggleFx
{

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
            return;
        double to = t.IsChecked == true ? Travel : 0;

        double from = knob.Tag is double d ? d : Travel - to;
        knob.Tag = to;
        if (!animate)
        {
            Motion.Stop(knob);
            if (to == 0) knob.ClearValue(Avalonia.Visual.RenderTransformProperty);
            else knob.RenderTransform = TransformOperations.Parse($"translate({to}px, 0px)");
            return;
        }

        Motion.Tween(knob, from, 0, 1, to, 0, 1, 180);
    }
}
