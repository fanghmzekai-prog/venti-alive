using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Aeris.WindowProbe;

internal sealed class Overlay : Window
{
    private readonly ProbeLog log;
    private readonly VentiMotion? sprite;
    private readonly DispatcherTimer dragTimer;
    private readonly TranslateTransform orbit = new();
    private readonly List<double> frameIntervals = [];
    private readonly Gesture?[] idleActions;
    private HwndSource? source;
    private Native.Point? dragOffset;
    private Native.Point dragOrigin;
    private bool dragMoved;
    private bool dragPoseShown;
    private Native.Point dragMotion;
    private long lastDragUpdate;
    private nint dragForeground;
    private long previousFrame;
    private long animationStarted;
    private bool blinkActive;
    private bool sessionNotification;
    private bool autonomousEnabled;
    private bool passThroughEnabled;
    private ScreenFlight? screenFlight;
    internal event Action? UserInteracted;
    internal bool BubbleVisible { get; set; }
    internal bool CanStartIndependentAction => IsVisible && animationStarted != 0 && !passThroughEnabled &&
        dragOffset is null && screenFlight is null && sprite?.IsAtRest == true;
    private sealed record ScreenFlight(Native.Rect Start, Native.Point End, Native.Rect Work,
        double DpiScale, double Lift, double Seconds, long Started);
    private bool playingIdleGesture;
    private int nextIdleAction;
    private double idleDelaySeconds = 20;
    private long lastInteraction;
    private int previousLandingFrame;
    private FlightStage previousFlightStage;
    private DozeStage previousDozeStage;
    private int previousGestureFrame;
    private (int Emitted, int Visible) previousLyreNotes;
    private double previousRenderingTime = -1;

    internal Overlay(ProbeLog log, double left, double top, string? spritePath, string? motionDirectory,
        bool requirePreviewAssets)
    {
        this.log = log;
        Title = "Aeris M1 Overlay";
        Width = Height = 320;
        Left = left;
        Top = top;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        Topmost = true;

        sprite = spritePath is null ? null : new VentiMotion(spritePath, motionDirectory, requirePreviewAssets);
        idleActions = CanDoze
            ? [Gesture.Lyre, Gesture.HandsOnHips, Gesture.FoldedArms, Gesture.Doze, null]
            : [Gesture.Lyre, Gesture.HandsOnHips, Gesture.FoldedArms, null];
        nextIdleAction = idleActions.Length;
        Content = sprite?.Visual ?? CreateGeometrySample();
        dragTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        dragTimer.Tick += (_, _) => TrackDrag();
    }

    private Canvas CreateGeometrySample()
    {
        var center = new Point(160, 160);
        var gradient = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = 118,
            RadiusY = 118,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(24, 126, 111), 0),
                new GradientStop(Color.FromRgb(35, 166, 134), 0.83),
                new GradientStop(Color.FromArgb(0, 35, 166, 134), 1)
            }
        };
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(center, 118, 118), new EllipseGeometry(center, 47, 47));
        var canvas = new Canvas();
        canvas.Children.Add(new Path
        {
            Data = ring, Fill = gradient, Stretch = Stretch.None, Width = 320, Height = 320
        });
        var marker = new Ellipse
        {
            Width = 14, Height = 14, Fill = Brushes.Wheat, RenderTransform = orbit
        };
        Canvas.SetLeft(marker, 153);
        Canvas.SetTop(marker, 153);
        canvas.Children.Add(marker);
        orbit.Y = -78;
        return canvas;
    }

    internal nint Handle => source?.Handle ?? 0;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        Native.SetInputStyle(Handle, false);
        source.AddHook(WindowMessage);
        sessionNotification = Native.WTSRegisterSessionNotification(Handle, 0);
        log.Write("overlay", new { hwnd = Handle.ToInt64() });
    }

    internal void RestorePosition(int? x = null, int? y = null, bool centerOnPrimary = false)
    {
        EndDrag("reposition");
        StopScreenFlight("reposition");
        if (!Native.GetWindowRect(Handle, out var bounds))
        {
            return;
        }
        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        int left = x ?? bounds.Left;
        int top = y ?? bounds.Top;
        var center = new Native.Point(
            (int)Math.Clamp((long)left + width / 2, int.MinValue, int.MaxValue),
            (int)Math.Clamp((long)top + height / 2, int.MinValue, int.MaxValue));
        if (!Native.TryGetWorkArea(center, centerOnPrimary, out var work))
        {
            return;
        }

        if (centerOnPrimary)
        {
            if (CenterWindow(bounds))
            {
                // WPF can update the physical window size when the new monitor's DPI applies.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
                {
                    if (dragOffset is null && Native.GetWindowRect(Handle, out var settled))
                    {
                        CenterWindow(settled);
                    }
                }));
            }
            return;
        }

        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (left != bounds.Left || top != bounds.Top)
        {
            Native.Move(Handle, left, top);
        }

        bool CenterWindow(Native.Rect current)
        {
            int x = work.Left + Math.Max(0, work.Right - work.Left - (current.Right - current.Left)) / 2;
            int y = work.Top + Math.Max(0, work.Bottom - work.Top - (current.Bottom - current.Top)) / 2;
            return (x != current.Left || y != current.Top) && Native.Move(Handle, x, y);
        }
    }

    internal void SetAutonomous(bool enabled)
    {
        autonomousEnabled = enabled;
        lastInteraction = Stopwatch.GetTimestamp();
        idleDelaySeconds = 20;
        if (!enabled)
        {
            InterruptIdleGesture();
        }
        log.Write("autonomousMode", new { enabled });
    }

    internal void SetVisible(bool visible)
    {
        if (visible)
        {
            Show();
        }
        else
        {
            EndDrag("hide");
            StopScreenFlight("hide");
            Hide();
        }
    }

    internal void SetPassThrough(bool enabled)
    {
        EndDrag("mode");
        if (enabled) StopScreenFlight("mode");
        passThroughEnabled = enabled;
        Native.SetInputStyle(Handle, enabled);
        log.Write("passThrough", new { enabled });
    }

    internal void SetPlaying(bool enabled)
    {
        if (enabled == (animationStarted != 0))
        {
            return;
        }

        if (enabled)
        {
            previousFrame = 0;
            previousRenderingTime = -1;
            frameIntervals.Clear();
            animationStarted = lastInteraction = Stopwatch.GetTimestamp();
            idleDelaySeconds = 20;
            dragTimer.Stop();
            CompositionTarget.Rendering += RenderFrame;
            log.Write("animationStart");
            return;
        }

        CompositionTarget.Rendering -= RenderFrame;
        double seconds = Stopwatch.GetElapsedTime(animationStarted).TotalSeconds;
        animationStarted = 0;
        dragMotion = default;
        StopScreenFlight("paused");
        sprite?.Reset();
        if (dragOffset is not null) dragTimer.Start();
        RecordFlightStage();
        RecordLyreNotes();
        RecordDozeStage();
        playingIdleGesture = false;
        blinkActive = false;
        previousLandingFrame = 0;
        previousGestureFrame = 0;
        frameIntervals.Sort();
        double? p95 = frameIntervals.Count == 0
            ? null
            : frameIntervals[(int)Math.Ceiling(frameIntervals.Count * 0.95) - 1];
        log.Write("animationStop", new { seconds, intervals = frameIntervals.Count, p95 });
    }

    internal bool CanNotice => sprite?.HasEyeLayers == true;

    internal bool CanPlayGestures => sprite?.HasGestures == true;
    internal bool CanDoze => sprite?.CanDoze == true;

    internal bool PlayGesture(Gesture gesture, bool spontaneous = false)
    {
        if (!spontaneous)
        {
            UserInteracted?.Invoke();
            StopScreenFlight("manual-action");
            InterruptIdleGesture();
        }
        if (animationStarted == 0 || dragOffset is not null || sprite?.PlayGesture(gesture) != true)
        {
            return false;
        }
        lastInteraction = Stopwatch.GetTimestamp();
        playingIdleGesture = spontaneous;
        log.Write("gestureStart", new { gesture = gesture.ToString(), spontaneous });
        return true;
    }

    internal bool Notice(bool spontaneous = false)
    {
        if (!spontaneous)
        {
            UserInteracted?.Invoke();
            StopScreenFlight("manual-action");
            InterruptIdleGesture();
        }
        if (animationStarted == 0 || dragOffset is not null || sprite?.Notice() != true)
        {
            return false;
        }
        lastInteraction = Stopwatch.GetTimestamp();
        log.Write("noticeStart", new { spontaneous });
        return true;
    }

    private void PlayIdleAction()
    {
        bool started;
        if (CanPlayGestures)
        {
            if (nextIdleAction == idleActions.Length)
            {
                Gesture? last = idleActions[^1];
                Random.Shared.Shuffle(idleActions);
                if (idleActions[0] == last)
                {
                    int other = Random.Shared.Next(1, idleActions.Length);
                    (idleActions[0], idleActions[other]) = (idleActions[other], idleActions[0]);
                }
                nextIdleAction = 0;
            }
            started = idleActions[nextIdleAction] is { } gesture
                ? PlayGesture(gesture, spontaneous: true)
                : Notice(spontaneous: true);
            if (started)
            {
                nextIdleAction++;
            }
        }
        else
        {
            started = Notice(spontaneous: true);
        }
        if (started)
        {
            idleDelaySeconds = Random.Shared.Next(18, 33);
        }
    }

    private void InterruptIdleGesture()
    {
        if (playingIdleGesture)
        {
            sprite?.InterruptGesture();
            playingIdleGesture = false;
            previousGestureFrame = 0;
            log.Write("idleGestureInterrupted");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        EndDrag("close");
        StopScreenFlight("close");
        SetPlaying(false);
        if (sessionNotification)
        {
            Native.WTSUnRegisterSessionNotification(Handle);
        }
        source?.RemoveHook(WindowMessage);
        base.OnClosed(e);
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Native.DisplayChanged ||
            (message == Native.DpiChanged && dragOffset is null) ||
            (message == Native.PowerBroadcast && wParam == Native.ResumeAutomatic) ||
            (message == Native.SessionChanged && wParam == Native.SessionUnlock))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                if (message != Native.DpiChanged || dragOffset is null)
                {
                    RestorePosition();
                }
            }));
        }

        if (message == Native.MouseActivate)
        {
            handled = true;
            return Native.NoActivate;
        }

        if (message == Native.LeftButtonDown &&
            Native.GetWindowRect(hwnd, out var rect))
        {
            Native.Point cursor = Native.ScreenPosition(hwnd, lParam);
            lastInteraction = Stopwatch.GetTimestamp();
            UserInteracted?.Invoke();
            InterruptIdleGesture();
            sprite?.WakeDoze();
            dragOrigin = cursor;
            dragMoved = screenFlight is not null;
            StopScreenFlight("grab", keepPose: true);
            dragPoseShown = false;
            dragMotion = default;
            lastDragUpdate = 0;
            dragOffset = new Native.Point(cursor.X - rect.Left, cursor.Y - rect.Top);
            dragForeground = Native.GetForegroundWindow();
            if (animationStarted == 0) dragTimer.Start();
            log.Write("overlayDown", new { cursor.X, cursor.Y, foreground = dragForeground.ToInt64() });
            handled = true;
        }

        if (message == Native.MouseMove && dragOffset is not null && animationStarted != 0 &&
            Stopwatch.GetElapsedTime(lastDragUpdate).TotalMilliseconds >= 5)
        {
            // Follow mouse input directly when over the sprite. The render pass also
            // samples the pointer when it outruns the window or crosses its transparent area.
            TrackDrag();
        }

        if (message == Native.CancelMode)
        {
            EndDrag("cancel");
            StopScreenFlight("cancel");
        }
        return 0;
    }

    private void TrackDrag()
    {
        // A non-activating background window cannot rely on capture outside its visible pixels.
        if (!Native.KeyDown(Native.LeftMouseKey))
        {
            EndDrag("release");
            return;
        }
        if (Native.KeyDown(Native.EscapeKey) || Native.GetForegroundWindow() != dragForeground)
        {
            EndDrag("cancel");
            return;
        }
        if (dragOffset is not { } offset || !Native.GetCursorPos(out var cursor) ||
            !Native.GetWindowRect(Handle, out var rect))
        {
            EndDrag("position");
            return;
        }

        if (!dragMoved)
        {
            if (Math.Abs(cursor.X - dragOrigin.X) < 4 && Math.Abs(cursor.Y - dragOrigin.Y) < 4)
            {
                return;
            }
            dragMoved = true;
            log.Write("dragStarted");
        }

        int x = cursor.X - offset.X;
        int y = cursor.Y - offset.Y;
        if (rect.Left == x && rect.Top == y)
        {
            return;
        }
        lastDragUpdate = Stopwatch.GetTimestamp();
        if (!Native.Move(Handle, x, y))
        {
            EndDrag("position");
            return;
        }
        if (animationStarted != 0)
        {
            dragMotion.X += x - rect.Left;
            dragMotion.Y += y - rect.Top;
        }
    }

    private void EndDrag(string reason)
    {
        if (dragOffset is null)
        {
            return;
        }

        dragOffset = null;
        dragMotion = default;
        lastInteraction = Stopwatch.GetTimestamp();
        dragTimer.Stop();
        if (dragMoved)
        {
            sprite?.ReleaseDrag(reason == "release" && animationStarted != 0);
            RecordFlightStage();
        }
        if (reason == "release" && CanNotice)
        {
            if (dragMoved && animationStarted != 0)
            {
                log.Write("landing");
            }
            else
            {
                Notice();
            }
        }
        log.Write("dragEnd", new { reason, foreground = Native.GetForegroundWindow().ToInt64() });
    }

    internal void StartScreenFlight()
    {
        if (!CanStartIndependentAction || BubbleVisible || sprite is null ||
            !Native.GetWindowRect(Handle, out var bounds)) return;

        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        var center = new Native.Point(bounds.Left + width / 2, bounds.Top + height / 2);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (!Native.TryGetWorkArea(center, false, out var work) ||
            work.Right - work.Left < width || work.Bottom - work.Top < height) return;

        int maxLeft = work.Right - width;
        int maxTop = work.Bottom - height;
        Vector reach = new(Math.Max(bounds.Left - work.Left, maxLeft - bounds.Left),
            Math.Max(bounds.Top - work.Top, maxTop - bounds.Top));
        if (reach.Length < 95 * dpi) return;
        double minimum = Math.Min(380 * dpi, 0.55 * reach.Length);
        double maximum = Math.Min(820 * dpi, reach.Length);

        for (int attempt = 0; attempt < 16; attempt++)
        {
            double angle = Random.Shared.NextDouble() * Math.Tau;
            double distance = minimum + Random.Shared.NextDouble() * (maximum - minimum);
            var end = new Native.Point(
                Math.Clamp(bounds.Left + (int)Math.Round(Math.Cos(angle) * distance), work.Left, maxLeft),
                Math.Clamp(bounds.Top + (int)Math.Round(Math.Sin(angle) * distance), work.Top, maxTop));
            Vector travel = new(end.X - bounds.Left, end.Y - bounds.Top);
            if (travel.Length < minimum) continue;

            double distanceDips = travel.Length / dpi;
            double duration = Math.Clamp(distanceDips / 225 + 0.7, 1.8, 4.5);
            screenFlight = new(bounds, end, work, dpi, (45 + Random.Shared.NextDouble() * 45) * dpi,
                duration, Stopwatch.GetTimestamp());
            sprite.MoveDrag(0, 0, dpi);
            log.Write("screenFlightStart", new { bounds.Left, bounds.Top, end.X, end.Y, work, width, height, distanceDips });
            return;
        }
    }

    internal void StopScreenFlight(string reason, bool keepPose = false)
    {
        if (screenFlight is null) return;
        screenFlight = null;
        bool landed = reason == "landed";
        if (!landed) lastInteraction = Stopwatch.GetTimestamp();
        if (!keepPose) sprite?.ReleaseDrag(landed);
        log.Write("screenFlightEnd", new { reason });
    }

    private void AdvanceScreenFlight(long now)
    {
        if (screenFlight is not { } flight || sprite is null) return;
        double progress = Math.Clamp(Stopwatch.GetElapsedTime(flight.Started, now).TotalSeconds / flight.Seconds, 0, 1);
        double t = progress * progress * (3 - 2 * progress);
        int x = (int)Math.Round(flight.Start.Left + (flight.End.X - flight.Start.Left) * t);
        int y = (int)Math.Round(flight.Start.Top + (flight.End.Y - flight.Start.Top) * t -
            flight.Lift * Math.Sin(Math.PI * t));
        y = Math.Clamp(y, flight.Work.Top, flight.Work.Bottom - (flight.Start.Bottom - flight.Start.Top));
        if (!Native.GetWindowRect(Handle, out var current))
        {
            StopScreenFlight("position");
            return;
        }
        if (x != current.Left || y != current.Top)
        {
            if (!Native.Move(Handle, x, y))
            {
                StopScreenFlight("position");
                return;
            }
            sprite.MoveDrag(x - current.Left, y - current.Top, flight.DpiScale);
        }
        if (progress >= 1) StopScreenFlight("landed");
    }

    private void RecordFlightStage()
    {
        if (!log.Enabled || sprite is null || sprite.FlightStage == previousFlightStage) return;
        previousFlightStage = sprite.FlightStage;
        log.Write("flightStage", new { stage = previousFlightStage.ToString(), spread = sprite.WingSpread });
    }

    private void RecordDozeStage()
    {
        if (!log.Enabled || sprite is null || sprite.DozeStage == previousDozeStage) return;
        previousDozeStage = sprite.DozeStage;
        log.Write("dozeStage", new { stage = previousDozeStage.ToString(), eyelid = sprite.BlinkWeight });
    }

    private void RecordLyreNotes()
    {
        if (!log.Enabled || sprite is null || sprite.LyreNoteState == previousLyreNotes) return;
        previousLyreNotes = sprite.LyreNoteState;
        log.Write("lyreNotes", new { emitted = previousLyreNotes.Emitted, visible = previousLyreNotes.Visible,
            frame = sprite.GestureFrame });
    }

    private void RenderFrame(object? sender, EventArgs args)
    {
        var rendering = (RenderingEventArgs)args;
        if (rendering.RenderingTime.TotalMilliseconds == previousRenderingTime)
        {
            return;
        }

        previousRenderingTime = rendering.RenderingTime.TotalMilliseconds;
        if (dragOffset is not null)
        {
            TrackDrag();
            if (dragMoved && (dragMotion.X != 0 || dragMotion.Y != 0))
            {
                sprite?.MoveDrag(dragMotion.X, dragMotion.Y, VisualTreeHelper.GetDpi(this).DpiScaleX);
                dragMotion = default;
            }
        }

        long now = Stopwatch.GetTimestamp();
        if (log.Enabled && previousFrame != 0)
        {
            frameIntervals.Add(Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds);
        }
        previousFrame = now;
        double seconds = Stopwatch.GetElapsedTime(animationStarted, now).TotalSeconds;
        if (sprite is not null)
        {
            AdvanceScreenFlight(now);
            if (autonomousEnabled && dragOffset is null && screenFlight is null && !BubbleVisible &&
                Stopwatch.GetElapsedTime(lastInteraction, now).TotalSeconds >= idleDelaySeconds)
            {
                PlayIdleAction();
            }
            if (sprite.Render(seconds))
            {
                log.Write("noticeEnd");
            }
            RecordFlightStage();
            RecordLyreNotes();
            RecordDozeStage();
            if (sprite.GestureFrame != previousGestureFrame)
            {
                previousGestureFrame = sprite.GestureFrame;
                log.Write("gestureFrame", new { frame = previousGestureFrame });
            }
            if (sprite.ActiveGesture is null)
            {
                playingIdleGesture = false;
            }
            if (log.Enabled && dragOffset is not null && dragMoved && !dragPoseShown && sprite.DragLiftDips >= 8)
            {
                dragPoseShown = true;
                log.Write("dragPose", new { liftDips = sprite.DragLiftDips });
            }
            if (sprite.LandingFrame != previousLandingFrame)
            {
                previousLandingFrame = sprite.LandingFrame;
                log.Write("landingFrame", new { frame = previousLandingFrame });
            }
            bool closing = sprite.BlinkWeight > 0.05;
            if (closing != blinkActive)
            {
                blinkActive = closing;
                log.Write(closing ? "blinkStart" : "blinkEnd");
            }
            return;
        }

        double angle = seconds * Math.PI;
        orbit.X = Math.Sin(angle) * 78;
        orbit.Y = -Math.Cos(angle) * 78;
    }
}
