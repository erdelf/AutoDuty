using AutoDuty.Logging;
namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ECommons;
    using ECommons.DalamudServices;
    using FFXIVClientStructs.FFXIV.Client.Game;
    using FFXIVClientStructs.FFXIV.Client.Game.UI;
    using Lumina.Excel;
    using Lumina.Excel.Sheets;

    internal static class BLUHelper
    {
        internal record BLUSpell(uint ID, byte Entry, string Name, uint Unlock, uint ActionId);


        internal static readonly Dictionary<uint, BLUSpell> spellsById     = [];
        internal static readonly Dictionary<byte, BLUSpell> spellsByEntry = [];
        internal static readonly List<BLUSpell>             spells        = [];

        internal static readonly ExcelSheet<AozAction>          AozActions;
        internal static readonly ExcelSheet<AozActionTransient> AozActionsData;

        static BLUHelper()
        {
            AozActions     = Svc.Data.GetExcelSheet<AozAction>();
            AozActionsData = Svc.Data.GetExcelSheet<AozActionTransient>();

            foreach (AozAction aozAction in AozActions)
                if (aozAction.Rank != 0)
                {
                    AozActionTransient aozActionTransient = AozActionsData.GetRow(aozAction.RowId);
                    if (aozActionTransient.Number != 0)
                    {
                        BLUSpell spell = new(aozAction.RowId, aozActionTransient.Number, aozAction.Action.Value.Name.GetText() ?? "No name", aozAction.Action.Value.UnlockLink.RowId, aozAction.Action.Value.RowId);
                        spellsById[spell.ID] = spell;
                        spellsByEntry[spell.Entry] = spell;
                        spells.Add(spell);
                    }
                }

            spells = spells.OrderBy(sp => sp.Entry).ToList();
        }

        internal static bool SpellUnlocked(uint id) =>
            spells.FirstOrDefault(spell => spell.ID == id) is { } bs && SpellUnlocked(bs);
        internal static bool SpellUnlocked(BLUSpell spell) =>
            SpellUnlockedInternal(spell.Unlock);
        private static unsafe bool SpellUnlockedInternal(uint unlockLink) => 
            UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink);

        public static uint AozToNormal(uint id) => 
            id == 0 ? 0 : AozActions.GetRow(id).Action.RowId;

        public static uint NormalToAoz(uint id)
        {
            AozAction? res = AozActions.FirstOrNull(aoz => aoz.Action.RowId == id);
            return res?.RowId ?? AozActions.First().Action.RowId;
        }

        private static unsafe List<BLUSpell?> GetCurrentBluSpells()
        {
            List<BLUSpell?> spellList = [];

            Span<uint> blu = ActionManager.Instance()->BlueMageActions;
            foreach (uint u in blu)
                spellList.Add(u == 0 ? null : spellsById[NormalToAoz(u)]);
            return spellList;
        }

        public static unsafe void SpellLoadoutOut(byte entry)
        {
            DebugLog($"Trying to remove {entry}");

            List<BLUSpell?> bluSpells = GetCurrentBluSpells();
            int             index     = bluSpells.FindIndex(sp => sp?.Entry == entry);

            DebugLog($"Found at {index}");

            if (index != -1)
                ActionManager.Instance()->AssignBlueMageActionToSlot(index, 0);
        }

        public static unsafe void SpellLoadoutIn(byte entry)
        {
            DebugLog($"Trying to slot in {entry}");
            List<BLUSpell?> bluSpells = GetCurrentBluSpells();

            if(bluSpells.Any(sp => sp?.Entry == entry))
            {
                DebugLog($"Spell {entry} is already slotted in, skipping");
                return;
            }

            int index = bluSpells.FindIndex(sp => sp == null);

            DebugLog($"Found empty slot at {index}");

            if (index != -1)
            {
                if (spellsByEntry.TryGetValue(entry, out BLUSpell? bluSpell))
                {
                    if(!SpellUnlocked(bluSpell))
                    {
                        DebugLog($"Spell {bluSpell.Name} with id {bluSpell.ID} is not unlocked, cannot slot in");
                        return;
                    }


                    DebugLog($"Slotting in spell {bluSpell.Name} with id {bluSpell.ID}");
                    ActionManager.Instance()->AssignBlueMageActionToSlot(index, bluSpell.ActionId);
                }
            }
        }

        private static void DebugLog(string s)
        {
            ADLogging.Debug($"[BLUHelper] {s}");
        }
    
    }
}
