using System.Text.Json;

namespace Venti.LocalDialogue;

internal static class DialogueConsole
{
    internal static async Task Run(DialogueSession session, CancellationToken cancellation)
    {
        Console.WriteLine("输入文字开始对话。/clear 清空会话，/memory on 启用记忆，/memory off 停用记忆，/quit 结束。");
        while (await Console.In.ReadLineAsync(cancellation) is { } line)
        {
            switch (line.Trim())
            {
                case "":
                    continue;
                case "/quit":
                    return;
                case "/clear":
                    session.Clear();
                    Console.WriteLine("已清空本次会话。");
                    continue;
                case "/memory on":
                    session.SetMemory(true);
                    Console.WriteLine("记忆已启用。");
                    continue;
                case "/memory off":
                    session.SetMemory(false);
                    Console.WriteLine("记忆已停用。");
                    continue;
            }

            try
            {
                Console.WriteLine(await session.Reply(line, cancellation));
            }
            catch (TimeoutException ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !cancellation.IsCancellationRequested)
            {
                Console.Error.WriteLine("本地对话服务暂不可用。本次输入未加入会话，可重试。");
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or
                JsonException or DialogueCapacityException)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }
    }
}
