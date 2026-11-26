namespace AutoDuty.Helpers;
using System.Collections.Generic;
using System.Linq;
using Configurations;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using IPC;
using Logging;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using EventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;

public class GlamourChestHelper : ActiveHelperBase<GlamourChestHelper, GlamourLoopActionConfig>
{
    public override    string    Name               { get; }       = nameof(GlamourChestHelper);
    public override    string    DisplayName        { get; }       = "Glamour Chest";
    public override    string[]? Commands           { get; init; } = ["glamour"];
    public override    string?   CommandDescription { get; init; } = "Stores items in your inventory to the Glamour Chest.";
    protected override int       UpdateBaseThrottle { get; set; }  = 125;

    private bool glogStarted;

    protected override string[] AddonsToClose { get; } =
    [
        "SelectYesno", "MiragePrismPrismBox", "MiragePrismPrismBoxCrystallize",
        "MiragePrismMiragePlate", "CabinetWithdraw", "SelectString"
    ];

    internal override void Start()
    {
        if (!GlamourLog_IPCSubscriber.IsEnabled)
            return;

        if (!QuestManager.IsQuestComplete(68553))
            ADLogging.Info("Glamour Chest requires having completed quest: If I Had a Glamour");


        ExcelSheet<MirageStoreSetItemLookup> setLookups = Svc.Data.GetExcelSheet<MirageStoreSetItemLookup>();
        IEnumerable<InventoryItem> items = InventoryHelper.GetInventorySelection(InventoryHelper.Bag).Where(item => setLookups.HasRow(item.ItemId)).ToList();

        if (!items.Any() || items.All(item => GlamourLog_IPCSubscriber.IsStored(item.ItemId)))
            return;

        this.glogStarted = false;
        base.Start();
    }

    protected override unsafe void HelperUpdate(IFramework framework)
    {
        if (!this.UpdateBase() || !PlayerHelper.IsValid)
            return;

        if (GotoInnHelper.State == ActionState.Running)
            return;

        if (this.glogStarted)
        {
            if (!GlamourLog_IPCSubscriber.Busy)
                this.Stop();
            return;
        }

        Plugin.action = "Glamour Chest";

        if(Svc.Targets.Target == null || Svc.Targets.Target.Struct()->EventHandler->Info.EventId != 721347)
        {
            this.DebugLog("Target is not the glamour chest.");
            IGameObject? glamourChest = Svc.Objects.OrderBy(ObjectHelper.GetDistanceToPlayer).FirstOrDefault(o =>
                                                                                                        {
                                                                                                            EventHandler* eventHandler = o.Struct()->EventHandler;
                                                                                                            return eventHandler != null && eventHandler->Info.EventId == 721347;
                                                                                                        });

            if (glamourChest != null)
            {
                Svc.Targets.Target = glamourChest;
            }
            else if (!GotoInnHelper.InGCInn())
            {
                this.DebugLog("Not in the correct territory for the inn.");
                GotoInnHelper.Invoke();
            }
            return;
        }

        if (!ObjectHelper.BelowDistanceToPlayer(Svc.Targets.Target.Position, 3f, 2f))
        {
            this.DebugLog("Glamour Chest is too far away.");
            if (!VNavmesh_IPCSubscriber.Path_IsRunning)
                VNavmesh_IPCSubscriber.SimpleMove_PathfindAndMoveTo(Svc.Targets.Target.Position, false);
            return;
        }
        
        if (VNavmesh_IPCSubscriber.Path_IsRunning)
            VNavmesh_IPCSubscriber.Path_Stop();

        if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno))
        {
            this.DebugLog("Selecting SelectYesno");
            AddonHelper.ClickSelectYesno();
            return;
        }

        AgentMiragePrismPrismBox* agentMirage  = AgentMiragePrismPrismBox.Instance();
        if (agentMirage->IsAddonReady() && GenericHelpers.TryGetAddonByName("MiragePrismPrismBoxCrystallize", out AtkUnitBase* addonMirage))
        {
            this.DebugLog("MiragePrism addon is ready.");


            if (addonMirage->AtkValuesCount <= 0)
                return;

            if (addonMirage->AtkValues[0].UInt <= 0) // Number of items in the current category
            {
                this.DebugLog("no items left.");
                this.Stop();
                return;
            }

            this.DebugLog("Activating Glamour Log");

            GlamourLog_IPCSubscriber.Entrust();
            this.glogStarted = true;
            return;
        }

        if (!agentMirage->IsAddonShown())
        {
            this.DebugLog("Interact with GlamourChest");
            ObjectHelper.InteractWithObject(Svc.Targets.Target);
            return;
        }
    }
}