using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System.Numerics;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using ECommons.ExcelServices;
    using global::AutoDuty.Configurations;
    using System;
    using System.Collections.Generic;

    public class GotoInnHelper : ActiveHelperBase<GotoInnHelper, LoopActionConfigBare>
    {

        public override string Name        => nameof(GotoInnHelper);
        public override string DisplayName => string.Empty;
        protected override int    TimeOut     { get; set; } = 600_000;

        protected override string[] AddonsToClose { get; } = ["SelectYesno", "SelectString", "Talk"];

        private static GrandCompany whichGrandCompany = 0;

        internal static void Invoke(GrandCompany grandCompany = GrandCompany.Unemployed)
        {
            whichGrandCompany = grandCompany is GrandCompany.Unemployed or > GrandCompany.ImmortalFlames ?
                                                  PlayerHelper.GetGrandCompany() :
                                                  grandCompany;

            if (Svc.ClientState.TerritoryType != InnTerritoryType(whichGrandCompany))
            {
                Instance.Start();
                ADLogging.Info($"Goto Inn Started {whichGrandCompany}");
            }
        }


        internal override void Stop() 
        {
            GotoHelper.ForceStop();
            whichGrandCompany = 0;
            base.Stop();
        }

        internal static bool InGCInn(GrandCompany grandCompany = GrandCompany.Unemployed)
        {
            whichGrandCompany = grandCompany is GrandCompany.Unemployed or > GrandCompany.ImmortalFlames ?
                                    PlayerHelper.GetGrandCompany() :
                                    grandCompany;

            return Svc.ClientState.TerritoryType == InnTerritoryType(whichGrandCompany);
        }

        internal static uint InnTerritoryType(GrandCompany grandCompany) => grandCompany switch
        {
            GrandCompany.Maelstrom => 177u,
            GrandCompany.TwinAdder => 179u,
            _ => 178u
        };

        internal static uint ExitInnDoorDataId(GrandCompany grandCompany) => grandCompany switch
        {
            GrandCompany.Maelstrom => 2001010u,
            GrandCompany.TwinAdder => 2000087u,
            _ => 2001011u
        };

        private static List<Vector3> InnKeepLocation => whichGrandCompany switch
        {
            GrandCompany.Maelstrom => [new Vector3(15.42688f,          39.99999f, 12.466553f)],
            GrandCompany.TwinAdder => [new Vector3(25.6627f,           -8f,       99.74237f)],
            GrandCompany.ImmortalFlames => [new Vector3(28.85994f, 6.999999f, -80.12716f)],
            GrandCompany.Unemployed => [],
            _ => throw new ArgumentOutOfRangeException()
        };

        private static uint InnKeepDataId => whichGrandCompany switch
        {
            GrandCompany.Maelstrom => 1000974u,
            GrandCompany.TwinAdder => 1000102u,
            GrandCompany.ImmortalFlames => 1001976u,
            GrandCompany.Unemployed => 0,
            _ => throw new ArgumentOutOfRangeException()
        };

        private static IGameObject? InnKeepGameObject => ObjectHelper.GetObjectByDataId(InnKeepDataId);

        protected override unsafe void HelperStopUpdate(IFramework framework)
        {
            if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent])
                base.HelperStopUpdate(framework);
            else if (Svc.Targets.Target != null)
                Svc.Targets.Target = null;
            else
                this.CloseAddons();
        }

        protected override void HelperUpdate(IFramework framework)
        {
            if (Plugin.States.HasFlag(PluginState.Navigating))
            {
                ADLogging.Debug($"AutoDuty has Started, Stopping GotoInn");
                this.Stop();
            }

            if (!EzThrottler.Check("GotoInn"))
                return;

            EzThrottler.Throttle("GotoInn", 50);

            if (!Player.Available)
            {
                ADLogging.Debug($"Our player is null");
                return;
            }

            if (GotoHelper.State == ActionState.Running)
                return;

            Plugin.action = "Retiring to Inn";

            if (Svc.ClientState.TerritoryType == InnTerritoryType(whichGrandCompany))
            {
                ADLogging.Debug($"We are in the Inn, stopping GotoInn");
                this.Stop();
                return;
            }

            if (Svc.ClientState.TerritoryType != PlayerHelper.GetGrandCompanyTerritoryType(whichGrandCompany) || InnKeepGameObject == null || Vector3.Distance(Player.Position, InnKeepGameObject.Position) > 7f)
            {
                ADLogging.Debug($"We are not in the correct TT or our innkeepGO is null or out innkeepPosition is > 7f, moving there");
                GotoHelper.Invoke(PlayerHelper.GetGrandCompanyTerritoryType(whichGrandCompany), InnKeepLocation, 0.25f, 5f, false);
                return;
            }
            else if (PlayerHelper.IsValid)
            {
                ADLogging.Debug($"Interacting with GO and Addons");
                ObjectHelper.InteractWithObject(InnKeepGameObject);
                AddonHelper.ClickSelectString(0);
                AddonHelper.ClickSelectYesno();
                AddonHelper.ClickTalk();
            }
        }
    }
}
