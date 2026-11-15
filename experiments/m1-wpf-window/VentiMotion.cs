using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aeris.WindowProbe;

internal sealed class VentiAssetException(string assetPath, Exception cause) : Exception(
    $"Unable to load Venti artwork: {Path.GetFileName(assetPath)}.", cause)
{
    internal string AssetName { get; } = Path.GetFileName(assetPath);
}

internal sealed class VentiMotion
{
    private readonly Grid standing;
    private readonly VentiGestures? gestures;
    private readonly VentiDoze? doze;
    private readonly Image bodyImage;
    private readonly Grid? dragBody;
    private readonly VentiFlight? flight;
    private long landingStarted;
    private long dragStarted;
    private (double Amount, double Swing, double Tilt) dragPose;
    private (double Amount, double Swing, double Tilt) pickupPose;
    private (double Amount, double Swing, double Tilt) releasePose;
    internal int LandingFrame { get; private set; }
    internal double DragLiftDips => -lift.Y * 198 / standing.Height;
    internal FlightStage FlightStage => flight?.Stage ?? FlightStage.Hidden;
    internal double WingSpread => flight?.Spread ?? 0;

    private readonly ScaleTransform breath = new(1, 1, 512, 1440);
    private readonly RotateTransform lean = new(0, 512, 1440);
    private readonly TranslateTransform lift = new();
    private readonly MatrixTransform leftLeg = new();
    private readonly MatrixTransform rightLeg = new();
    private readonly RotateTransform capeLeft = new(0, 404, 887);
    private readonly RotateTransform capeRight = new(0, 677, 898);
    private readonly RotateTransform plume = new(0, 742, 259);
    private readonly RotateTransform braidLeft = new(0, 383, 580);
    private readonly RotateTransform braidRight = new(0, 676, 551);
    private readonly Image? blink;
    private readonly Image? gaze;
    private long noticeStarted;
    private long previousFrame;
    private double dragTilt;
    private double tiltVelocity;
    private double dragBias;
    private bool Dragging => dragStarted != 0;
    internal double BlinkWeight { get; private set; }

    internal VentiMotion(string frontPath, string? motionDirectory, bool requirePreviewAssets)
    {
        BitmapSource front = Load(frontPath);
        if (requirePreviewAssets && (front.PixelWidth != 1024 || front.PixelHeight != 1536))
        {
            throw new VentiAssetException(frontPath, new InvalidDataException("The preview canvas must be 1024 by 1536 pixels."));
        }
        var layers = new Grid { Width = front.PixelWidth, Height = front.PixelHeight };
        standing = layers;
        BitmapSource body = motionDirectory is null ? front : Load(requirePreviewAssets
            ? Path.Combine(motionDirectory, "body-v0.2.png")
            : PreferredAsset(motionDirectory, "body"), front);
        if (motionDirectory is not null && front.PixelWidth == 1024 && front.PixelHeight == 1536)
        {
            flight = new VentiFlight();
            layers.Children.Add(flight);
        }
        if (motionDirectory is not null &&
            (requirePreviewAssets || File.Exists(Path.Combine(motionDirectory, "cape-left.png"))))
        {
            AddPatch(layers, front, Path.Combine(motionDirectory, "cape-left.png"), capeLeft);
            AddPatch(layers, front, Path.Combine(motionDirectory, "cape-right.png"), capeRight);
        }
        bodyImage = new Image { Source = body };
        layers.Children.Add(bodyImage);
        if (motionDirectory is not null && body.PixelWidth == 1024 && body.PixelHeight == 1536)
        {
            dragBody = CreateDragBody(body);
            layers.Children.Add(dragBody);
        }
        if (motionDirectory is not null)
        {
            blink = AddPatch(layers, front, Path.Combine(motionDirectory, "blink.png"));
            gaze = AddPatch(layers, front, Path.Combine(motionDirectory, "gaze.png"));
            AddPatch(layers, front, Path.Combine(motionDirectory, "plume.png"), plume);
            AddPatch(layers, front, Path.Combine(motionDirectory, "braid-left.png"), braidLeft);
            AddPatch(layers, front, Path.Combine(motionDirectory, "braid-right.png"), braidRight);
        }
        layers.RenderTransform = new TransformGroup { Children = { breath, lean, lift } };
        var poses = new Grid { Width = front.PixelWidth, Height = front.PixelHeight };
        poses.Children.Add(layers);
        if (motionDirectory is not null &&
            (requirePreviewAssets || Directory.Exists(Path.Combine(motionDirectory, "gestures"))))
        {
            gestures = new VentiGestures(Path.Combine(motionDirectory, "gestures"), poses, front);
            if (front.PixelWidth == 1024 && front.PixelHeight == 1536 && blink is not null)
            {
                doze = new VentiDoze();
                layers.Children.Add(doze);
            }
        }
        var view = new Viewbox { Width = 132, Height = 198, Child = poses };
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(view, 94);
        Canvas.SetTop(view, 61);
        Visual = new Canvas();
        Visual.Children.Add(view);
    }

    internal Canvas Visual { get; }
    internal bool HasEyeLayers => blink is not null;

    internal bool HasGestures => gestures is not null;
    internal int GestureFrame => gestures?.Frame ?? 0;
    internal bool CanDoze => doze is not null;
    internal DozeStage DozeStage => doze?.Stage ?? DozeStage.Awake;
    internal Gesture? ActiveGesture => gestures?.Current ?? (DozeStage == DozeStage.Awake ? null : Gesture.Doze);
    internal bool IsAtRest => !Dragging && landingStarted == 0 && ActiveGesture is null && noticeStarted == 0;
    internal (int Emitted, int Visible) LyreNoteState => gestures?.NoteState ?? default;

    internal bool PlayGesture(Gesture gesture)
    {
        if (Dragging || landingStarted != 0) return false;
        if (gesture == Gesture.Doze)
        {
            if (gestures?.Current is not null || doze?.Start() != true) return false;
            noticeStarted = 0;
            return true;
        }
        if (gestures?.Start(gesture) != true) return false;

        doze?.ResetPose();
        noticeStarted = 0;
        BlinkWeight = 0;
        ShowStanding();
        return true;
    }

    internal void InterruptGesture()
    {
        WakeDoze();
        if (gestures?.Current is null) return;
        gestures.Stop();
        ShowStanding();
    }

    internal void WakeDoze() => doze?.Wake();

    internal bool Notice()
    {
        if (!HasEyeLayers || Dragging || gestures?.Current is not null || landingStarted != 0 || noticeStarted != 0)
        {
            return false;
        }
        WakeDoze();
        noticeStarted = Stopwatch.GetTimestamp();
        return true;
    }

    internal void MoveDrag(int deltaX, int deltaY, double dpiScale)
    {
        if (!HasEyeLayers)
        {
            return;
        }
        if (!Dragging)
        {
            InterruptGesture();
            pickupPose = dragPose;
            flight?.CaptureSpread();
            dragStarted = Stopwatch.GetTimestamp();
            noticeStarted = landingStarted = 0;
            LandingFrame = 0;
        }
        dragBias = Math.Clamp(deltaX * 0.65, -14, 14);
        flight?.Move(deltaX / dpiScale, deltaY / dpiScale);
    }

    internal void ReleaseDrag(bool land)
    {
        dragStarted = 0;
        dragBias = 0;
        if (land && HasEyeLayers)
        {
            releasePose = dragPose;
            flight?.CaptureSpread();
            landingStarted = Stopwatch.GetTimestamp();
            LandingFrame = 1;
        }
        else
        {
            landingStarted = 0;
            LandingFrame = 0;
            dragPose = default;
            dragTilt = tiltVelocity = 0;
            flight?.Reset();
        }
    }

    internal bool Render(double seconds)
    {
        if (gestures?.Current is not null)
        {
            gestures.Update();
            ShowStanding();
            if (gestures.Current is not null)
            {
                return false;
            }
        }
        long now = Stopwatch.GetTimestamp();
        doze?.Update(now);
        double drowsy = doze?.Depth ?? 0;
        double phase = seconds % 5.2;
        double breathing = 0.5 - 0.5 * Math.Cos(phase * Math.PI * 2 / 5.2);
        breath.ScaleY = 1 + breathing * (0.008 + drowsy * 0.004) - drowsy * 0.025;
        breath.ScaleX = 1 + breathing * 0.003;
        if (blink is null || gaze is null)
        {
            return false;
        }

        double closure = Pulse(phase, 3.1, 0.065, 0.045, 0.12);
        double response = noticeStarted == 0 ? -1 : Stopwatch.GetElapsedTime(noticeStarted).TotalSeconds;
        double gazeWeight = Pulse(response, 0, 0.085, 0.56, 0.19);
        double posture = Pulse(response, 0.105, 0.25, 0.25, 0.40);
        blink.Opacity = BlinkWeight = Math.Max(drowsy, LandingFrame == 0 ? closure * (1 - gazeWeight) : 0);
        gaze.Opacity = gazeWeight * (1 - drowsy);
        double step = previousFrame == 0 ? 0 : Math.Min(Stopwatch.GetElapsedTime(previousFrame, now).TotalSeconds, 0.025);
        previousFrame = now;
        // Follow the pointer with a damped turn, then keep a small hanging sway while held.
        dragBias *= Math.Exp(-step * 7);
        tiltVelocity += ((dragBias - dragTilt) * 175 - tiltVelocity * 17) * step;
        dragTilt += tiltVelocity * step;
        double squash = UpdateDragPose(now, step);
        ApplyDragPose();
        if (squash > 0)
        {
            breath.ScaleY -= squash * 0.042;
            breath.ScaleX += squash * 0.023;
        }
        double tilt = dragPose.Tilt;
        lean.Angle = posture * 0.35 + tilt + drowsy * (2.6 + 0.36 * Math.Sin(seconds * 2.1));
        double wave = phase * Math.PI * 2 / 5.2;
        capeLeft.Angle = 1.1 * Math.Sin(wave - 1.3) - posture * 0.4 - tilt * 0.56 - dragPose.Amount * 4;
        capeRight.Angle = -0.9 * Math.Sin(wave - 1.5) + posture * 0.4 - tilt * 0.48 + dragPose.Amount * 4;
        plume.Angle = 1.15 * Math.Sin(wave - 0.7) - posture * 0.9 - tilt * 0.65;
        braidLeft.Angle = 1.05 * Math.Sin(wave - 0.95) + posture * 1.4 - tilt * 0.35;
        braidRight.Angle = -0.85 * Math.Sin(wave - 1.20) + posture * 1.0 - tilt * 0.4;
        if (response < 1.005)
        {
            return false;
        }
        noticeStarted = 0;
        return true;
    }

    internal void Reset()
    {
        InterruptGesture();
        noticeStarted = landingStarted = dragStarted = 0;
        LandingFrame = 0;
        BlinkWeight = 0;
        previousFrame = 0;
        dragTilt = tiltVelocity = dragBias = 0;
        dragPose = pickupPose = releasePose = default;
        flight?.Reset();
        doze?.ResetPose();
        ApplyDragPose();
        breath.ScaleX = breath.ScaleY = 1;
        lean.Angle = plume.Angle = braidLeft.Angle = braidRight.Angle = 0;
        capeLeft.Angle = capeRight.Angle = 0;
        if (blink is not null)
        {
            blink.Opacity = 0;
            gaze!.Opacity = 0;
        }
    }

    private double UpdateDragPose(long now, double step)
    {
        if (Dragging)
        {
            double held = Stopwatch.GetElapsedTime(dragStarted, now).TotalSeconds;
            flight?.Update(true, held, step);
            double progress = Smooth(held / 0.13);
            double swing = Math.Sin(held * 8);
            double remaining = 1 - progress;
            dragPose = (
                pickupPose.Amount * remaining + progress,
                pickupPose.Swing * remaining + progress * swing,
                pickupPose.Tilt * remaining + progress * (dragTilt + 2.4 * swing));
            return 0;
        }
        if (landingStarted == 0)
        {
            return 0;
        }

        double age = Stopwatch.GetElapsedTime(landingStarted, now).TotalSeconds;
        flight?.Update(false, age, step);
        const double contact = 0.095;
        const double settle = 0.35;
        double airborne = 1 - Smooth(age / contact);
        dragPose = (releasePose.Amount * airborne, releasePose.Swing * airborne, releasePose.Tilt * airborne);
        LandingFrame = age < contact ? 1 : age < settle ? 2 : 0;
        if (LandingFrame == 0)
        {
            landingStarted = 0;
            dragTilt = tiltVelocity = 0;
        }
        return Pulse(age, contact, 0.068, 0, settle - contact - 0.068);
    }

    private void ApplyDragPose()
    {
        var (amount, swing, _) = dragPose;
        lift.Y = -74 * amount;
        lean.CenterY = 1440 - 810 * amount;
        if (dragBody is null)
        {
            return;
        }
        if (amount > 0)
        {
            // The affine maps meet the torso exactly at the shorts hem.
            leftLeg.Matrix = LegMatrix(0.105 * swing + 0.085 * amount, 1 - 0.105 * amount + 0.040 * swing);
            rightLeg.Matrix = LegMatrix(0.105 * swing - 0.065 * amount, 1 - 0.145 * amount - 0.040 * swing);
        }
        dragBody.Visibility = amount > 0 ? Visibility.Visible : Visibility.Hidden;
        bodyImage.Visibility = amount > 0 ? Visibility.Hidden : Visibility.Visible;
    }

    private const double Hem = 1064;

    private static Matrix LegMatrix(double shear, double height) =>
        new(1, 0, shear, height, -Hem * shear, Hem * (1 - height));

    private Grid CreateDragBody(BitmapSource body)
    {
        const double legSplit = 544;
        var parts = new Grid { Visibility = Visibility.Hidden };
        AddPart(new Rect(0, 0, body.PixelWidth, Hem + 1), Transform.Identity);
        AddPart(new Rect(0, Hem, legSplit, body.PixelHeight - Hem), leftLeg);
        AddPart(new Rect(legSplit, Hem, body.PixelWidth - legSplit, body.PixelHeight - Hem), rightLeg);
        return parts;

        void AddPart(Rect bounds, Transform transform) => parts.Children.Add(new Image
        {
            Source = body,
            Clip = new RectangleGeometry(bounds),
            RenderTransform = transform
        });
    }

    private void ShowStanding() => standing.Visibility =
        gestures?.Current is null ? Visibility.Visible : Visibility.Hidden;

    private static string PreferredAsset(string folder, string name)
    {
        string revised = Path.Combine(folder, name + "-v0.2.png");
        return File.Exists(revised) ? revised : Path.Combine(folder, name + ".png");
    }

    internal static Image AddPose(Grid parent, BitmapSource source, string path)
    {
        BitmapSource pose = ReadAsset(path, file =>
        {
            var artwork = BitmapDecoder.Create(file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            RequireCanvas(source, artwork);
            file.Position = 0;

            // A 198 DIP pose is 396 px tall at 200% DPI. Keep headroom for its alpha edges.
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = file;
            bitmap.DecodePixelHeight = 576;
            bitmap.EndInit();
            return bitmap;
        });
        var image = new Image
        {
            Source = pose,
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Visibility = Visibility.Hidden
        };
        parent.Children.Add(image);
        return image;
    }

    private static double Pulse(double elapsed, double start, double rise, double hold, double fall)
    {
        double age = elapsed - start;
        double weight = Math.Min(age / rise, (rise + hold + fall - age) / fall);
        return Smooth(weight);
    }

    private static double Smooth(double fraction)
    {
        double value = Math.Clamp(fraction, 0, 1);
        return value * value * (3 - 2 * value);
    }

    private static Image AddPatch(Grid layers, BitmapSource source, string path, Transform? movement = null)
    {
        BitmapSource patch = Load(path, source);
        var image = new Image { Source = patch, Opacity = movement is null ? 0 : 1 };
        if (movement is not null)
        {
            image.RenderTransform = movement;
        }
        layers.Children.Add(image);
        return image;
    }

    private static void RequireCanvas(BitmapSource source, BitmapSource patch)
    {
        if (patch.PixelWidth != source.PixelWidth || patch.PixelHeight != source.PixelHeight)
        {
            throw new InvalidDataException("Venti layers must share the front sprite pixel canvas.");
        }
    }

    private static BitmapSource Load(string path, BitmapSource? canvas = null) => ReadAsset(path, file =>
    {
        BitmapSource bitmap = BitmapDecoder.Create(file,
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (canvas is not null)
        {
            RequireCanvas(canvas, bitmap);
        }
        return bitmap;
    });

    private static BitmapSource ReadAsset(string path, Func<FileStream, BitmapSource> decode)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            BitmapSource bitmap = decode(file);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FileFormatException or
            InvalidDataException or NotSupportedException or ArgumentException or COMException)
        {
            throw new VentiAssetException(path, error);
        }
    }
}
