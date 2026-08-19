using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AutoDuty.IPC;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using Lumina.Excel.Sheets;
    using ECommons.MathHelpers;
    using FFXIVClientStructs.FFXIV.Client.UI.Misc;

    internal class DesynthHelper : ActiveHelperBase<DesynthHelper>
    {
        protected override string Name        => nameof(DesynthHelper);
        protected override string DisplayName => "Desynthing";

        public override string[]? Commands { get; init; } = ["desynth"];
        public override string? CommandDescription { get; init; } = "Desynth's items in your inventory";

        protected override string[] AddonsToClose { get; } = ["Desynth", "SalvageResult", "SalvageDialog", "SalvageItemSelector"];

        internal override void Start()
        {
            this._maxDesynthLevel = PlayerHelper.GetMaxDesynthLevel();
            this._gearsetterProtectedSlots = this.BuildGearsetterProtectedSlots();
            if(this.NextCategory(true))
                base.Start();
        }

        private float _maxDesynthLevel = 1;

        private HashSet<(InventoryType InventoryType, int Slot)> _gearsetterProtectedSlots = [];

        private unsafe HashSet<(InventoryType, int)> BuildGearsetterProtectedSlots()
        {
            HashSet<(InventoryType, int)> protectedSlots = [];

            if (!Configuration.AutoDesynthProtectGearsetterUpgrades || !Gearsetter_IPCSubscriber.IsEnabled)
                return protectedSlots;

            try
            {
                RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();

                for (int gearsetId = 0; gearsetId < gearsetModule->NumGearsets; gearsetId++)
                {
                    if (!gearsetModule->GetGearset(gearsetId)->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                        continue;

                    List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)>? recommendations =
                        Gearsetter_IPCSubscriber.GetRecommendationsForGearset((byte)gearsetId);

                    if (recommendations == null)
                        continue;

                    foreach ((uint recItemId, InventoryType? sourceInventory, byte? sourceInventorySlot, _) in recommendations)
                    {
                        if (sourceInventory == null || sourceInventorySlot == null)
                            continue;

                        if (protectedSlots.Add((sourceInventory.Value, sourceInventorySlot.Value)))
                            this.DebugLog($"Gearsetter protects item {recItemId} in {sourceInventory} slot {sourceInventorySlot} (recommended for gearset {gearsetId}) from Auto Desynth");
                    }
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"[AutoDesynth] Gearsetter IPC call failed while collecting upgrade protection, falling back to gearset-only protection for this run: {ex.Message}");
                protectedSlots.Clear();
            }

            return protectedSlots;
        }

        private AgentSalvage.SalvageItemCategory curCategory;

        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (Plugin.States.HasFlag(PluginState.Navigating) || InDungeon) this.Stop();

            if (!EzThrottler.Throttle("Desynth", 250))
                return;

            if (Conditions.Instance()->Mounted)
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
                return;
            }

            Plugin.action = "Desynthing Inventory";

            if (InventoryManager.Instance()->GetEmptySlotsInBag() < 1)
            {
                this.Stop();
                return;
            }

            if (PlayerHelper.IsOccupied)
                return;

            if (GenericHelpers.TryGetAddonByName("SalvageResult", out AtkUnitBase* addonSalvageResult) && GenericHelpers.IsAddonReady(addonSalvageResult))
            {
                this.DebugLog("Closing SalvageResult");
                addonSalvageResult->Close(true);
                return;
            }
            else if (GenericHelpers.TryGetAddonByName("SalvageDialog", out AtkUnitBase* addonSalvageDialog) && GenericHelpers.IsAddonReady(addonSalvageDialog))
            {
                this.DebugLog("Confirming SalvageDialog");
                AddonHelper.FireCallBack(addonSalvageDialog, true, 15, Configuration.AutoDesynthNQOnly);
                AddonHelper.FireCallBack(addonSalvageDialog, true, 0, false);
                return;
            }

            if (!GenericHelpers.TryGetAddonByName("SalvageItemSelector", out AddonSalvageItemSelector* addonSalvageItemSelector))
            {
                AgentSalvage.Instance()->AgentInterface.Show();
                EzThrottler.Throttle("Desynth", 2000, true);
                return;
            }
            else if (GenericHelpers.IsAddonReady((AtkUnitBase*)addonSalvageItemSelector) && addonSalvageItemSelector->IsReady)
            {
                AgentSalvage.Instance()->ItemListRefresh(true);
                if (AgentSalvage.Instance()->SelectedCategory != this.curCategory)
                {
                    this.DebugLog("Switching Category to " + this.curCategory);
                    AgentSalvage.Instance()->SelectedCategory = this.curCategory;
                    return;
                }
                else if (addonSalvageItemSelector->ItemCount > 0)
                {
                    HashSet<uint>? gearsetItemIds = null;

                    bool foundOne = false;
                    for (int i = 0; i < AgentSalvage.Instance()->ItemCount; i++)
                    {
                        AgentSalvage.SalvageListItem            item          = AgentSalvage.Instance()->ItemList[i];
                        InventoryItem* inventoryItem = InventoryManager.Instance()->GetInventorySlot(item.InventoryType, (int)item.InventorySlot);
                        uint            itemId        = inventoryItem->ItemId;
                            
                        if (itemId == 10146) 
                            continue;

                        Item? itemSheetRow = Svc.Data.Excel.GetSheet<Item>()?.GetRow(itemId);
                        uint? itemLevel    = itemSheetRow?.LevelItem.ValueNullable?.RowId;
                        float desynthLevel = PlayerHelper.GetDesynthLevel(item.ClassJob);

                        if (itemLevel == null || itemSheetRow == null || desynthLevel <= 0) 
                            continue;

                        if (!Configuration.AutoDesynthSkillUp || (desynthLevel < itemLevel + Configuration.AutoDesynthSkillUpLimit && desynthLevel < this._maxDesynthLevel))
                        {
                            if (Configuration.AutoDesynthNoGearset)
                            {
                                if (gearsetItemIds == null)
                                {
                                    gearsetItemIds = [];

                                    RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
                                    byte                  num           = gearsetModule->NumGearsets;
                                    for (byte j = 0; j < num; j++)
                                    {
                                        foreach (RaptureGearsetModule.GearsetEntry entry in gearsetModule->Entries)
                                            foreach (RaptureGearsetModule.GearsetItem gearsetItem in entry.Items)
                                            {
                                                uint gearsetItemItemId = gearsetItem.ItemId;
                                                if(gearsetItemItemId > 0) 
                                                    gearsetItemIds.Add(gearsetItemItemId);
                                            }
                                    }
                                }

                                if (gearsetItemIds.Contains(inventoryItem->GetItemId()))
                                    continue;
                            }

                            if (this._gearsetterProtectedSlots.Contains((item.InventoryType, (int)item.InventorySlot)))
                            {
                                this.DebugLog($"Skipping Item({i}): {itemSheetRow.Value.Name} - protected by Gearsetter as an upgrade for another gearset");
                                continue;
                            }

                            this.DebugLog($"Salvaging Item({i}): {itemSheetRow.Value.Name} {inventoryItem->ItemId} {inventoryItem->GetItemId()} {inventoryItem->GetBaseItemId()} with iLvl {itemLevel} because our desynth level is {desynthLevel}");
                            foundOne = true;
                            AddonHelper.FireCallBack((AtkUnitBase*)addonSalvageItemSelector, true, 12, i);
                            return;
                        }
                    }

                    if (!foundOne)
                        if (!this.NextCategory())
                        {
                            addonSalvageItemSelector->Close(true);
                            this.DebugLog("Desynth Finished");
                            this.Stop();
                        }
                }
                else
                {
                    if (!this.NextCategory())
                    {
                        addonSalvageItemSelector->Close(true);
                        this.DebugLog("Desynth Finished");
                        this.Stop();
                    }
                }
            }
        }

        public bool NextCategory(bool reset = false)
        {
            AgentSalvage.SalvageItemCategory[]? categories = Enum.GetValues<AgentSalvage.SalvageItemCategory>();
            for (int i = reset ? 0 : (int) this.curCategory + 1; i < categories.Length; i++)
            {
                if(Bitmask.IsBitSet(Configuration.AutoDesynthCategories, i))
                {
                    this.curCategory = categories[i];
                    return true;
                }
            }

            return false;
        }
    }
}
