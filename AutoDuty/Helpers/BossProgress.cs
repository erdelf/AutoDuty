using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AutoDuty.Helpers;

// Per-duty evidence, independent of task queues and route list/object identity.
internal sealed class BossProgress
{
    internal readonly record struct Key(uint Territory, string Note, Vector3 Position);
    private readonly HashSet<Key> defeated = [];

    internal bool IsDefeated(Key key) => this.defeated.Contains(key);
    internal bool Observe(Key key, bool liveBoss, bool deadBoss, bool objectiveComplete)
    {
        // A respawn/new phase wins over a corpse or stale local evidence.
        if (liveBoss) { this.defeated.Remove(key); return false; }
        return (deadBoss || objectiveComplete) && this.defeated.Add(key);
    }
    internal void Reset() => this.defeated.Clear();

    internal static bool ObjectiveComplete(string text, int current, int needed, IEnumerable<string> names) =>
        needed > 0 && current >= needed && names.Any(name => ObjectiveMatches(text, name));

    internal static bool ObjectiveMatches(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        int start = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        // Avoid matching e.g. 'King' in 'Kingdom'; CJK objectives do not use spaces.
        static bool Word(char c) => c < 128 && char.IsLetterOrDigit(c);
        return !(Word(name[0]) && start > 0 && Word(text[start - 1]))
            && !(Word(name[^1]) && start + name.Length < text.Length && Word(text[start + name.Length]));
    }
}
