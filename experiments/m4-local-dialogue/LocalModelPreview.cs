using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Venti.LocalDialogue;

internal static class LocalModelPreview
{
    internal static async Task RunWithModel(string backend, string serverPath, string modelPath,
        Func<DialogueClient, Task, CancellationToken, Task> useModel, CancellationToken cancellation,
        Action? loading = null, Action<int>? processStarted = null)
    {
        bool accelerated = backend switch
        {
            "vulkan" => true,
            "cpu" => false,
            _ => throw new ArgumentException("本地对话方式应为 vulkan 或 cpu。")
        };
        string executable = Path.GetFullPath(serverPath);
        string model = Path.GetFullPath(modelPath);
        if (!File.Exists(executable) || !File.Exists(model))
        {
            throw new FileNotFoundException("缺少本地推理程序或模型文件。请核对模型文件夹和推理方式。");
        }

        int port = GetLocalPort();
        string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var command = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        string[] options = ["-m", model, "--host", "127.0.0.1", "--port", port.ToString(),
            "-t", "4", "-c", accelerated ? "12288" : "4096", "-np", "1", "-ngl", accelerated ? "16" : "0",
            "--reasoning", "off", "--no-webui", "--cors-origins", "localhost", "--no-cors-credentials", "--log-disable"];
        foreach (string option in options)
        {
            command.ArgumentList.Add(option);
        }
        // The preview owns its server configuration and uses a new key for every run.
        foreach (string name in command.Environment.Keys.Where(name => name.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            command.Environment.Remove(name);
        }
        command.Environment["LLAMA_API_KEY"] = key;

        using var job = ModelProcessJob.Create();
        cancellation.ThrowIfCancellationRequested();
        loading?.Invoke();
        using var process = new Process { StartInfo = command };
        try
        {
            if (!process.Start())
            {
                throw new IOException("无法启动本地推理程序。");
            }
        }
        catch (Win32Exception error)
        {
            throw new IOException("无法启动本地推理程序。请检查本地推理程序的文件是否完整。", error);
        }
        bool assigned = false;
        try
        {
            ModelProcessJob.Assign(job, process.SafeHandle);
            assigned = true;
            processStarted?.Invoke(process.Id);
            await WaitUntilReady(process, port, cancellation);
            using var client = new DialogueClient(port, key);
            await useModel(client, process.WaitForExitAsync(), cancellation);
        }
        finally
        {
            job.Dispose();
            if (!assigned && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            if (!process.WaitForExit(5000))
            {
                throw new IOException("未能确认本地模型进程已退出。");
            }
        }
    }

    private static int GetLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitUntilReady(Process process, int port, CancellationToken cancellation)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(1)
        };
        var health = new Uri($"http://{IPAddress.Loopback}:{port}/health");
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(60))
        {
            cancellation.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new IOException("本地模型启动失败。请检查模型文件和可用内存，或改用 CPU 启动。");
            }
            try
            {
                using HttpResponseMessage response = await http.GetAsync(health, cancellation);
                if (response.IsSuccessStatusCode && !process.HasExited)
                {
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
            await Task.Delay(200, cancellation);
        }
        throw new TimeoutException("本地模型没有在 60 秒内加载完成。");
    }
}
