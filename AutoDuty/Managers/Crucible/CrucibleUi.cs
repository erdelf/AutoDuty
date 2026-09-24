using Dalamud.Memory;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoDuty.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using ECommons;
    using ECommons.UIHelpers;
    using ECommons.UIHelpers.AtkReaderImplementations;
    using Helpers;

    internal static unsafe class CrucibleUi
    {
        public const string TeamWindow     = "XBMPetParty";
        public const string BestiaryWindow = "XBMMonsterNotebook";
        public const string BoardList      = "XBMStageList";
        public const string BoardLayout    = "XBMStageDetailList";
        public const string LootWindow     = "XBMContentsBooty";
        public const string TreasureWindow = "XBMContentsTreasure";
        public const string ResultWindow   = "XBMResult";
        public const string ShopWindow     = "XBMContentsItemShop";
        public const string YesNo          = "SelectYesno";
        public const string ContextMenu    = "ContextMenu";
        public const string MainHud        = "XBMContentsMainHUD";

        public const int BestiaryPageSize = 25;

        private const uint TeamList = 11;

        private static readonly Regex Number        = new("[0-9]+");
        private static readonly Regex GroupedNumber = new("[0-9][0-9,]*");
        private static readonly Regex LeadingGlyphs = new("^[^\\p{L}]+");
        
        public static AtkUnitBase* Ready(string name)
        {
            AtkUnitBase* addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
            return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
        }

        public static bool IsOpen(string name) => Ready(name) != null;

        public static bool TryReady(string name, out AtkUnitBase* addon)
        {
            addon = Ready(name);
            return addon != null;
        }

        public static bool ClickButton(AtkUnitBase* addon, uint nodeId)
        {
            AtkResNode*       node   = addon->UldManager.SearchNodeById(nodeId);
            AtkComponentNode* button = node == null ? null : node->GetAsAtkComponentNode();
            if (button == null)
            {
                Svc.Log.Warning($"[Crucible] {addon->NameString} has no button node {nodeId}");
                return false;
            }

            AtkEvent* evt = button->AtkResNode.AtkEventManager.Event;
            while (evt != null && evt->State.EventType != AtkEventType.ButtonClick)
                evt = evt->NextEvent;

            if (evt == null)
            {
                Svc.Log.Warning($"[Crucible] {addon->NameString} button {nodeId} has no click event");
                return false;
            }

            Svc.Log.Debug($"[Crucible] {addon->NameString} click button {nodeId} (param {evt->Param})");
            addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
            return true;
        }

        public static bool ClickEvent(AtkUnitBase* addon, AtkEventType type, uint param)
        {
            AtkEvent* evt = FindEvent(&addon->UldManager, type, param, 0);
            if (evt == null)
            {
                Svc.Log.Warning($"[Crucible] {addon->NameString} has no {type} event with param {param}");
                return false;
            }

            Svc.Log.Debug($"[Crucible] {addon->NameString} {type} (param {param})");
            AtkEventData data = new();
            addon->ReceiveEvent(type, (int)param, evt, &data);
            return true;
        }

        private static AtkEvent* FindEvent(AtkUldManager* uld, AtkEventType type, uint param, int depth)
        {
            if (depth > 6)
                return null;

            for (int i = 0; i < uld->NodeListCount; i++)
            {
                AtkResNode* node = uld->NodeList[i];
                if (node == null || !node->IsVisible())
                    continue;

                for (AtkEvent* evt = node->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
                    if (evt->State.EventType == type && evt->Param == param)
                        return evt;

                AtkComponentNode* component = node->GetAsAtkComponentNode();
                if (component == null || component->Component == null)
                    continue;

                AtkEvent* found = FindEvent(&component->Component->UldManager, type, param, depth + 1);
                if (found != null)
                    return found;
            }

            return null;
        }

        public static int ContextMenuOptionCount(AtkUnitBase* menu)
        {
            int count = 0;
            for (int i = 0; i < menu->AtkValuesCount; i++)
                if (menu->AtkValues[i].Type.ToString().Contains("String") && menu->AtkValues[i].GetValueAsString().Length > 0)
                    count++;
            return count;
        }

        public static int SelectStringIndex(AtkUnitBase* addon, params string[] contains)
        {
            AddonSelectString* select = (AddonSelectString*)addon;
            ref PopupMenu      menu   = ref select->PopupMenu.PopupMenu;
            if (menu.EntryNames == null)
                return -1;

            for (int i = 0; i < menu.EntryCount; i++)
            {
                if (menu.EntryNames[i].Value == null)
                    continue;

                string entry = MemoryHelper.ReadSeStringNullTerminated((nint)menu.EntryNames[i].Value).TextValue;
                if (contains.Any(s => entry.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    return i;
            }

            return -1;
        }

        public readonly record struct ItemSlot(int Slot, uint Row, string Name);

        public static List<ItemSlot> HudItems(AtkUnitBase* hud)
        {
            List<ItemSlot> items = [];
            for (int slot = 0; slot < 10; slot++)
            {
                int at = 9 + slot * 5;
                if (at + 4 >= hud->AtkValuesCount)
                    break;

                uint row = AsUInt(hud->AtkValues[at + 3]);
                if (hud->AtkValues[at + 1].Byte == 0 || row == 0)
                    continue;

                items.Add(new ItemSlot(slot, row, hud->AtkValues[at + 4].GetValueAsString()));
            }

            return items;
        }

        public static int ShopCoins(AtkUnitBase* shop) =>
            (int)new ReaderXBMContentsItemShop(shop).Coins;

        public static List<ReaderXBMContentsItemShop.StockEntry> ShopStock(AtkUnitBase* shop) => 
            new ReaderXBMContentsItemShop(shop).StockEntries;

        public static HashSet<uint> ShopHeldItems(AtkUnitBase* shop) => 
            new ReaderXBMContentsItemShop(shop).ItemEntriesValid.Select(ie => ie.Id).ToHashSet();

        public static HashSet<uint> ShopOwnedGear(AtkUnitBase* shop) => 
            new ReaderXBMContentsItemShop(shop).OwnedEntriesOwned.Select(ge => ge.Id).ToHashSet();

        public static IEnumerable<uint> BestiaryShowing(AtkUnitBase* notebook)
        {
            ReaderXBMMonsterNotebook x = new(notebook);
            return x.CurrentPageEntries.Select(m => m.Number);
        }

        public static bool BestiaryShows(AtkUnitBase* notebook, uint number)
        {
            ReaderXBMMonsterNotebook x = new(notebook);

            foreach (ReaderXBMMonsterNotebook.MonsterEntry entry in x.CurrentPageEntries)
                if (entry.Number == number)
                    return true;

            return false;
        }

        public static CrucibleFamiliar? BestiarySelected(AtkUnitBase* notebook)
        {
            ReaderXBMMonsterNotebook reader = new(notebook);

            if (notebook->AtkValuesCount <= 273 || !reader.Selected)
                return null;

            uint number = reader.SelectedNumber;
            int  rank   = reader.SelectedRank;
            if (number == 0 || rank == 0)
                return null;

            return new CrucibleFamiliar
                   {
                       Number             = number,
                       Name               = reader.SelectedName.GetText(),
                       Rank               = rank,
                       Hp                 = reader.SelectedMaxHP,
                       Exp                = reader.SelectedXP.ToString(),
                       Strength           = reader.SelectedStrength,
                       PhysicalResistance = reader.SelectedPhysResistance,
                       Constitution       = reader.SelectedConstitution,
                       Intelligence       = reader.SelectedIntelligence,
                       MagicResistance    = reader.SelectedMagicResistance,
                       Classification     = reader.Classification.GetText()
                   };
        }

        public static List<ReaderXBMPetParty.MonsterEntry>? Team(ReaderXBMPetParty? party = null)
        {
            if (party == null)
            {
                AtkUnitBase* addon = Ready(TeamWindow);
                if (addon == null)
                    return null;

                party = new ReaderXBMPetParty(addon);
            }

            return party.TeamEntries;
        }

        public static CrucibleFamiliar? FamiliarDetail(string window)
        {
            AtkUnitBase* addon = Ready(window);
            if (addon == null)
                return null;

            AtkUldManager* uld  = &addon->UldManager;
            string         name = Text(uld, 13);
            string         hp   = Text(uld, 45);
            if (name.Length == 0 || hp.Length == 0)
                return null;

            return new CrucibleFamiliar
                   {
                       Number             = (uint)Digits(Text(uld, 12)),
                       Name               = name,
                       Rank               = Digits(Text(uld, 40)),
                       Hp                 = MaxOf(hp),
                       Strength           = Digits(ComponentText(uld, 47, 3)),
                       PhysicalResistance = Digits(ComponentText(uld, 48, 3)),
                       Constitution       = Digits(ComponentText(uld, 49, 3)),
                       Intelligence       = Digits(ComponentText(uld, 50, 3)),
                       MagicResistance    = Digits(ComponentText(uld, 51, 3)),
                       Exp                = Text(uld, 42),
                       Classification     = Text(uld, 21),
                       Element            = LeadingGlyphs.Replace(Text(uld, 23), "")
                   };
        }

        public static float ExpShare(string exp)
        {
            int slash = exp.IndexOf('/');
            return slash > 0 && TryNumber(exp[..slash], out float have) && TryNumber(exp[(slash + 1)..], out float need) && need > 0 ? have / need : 0f;

            static bool TryNumber(string text, out float value)
            {
                Match match = GroupedNumber.Match(text);
                value = 0;
                return match.Success && float.TryParse(match.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }

        private static string AllText(AtkUldManager* uld)
        {
            List<string> parts = [];
            for (int i = 0; i < uld->NodeListCount; i++)
            {
                AtkResNode* node = uld->NodeList[i];
                if (node == null || !node->IsVisible())
                    continue;

                AtkTextNode* text = node->GetAsAtkTextNode();
                if (text == null)
                    continue;

                string value = text->NodeText.ToString().Trim();
                if (value.Length > 0)
                    parts.Add(value);
            }

            return string.Join(" | ", parts);
        }

        private static string Text(AtkUldManager* uld, uint id, bool visibleOnly = false)
        {
            AtkResNode* node = uld->SearchNodeById(id);
            if (node == null || (visibleOnly && !node->IsVisible()))
                return "";

            AtkTextNode* text = node->GetAsAtkTextNode();
            return text == null ? "" : text->NodeText.ToString().Trim();
        }

        private static string ComponentText(AtkUldManager* uld, uint componentId, uint textId)
        {
            AtkResNode* node = uld->SearchNodeById(componentId);
            if (node == null)
                return "";

            AtkComponentNode* componentNode = node->GetAsAtkComponentNode();
            return componentNode == null || componentNode->Component == null ? "" : Text(&componentNode->Component->UldManager, textId);
        }

        private static uint AsUInt(AtkValue value) =>
            value.Type.ToString() == "UInt" ? value.UInt : (uint)Math.Max(0, value.Int);

        private static int FirstNumber(string text)
        {
            Match match = GroupedNumber.Match(text);
            return match.Success && int.TryParse(match.Value.Replace(",", ""), out int value) ? value : 0;
        }

        private static int Digits(string text)
        {
            Match match = Number.Match(text);
            return match.Success && int.TryParse(match.Value, out int value) ? value : 0;
        }

        private static int CurrentOf(string fraction)
        {
            int slash = fraction.IndexOf('/');
            return Digits(slash >= 0 ? fraction[..slash] : fraction);
        }

        private static int MaxOf(string fraction)
        {
            int slash = fraction.LastIndexOf('/');
            return Digits(slash >= 0 ? fraction[(slash + 1)..] : fraction);
        }

        internal static class Screens
        {
            internal static class PetParty
            {
                private const uint RestOrReturnButton = 21;

                public static int  GetTeamSize(AtkUnitBase*  party)          => (int) new ReaderXBMPetParty(party).TeamSize;
                public static void Pick(AtkUnitBase*         party, int row) => AddonHelper.FireCallBack(party, true, 1, row);
                public static void OpenRowMenu(AtkUnitBase*  party, int row) => AddonHelper.FireCallBack(party, true, 2, row);
                public static void OpenBestiary(AtkUnitBase* party) => AddonHelper.FireCallBack(party, true, 5);
                public static bool Rest(AtkUnitBase*         party) => ClickButton(party, RestOrReturnButton);
                public static bool Return(AtkUnitBase*       party) => ClickButton(party, RestOrReturnButton);
            }

            internal static class StageList
            {
                public static void Highlight(AtkUnitBase* list, uint board) => AddonHelper.FireCallBack(list, true, 2, (int)board);
                public static void Open(AtkUnitBase*      list, uint board) => AddonHelper.FireCallBack(list, true, 1, (int)board);
            }

            internal static class StageDetail
            {
                public static void Confirm(AtkUnitBase* layout) => AddonHelper.FireCallBack(layout, true, 8);
            }

            internal static class Notebook
            {
                private const uint FirstEntryParam = 4;

                public static void ShowPage(AtkUnitBase* notebook, uint page) => AddonHelper.FireCallBack(notebook, true, 3, page);

                public static bool PickEntry(AtkUnitBase* notebook, uint slotOnPage)
                {
                    AddonHelper.FireCallBack(notebook, true, 5, (int)slotOnPage);
                    return ClickEvent(notebook, AtkEventType.MouseDown, FirstEntryParam + slotOnPage);
                }
            }

            internal static class Menu
            {
                private const int RemoveAllOption = 2;

                public static void ChooseFirst(AtkUnitBase*     menu) => AddonHelper.FireCallBack(menu, true, 0, 0,               0);
                public static void ChooseRemoveAll(AtkUnitBase* menu) => AddonHelper.FireCallBack(menu, true, 0, RemoveAllOption, 0);
                public static void Close(AtkUnitBase*           menu) => AddonHelper.FireCallBack(menu, true, 0, -1,              0);
            }

            internal static class Booty
            {
                public static void Close(AtkUnitBase* loot)            => AddonHelper.FireCallBack(loot, true, 0);
                public static void TakeCoins(AtkUnitBase* loot)            => AddonHelper.FireCallBack(loot, true, 3);
                public static void Take(AtkUnitBase*      loot, int index) => AddonHelper.FireCallBack(loot, true, 4, index);
                public static bool TakeAll(AtkUnitBase*   loot) => ClickButton(loot, 46);
            }

            internal static class Treasure
            {
                public static void Close(AtkUnitBase* treasure) => AddonHelper.FireCallBack(treasure, true, 0);
                public static void Take(AtkUnitBase* treasure, uint nodeId) => AddonHelper.FireCallBack(treasure, true, 2, nodeId);
            }

            internal static class Result
            {
                public static bool Continue(AtkUnitBase* result) => ClickButton(result, 61);
            }

            internal static class ItemShop
            {
                public static void Buy(AtkUnitBase* shop, int index) => AddonHelper.FireCallBack(shop, true, 2, index);

                public static bool Close(AtkUnitBase* shop) => ClickButton(shop, 40);
            }

            internal static class MainHud
            {
                public static void OpenItemMenu(AtkUnitBase* hud, int slot) => AddonHelper.FireCallBack(hud, true, 6, slot);
            }

            internal static class Prompt
            {
                public static void Yes(AtkUnitBase* prompt) => AddonHelper.FireCallBack(prompt, true, 0);
            }
        }
    }
}
