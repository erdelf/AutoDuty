using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using ECommons.ExcelServices;
    using global::AutoDuty.Configurations;

    public class RepairHelper : ActiveHelperBase<RepairHelper, RepairLoopActionConfig>
    {
        public override string   Name          { get; } = nameof(RepairHelper);
        public override string   DisplayName   { get; } = "Repair Gear";

        public override string[]? Commands { get; init; } = ["repair"];
        public override string? CommandDescription { get; init; } = "Repairs your gear";

        protected override int      TimeOut       => this.ActionConfig.AutoRepairSelf ? 300000 : 600000;
        protected override string[] AddonsToClose { get; } = ["SelectYesno", "SelectIconString", "Repair", "SelectString"];

        internal override void Start()
        {
            if (!InventoryHelper.CanRepair(99))
                return;

            base.Start();
        }

        internal override unsafe void Stop() 
        {
            base.Stop();
            this.seenAddon = false;
            AgentModule.Instance()->GetAgentByInternalId(AgentId.Repair)->Hide();
        }

        private Vector3 RepairVendorLocation =>
            this.PreferredRepairNpc?.Position ?? PlayerHelper.GetGrandCompany() switch
        {
            GrandCompany.Maelstrom => new Vector3(17.715698f, 40.200005f, 3.9520264f),
            GrandCompany.TwinAdder => new Vector3(24.826416f, -8f,        93.18677f),
            _ => new Vector3(32.85266f,                       6.999999f,  -81.31531f),
        };

        private uint RepairVendorDataId =>
            this.PreferredRepairNpc?.DataId ?? PlayerHelper.GetGrandCompany() switch
        {
            GrandCompany.Maelstrom => 1003251u,
            GrandCompany.TwinAdder => 1000394u,
            _ => 1004416u
        };

        private IGameObject? RepairVendorGameObject    => ObjectHelper.GetObjectByDataId(this.RepairVendorDataId);
        private uint         RepairVendorTerritoryType => this.PreferredRepairNpc?.TerritoryType ?? PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany());

        private bool seenAddon = false;

        private static unsafe AtkUnitBase* addonRepair = null;
        private static unsafe AtkUnitBase* addonSelectYesno = null;
        private static unsafe AtkUnitBase* addonSelectIconString = null;
        private RepairNPCHelper.RepairNpcData? PreferredRepairNpc => this.ActionConfig.PreferredRepairNPC;

        protected override unsafe void HelperStopUpdate(IFramework framework)
        {
            if (!Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
                base.HelperStopUpdate(framework);
            else if (Svc.Targets.Target != null)
                Svc.Targets.Target = null;
            else
                this.CloseAddons();
        }

        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (Plugin.States.HasFlag(PluginState.Navigating)) 
                this.Stop();

            if (Conditions.Instance()->Mounted && GotoHelper.State != ActionState.Running)
            {
                ADLogging.Debug("Dismounting");
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
                return;
            }

            if (!EzThrottler.Check("Repair"))
                return;

            EzThrottler.Throttle("Repair", 250);

            if (!Player.Available)
                return;

            if (GotoHelper.State == ActionState.Running)
                return;

            Plugin.action = "Repairing";

            if (this.ActionConfig.AutoRepairSelf)
            {
                if (EzThrottler.Throttle("GearCheck"))
                {
                    if (!PlayerHelper.IsOccupied && InventoryHelper.CanRepair())
                    {
                        if (Svc.Condition[ConditionFlag.Occupied39])
                        {
                            ADLogging.Debug("Done Repairing");
                            this.Stop();
                        }
                        if (!GenericHelpers.TryGetAddonByName("Repair", out addonRepair) && !GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno))
                        {
                            ADLogging.Debug("Using Repair Action");
                            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 6);
                            return;
                        }
                        else if (!this.seenAddon && (!GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno) || !GenericHelpers.IsAddonReady(addonSelectYesno)))
                        {
                            ADLogging.Debug("Clicking Repair");
                            AddonHelper.ClickRepair();
                            return;
                        }
                        else if (GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno))
                        {
                            ADLogging.Debug("Clicking SelectYesno");
                            AddonHelper.ClickSelectYesno();
                            this.seenAddon = true;
                        }
                        else if (this.seenAddon && (!GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno) || !GenericHelpers.IsAddonReady(addonSelectYesno)))
                        {
                            ADLogging.Debug("Stopping-SelfRepair");
                            this.Stop();
                        }
                    }
                    else
                    {
                        ADLogging.Debug("Stopping-SelfRepair");
                        this.Stop();
                    }
                }
                return;
            }

            if (Svc.ClientState.TerritoryType != this.RepairVendorTerritoryType && ContentHelper.DictionaryContent.ContainsKey(Svc.ClientState.TerritoryType) && Conditions.Instance()->BoundByDuty)
                this.Stop();

            if (Svc.ClientState.TerritoryType != this.RepairVendorTerritoryType || RepairVendorGameObject == null || Vector3.Distance(Player.Position, RepairVendorGameObject.Position) > 3f)
            {
                ADLogging.Debug("Going to RepairVendor");
                GotoHelper.Invoke(this.RepairVendorTerritoryType, [this.RepairVendorLocation], 0.25f, 3f);
            }
            else if (PlayerHelper.IsValid)
            {
                if (!InventoryHelper.CanRepair(99))
                {
                    this.Stop();
                    return;
                }

                if (GenericHelpers.TryGetAddonByName("SelectIconString", out addonSelectIconString) && GenericHelpers.IsAddonReady(addonSelectIconString))
                {
                    ADLogging.Debug($"Clicking SelectIconString({this.PreferredRepairNpc?.RepairIndex})");
                    AddonHelper.ClickSelectIconString(this.PreferredRepairNpc?.RepairIndex ?? 0);
                }
                else if (!GenericHelpers.TryGetAddonByName("Repair", out addonRepair) && !GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno))
                {
                    ADLogging.Debug("Interacting with RepairVendor");
                    ObjectHelper.InteractWithObject(RepairVendorGameObject);
                }
                else if (!this.seenAddon && (!GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno) || !GenericHelpers.IsAddonReady(addonSelectYesno)))
                {
                    ADLogging.Debug("Clicking Repair");
                    AddonHelper.ClickRepair();
                }
                else if (GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno))
                {
                    ADLogging.Debug("Clicking SelectYesno");
                    AddonHelper.ClickSelectYesno();
                    this.seenAddon = true;
                }
                else if (this.seenAddon && (!GenericHelpers.TryGetAddonByName("SelectYesno", out addonSelectYesno) || !GenericHelpers.IsAddonReady(addonSelectYesno)))
                {
                    ADLogging.Debug("Stopping-RepairCity");
                    this.Stop();
                }
            }
        }
    }
}
