using AutoDuty.IPC;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using ECommons.Automation;
    using FFXIVClientStructs.FFXIV.Client.Game;
    using System;
    using System.Linq;
    using Configurations;

    public class AutoRetainerHelper : ActiveHelperBase<AutoRetainerHelper, AutoRetainerLoopActionConfig>
    {
        public override string Name        { get; } = nameof(AutoRetainerHelper);
        public override string DisplayName { get; } = "AutoRetainer";

        public override string[]? Commands { get; init; } = ["ar", "autoretainer"];
        public override string? CommandDescription { get; init; } = "Automatically manages retainers using the AutoRetainer plugin";

        protected override int TimeOut => 600_000 + ((int) this.ActionConfig.AutoRetainerRemainingTime * 60);

        protected override string[] AddonsToClose { get; } = ["RetainerList", "SelectYesno", "SelectString", "RetainerTaskAsk"];

        internal override void Start()
        {
            if (!AutoRetainer_IPCSubscriber.RetainersAvailable(this.ActionConfig.AutoRetainerRemainingTime))
                return;
            this.DebugLog("AutoRetainerHelper.Invoke");
            if (!AutoRetainer_IPCSubscriber.IsEnabled)
                ADLogging.Info("AutoRetainer requires a plugin, visit https://puni.sh/plugin/AutoRetainer for more info");
            else if (State != ActionState.Running) 
                base.Start();
        }

        internal override void Stop()
        {
            this._autoRetainerStarted = false;
            this._autoRetainerStopped = false;
            GotoInnHelper.ForceStop();

            base.Stop();

            if (AutoRetainer_IPCSubscriber.IsBusy())
                AutoRetainer_IPCSubscriber.AbortAllTasks();
            Chat.ExecuteCommand("/autoretainer d");
        }

        private        bool         _autoRetainerStarted = false;
        private        bool         _autoRetainerStopped = false;
        private IGameObject? SummoningBellGameObject => Svc.Objects.FirstOrDefault(x => x.BaseId == SummoningBellHelper.SummoningBellDataIds((uint) this.ActionConfig.PreferredSummoningBellEnum));

        protected override unsafe void HelperStopUpdate(IFramework framework)
        {
            if (!Svc.Condition[ConditionFlag.OccupiedSummoningBell])
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
    

        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (!this.UpdateBase())
                return;

            if (!PlayerHelper.IsValid) 
                return;

            if(InventoryManager.Instance()->GetEmptySlotsInBag() <= 1)
            {
                this.DebugLog("Not enough inventory space, stopping");
                this.Stop();
                return;
            }

            if (this._autoRetainerStopped)
            {
                this.DebugLog("Stopped?");
                if (AutoRetainer_IPCSubscriber.IsBusy())
                {
                    this._autoRetainerStopped = false;
                    this.DebugLog("still busy");

                } else if (AutoRetainer_IPCSubscriber.RetainersAvailable(this.ActionConfig.AutoRetainerRemainingTime))
                {
                    this._autoRetainerStopped = false;
                    this._autoRetainerStarted = false;

                    this.DebugLog("Retainers available, restarting");
                }
                else
                {
                    this.Stop();
                    return;
                }
            }

            if (!this._autoRetainerStarted)
            {
                if (AutoRetainer_IPCSubscriber.IsBusy())
                {
                    this.DebugLog("AutoRetainer has Started");
                    this._autoRetainerStarted = true;
                    this.UpdateBaseThrottle   = 1000;
                    return;
                } else if (Svc.Condition[ConditionFlag.OccupiedSummoningBell])
                {
                    if (AutoRetainer_IPCSubscriber.AreAnyRetainersAvailableForCurrentChara())
                    {
                        this.DebugLog("Waiting for AutoRetainer to Start");
                        Chat.ExecuteCommand("/autoretainer e");
                    }
                }
            }
            else if (this._autoRetainerStarted && !AutoRetainer_IPCSubscriber.IsBusy())
            {
                this.DebugLog("AutoRetainer is Complete");
                this._autoRetainerStopped = true;
                EzThrottler.Throttle(this.Name, 2000, true);
            }

            if (this.SummoningBellGameObject != null && !SummoningBellHelper.HousingZones.Contains(Player.Territory.RowId) && ObjectHelper.GetDistanceToPlayer(this.SummoningBellGameObject) > 4)
            {
                this.DebugLog("Moving Closer to Summoning Bell");
                MovementHelper.Move(this.SummoningBellGameObject, 0.25f, 4);
            }
            else if ((this.SummoningBellGameObject == null || SummoningBellHelper.HousingZones.Contains(Player.Territory.RowId)) && GotoHelper.State != ActionState.Running)
            {
                this.DebugLog("Moving to Summoning Bell Location");
                SummoningBellHelper.Invoke(this.ActionConfig.PreferredSummoningBellEnum);
            }
            else if (this.SummoningBellGameObject != null && ObjectHelper.GetDistanceToPlayer(this.SummoningBellGameObject) <= 4 && !this._autoRetainerStarted && !GenericHelpers.TryGetAddonByName("RetainerList", out AtkUnitBase* _) && (ObjectHelper.InteractWithObjectUntilAddon(this.SummoningBellGameObject, "RetainerList") == null))
            {
                this.DebugLog("Interacted");
                if (Svc.Condition[ConditionFlag.OccupiedSummoningBell])
                {
                    this.DebugLog("Occupied");
                    if (VNavmesh_IPCSubscriber.Path_IsRunning)
                        VNavmesh_IPCSubscriber.Path_Stop();
                }
                else
                {
                    this.DebugLog("Interacting with SummoningBell");
                }
            }
        }
    }
}
