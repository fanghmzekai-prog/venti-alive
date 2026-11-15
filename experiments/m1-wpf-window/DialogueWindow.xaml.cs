using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Venti.LocalDialogue;

namespace Aeris.WindowProbe;

internal partial class DialogueWindow : Window
{
    private readonly MemoryStore memory;
    private readonly Action<string?, bool> savePreferences;
    private readonly ProbeLog log;
    private string? modelDirectory;
    private bool useCpu;
    private bool loaded;
    private bool closed;
    private DialogueSession? session;
    private CancellationTokenSource? modelCancellation;
    private CancellationTokenSource? replyCancellation;
    private Task modelLifetime = Task.CompletedTask;

    internal DialogueWindow(string memoryPath, string? directory, bool cpu, Action<string?, bool> savePreferences, ProbeLog log)
    {
        memory = new MemoryStore(memoryPath);
        modelDirectory = directory;
        useCpu = cpu;
        this.savePreferences = savePreferences;
        this.log = log;
        InitializeComponent();
        Backend.SelectedIndex = useCpu ? 1 : 0;
        loaded = true;
        UpdateControls();
        Status.Text = string.IsNullOrWhiteSpace(modelDirectory)
            ? "选择包含本地模型与推理程序的文件夹后启动聊天。"
            : "正在准备本地模型。";
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(modelDirectory) && modelCancellation is null && !closed)
            {
                StartModel();
            }
        };
    }

    internal Task ModelStopped => modelLifetime;

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        modelCancellation?.Cancel();
        base.OnClosed(e);
    }

    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "选择离线模型文件夹" };
        if (Directory.Exists(modelDirectory))
        {
            picker.InitialDirectory = modelDirectory;
        }
        if (picker.ShowDialog(this) == true)
        {
            modelDirectory = picker.FolderName;
            savePreferences(modelDirectory, useCpu);
            Status.Text = "模型文件夹已选定。";
            UpdateControls();
        }
    }

    private void ChangeBackend(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;
        useCpu = Backend.SelectedIndex == 1;
        savePreferences(modelDirectory, useCpu);
    }

    private void ToggleModel(object sender, RoutedEventArgs e)
    {
        if (modelCancellation is { } active)
        {
            Status.Text = "正在关闭本地模型…";
            active.Cancel();
            UpdateControls();
        }
        else
        {
            StartModel();
        }
    }

    private void StartModel()
    {
        if (string.IsNullOrWhiteSpace(modelDirectory) || modelCancellation is not null || closed) return;
        string directory = modelDirectory;
        string backend = useCpu ? "cpu" : "vulkan";
        string server = Path.Combine(directory, useCpu ? "llama.cpp" : "vulkan-b12386", "llama-server.exe");
        string model = Path.Combine(directory, "qwen3-4b-instruct-2507-q8_0.gguf");
        var cancellation = new CancellationTokenSource();
        modelCancellation = cancellation;
        ClearConversation();
        Status.Text = "正在加载本地模型…";
        UpdateControls();
        modelLifetime = RunModel(server, model, backend, cancellation);
    }

    private async Task RunModel(string server, string model, string backend, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        string status = "模型已关闭。";
        try
        {
            await Task.Run(() => LocalModelPreview.RunWithModel(backend, server, model, UseModel, token,
                processStarted: pid => Dispatcher.BeginInvoke((Action)(() => log.Write("dialogueProcess", new { pid })))));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            TimeoutException or InvalidOperationException or HttpRequestException)
        {
            status = error.Message;
        }
        finally
        {
            session = null;
            modelCancellation = null;
            cancellation.Cancel();
            cancellation.Dispose();
            if (!closed)
            {
                Status.Text = status;
                UpdateControls();
            }
        }
    }

    private async Task UseModel(DialogueClient client, Task processExit, CancellationToken token)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            session = new DialogueSession(client, memory);
            session.SetMemory(MemoryToggle.IsChecked == true);
            Status.Text = "本地模型已就绪。";
            log.Write("dialogueReady");
            UpdateControls();
            Prompt.Focus();
        }, DispatcherPriority.Normal, token);
        await processExit.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        throw new IOException("本地模型进程意外退出。可点击「启动模型」重试。");
    }

    private void SendOrCancel(object sender, RoutedEventArgs e)
    {
        if (replyCancellation is { } request) request.Cancel();
        else _ = Send();
    }

    private void PromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        _ = Send();
    }

    private async Task Send()
    {
        if (session is not { } dialog || modelCancellation is not { IsCancellationRequested: false } active ||
            replyCancellation is not null || string.IsNullOrWhiteSpace(Prompt.Text)) return;
        string input = Prompt.Text.Trim();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(active.Token);
        replyCancellation = request;
        Status.Text = "温迪正在回复…";
        UpdateControls();
        try
        {
            string response = await dialog.Reply(input, request.Token);
            request.Token.ThrowIfCancellationRequested();
            Transcript.Items.Add(new DialogueLine("你", input));
            var line = new DialogueLine("温迪", response);
            Transcript.Items.Add(line);
            Transcript.ScrollIntoView(line);
            Prompt.Clear();
            Status.Text = "本地模型已就绪。";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (!closed && !active.IsCancellationRequested) Status.Text = "已停止回复。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or TimeoutException or InvalidOperationException or HttpRequestException or JsonException or
            OperationCanceledException)
        {
            if (!closed && !request.IsCancellationRequested)
            {
                Status.Text = error is HttpRequestException or OperationCanceledException
                    ? "本地模型暂时不可用，可以停止模型后重新启动。"
                    : error.Message;
            }
        }
        finally
        {
            replyCancellation = null;
            if (!closed)
            {
                UpdateControls();
                if (Prompt.IsEnabled) Prompt.Focus();
            }
        }
    }

    private void ToggleMemory(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;
        session?.SetMemory(MemoryToggle.IsChecked == true);
        ClearConversation();
        Status.Text = MemoryToggle.IsChecked == true ? "已启用记忆读取，当前对话已清空。" : "已停用记忆读取，当前对话已清空。";
    }

    private void ManageMemory(object sender, RoutedEventArgs e)
    {
        var window = new DialogueMemoryWindow(memory) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            ClearConversation();
            Status.Text = "记忆已更新，当前对话已清空。";
        }
    }

    private void ClearChat(object sender, RoutedEventArgs e)
    {
        ClearConversation();
        Status.Text = "当前对话已清空。";
    }

    private void ClearConversation()
    {
        session?.Clear();
        Transcript.Items.Clear();
    }

    private void UpdateControls()
    {
        bool running = modelCancellation is not null;
        bool ready = session is not null && modelCancellation is { IsCancellationRequested: false };
        bool replying = replyCancellation is not null;
        FolderLabel.Text = modelDirectory ?? string.Empty;
        FolderLabel.ToolTip = modelDirectory;
        FolderButton.IsEnabled = Backend.IsEnabled = !running;
        ModelButton.Content = running ? "停止模型" : "启动模型";
        ModelButton.IsEnabled = running
            ? !modelCancellation!.IsCancellationRequested
            : !replying && !string.IsNullOrWhiteSpace(modelDirectory);
        Prompt.IsEnabled = ready && !replying;
        SendButton.IsEnabled = ready;
        SendButton.Content = replying ? "停止回复" : "发送";
        MemoryToggle.IsEnabled = MemoryButton.IsEnabled = ClearButton.IsEnabled = !replying && (!running || ready);
    }

    private sealed record DialogueLine(string Speaker, string Text);
}
