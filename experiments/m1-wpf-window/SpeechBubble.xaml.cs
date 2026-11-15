using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Aeris.WindowProbe;

internal partial class SpeechBubble : Window
{
    private readonly Overlay character;
    private readonly Func<int> seconds;
    private readonly Func<string, bool, Task<string>> reply;
    private readonly Action<string> interacted;
    private readonly DispatcherTimer clock;
    private long previousTick;
    private long editorActivity;
    private double remaining;
    private bool editing;
    private bool sending;
    private bool closed;
    private nint handle;

    internal SpeechBubble(Overlay character, Func<int> seconds, Func<string, bool, Task<string>> reply,
        Action<string> interacted)
    {
        this.character = character;
        this.seconds = seconds;
        this.reply = reply;
        this.interacted = interacted;
        InitializeComponent();
        clock = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        clock.Tick += (_, _) => Tick();
        Reply.Click += (_, _) => OpenEditor();
        Card.MouseLeftButtonUp += (_, _) =>
        {
            if (!editing) OpenEditor();
        };
        Dismiss.Click += (_, _) => Close();
        Send.Click += async (_, _) => await SendDraft();
        Draft.TextChanged += (_, _) => editorActivity = Stopwatch.GetTimestamp();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            if (editing && e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendDraft();
            }
        };
        SizeChanged += (_, _) => PlaceNearCharacter();
    }

    internal void Present(string text)
    {
        if (closed) return;
        Line.Text = text;
        editing = false;
        Editor.Visibility = Visibility.Collapsed;
        Reply.Visibility = Visibility.Visible;
        remaining = seconds();
        previousTick = Stopwatch.GetTimestamp();
        Topmost = character.Topmost;
        if (!IsVisible)
        {
            new WindowInteropHelper(this).EnsureHandle();
            PlaceNearCharacter();
            Show();
        }
        Native.SetInputStyle(handle, false);
        PlaceNearCharacter();
        Card.Opacity = 1;
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            { FillBehavior = FillBehavior.Stop });
        Drift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        clock.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        handle = new WindowInteropHelper(this).Handle;
        HwndSource source = HwndSource.FromHwnd(handle);
        Native.SetInputStyle(handle, false);
        source.AddHook(WindowMessage);
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        clock.Stop();
        base.OnClosed(e);
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != Native.MouseActivate || editing) return 0;
        handled = true;
        return Native.NoActivate;
    }

    private void OpenEditor()
    {
        editing = true;
        editorActivity = Stopwatch.GetTimestamp();
        Reply.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Status.Text = "";
        Card.Opacity = 1;
        Native.SetInputStyle(handle, false, noActivate: false);
        interacted("edit");
        Activate();
        Draft.Focus();
    }

    private async Task SendDraft()
    {
        string text = Draft.Text.Trim();
        if (!editing || sending || text.Length == 0) return;
        sending = true;
        Send.IsEnabled = Draft.IsEnabled = UseMemory.IsEnabled = false;
        Status.Text = "温迪正在回复…";
        try
        {
            string answer = await reply(text, UseMemory.IsChecked == true);
            if (closed) return;
            Draft.Clear();
            Present(answer);
            interacted("answer");
        }
        catch (Exception error) when (error is System.IO.IOException or System.Net.Http.HttpRequestException or
            TimeoutException or InvalidOperationException or OperationCanceledException or ArgumentException or
            System.Text.Json.JsonException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            if (!closed) Status.Text = "这次没有收到回复，可以再试一次。";
        }
        finally
        {
            sending = false;
            editorActivity = Stopwatch.GetTimestamp();
            if (!closed) Send.IsEnabled = Draft.IsEnabled = UseMemory.IsEnabled = true;
        }
    }

    private void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = Stopwatch.GetElapsedTime(previousTick, now).TotalSeconds;
        previousTick = now;
        if (editing)
        {
            if (!sending && Stopwatch.GetElapsedTime(editorActivity, now).TotalSeconds >= 75) Close();
            return;
        }
        if (!IsMouseOver) remaining -= elapsed;
        Card.Opacity = IsMouseOver ? 1 : Math.Clamp(remaining / 0.22, 0, 1);
        if (remaining <= 0)
        {
            interacted("expired");
            Close();
        }
    }

    private void PlaceNearCharacter()
    {
        if (handle == 0 || character.Handle == 0 ||
            !Native.GetWindowRect(character.Handle, out var pet) || !Native.GetWindowRect(handle, out var bubble)) return;

        int petWidth = pet.Right - pet.Left;
        int petHeight = pet.Bottom - pet.Top;
        var center = new Native.Point(pet.Left + petWidth / 2, pet.Top + petHeight / 2);
        if (!Native.TryGetWorkArea(center, false, out var work)) return;

        int width = bubble.Right - bubble.Left;
        int height = bubble.Bottom - bubble.Top;
        int gap = Math.Max(5, petWidth / 32);
        int right = center.X + petWidth / 8 + gap;
        int left = right + width <= work.Right - gap ? right : center.X - petWidth / 8 - gap - width;
        int top = pet.Top + petHeight / 3 - height / 2;
        left = Math.Clamp(left, work.Left + gap, Math.Max(work.Left + gap, work.Right - gap - width));
        top = Math.Clamp(top, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - gap - height));
        if (left != bubble.Left || top != bubble.Top) Native.Move(handle, left, top);
    }
}
