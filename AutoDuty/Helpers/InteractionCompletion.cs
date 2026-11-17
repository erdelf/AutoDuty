using System;

namespace AutoDuty.Helpers;

internal static class InteractionCompletion
{
    // Persistent NPCs do not become untargetable after handing over a key.
    // Scope evidence to the exact duty and NPC; other interactions still need
    // their own dialogue/object completion rather than guessing from one click.
    internal static bool HasReward(uint territory, uint objectId, Func<uint, int> itemCount)
        => territory == 1041 && objectId == 1004346 && itemCount(2000521) > 0;
}

internal sealed class InteractionDialogueProgress
{
    private bool seen;

    internal bool Observe(bool visible, bool casting)
    {
        if (visible) this.seen = true;
        return this.seen && !visible && !casting;
    }
}
