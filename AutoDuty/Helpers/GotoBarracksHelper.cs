using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using AutoDuty.Managers;

namespace AutoDuty.Helpers
{
    using ECommons.ExcelServices;

    internal class GotoBarracksHelper : ActiveHelperBase<GotoBarracksHelper>
    {
        protected override string Name        => nameof(GotoBarracksHelper);
        protected override string DisplayName => string.Empty;

        protected override string[] AddonsToClose { get; } = ["SelectYesno"];

        internal override void Start()
        {
            if (Svc.ClientState.TerritoryType != BarracksTerritoryType(PlayerHelper.GetGrandCompany())) 
                base.Start();
        }

        internal override void Stop() 
        {
            GotoHelper.ForceStop();
            base.Stop();
        }

        internal static uint BarracksTerritoryType(GrandCompany  grandCompany) =>
            grandCompany switch
            {
                GrandCompany.Maelstrom => 536u,
                GrandCompany.TwinAdder => 534u,
                _ => 535u
            };
        internal static uint ExitBarracksDoorDataId(GrandCompany grandCompany) =>
            grandCompany switch
            {
                GrandCompany.Maelstrom => 2007528u,
                GrandCompany.TwinAdder => 2006963u,
                _ => 2007530u
            };

        private static Vector3 BarracksDoorLocation =>
            PlayerHelper.GetGrandCompany() switch
            {
                GrandCompany.Maelstrom => new Vector3(98.00867f,   41.275635f,  62.790894f),
                GrandCompany.TwinAdder => new Vector3(-80.216736f, 0.47296143f, -7.0039062f),
                _ => new Vector3(-153.30743f,                      5.2338257f,  -98.039246f)
            };

        private static uint BarracksDoorDataId =>
            PlayerHelper.GetGrandCompany() switch
            {
                GrandCompany.Maelstrom => 2007527u,
                GrandCompany.TwinAdder => 2006962u,
                _ => 2007529u
            };

        private static IGameObject? BarracksDoorGameObject => ObjectHelper.GetObjectByDataId(BarracksDoorDataId);

        protected override void HelperUpdate(IFramework framework)
        {
            if (Plugin.states.HasFlag(PluginState.Navigating)) this.Stop();

            if (!EzThrottler.Check("GotoBarracks"))
                return;

            EzThrottler.Throttle("GotoBarracks", 50);

            if (!Player.Available)
                return;

            if (GotoHelper.State == ActionState.Running)
                return;

            Plugin.action = Loc.Get("Overlay.Actions.RetiringToBarracks");

            if (Svc.ClientState.TerritoryType == BarracksTerritoryType(PlayerHelper.GetGrandCompany()))
            {
                this.Stop();
                return;
            }

            if (Svc.ClientState.TerritoryType != PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()) || BarracksDoorGameObject == null || Vector3.Distance(Player.Position, BarracksDoorGameObject.Position) > 2f)
            {
                GotoHelper.Invoke(PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()), BarracksDoorLocation, 0.25f, 2f, false);
                return;
            }
            else if (PlayerHelper.IsValid)
            {
                ObjectHelper.InteractWithObject(BarracksDoorGameObject);
                AddonHelper.ClickSelectYesno();
            }
        }
    }
}
