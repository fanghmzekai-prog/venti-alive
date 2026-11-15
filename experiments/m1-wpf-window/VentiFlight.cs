using System.Windows;
using System.Windows.Media;

namespace Aeris.WindowProbe;

internal enum FlightStage { Hidden, Opening, Spread, Folding }

internal sealed class VentiFlight : FrameworkElement
{
    private const double OpenSeconds = 0.215;
    private const double FoldSeconds = 0.275;
    private static readonly Drawing Wing = CreateWing();
    private static readonly Drawing Wind = CreateWind();
    private static readonly Brush Mote = Solid("#DFFEF1");

    private readonly WindCurrent[] currents = [new(-1, 0.11, 923, 1.18),
        new(1, 0.57, 1015, 1.08), new(-1, 0.65, 1270, 0.73)];
    private readonly MatrixTransform leftWing = new();
    private readonly MatrixTransform rightWing = new();
    private readonly DrawingGroup wings = new();

    private double spread;
    private double startSpread;
    private double beat;
    private double wake;
    private Vector gust;
    private Vector impulse;

    internal FlightStage Stage { get; private set; }
    internal double Spread => spread;

    internal VentiFlight()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Hidden;
        wings.Children.Add(new DrawingGroup { Transform = leftWing, Children = { Wing } });
        wings.Children.Add(new DrawingGroup { Transform = rightWing, Children = { Wing } });
    }

    internal void CaptureSpread() => startSpread = spread;

    internal void Move(double x, double y) => impulse = new Vector(
        Math.Clamp(x / 18, -1, 1), Math.Clamp(y / 18, -1, 1));

    internal void Update(bool held, double age, double step)
    {
        double progress = Ease(age / (held ? OpenSeconds : FoldSeconds));
        spread = startSpread + ((held ? 1 : 0) - startSpread) * progress;
        if (spread <= 0)
        {
            if (Stage != FlightStage.Hidden) Reset();
            return;
        }

        Stage = held
            ? progress >= 1 ? FlightStage.Spread : FlightStage.Opening
            : FlightStage.Folding;
        beat += step * (2.05 + 0.28 * Math.Abs(gust.X));
        wake += step * (0.89 + 0.27 * gust.Length);
        gust += (impulse - gust) * (1 - Math.Exp(-step * 16));
        impulse *= Math.Exp(-step * 6);

        // Keep the drawing attached and change only its transforms and opacity.
        double flap = Math.Sin(beat * Math.Tau);
        double featherLag = Math.Sin(beat * Math.Tau - 0.65);
        leftWing.Matrix = WingMatrix(-1, flap, featherLag);
        rightWing.Matrix = WingMatrix(1, flap, featherLag);
        wings.Opacity = Ease(spread / 0.27);
        foreach (WindCurrent current in currents) current.Update(wake, gust, spread);
        Visibility = Visibility.Visible;
    }

    internal void Reset()
    {
        spread = startSpread = beat = wake = 0;
        gust = impulse = default;
        Stage = FlightStage.Hidden;
        Visibility = Visibility.Hidden;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        foreach (WindCurrent current in currents) drawing.DrawDrawing(current.Drawing);
        drawing.DrawDrawing(wings);
    }

    private Matrix WingMatrix(int side, double flap, double featherLag)
    {
        double width = 0.15 + spread * (0.93 + 0.11 * flap) + side * gust.X * spread * 0.075;
        double height = 0.36 + spread * (0.68 + 0.11 * featherLag);
        double angle = side * (44 * (1 - spread) + 11 * flap * spread) + 5 * gust.X;

        return FlightMatrix(side * width, height, angle, 512 + side * 62, 796);
    }

    private static Matrix FlightMatrix(double width, double height, double angle, double x, double y)
    {
        Matrix matrix = Matrix.Identity;
        matrix.Scale(width, height);
        matrix.Rotate(angle);
        matrix.Translate(x, y);
        return matrix;
    }

    private sealed class WindCurrent
    {
        private readonly int side;
        private readonly double offset;
        private readonly double height;
        private readonly double size;
        private readonly MatrixTransform transform = new();
        private readonly EllipseGeometry nearMote = new();
        private readonly EllipseGeometry farMote = new();

        internal DrawingGroup Drawing { get; }

        internal WindCurrent(int side, double offset, double height, double size)
        {
            this.side = side;
            this.offset = offset;
            this.height = height;
            this.size = size;
            Drawing = new DrawingGroup
            {
                Transform = transform,
                Children = { Wind, new GeometryDrawing(Mote, null, nearMote),
                    new GeometryDrawing(Mote, null, farMote) }
            };
        }

        internal void Update(double wake, Vector gust, double spread)
        {
            double cycle = wake + offset;
            double progress = cycle - Math.Floor(cycle);
            double arc = Math.Sin(progress * Math.PI);
            double x = 512 + side * (400 + progress * 252) - gust.X * 224;
            double y = height - progress * 170 - gust.Y * 166;
            double taper = size * (0.78 + progress * 0.24);

            Drawing.Opacity = spread * Math.Pow(arc, 0.72);
            transform.Matrix = FlightMatrix(side * taper, taper, -8 * side + 19 * gust.Y, x, y);
            nearMote.Center = new Point(174 + progress * 40, -42);
            nearMote.RadiusX = 11 * arc;
            nearMote.RadiusY = 8 * arc;
            farMote.Center = new Point(226 + progress * 40, -68);
            farMote.RadiusX = 6 * arc;
            farMote.RadiusY = 5 * arc;
        }
    }

    private static Drawing CreateWing()
    {
        var ink = new Pen(Solid("#315E55"), 12) { LineJoin = PenLineJoin.Round };
        var seam = new Pen(Solid("#68A792"), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var light = new Pen(Solid("#E9FFF4"), 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        Geometry outline = Geometry.Parse("M 0,23 C 45,-56 64,-171 145,-238 C 237,-320 348,-314 493,-386"
            + " C 497,-325 444,-258 363,-208 C 412,-220 474,-253 509,-276"
            + " C 501,-210 436,-153 369,-128 C 407,-132 453,-149 487,-174"
            + " C 469,-113 414,-76 343,-54 C 379,-55 421,-72 448,-88"
            + " C 424,-30 368,-4 305,4 C 331,11 351,8 373,3"
            + " C 345,41 273,61 209,50 C 154,82 68,78 18,51 Q -2,40 0,23 Z");
        var wing = new DrawingGroup();
        using (DrawingContext art = wing.Open())
        {
            art.DrawGeometry(Fade("#FFF5D9", "#7ACDB5", "#43A994"), null, outline);
            Shape(art, "M 173,-116 C 290,-170 411,-303 493,-386"
                + " C 489,-326 422,-237 332,-192 Q 272,-137 173,-116 Z",
                Fade("#FFFAE4", "#DEF2DC", "#A7DBC3"));
            Shape(art, "M 204,-54 C 326,-92 428,-192 509,-276"
                + " C 497,-218 427,-153 369,-128 Q 310,-78 204,-54 Z",
                Fade("#FFF6DF", "#C5ECD8", "#8BCCB1"));
            Shape(art, "M 195,-2 C 314,-26 410,-102 487,-174"
                + " C 470,-119 401,-75 343,-54 Q 268,-4 195,-2 Z",
                Fade("#F3F5DB", "#B4E7D0", "#76C5AB"));
            Shape(art, "M 5,21 C 73,-45 110,-170 207,-218 C 258,-244 300,-246 345,-266"
                + " C 325,-223 276,-180 234,-159 Q 269,-164 301,-181"
                + " C 271,-130 229,-98 188,-83 Q 218,-86 245,-105"
                + " C 212,-51 158,-17 115,-5 Q 70,48 20,50 Z",
                Fade("#FFF3D3", "#D4ECD2", "#83C9AB"), seam);
            Shape(art, "M 50,10 C 89,-18 120,-75 152,-99 C 160,-64 130,-19 106,5"
                + " Q 117,5 130,-1 C 115,25 81,46 34,41 Z", Solid("#A6DCC1"));
            Shape(art, "M 121,-150 Q 166,-219 247,-238 M 241,-129 Q 329,-160 391,-226"
                + " M 262,-60 Q 330,-80 379,-117 M 241,-9 Q 299,-22 337,-48", null, seam);
            Shape(art, "M 129,-181 Q 173,-234 234,-252 M 308,-220 Q 379,-268 442,-331", null, light);
            art.DrawGeometry(null, ink, outline);
        }
        wing.Freeze();
        return wing;
    }

    private static Drawing CreateWind()
    {
        var current = new DrawingGroup();
        using (DrawingContext art = current.Open())
        {
            Shape(art, "M -171,41 C -73,53 77,14 119,-31 C 144,-59 115,-58 86,-43"
                + " C 101,-65 151,-76 155,-52 C 162,-12 54,53 -59,59 Q -132,64 -171,41 Z",
                Fade("#44BCA2", "#B4FFE4", "#346DBA9F"));
            Shape(art, "M -142,87 C -70,86 -13,69 57,38 Q 25,71 -49,88 Q -107,104 -142,87 Z",
                Fade("#DBFFEF", "#81DFC1", "#506FB097"));
        }
        current.Freeze();
        return current;
    }

    private static void Shape(DrawingContext drawing, string path, Brush? fill, Pen? pen = null) =>
        drawing.DrawGeometry(fill, pen, Geometry.Parse(path));

    private static SolidColorBrush Solid(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private static LinearGradientBrush Fade(string top, string middle, string bottom) => new(
        new GradientStopCollection
        {
            new((Color)ColorConverter.ConvertFromString(top), 0),
            new((Color)ColorConverter.ConvertFromString(middle), 0.60),
            new((Color)ColorConverter.ConvertFromString(bottom), 1)
        }, 82);

    private static double Ease(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }
}
