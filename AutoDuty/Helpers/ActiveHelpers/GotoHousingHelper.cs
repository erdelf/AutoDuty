using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using System.Numerics;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using FFXIVClientStructs.FFXIV.Client.Game;
    using global::AutoDuty.Configurations;
    using IPC;
    using System;
    using System.Collections.Generic;

    public class GotoHousingHelper : ActiveHelperBase<GotoHousingHelper, LoopActionConfigBare>
    {
        public override string Name        { get; } = nameof(GotoHousingHelper);
        public override string DisplayName { get; } = string.Empty;

        protected override string[] AddonsToClose { get; } = ["SelectYesno", "SelectString", "HousingWardSelection", "HousingWardSelectionDialog"];

        protected override int TimeOut { get; set; } = 600_000;

        internal static void Invoke(Housing whichHousing)
        {
            if (!InPrivateHouse(whichHousing))
            {
                GotoHousingHelper.whichHousing = whichHousing;
                Instance.Start();
            }
        }

        internal override void Stop() 
        {
            GotoHelper.ForceStop();
            base.Stop();
            whichHousing = Housing.Apartment;
            this.index   = 0;
        }

        private static HouseId GetOwnedHouseId(Housing whichHousing)
        {
            return HousingManager.GetOwnedHouseId(whichHousing switch
            {
                Housing.Apartment => EstateType.ApartmentRoom,
                Housing.Personal_Home => EstateType.PersonalEstate,
                Housing.FC_Estate => EstateType.FreeCompanyEstate,
                _ => throw new ArgumentOutOfRangeException(nameof(whichHousing), whichHousing, null)
            });
        }

        internal static unsafe bool InPrivateHouse(Housing whichHousing) => 
            HousingManager.Instance()->GetCurrentIndoorHouseId() == GetOwnedHouseId(whichHousing);

        internal static bool InHousingArea(Housing whichHousing) =>
            GetOwnedHouseId(whichHousing).TerritoryTypeId == Svc.ClientState.TerritoryType;

        private static IGameObject? EntranceGameObject => whichHousing switch
        {
            Housing.FC_Estate => TeleportHelper.FCEstateEntranceGameObject,
            Housing.Personal_Home => TeleportHelper.PersonalHomeEntranceGameObject,
            _ => TeleportHelper.ApartmentEntranceGameObject
        };
        private static Housing whichHousing = Housing.Apartment;
        private int index = 0;

        protected override void HelperUpdate(IFramework framework)
        {
            if (Plugin.States.HasFlag(PluginState.Navigating))
            {
                ADLogging.Debug($"AutoDuty has Started, Stopping GotoHousing");
                this.Stop();
            }

            if (!EzThrottler.Check("GotoHousing"))
                return;

            EzThrottler.Throttle("GotoHousing", 50);

            if (!Player.Available)
            {
                ADLogging.Debug($"Our player is null");
                return;
            }

            if (GotoHelper.State == ActionState.Running || Lifestream_IPCSubscriber.IsBusy)
                return;

            Plugin.action = $"Retiring to {whichHousing}";

            if (InPrivateHouse(whichHousing))
            {
                ADLogging.Debug($"We are in a private house, Stopping GotoHousing");
                this.Stop();
                return;
            }

            if (!InHousingArea(whichHousing))
            {
                if (!PlayerHelper.IsCasting)
                {
                    ADLogging.Debug($"We are not in the correct housing area, teleporting there");
                    switch (whichHousing)
                    {
                        case Housing.Apartment when !TeleportHelper.TeleportApartment()        && TeleportHelper.ApartmentTeleportId    == 0:
                        case Housing.Personal_Home when !TeleportHelper.TeleportPersonalHome() && TeleportHelper.PersonalHomeTeleportId == 0:
                        case Housing.FC_Estate when !TeleportHelper.TeleportFCEstate()         && TeleportHelper.FCEstateTeleportId     == 0:
                            this.Stop();
                            return;
                        default:
                            EzThrottler.Throttle("GotoHousing", 7500, true);
                            break;
                    }
                }
                return;
            }
            else if (PlayerHelper.IsValid)
            {
                if (EntranceGameObject == null)
                {
                    ADLogging.Debug($"unable to find entrance door {TeleportHelper.FCEstateWardCenterVector3} {TeleportHelper.FCEstateEntranceGameObject}");
                }
                else if (MovementHelper.Move(EntranceGameObject, 0.25f, 3f, false, false))
                {
                    ADLogging.Debug($"We are in range of the entrance door, entering");
                    ObjectHelper.InteractWithObject(EntranceGameObject);
                    AddonHelper.ClickSelectString(0);
                    AddonHelper.ClickSelectYesno();
                    AddonHelper.ClickTalk();
                }
                else
                {
                    ADLogging.Debug($"Moving closer to {EntranceGameObject?.Name} at location {EntranceGameObject?.Position}, we are {Vector3.Distance(EntranceGameObject?.Position ?? Vector3.Zero, Player.Position)} away");
                }
            }
        }
    }
}
