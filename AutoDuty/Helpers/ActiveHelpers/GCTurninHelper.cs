using AutoDuty.IPC;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System.Numerics;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using Configurations;
    using ECommons.ExcelServices;

    public class GCTurninHelper : ActiveHelperBase<GCTurninHelper, GCTurnInLoopActionConfig>
    {
        public override string Name        { get; } = nameof(GCTurninHelper);
        public override string DisplayName { get; } = "GC Turnin";

        public override string[]? Commands { get; init; } = ["turnin", "gcturnin"];
        public override string? CommandDescription { get; init; } = "Automatically turns in items into the Grand Company Supply";

        protected override string[] AddonsToClose { get; } = ["GrandCompanySupplyReward", "SelectYesno", "SelectString", "GrandCompanySupplyList"];

        protected override int TimeOut { get; set; } = 600_000;

        internal override void Start()
        {
            if (!AutoRetainer_IPCSubscriber.IsEnabled)
                ADLogging.Info("GC Turnin Requires AutoRetainer plugin. Get @ https://love.puni.sh/ment.json");
            else if (PlayerHelper.GetGrandCompanyRank() <= 5)
                ADLogging.Info("GC Turnin requires GC Rank 6 or Higher");
            else
                base.Start();
        }

        internal override void Stop() 
        {
            this.turninStarted = false;
            GotoHelper.ForceStop();
            base.Stop();
        }

        internal static Vector3 GCSupplyLocation =>
            PlayerHelper.GetGrandCompany() switch
            {
                GrandCompany.Maelstrom => new Vector3(94.02183f,        40.27537f,   74.475525f),
                GrandCompany.TwinAdder => new Vector3(-68.678566f,      -0.5015295f, -8.470145f),
                _ => new Vector3(-142.82619f, 4.0999994f,  -106.31349f),
            };

        private static uint PersonnelOfficerDataId =>
            PlayerHelper.GetGrandCompany() switch
            {
                GrandCompany.Maelstrom => 1002388u,
                GrandCompany.TwinAdder => 1002394u,
                _ => 1002391u
            };

        private static IGameObject? PersonnelOfficerGameObject => ObjectHelper.GetObjectByDataId(PersonnelOfficerDataId);

        private static uint AetheryteTicketId =>
            PlayerHelper.GetGrandCompany() switch
            {
                GrandCompany.Maelstrom => 21069u,
                GrandCompany.TwinAdder => 21070u,
                _ => 21071u
            };

        private bool turninStarted = false;

        protected override void HelperStopUpdate(IFramework framework)
        {
            if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent])
            {
                base.HelperStopUpdate(framework);
            }
            else
            {
                if (Svc.Targets.Target != null)
                    Svc.Targets.Target = null;
                this.CloseAddons();
            }
        }

        protected override void HelperUpdate(IFramework framework)
        {
            if (Plugin.States.HasFlag(PluginState.Navigating))
            {
                this.DebugLog("AutoDuty is Started, Stopping GCTurninHelper");
                this.Stop();
                return;
            }

            switch (this.turninStarted)
            {
                case false when AutoRetainer_IPCSubscriber.IsBusy():
                    this.InfoLog("TurnIn has Started");
                    this.turninStarted = true;
                    return;
                case true when !AutoRetainer_IPCSubscriber.IsBusy():
                    this.DebugLog("TurnIn is Complete");
                    this.Stop();
                    return;
            }

            if (!EzThrottler.Throttle("Turnin", 250))
                return;

            if (GotoHelper.State == ActionState.Running)
                //DebugLog("Goto Running");
                return;
            Plugin.action = "GC Turning In";

            if (GotoHelper.State != ActionState.Running && Svc.ClientState.TerritoryType != PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()))
            {
                this.DebugLog("Moving to GC Supply");
                if (this.ActionConfig.UseTicket && InventoryHelper.ItemCount(AetheryteTicketId) > 0)
                {
                    if (!PlayerHelper.IsCasting)
                        InventoryHelper.UseItem(AetheryteTicketId);
                }
                else
                {
                    GotoHelper.Invoke(PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()), [GCSupplyLocation], 0.25f, 2f, false);
                }

                return;
            }

            if (ObjectHelper.GetDistanceToPlayer(GCSupplyLocation) > 4 && PlayerHelper.IsReady && VNavmesh_IPCSubscriber.Nav_IsReady && !VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress && VNavmesh_IPCSubscriber.Path_NumWaypoints == 0)
            {
                this.DebugLog("Setting Move to Personnel Officer");
                MovementHelper.Move(GCSupplyLocation, 0.25f, 4f);
                return;
            }
            else if (ObjectHelper.GetDistanceToPlayer(GCSupplyLocation) > 4 && VNavmesh_IPCSubscriber.Path_NumWaypoints > 0)
            {
                this.DebugLog("Moving to Personnel Officer");
                return;
            }
            else if (ObjectHelper.GetDistanceToPlayer(GCSupplyLocation) <= 4 && VNavmesh_IPCSubscriber.Path_NumWaypoints > 0)
            {
                this.DebugLog("Stopping Path");
                VNavmesh_IPCSubscriber.Path_Stop();
                return;
            }
            else if (ObjectHelper.GetDistanceToPlayer(GCSupplyLocation) <= 4 && VNavmesh_IPCSubscriber.Path_NumWaypoints == 0 && !this.turninStarted)
            {
                this.DebugLog("Starting TurnIn proper");
                AutoRetainer_IPCSubscriber.EnqueueGCInitiation();
                return;
            }
        }
    }
}
