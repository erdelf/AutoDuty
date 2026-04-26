using AutoDuty.IPC;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using AutoDuty.Managers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using Lumina.Excel.Sheets;
    using GrandCompany = ECommons.ExcelServices.GrandCompany;

    internal class GotoHelper : ActiveHelperBase<GotoHelper>
    {
        protected override string Name        { get; } = nameof(GotoHelper);
        protected override string DisplayName { get; } = string.Empty;

        public override string[]? Commands { get; init; } = ["goto", "go"];
        public override string? CommandDescription { get; init; } = "Goes to a specific location in the game world\ttargets: inn / barracks / gc / bell / apartment / home / fc";

        protected override string[] AddonsToClose { get; } = ["SelectYesno"];

        protected override int TimeOut { get; set; } = 0;

        internal static void Invoke(uint territoryType) => Invoke(territoryType, 0);

        internal static void Invoke(uint territoryType, uint gameObjectDataId) => Invoke(territoryType, [], gameObjectDataId, 0.25f, 0.25f, false, false, true);

        internal static void Invoke(uint territoryType, Vector3 moveLocation) => Invoke(territoryType, [moveLocation], 0, 0.25f, 0.25f, false, false, true);

        internal static void Invoke(uint territoryType, List<Vector3> moveLocations) => Invoke(territoryType, moveLocations, 0, 0.25f, 0.25f, false, false, true);

        internal static void Invoke(uint territoryType, uint gameObjectDataId, float tollerance = 0.25f, float lastPointTollerance = 0.25f, bool useAethernetTravel = false, bool useFlight = false, bool useMesh = true) => Invoke(territoryType, [], gameObjectDataId, tollerance, lastPointTollerance, useAethernetTravel, useFlight, useMesh);
        
        internal static void Invoke(uint territoryType, Vector3 moveLocation, float tollerance = 0.25f, float lastPointTollerance = 0.25f, bool useAethernetTravel = false, bool useFlight = false, bool useMesh = true) => Invoke(territoryType, [moveLocation], 0, tollerance, lastPointTollerance, useAethernetTravel, useFlight, useMesh);

        internal static void Invoke(uint territoryType, List<Vector3> moveLocations, float tollerance = 0.25f, float lastPointTollerance = 0.25f, bool useAethernetTravel = false, bool useFlight = false, bool useMesh = true) => Invoke(territoryType, moveLocations, 0, tollerance, lastPointTollerance, useAethernetTravel, useFlight, useMesh);

        internal static void Invoke(uint territoryType, List<Vector3> moveLocations, uint gameObjectDataId, float tollerance, float lastPointTollerance, bool useAethernetTravel, bool useFlight, bool useMesh)
        {
            if (State != ActionState.Running)
            {
                Svc.Log.Info($"Goto Started, Going to {territoryType}{(moveLocations.Count > 0 ? $" and moving to {moveLocations[^1]} using {moveLocations.Count} pathLocations" : "")}");
                
                GotoHelper.territoryType      = territoryType;
                GotoHelper.gameObjectDataId   = gameObjectDataId;
                GotoHelper.moveLocations      = moveLocations;
                tolerance          = tollerance;
                lastPointTolerance            = lastPointTollerance;
                GotoHelper.useAethernetTravel = useAethernetTravel;
                GotoHelper.useFlight          = useFlight;
                GotoHelper.useMesh            = useMesh;
                Instance.Start();
            }
        }

        internal override unsafe void Stop() 
        {
            if (State == ActionState.Running) 
                this.InfoLog($"Goto Finished");
            Svc.Framework.Update -= this.HelperUpdate;
            State                =  ActionState.None;
            Plugin.states        &= ~PluginState.Other;
            if (!Plugin.states.HasFlag(PluginState.Looping) )
                Plugin.SetGeneralSettings(true);

            territoryType = 0;
            gameObjectDataId = 0;
            moveLocations = [];
            locationIndex = 0;
            tolerance = 0.25f;
            lastPointTolerance = 0.25f;
            useAethernetTravel = false;
            useFlight = false;
            useMesh = true;
            Plugin.action = "";

            if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno))
                addonSelectYesno->Close(true);
            if (VNavmesh_IPCSubscriber.IsEnabled && VNavmesh_IPCSubscriber.Path_IsRunning)
                VNavmesh_IPCSubscriber.Path_Stop();
        }

        private static uint          territoryType      = 0;
        private static uint          gameObjectDataId   = 0;
        private static List<Vector3> moveLocations      = [];
        private static int           locationIndex      = 0;
        private static float         tolerance          = 0.25f;
        private static float         lastPointTolerance = 0.25f;
        private static bool          useAethernetTravel = false;
        private static bool          useFlight          = false;
        private static bool          useMesh            = true;
        private static IGameObject?  GameObject => gameObjectDataId > 0 ? ObjectHelper.GetObjectByDataId(gameObjectDataId) : null;


        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (Plugin.states.HasFlag(PluginState.Navigating)) this.Stop();

            if (!EzThrottler.Check("Goto"))
                return;

            EzThrottler.Throttle("Goto", 50);

            Plugin.action = Loc.Get("Overlay.Actions.GoingTo", TerritoryName.GetTerritoryName(territoryType));

            if (!Player.Available)
                return;

            if (!PlayerHelper.IsValid || PlayerHelper.IsCasting || PlayerHelper.IsJumping || !VNavmesh_IPCSubscriber.Nav_IsReady)
                return;

            if (InDungeon)
            {
                ExitDutyHelper.Invoke();
                return;
            }

            if (Svc.ClientState.TerritoryType != territoryType)
            {
                GrandCompany which = territoryType switch
                {
                    128 => GrandCompany.Maelstrom,
                    132 => GrandCompany.TwinAdder,
                    130 => GrandCompany.ImmortalFlames,
                    _ => GrandCompany.Unemployed
                };

                bool         moveFromInnOrBarracks = territoryType is 128 or 132 or 130;
                
                if (moveFromInnOrBarracks && (Svc.ClientState.TerritoryType == GotoBarracksHelper.BarracksTerritoryType(which) || Svc.ClientState.TerritoryType == GotoInnHelper.InnTerritoryType(which)))
                {
                    IGameObject? exitGameObject = Svc.ClientState.TerritoryType == GotoBarracksHelper.BarracksTerritoryType(which) ? 
                                                      ObjectHelper.GetObjectByDataId(GotoBarracksHelper.ExitBarracksDoorDataId(which)) : 
                                                      ObjectHelper.GetObjectByDataId(GotoInnHelper.ExitInnDoorDataId(which));

                    if (MovementHelper.Move(exitGameObject, 0.25f, 3f))
                        if (ObjectHelper.InteractWithObjectUntilAddon(exitGameObject, "SelectYesno") != null)
                            AddonHelper.ClickSelectYesno();
                    return;
                }
               
                Aetheryte? aetheryte = MapHelper.GetClosestAetheryte(territoryType, moveLocations.Count > 0 ? moveLocations[0] : Vector3.Zero);
                if (aetheryte == null)
                {
                    aetheryte = MapHelper.GetClosestAethernet(territoryType, moveLocations.Count > 0 ? moveLocations[0] : Vector3.Zero);

                    if (aetheryte == null)
                    {
                        this.InfoLog($"We are unable to find the closest Aetheryte to: {territoryType}, Most likely the zone does not have one");

                        this.Stop();
                        return;
                    }

                    if (Svc.ClientState.TerritoryType != MapHelper.GetAetheryteForAethernet(aetheryte.Value)?.Territory.ValueNullable?.RowId)
                    {
                        TeleportHelper.TeleportAetheryte(MapHelper.GetAetheryteForAethernet(aetheryte.Value)?.RowId ?? 0, 0);
                        EzThrottler.Throttle("Goto", 7500, true);
                    }
                    else
                    {
                        if (TeleportHelper.MoveToClosestAetheryte())
                            TeleportHelper.TeleportAethernet(aetheryte.Value.AethernetName.ValueNullable?.Name.ToString() ?? "", territoryType);
                    }
                    return;
                }
                TeleportHelper.TeleportAetheryte(aetheryte?.RowId ?? 0, 0);
                EzThrottler.Throttle("Goto", 7500, true);
                return;
            }
            else if(useAethernetTravel)
            {
                Aetheryte? aetheryteLoc = MapHelper.GetClosestAethernet(territoryType, moveLocations.Count > 0 ? moveLocations[0] : Vector3.Zero);
                Aetheryte? aetheryteMe = MapHelper.GetClosestAethernet(territoryType, Player.Position);

                if (aetheryteLoc?.RowId != aetheryteMe?.RowId)
                {
                    if (TeleportHelper.MoveToClosestAetheryte()) 
                        TeleportHelper.TeleportAethernet(aetheryteLoc?.AethernetName.ValueNullable?.Name.ToString() ?? "", territoryType);
                    return;
                }
            }
            //Svc.Log.Info($"{_locationIndex < _moveLocations.Count} || ({_gameObject} != null && {ObjectHelper.GetDistanceToPlayer(_gameObject!)} > {_lastPointTollerance}) && {PlayerHelper.IsReady}");
            if (locationIndex < moveLocations.Count || (GameObject != null && ObjectHelper.GetDistanceToPlayer(GameObject) > lastPointTolerance) && PlayerHelper.IsReady)
            {
                Vector3 moveLoc;
                float pointTolerance = lastPointTolerance;
                if (GameObject != null)
                {
                    moveLoc = GameObject.Position;
                }
                else if (locationIndex < moveLocations.Count)
                {
                    moveLoc = moveLocations[locationIndex];
                    if (locationIndex < moveLocations.Count - 1)
                        pointTolerance = tolerance;
                }
                else
                {
                    return;
                }

                if (MovementHelper.Move(moveLoc, tolerance, pointTolerance, useFlight, useMesh))
                    locationIndex++;
                return;
            }

            this.Stop();
        }

        public override void OnCommand(string[] argsArray)
        {
            Svc.Log.Debug("going to " + argsArray[1]);
            switch (argsArray[1])
            {
                case "inn":
                    GotoInnHelper.Invoke(argsArray.Length > 2 ? 
                                             (GrandCompany) Convert.ToUInt32(argsArray[2]) : 
                                             PlayerHelper.GetGrandCompany());
                    break;
                case "barracks":
                    GotoBarracksHelper.Invoke();
                    break;
                case "gc":
                case "gcsupply":
                    Invoke(PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()), [GCTurninHelper.GCSupplyLocation], 0.25f, 2f, false);
                    break;
                case "ar":
                case "bell":
                case "summoningbell":
                    SummoningBellHelper.Invoke(Configuration.PreferredSummoningBellEnum);
                    break;
                case "ap":
                case "apartment":
                    GotoHousingHelper.Invoke(Housing.Apartment);
                    break;
                case "personal":
                case "home":
                case "personalhome":
                    GotoHousingHelper.Invoke(Housing.Personal_Home);
                    break;
                case "estate":
                case "fc":
                case "fcestate":
                    GotoHousingHelper.Invoke(Housing.FC_Estate);
                    break;
            }
        }
    }
}
