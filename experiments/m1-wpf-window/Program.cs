using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aeris.WindowProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool targetMode = args.Length > 0 && args[0] == "--target";
        string[] options = targetMode ? args[1..] : args;
        bool automation = options.Contains("--test");
        bool launchAtLogon = !targetMode && options.Contains("--startup");
        bool play = launchAtLogon || options.Contains("--play");
        bool requirePreviewAssets = launchAtLogon || options.Contains("--preview");
        int spriteIndex = Array.IndexOf(options, "--sprite");
        string? spritePath = spriteIndex >= 0 ? Path.GetFullPath(options[spriteIndex + 1]) : null;
        int motionIndex = Array.IndexOf(options, "--motion");
        string? motionDirectory = motionIndex >= 0 ? Path.GetFullPath(options[motionIndex + 1]) : null;
        if (launchAtLogon)
        {
            motionDirectory = WindowsStartup.Assets;
            spritePath = Path.Combine(motionDirectory, "front.png");
        }
        int logIndex = Array.IndexOf(options, "--log");
        string? logPath = logIndex >= 0 ? Path.GetFullPath(options[logIndex + 1]) : null;
        var log = new ProbeLog(logPath);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Rect screen = SystemParameters.WorkArea;
        double left = screen.Left + (screen.Width - 320) / 2;
        double top = screen.Top + (screen.Height - 320) / 2;

        if (targetMode)
        {
            return app.Run(CreateTarget(left, top, log));
        }

        int settingsIndex = Array.IndexOf(options, "--settings");
        PreferencesStore? store = settingsIndex >= 0
            ? new(Path.GetFullPath(options[settingsIndex + 1]))
            : automation || spritePath is null ? null : new(PreferencesStore.DefaultPath);
        using var instance = automation || store is null ? null : new Mutex(false, @"Local\AerisAlive.Desktop");
        if (instance is not null && !TryEnter(instance))
        {
            if (!launchAtLogon)
            {
                MessageBox.Show("温迪已经在运行。可从任务栏打开控制窗口。", "VENTI ALIVE");
            }
            return 0;
        }
        try
        {
            return RunDesktop(app, log, automation, play, spritePath, motionDirectory, store, left, top,
                requirePreviewAssets, options.Contains("--test-active"), launchAtLogon);
        }
        finally
        {
            instance?.ReleaseMutex();
        }
    }

    private static bool TryEnter(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static int RunDesktop(Application app, ProbeLog log, bool automation, bool play,
        string? spritePath, string? motionDirectory, PreferencesStore? store, double left, double top,
        bool requirePreviewAssets, bool testActive, bool launchAtLogon)
    {
        DesktopPreferences settings = store?.Load() ?? new DesktopPreferences
        {
            Playing = play,
            Autonomous = !automation
        };
        Overlay overlay;
        try
        {
            overlay = new Overlay(log, left, top, spritePath, motionDirectory, requirePreviewAssets)
                { Topmost = settings.Topmost };
        }
        catch (VentiAssetException error)
        {
            log.Write("startupAssetFailed", new { asset = error.AssetName, reason = error.InnerException?.GetType().Name });
            if (!automation && !launchAtLogon)
            {
                string problem = error.InnerException is FileNotFoundException or DirectoryNotFoundException
                    ? "缺少角色素材"
                    : "无法读取角色素材";
                MessageBox.Show($"温迪无法启动。{problem}「{error.AssetName}」。\n\n请重新运行安装程序，或将 ZIP 完整解压到新文件夹后再启动。",
                    "VENTI ALIVE · 启动提示", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return 2;
        }
        var control = new DesktopControlWindow(spritePath)
        {
            ShowActivated = !automation && !launchAtLogon,
            WindowState = launchAtLogon ? WindowState.Minimized : WindowState.Normal
        };
        Rect workArea = SystemParameters.WorkArea;
        const double edgeMargin = 16;
        control.Width = Math.Min(control.Width, workArea.Width - 2 * edgeMargin);
        control.MaxHeight = workArea.Height - 2 * edgeMargin;
        control.Left = Math.Max(workArea.Left + edgeMargin, left - control.Width - 20);
        control.Top = workArea.Top + edgeMargin;
        DialogueWindow? dialogue = null;
        Task previousDialogue = Task.CompletedTask;
        bool waitingForDialogue = false;
        string? dialogueDirectory = settings.DialogueModelDirectory;
        bool dialogueUseCpu = settings.DialogueUseCpu;
        var chatButton = control.ChatButton;
        ActiveDesktop? active = spritePath is not null && store is not null && (!automation || testActive)
            ? new ActiveDesktop(overlay, control, log, store.DataDirectory,
                () => dialogueDirectory ?? ReadPackagedModelDirectory(motionDirectory),
                () => dialogueUseCpu, BubbleSeconds, () => dialogue is not null || !chatButton.IsEnabled)
            : null;
        control.ActiveSection.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        control.BubbleDuration.SelectedIndex = settings.BubbleSeconds switch { 5 => 0, 10 => 2, _ => 1 };
        if (active is not null)
        {
            active.SpeechEnabled = settings.ProactiveSpeech;
            active.FlightEnabled = settings.AutonomousFlight;
            BindToggle(control.ProactiveSpeech, settings.ProactiveSpeech, value =>
            {
                active.SpeechEnabled = value;
                active.CheckAvailability();
            });
            BindToggle(control.AutonomousFlight, settings.AutonomousFlight, value =>
            {
                active.FlightEnabled = value;
                active.CheckAvailability();
            });
            control.PreviewMouseDown += (_, _) => ControlInteracted();
            control.PreviewKeyDown += (_, _) => ControlInteracted();
            control.Loaded += (_, _) => active.Start();
        }
        void ControlInteracted()
        {
            active?.Interact();
            overlay.StopScreenFlight("control");
        }

        int BubbleSeconds() => control.BubbleDuration.SelectedIndex switch { 0 => 5, 2 => 10, _ => 7 };

        var topmost = control.TopmostCharacter;
        var passThrough = control.PassThrough;
        var visible = control.CharacterVisible;
        var playing = control.Playing;
        bool supportsStartup = requirePreviewAssets && store is not null &&
            string.Equals(motionDirectory, WindowsStartup.Assets, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(spritePath, Path.Combine(WindowsStartup.Assets, "front.png"), StringComparison.OrdinalIgnoreCase);
        control.StartWithWindows.Visibility = supportsStartup ? Visibility.Visible : Visibility.Collapsed;
        if (supportsStartup) WindowsStartup.Bind(control, automation);
        var autonomous = overlay.CanNotice ? control.Autonomous : null;
        BindToggle(topmost, settings.Topmost, value => overlay.Topmost = value && dialogue is null);
        BindToggle(passThrough, settings.PassThrough, value =>
        {
            active?.Interact();
            overlay.SetPassThrough(value);
        });
        visible.IsChecked = settings.Visible;
        playing.IsChecked = settings.Playing;
        control.Autonomous.Visibility = overlay.CanNotice ? Visibility.Visible : Visibility.Collapsed;
        if (autonomous is not null)
        {
            autonomous.Content = overlay.CanPlayGestures ? "随机播放待机动作" : "允许自主回应";
            BindToggle(autonomous, settings.Autonomous, overlay.SetAutonomous);
        }
        void SyncPlayback()
        {
            bool shown = visible.IsChecked == true;
            if (shown != overlay.IsVisible)
            {
                overlay.SetVisible(shown);
            }
            bool animate = shown && playing.IsChecked == true;
            if (!animate) active?.Interact();
            overlay.SetPlaying(animate);
        }
        visible.Checked += (_, _) => SyncPlayback();
        visible.Unchecked += (_, _) => SyncPlayback();
        playing.Checked += (_, _) => SyncPlayback();
        playing.Unchecked += (_, _) => SyncPlayback();
        control.RecallButton.Click += (_, _) => ResetOverlay();
        control.ActionsSection.Visibility = overlay.CanNotice || overlay.CanPlayGestures ? Visibility.Visible : Visibility.Collapsed;
        control.LookButton.Visibility = overlay.CanNotice ? Visibility.Visible : Visibility.Collapsed;
        control.LookButton.Click += (_, _) =>
        {
            visible.IsChecked = true;
            playing.IsChecked = true;
            overlay.Notice();
        };
        BindGestureButton(control.LyreButton, Gesture.Lyre);
        BindGestureButton(control.HipsButton, Gesture.HandsOnHips);
        BindGestureButton(control.FoldedButton, Gesture.FoldedArms);
        BindGestureButton(control.DozeButton, Gesture.Doze);
        chatButton.Visibility = spritePath is not null && store is not null ? Visibility.Visible : Visibility.Collapsed;
        if (spritePath is not null && store is not null)
        {
            chatButton.Click += async (_, _) =>
            {
                if (dialogue is not null)
                {
                    dialogue.Activate();
                    return;
                }
                chatButton.IsEnabled = false;
                try
                {
                    overlay.StopScreenFlight("chat");
                    await Task.WhenAll(previousDialogue, active?.StopConversation() ?? Task.CompletedTask);
                    if (!control.IsLoaded || waitingForDialogue) return;
                    active?.Interact();
                    var window = new DialogueWindow(Path.Combine(store.DataDirectory, "dialogue-memory.json"),
                        dialogueDirectory ?? ReadPackagedModelDirectory(motionDirectory), dialogueUseCpu, (folder, cpu) =>
                        {
                            dialogueDirectory = folder;
                            dialogueUseCpu = cpu;
                        }, log) { Owner = control };
                    dialogue = window;
                    overlay.Topmost = false;
                    window.Closed += (_, _) =>
                    {
                        previousDialogue = window.ModelStopped;
                        dialogue = null;
                        overlay.Topmost = topmost.IsChecked == true;
                        active?.Interact();
                    };
                    window.Show();
                }
                finally
                {
                    if (control.IsLoaded) chatButton.IsEnabled = true;
                }
            };
        }

        void BindGestureButton(Button button, Gesture gesture)
        {
            bool available = gesture == Gesture.Doze ? overlay.CanDoze : overlay.CanPlayGestures;
            button.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            button.Click += (_, _) =>
            {
                visible.IsChecked = true;
                playing.IsChecked = true;
                overlay.PlayGesture(gesture);
            };
        }
        control.ExitButton.Click += (_, _) => control.Close();

        void ResetOverlay()
        {
            active?.Interact();
            overlay.RestorePosition(centerOnPrimary: true);
        }

        control.SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(control);
            if (automation)
            {
                // The harness enables this process-local test interface explicitly.
                source.AddHook((nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
                {
                    bool value = wParam != 0;
                    switch (msg)
                    {
                        case 0x8011: passThrough.IsChecked = value; break;
                        case 0x8012: visible.IsChecked = value; break;
                        case 0x8013: playing.IsChecked = value; break;
                        case 0x8014: ResetOverlay(); break;
                        case 0x8015: topmost.IsChecked = value; break;
                        case 0x8016: overlay.Notice(); break;
                        case 0x8017 when autonomous is not null: autonomous.IsChecked = value; break;
                        case 0x8018: overlay.PlayGesture((Gesture)(int)wParam); break;
                        case 0x8019 when active is not null: active.MakeDue(forSpeech: true); break;
                        case 0x801A when active is not null: active.MakeDue(forSpeech: false); break;
                        default: return 0;
                    }
                    handled = true;
                    return 1;
                });
            }
            log.Write("control", new { hwnd = source.Handle.ToInt64() });
        };
        control.Closing += async (_, args) =>
        {
            if (waitingForDialogue)
            {
                args.Cancel = true;
                return;
            }
            dialogue?.Close();
            Task pending = Task.WhenAll(previousDialogue, active?.Stop() ?? Task.CompletedTask);
            if (!pending.IsCompleted)
            {
                args.Cancel = true;
                waitingForDialogue = true;
                await pending;
                waitingForDialogue = false;
                control.Close();
                return;
            }
            if (store is null)
            {
                return;
            }

            bool haveBounds = Native.GetWindowRect(overlay.Handle, out var bounds);
            try
            {
                store.Save(new DesktopPreferences
                {
                    Topmost = topmost.IsChecked == true,
                    PassThrough = passThrough.IsChecked == true,
                    Visible = visible.IsChecked == true,
                    Playing = playing.IsChecked == true,
                    Autonomous = autonomous?.IsChecked == true,
                    ProactiveSpeech = active?.SpeechEnabled ?? settings.ProactiveSpeech,
                    AutonomousFlight = active?.FlightEnabled ?? settings.AutonomousFlight,
                    BubbleSeconds = BubbleSeconds(),
                    Left = haveBounds ? bounds.Left : null,
                    Top = haveBounds ? bounds.Top : null,
                    DialogueModelDirectory = dialogueDirectory,
                    DialogueUseCpu = dialogueUseCpu
                });
                log.Write("settingsSaved");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Write("settingsSaveFailed", new { ex.Message });
                if (!automation)
                {
                    MessageBox.Show(control, "无法保存桌面设置。本次退出后可能需要重新设置。",
                        "VENTI ALIVE", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        };
        control.Closed += (_, _) => overlay.Close();
        app.MainWindow = control;
        if (store is not null)
        {
            new WindowInteropHelper(overlay).EnsureHandle();
            overlay.RestorePosition(settings.Left, settings.Top);
            overlay.SetPassThrough(settings.PassThrough);
        }
        overlay.SetAutonomous(autonomous?.IsChecked == true);
        SyncPlayback();
        return app.Run(control);
    }

    private static string? ReadPackagedModelDirectory(string? motionDirectory)
    {
        if (motionDirectory is null) return null;
        try
        {
            string path = File.ReadAllText(Path.Combine(motionDirectory, "local-model-directory.txt")).Trim();
            return Path.IsPathFullyQualified(path) ? path : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void BindToggle(CheckBox toggle, bool initial, Action<bool> change)
    {
        toggle.IsChecked = initial;
        toggle.Checked += (_, _) => change(true);
        toggle.Unchecked += (_, _) => change(false);
    }

    private static Window CreateTarget(double left, double top, ProbeLog log)
    {
        var surface = new Grid();
        surface.ColumnDefinitions.Add(new ColumnDefinition());
        surface.ColumnDefinitions.Add(new ColumnDefinition());
        var light = new Border { Background = new SolidColorBrush(Color.FromRgb(232, 236, 235)) };
        var dark = new Border { Background = new SolidColorBrush(Color.FromRgb(30, 34, 40)) };
        Grid.SetColumn(dark, 1);
        surface.Children.Add(light);
        surface.Children.Add(dark);
        var target = new Window
        {
            Title = "Aeris M1 Underlay", Width = 520, Height = 420,
            Left = left - 100, Top = top - 55, Content = surface,
            ResizeMode = ResizeMode.NoResize
        };
        target.SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(target);
            source.AddHook((nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
            {
                if (msg == Native.LeftButtonDown)
                {
                    Native.Point cursor = Native.ScreenPosition(hwnd, lParam);
                    log.Write("targetDown", new { cursor.X, cursor.Y });
                }
                return 0;
            });
            log.Write("target", new { hwnd = source.Handle.ToInt64() });
        };
        return target;
    }
}
