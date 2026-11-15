using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Aeris.WindowProbe;

internal enum DozeStage { Awake, Settling, Resting, Waking }

internal sealed class VentiDoze : FrameworkElement
{
    private const double SettleSeconds = 0.62;
    private const double RestUntil = 3.68;
    private const double WakeSeconds = 0.55;

    private static readonly Drawing DreamMark = CreateDreamMark();
    private long started;
    private long interrupted;
    private double interruptedDepth;
    private double age;

    internal DozeStage Stage { get; private set; }
    internal double Depth { get; private set; }

    internal VentiDoze()
    {
        Visibility = Visibility.Hidden;
        IsHitTestVisible = false;
    }

    internal bool Start()
    {
        if (Stage != DozeStage.Awake) return false;
        started = Stopwatch.GetTimestamp();
        Stage = DozeStage.Settling;
        return true;
    }

    internal void Wake()
    {
        if (Stage == DozeStage.Awake || interrupted != 0) return;
        interruptedDepth = Depth;
        interrupted = Stopwatch.GetTimestamp();
        Stage = DozeStage.Waking;
    }

    internal void ResetPose()
    {
        started = interrupted = 0;
        Depth = 0;
        Stage = DozeStage.Awake;
        Visibility = Visibility.Hidden;
    }

    internal void Update(long now)
    {
        if (started == 0) return;
        age = Stopwatch.GetElapsedTime(started, now).TotalSeconds;
        if (interrupted != 0)
        {
            Depth = interruptedDepth * (1 - Ease(Stopwatch.GetElapsedTime(interrupted, now).TotalSeconds / 0.24));
        }
        else if (age < SettleSeconds)
        {
            Depth = Ease(age / SettleSeconds);
        }
        else if (age < RestUntil)
        {
            Stage = DozeStage.Resting;
            Depth = 1;
        }
        else
        {
            Stage = DozeStage.Waking;
            Depth = 1 - Ease((age - RestUntil) / WakeSeconds);
        }

        if (Stage == DozeStage.Waking && Depth <= 0)
        {
            ResetPose();
            return;
        }
        Visibility = age >= SettleSeconds && Depth > 0 ? Visibility.Visible : Visibility.Hidden;
        if (IsVisible) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        DrawMark(drawing, (age - 0.67) / 1.95, 0.85);
        DrawMark(drawing, (age - 1.68) / 1.95, 1);
    }

    private void DrawMark(DrawingContext drawing, double progress, double scale)
    {
        if (progress is <= 0 or >= 1) return;
        double strength = Depth * Ease(progress / 0.20) * (1 - Ease((progress - 0.48) / 0.52));
        drawing.PushOpacity(strength);
        drawing.PushTransform(new TranslateTransform(846 + 165 * progress, 488 - 276 * progress));
        drawing.PushTransform(new RotateTransform(-15 + 12 * progress));
        double growth = scale * (0.65 + 0.36 * progress);
        drawing.PushTransform(new ScaleTransform(growth, growth));
        drawing.DrawDrawing(DreamMark);
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }

    private static Drawing CreateDreamMark()
    {
        var mint = new SolidColorBrush(Color.FromRgb(217, 252, 224));
        var outline = new Pen(new SolidColorBrush(Color.FromRgb(69, 126, 102)), 7)
            { LineJoin = PenLineJoin.Round };
        var outlineShape = Geometry.Parse("M -29,-32 Q -11,-36 23,-33 Q 31,-31 29,-22 L -10,21 "
            + "L 24,19 Q 31,19 31,27 Q 31,36 23,36 L -29,36 Q -38,34 -34,24 L 6,-17 "
            + "L -27,-16 Q -36,-16 -36,-23 Q -36,-30 -29,-32 Z");
        var drawing = new GeometryDrawing(mint, outline, outlineShape);
        drawing.Freeze();
        return drawing;
    }

    private static double Ease(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }
}
