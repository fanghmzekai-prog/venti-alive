using System.IO;
using System.Text.Json;

namespace Venti.LocalDialogue;

internal sealed record MemoryEntry(Guid Id, string Text, DateTimeOffset UpdatedAt);

internal sealed class MemoryStore(string path)
{
    internal const int MaxEntries = 20;
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    internal MemoryEntry[] Read()
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var entries = JsonSerializer.Deserialize<MemoryEntry[]>(input) ?? [];
            if (entries.Length > MaxEntries || entries.Any(entry => entry is null || entry.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(entry.Text) || entry.Text.Length > 200) ||
                entries.Select(entry => entry.Id).Distinct().Count() != entries.Length)
            {
                throw new InvalidDataException("记忆文件格式有误。");
            }
            return entries;
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    internal Guid Add(string text) => Put(null, text);

    internal void Update(Guid id, string text) => Put(id, text);

    private Guid Put(Guid? id, string text)
    {
        text = text.Trim();
        if (text.Length is 0 or > 200)
        {
            throw new ArgumentException("每条记忆为 1 至 200 字符。");
        }
        using var gate = Lock();
        var items = Read().ToList();
        if (id is Guid existing)
        {
            int index = items.FindIndex(item => item.Id == existing);
            if (index < 0)
            {
                throw new ArgumentException("找不到该记忆。");
            }
            items[index] = new(existing, text, DateTimeOffset.UtcNow);
            Write(path, items);
            return existing;
        }

        if (items.Count >= MaxEntries)
        {
            throw new InvalidOperationException("最多保留 20 条记忆。");
        }
        Guid newId = Guid.NewGuid();
        items.Add(new(newId, text, DateTimeOffset.UtcNow));
        Write(path, items);
        return newId;
    }

    internal bool Delete(Guid id)
    {
        using var gate = Lock();
        var items = Read().ToList();
        if (items.RemoveAll(item => item.Id == id) == 0)
        {
            return false;
        }
        Write(path, items);
        return true;
    }

    internal void Export(string destination) => Write(destination, Read(), overwrite: false);

    private FileStream Lock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string lockPath = path + ".lock";
        return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void Write(string destination, IReadOnlyList<MemoryEntry> items, bool overwrite = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(output, items, Format);
                output.Flush(flushToDisk: true);
            }
            File.Move(temp, destination, overwrite);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
