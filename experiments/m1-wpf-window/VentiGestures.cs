using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Aeris.WindowProbe;

internal enum Gesture { Lyre = 1, HandsOnHips, FoldedArms, Doze }

internal sealed class VentiGestures
{
    private readonly Dictionary<Gesture, (Image Image, double End)[]> poses;
    private readonly Image pluckPose;
    private readonly LyreNotes notes = new();
    private Image? showing;
    private long started;
    private (Image Image, double End)[]? sequence;
    internal Gesture? Current { get; private set; }
    internal int Frame { get; private set; }
    internal (int Emitted, int Visible) NoteState => notes.State;

    internal VentiGestures(string folder, Grid canvas, BitmapSource reference)
    {
        var lyre = Load("lyre");
        pluckPose = Load("lyre-pluck");
        var ready = Load("lyre-ready");
        var hips = Load("hips");
        var crossed = Load("crossed");
        var crossedEnter = Load("crossed-enter");
        canvas.Children.Add(notes);

        poses = new()
        {
            [Gesture.Lyre] = [(ready, 0.14), (lyre, 0.44), (pluckPose, 0.62), (lyre, 0.90),
                (pluckPose, 1.10), (lyre, 1.39), (pluckPose, 1.56), (lyre, 1.83), (ready, 1.98)],
            [Gesture.HandsOnHips] = [(hips, 1.24)],
            [Gesture.FoldedArms] = [(crossedEnter, 0.14), (crossed, 1.51), (crossedEnter, 1.65)]
        };

        Image Load(string name)
        {
            var image = VentiMotion.AddPose(canvas, reference, Path.Combine(folder, name + ".png"));
            return image;
        }
    }

    internal bool Start(Gesture gesture)
    {
        if (sequence is not null || !poses.TryGetValue(gesture, out var frames))
        {
            return false;
        }
        Current = gesture;
        started = Stopwatch.GetTimestamp();
        sequence = frames;
        Show(0, 0);
        return true;
    }

    internal void Update()
    {
        if (sequence is null)
        {
            return;
        }
        double elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
        int index = Array.FindIndex(sequence, frame => elapsed < frame.End);
        if (index < 0)
        {
            Stop();
            return;
        }
        if (index + 1 != Frame)
        {
            Show(index, elapsed);
        }
        if (Current == Gesture.Lyre)
        {
            notes.Update(elapsed);
        }
    }

    internal void Stop()
    {
        if (showing is not null)
        {
            showing.Visibility = Visibility.Hidden;
        }
        showing = null;
        sequence = null;
        Current = null;
        Frame = 0;
        notes.Reset();
    }

    private void Show(int index, double elapsed)
    {
        Image next = sequence![index].Image;
        if (showing != next)
        {
            if (showing is not null)
            {
                showing.Visibility = Visibility.Hidden;
            }
            next.Visibility = Visibility.Visible;
            showing = next;
        }
        Frame = index + 1;
        if (next == pluckPose)
        {
            notes.Emit(elapsed);
        }
    }
}
