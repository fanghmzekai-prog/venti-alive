using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Aeris.WindowProbe;

internal sealed class ProbeLog(string? path)
{
    private readonly Stopwatch clock = Stopwatch.StartNew();

    internal bool Enabled => path is not null;

    internal void Write(string kind, object? values = null)
    {
        if (path is null)
        {
            return;
        }

        var entry = new { kind, elapsedMs = clock.Elapsed.TotalMilliseconds, values };
        File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
    }
}
