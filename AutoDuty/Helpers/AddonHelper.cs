using ECommons;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using System;

    internal static unsafe class AddonHelper
    {
        internal static bool SeenAddon = false;

        internal static unsafe void FireCallBack(AtkUnitBase* addon, bool updateState, params object[] args)
        {
            AtkUnitBase* addonPtr = addon;
            if (addon == null || addonPtr is null) return;
            try
            {
                Callback.Fire(addonPtr, updateState, args);
            }
            catch (Exception ex) 
            { 
                ADLogging.Error($"{ex}");
            }
        }

        internal static bool ClickSelectString(int index)
        {
            bool addonChecker = AddonChecker("SelectString", out AtkUnitBase* addon, out bool seenAddon);

            if (!addonChecker && seenAddon)
                new AddonMaster.SelectString(addon).Entries[index].Select();

            if (addonChecker && seenAddon)
                return true;

            return false;
        }

        internal static bool ClickSelectIconString(int index)
        {
            bool addonChecker = AddonChecker("SelectIconString", out AtkUnitBase* addon, out bool seenAddon);

            if (!addonChecker && seenAddon)
                new AddonMaster.SelectIconString(addon).Entries[index].Select();

            if (addonChecker && seenAddon)
                return true;

            return false;
        }

        internal static bool ClickSelectYesno(bool yes = true)
        {
            if (!EzThrottler.Throttle("ClickSelectYesno", 500)) return false;

            bool addonChecker = AddonChecker("SelectYesno", out AtkUnitBase* addon, out bool seenAddon);

            if (!addonChecker && seenAddon)
            {
                if (yes)
                    new AddonMaster.SelectYesno(addon).Yes();
                else
                    new AddonMaster.SelectYesno(addon).No();
                return false;
            }

            return addonChecker && seenAddon;
        }

        internal static bool SelectJournalResult(bool accept)
        {
            if (!EzThrottler.Throttle("JournalResult", 500)) 
                return false;

            bool addonChecker = AddonChecker("JournalResult", out AtkUnitBase* addon, out bool seenAddon);

            if (!addonChecker && seenAddon)
            {
                AddonMaster.JournalResult journalResult = new(addon);
                if(accept)
                    journalResult.Complete();
                else
                    journalResult.Decline();
                return false;
            }

            return addonChecker && seenAddon;
        }

        internal static bool ClickRepair()
        {
            bool addonChecker = AddonChecker("Repair", out AtkUnitBase* addon, out bool seenAddon);

            if (!addonChecker && seenAddon)
                new AddonMaster.Repair(addon).RepairAll();

            if (addonChecker && seenAddon)
                return true;

            return false;
        }

        internal static bool ClickTalk()
        {
            if (!EzThrottler.Throttle("ClickTalk", 500)) return false;

            bool addonChecker = AddonChecker("Talk", out AtkUnitBase* addon, out bool seenAddon);

            if (!addonChecker && seenAddon)
                new AddonMaster.Talk(addon).Click();
            
            if (addonChecker && seenAddon)
                return true;
                    
            return false;
        }

        private static bool AddonChecker(string addonName, out AtkUnitBase* outAddon, out bool outSeenAddon)
        {
            outSeenAddon = false;
            
            bool gotAddon = GenericHelpers.TryGetAddonByName(addonName, out outAddon);
            bool addonReady = gotAddon && GenericHelpers.IsAddonReady(outAddon);

            if (gotAddon && addonReady)
            {
                outSeenAddon = true;
                SeenAddon = true;
                return false;
            }

            if (!Player.Character->IsCasting && SeenAddon && (!gotAddon || !addonReady))
            {
                outSeenAddon = true;
                SeenAddon = false;
                return true;
            }
            return false;
        }

        public static void ClickCheckboxButton(this AtkComponentCheckBox target, AtkComponentBase* addon, uint which, EventType type = EventType.CHANGE) =>
            ClickHelper.ClickAddonComponent(addon, target.OwnerNode, which, type);
    }
}
