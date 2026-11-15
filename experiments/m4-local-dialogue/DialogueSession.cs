namespace Venti.LocalDialogue;

internal sealed class DialogueSession(DialogueClient client, MemoryStore store, string? opening = null)
{
    private const int MaxTurns = 6;
    private const int MaxHistoryCharacters = 1800;
    private readonly List<DialogueTurn> history = [];
    private MemoryEntry[] previousMemories = [];

    internal bool MemoryEnabled { get; private set; }

    internal void SetMemory(bool enabled)
    {
        if (MemoryEnabled == enabled)
        {
            return;
        }
        // A reply can repeat remembered information. Clear old turns when changing consent.
        Clear();
        MemoryEnabled = enabled;
    }

    internal void Clear()
    {
        history.Clear();
        previousMemories = [];
    }

    internal async Task<string> Reply(string text, CancellationToken cancellation)
    {
        MemoryEntry[] memories = MemoryEnabled ? store.Read() : [];
        if (!memories.SequenceEqual(previousMemories))
        {
            // Avoid feeding edited or deleted facts back to the model through old replies.
            history.Clear();
            previousMemories = memories;
        }
        int skippedTurns = 0;
        string reply;
        while (true)
        {
            try
            {
                reply = await client.Reply(text, memories, history.Skip(skippedTurns), cancellation, opening);
                break;
            }
            catch (DialogueCapacityException) when (skippedTurns < history.Count)
            {
                skippedTurns++;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        history.RemoveRange(0, skippedTurns);
        history.Add(new(text, reply));
        int characters = history.Sum(turn => turn.User.Length + turn.Assistant.Length);
        while (history.Count > MaxTurns || characters > MaxHistoryCharacters)
        {
            characters -= history[0].User.Length + history[0].Assistant.Length;
            history.RemoveAt(0);
        }
        return reply;
    }
}

internal sealed record DialogueTurn(string User, string Assistant);
