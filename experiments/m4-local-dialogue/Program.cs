using System.Text;
using System.Text.Json;
using Venti.LocalDialogue;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
if (args.Length < 2)
{
    Console.Error.WriteLine("用法：Venti.LocalDialogue <记忆文件> list|add|edit|delete|export|chat|session|preview [参数]");
    return 2;
}

var store = new MemoryStore(Path.GetFullPath(args[0]));
try
{
    switch (args[1])
    {
        case "list" when args.Length == 2:
            foreach (MemoryEntry entry in store.Read())
            {
                Console.WriteLine($"{entry.Id} {entry.Text}");
            }
            break;
        case "add" when args.Length == 3:
            Console.WriteLine(store.Add(args[2]));
            break;
        case "edit" when args.Length == 4:
            store.Update(Guid.Parse(args[2]), args[3]);
            break;
        case "delete" when args.Length == 3:
            if (!store.Delete(Guid.Parse(args[2])))
            {
                throw new ArgumentException("找不到该记忆。");
            }
            break;
        case "export" when args.Length == 3:
            store.Export(Path.GetFullPath(args[2]));
            break;
        case "chat" when args.Length is 4 or 5:
            if (args.Length == 5 && args[4] != "--remember")
            {
                throw new ArgumentException("无效的聊天选项。");
            }
            using (var client = new DialogueClient(int.Parse(args[2])))
            {
                MemoryEntry[] memory = args.Length == 5 ? store.Read() : [];
                Console.WriteLine(await client.Reply(args[3], memory, [], CancellationToken.None));
            }
            break;
        case "session" when args.Length == 3:
            using (var client = new DialogueClient(int.Parse(args[2])))
            {
                return await RunConsole(token => DialogueConsole.Run(new DialogueSession(client, store), token));
            }
        case "preview" when args.Length == 5:
            return await RunConsole(token => LocalModelPreview.RunWithModel(args[2], args[3], args[4],
                (client, _, sessionToken) => DialogueConsole.Run(new DialogueSession(client, store), sessionToken),
                token, () => Console.WriteLine("正在加载温迪的本地对话模型…")));
        default:
            throw new ArgumentException("命令或参数无效。");
    }
    return 0;
}

catch (TimeoutException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}
catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine("本地对话服务暂不可用。请检查是否已经启动，或稍后重试。");
    return 3;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
    ArgumentException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static async Task<int> RunConsole(Func<CancellationToken, Task> run)
{
    using var stop = new CancellationTokenSource();
    ConsoleCancelEventHandler handler = (_, e) =>
    {
        e.Cancel = true;
        stop.Cancel();
    };
    Console.CancelKeyPress += handler;
    try
    {
        await run(stop.Token);
        return 0;
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
        return 130;
    }
    finally
    {
        Console.CancelKeyPress -= handler;
    }
}
