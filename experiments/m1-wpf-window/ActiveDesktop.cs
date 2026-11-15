using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Venti.LocalDialogue;

namespace Aeris.WindowProbe;

internal sealed class ActiveDesktop
{
    private const int FirstSpeechSeconds = 100;
    private const int SpeechPreparationSeconds = 13;
    private const int FlightIntervalSeconds = 15;

    private static readonly string[] Topics =
    [
        "问对方喜欢哪种乐器", "聊到苹果时抛一个轻松的小问题", "问对方现在想听什么样的曲子",
        "邀请对方给一阵风取个名字", "问对方喜欢清晨还是傍晚", "问对方想听一个怎样的短故事",
        "用轻快的语气发出一个简单问候", "请对方从微风和海风中选一个"
    ];

    private readonly Overlay character;
    private readonly Window owner;
    private readonly ProbeLog log;
    private readonly MemoryStore memory;
    private readonly Func<string?> modelDirectory;
    private readonly Func<bool> useCpu;
    private readonly Func<int> bubbleSeconds;
    private readonly Func<bool> manualChatOpen;
    private readonly DispatcherTimer timer;
    private long speechDue;
    private long flightDue;
    private int lastTopic = -1;
    private Task speech = Task.CompletedTask;
    private CancellationTokenSource? stopSpeech;
    private SpeechBubble? bubble;

    internal bool SpeechEnabled { get; set; }
    internal bool FlightEnabled { get; set; }

    internal ActiveDesktop(Overlay character, Window owner, ProbeLog log, string dataDirectory,
        Func<string?> modelDirectory, Func<bool> useCpu, Func<int> bubbleSeconds, Func<bool> manualChatOpen)
    {
        this.character = character;
        this.owner = owner;
        this.log = log;
        this.modelDirectory = modelDirectory;
        this.useCpu = useCpu;
        this.bubbleSeconds = bubbleSeconds;
        this.manualChatOpen = manualChatOpen;
        memory = new MemoryStore(Path.Combine(dataDirectory, "dialogue-memory.json"));
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Tick();
        character.UserInteracted += Interact;
    }

    internal void Start()
    {
        Postpone();
        timer.Start();
    }

    internal void Interact()
    {
        Postpone();
        _ = StopConversation();
    }

    internal void CheckAvailability()
    {
        if (!SpeechEnabled) _ = StopConversation();
        if (!FlightEnabled) character.StopScreenFlight("disabled");
    }

    internal Task StopConversation()
    {
        stopSpeech?.Cancel();
        bubble?.Close();
        return speech;
    }

    internal Task Stop()
    {
        timer.Stop();
        character.UserInteracted -= Interact;
        character.StopScreenFlight("close");
        return StopConversation();
    }

    internal void MakeDue(bool forSpeech)
    {
        if (forSpeech) speechDue = Stopwatch.GetTimestamp();
        else if (character.CanStartIndependentAction) flightDue = Stopwatch.GetTimestamp();
        Tick();
    }

    private void Postpone()
    {
        long now = Stopwatch.GetTimestamp();
        speechDue = now + FirstSpeechSeconds * Stopwatch.Frequency;
        flightDue = now + FlightIntervalSeconds * Stopwatch.Frequency;
        log.Write("activeIdleReset");
    }

    private static long AfterSeconds(int seconds) => Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency;

    private void Tick()
    {
        if (!character.CanStartIndependentAction || !speech.IsCompleted || manualChatOpen()) return;
        long now = Stopwatch.GetTimestamp();
        long speechPreparationDue = speechDue - SpeechPreparationSeconds * Stopwatch.Frequency;
        if (FlightEnabled && now >= flightDue && (!SpeechEnabled || flightDue <= speechPreparationDue))
        {
            character.StartScreenFlight();
            flightDue = AfterSeconds(FlightIntervalSeconds);
        }
        else if (SpeechEnabled && now >= speechPreparationDue)
        {
            long displayAt = speechDue;
            speechDue = AfterSeconds(Random.Shared.Next(150, 261));
            string? folder = modelDirectory();
            if (string.IsNullOrWhiteSpace(folder)) return;
            stopSpeech = new CancellationTokenSource();
            speech = RunSpeech(folder, useCpu(), displayAt, stopSpeech);
        }
    }

    private async Task RunSpeech(string directory, bool cpu, long displayAt, CancellationTokenSource cancellation)
    {
        int topic = Random.Shared.Next(Topics.Length);
        if (topic == lastTopic) topic = (topic + 1) % Topics.Length;
        lastTopic = topic;
        string prompt = $"你要主动向用户开启一小段闲聊。{Topics[topic]}。只输出你说的一句简体中文台词，"
            + "最多 32 个字，直接说话，不加引号。你不知道用户现在正在做什么，也看不到屏幕。";
        CancellationToken token = cancellation.Token;
        string server = Path.Combine(directory, cpu ? "llama.cpp" : "vulkan-b12386", "llama-server.exe");
        string model = Path.Combine(directory, "qwen3-4b-instruct-2507-q8_0.gguf");
        log.Write("bubbleModelRequested");
        try
        {
            await Task.Run(() => LocalModelPreview.RunWithModel(cpu ? "cpu" : "vulkan", server, model,
                async (client, exit, lifetime) =>
                {
                    string opening = ShortOpening(await client.Reply(prompt, [], [], lifetime));
                    TimeSpan remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), displayAt);
                    if (remaining > TimeSpan.Zero)
                    {
                        Task display = Task.Delay(remaining, lifetime);
                        if (await Task.WhenAny(display, exit) == exit)
                        {
                            throw new IOException("本地推理进程提前退出。");
                        }
                        await display;
                    }
                    await owner.Dispatcher.InvokeAsync(() => ShowBubble(client, opening, lifetime),
                        DispatcherPriority.Normal, lifetime);
                    await exit.WaitAsync(lifetime);
                    lifetime.ThrowIfCancellationRequested();
                    throw new IOException("本地推理进程提前退出。");
                }, token, processStarted: pid => owner.Dispatcher.BeginInvoke((Action)(
                    () => log.Write("bubbleModelProcess", new { pid })))));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            TimeoutException or InvalidOperationException or HttpRequestException or JsonException or InvalidDataException)
        {
            log.Write("bubbleModelUnavailable", new { reason = error.GetType().Name });
        }
        finally
        {
            stopSpeech = null;
            bubble?.Close();
            cancellation.Dispose();
            log.Write("bubbleModelStopped");
        }
    }

    private void ShowBubble(DialogueClient client, string opening, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var session = new DialogueSession(client, memory, opening);
        var window = new SpeechBubble(character, bubbleSeconds, (text, remember) =>
        {
            Postpone();
            session.SetMemory(remember);
            return session.Reply(text, token);
        }, action =>
        {
            log.Write("bubbleInteraction", new { action });
            if (action != "expired") Postpone();
        });
        bubble = window;
        character.BubbleVisible = true;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(bubble, window)) bubble = null;
            character.BubbleVisible = false;
            stopSpeech?.Cancel();
            log.Write("bubbleClosed");
        };
        window.Present(opening);
        log.Write("bubbleShown", new { seconds = bubbleSeconds() });
    }

    private static string ShortOpening(string value)
    {
        string line = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?.Trim('“', '”', '"', '「', '」', ' ') ?? "";
        if (line.Length == 0) throw new InvalidDataException("本地模型没有生成弹幕。");
        var text = new StringInfo(line);
        return text.LengthInTextElements <= 46 ? line : text.SubstringByTextElements(0, 45) + "…";
    }
}
