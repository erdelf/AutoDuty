using AutoDuty.IPC;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using ECommons.Automation;
    using FFXIVClientStructs.FFXIV.Client.Game;
    using System;
    using Configurations;

    public class AutoRetainerMultiModeHelper : ActiveHelperBase<AutoRetainerMultiModeHelper, AutoRetainerMultiModeLoopActionConfig>
    {
        public override string Name { get; } = nameof(AutoRetainerMultiModeHelper);
        public override string DisplayName { get; } = "AutoRetainerMultiMode";

        public override string[]? Commands { get; init; } = ["arm", "autoretainermulti"];
        public override string? CommandDescription { get; init; } = "Runs one cycle of AutoRetainer's Multi Mode";

        protected override int TimeOut => (int)(TimeSpan.MillisecondsPerHour * 2);

        protected override string[] AddonsToClose { get; } = ["RetainerList", "SelectYesno", "SelectString", "RetainerTaskAsk"];

        internal override unsafe void Start()
        {
            this.DebugLog(this.DisplayName + ".Invoke");

            if (!AutoRetainer_IPCSubscriber.IsEnabled)
                ADLogging.Info("AutoRetainer functionality requires AutoRetainer, visit https://puni.sh/plugin/AutoRetainer for more info");
            else if(!Lifestream_IPCSubscriber.IsEnabled)
                ADLogging.Info("AutoRetainer MultiMode functionality requires Lifestream, visit https://github.com/NightmareXIV/Lifestream for more info");
            else if (InventoryManager.Instance()->GetEmptySlotsInBag() <= 1)
                this.DebugLog("Not enough inventory space, skipping");
            else if (State != ActionState.Running)
                base.Start();
        }

        internal override void Stop()
        {
            this.autoRetainerStarted = false;

            Plugin.Stage = this.priorStage;

            base.Stop();

            if (AutoRetainer_IPCSubscriber.IsBusy())
                AutoRetainer_IPCSubscriber.AbortAllTasks();
            Chat.ExecuteCommand("/autoretainer d");
        }

        private bool  autoRetainerStarted;
        private ulong CID;
        private Stage priorStage;
        
        protected override void HelperStopUpdate(IFramework framework)
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

        protected override void HelperUpdate(IFramework framework)
        {
            if (!base.UpdateBase())
                return;

            if (!this.autoRetainerStarted)
            {
                this.CID = Player.CID;

                this.autoRetainerStarted = true;
                AutoRetainer_IPCSubscriber.EnableSingleMultiMode(this.ActionConfig.MultiModeType);
                this.priorStage = Plugin.Stage;

                if (Plugin.Stage is not (Stage.Stopped or Stage.Paused))
                    Plugin.Stage = Stage.Paused;
            } else if(!AutoRetainer_IPCSubscriber.GetMultiModeState())
            {
                if (Lifestream_IPCSubscriber.IsBusy || !PlayerHelper.IsReady)
                    return;

                if (Player.CID == this.CID)
                {
                    this.DebugLog("Arrived at home");
                    this.Stop();
                    return;
                }

                this.DebugLog("Multi-mode finished. Moving Home now");
                Lifestream_IPCSubscriber.ChangeCharacter(ConfigurationMain.Instance.charByCID[this.CID]);
            }
        }
    }
}
