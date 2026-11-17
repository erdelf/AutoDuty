using System;

namespace AutoDuty.Helpers;

internal static class BossIdentity
{
    // A route note is only an identity hint when the entire NPC name matches.
    // Do not turn any current combat target (e.g. a summoned add) into a boss.
    internal static bool Matches(string hint, string localName, string englishName) => !string.IsNullOrWhiteSpace(hint)
        && (string.Equals(hint.Trim(), localName.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(hint.Trim(), englishName.Trim(), StringComparison.OrdinalIgnoreCase));
}
