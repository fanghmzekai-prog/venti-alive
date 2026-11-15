using System.Windows;
using System.Windows.Media;

namespace Aeris.WindowProbe;

internal sealed class LyreNotes : FrameworkElement
{
    private const double Lifetime = 0.52;
    private static readonly Drawing GoldNote = DrawNote("#FFE8A1", false);
    private static readonly Drawing MintNote = DrawNote("#BBF0D5", false);
    private static readonly Drawing GoldPair = DrawNote("#FFF0C0", true);
    private static readonly Drawing[] Symbols = [GoldNote, MintNote, GoldPair];

    private readonly List<double> births = new(3);
    private int firstSymbol;
    private int emitted;
    private double elapsed;

    internal LyreNotes()
    {
        Visibility = Visibility.Hidden;
        IsHitTestVisible = false;
    }

    internal (int Emitted, int Visible) State => (emitted, births.Count);

    internal void Emit(double at)
    {
        births.Add(at);
        emitted++;
    }

    internal void Update(double at)
    {
        elapsed = at;
        while (births.Count != 0 && at - births[0] >= Lifetime)
        {
            births.RemoveAt(0);
            firstSymbol++;
        }
        Visibility = births.Count == 0 ? Visibility.Hidden : Visibility.Visible;
        if (births.Count != 0)
        {
            InvalidateVisual();
        }
    }

    internal void Reset()
    {
        births.Clear();
        firstSymbol = emitted = 0;
        Visibility = Visibility.Hidden;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        for (int index = 0; index < births.Count; index++)
        {
            int symbol = (firstSymbol + index) % Symbols.Length;
            double age = Math.Clamp((elapsed - births[index]) / Lifetime, 0, 1);

            // This point lies on the strings at the plucking fingertip in the 1024 × 1536 pose.
            double x = 581 + (550 + 50 * symbol) * age - 190 * age * age;
            double y = 792 - 102 * age - (228 - 40 * symbol) * age * age;
            double opacity = Ease(age / 0.15) * (1 - Ease((age - 0.49) / 0.51));
            double scale = (0.55 + 0.44 * Ease(age / 0.40)) * (symbol == 2 ? 0.88 : 1);
            double tilt = (symbol - 1) * 8 + 12 * Math.Sin(age * Math.PI);

            drawing.PushOpacity(opacity);
            drawing.PushTransform(new TranslateTransform(x, y));
            drawing.PushTransform(new RotateTransform(tilt));
            drawing.PushTransform(new ScaleTransform(scale, scale));
            drawing.DrawDrawing(Symbols[symbol]);
            drawing.Pop();
            drawing.Pop();
            drawing.Pop();
            drawing.Pop();
        }
    }

    private static Drawing DrawNote(string color, bool pair)
    {
        var fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        var outline = new Pen(new SolidColorBrush(Color.FromRgb(46, 97, 74)), 7)
            { LineJoin = PenLineJoin.Round };
        var geometry = Geometry.Parse(pair
            ? "M -39,36 C -55,27 -46,10 -30,8 Q -21,7 -18,10 L -18,-58 L 42,-71 L 42,9"
                + " C 42,26 23,35 13,26 C 3,16 14,1 29,0 L 29,-43 L -5,-35 L -5,23"
                + " C -5,36 -26,44 -39,36 Z"
            : "M -31,42 C -46,35 -39,16 -21,13 Q -12,11 -8,14 L -8,-57 Q 4,-65 10,-50"
                + " C 15,-36 37,-37 34,-11 Q 32,0 23,4 Q 28,-13 16,-20 L 4,-27 L 4,29"
                + " C 3,42 -17,49 -31,42 Z");
        var symbol = new GeometryDrawing(fill, outline, geometry);
        symbol.Freeze();
        return symbol;
    }

    private static double Ease(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }
}
