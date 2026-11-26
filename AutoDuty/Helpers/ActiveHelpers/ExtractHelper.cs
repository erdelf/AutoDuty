using AutoDuty.Configurations;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    public class ExtractHelper : ActiveHelperBase<ExtractHelper, ExtractLoopActionConfig>
    {
        public override string Name        => nameof(ExtractHelper);
        public override string DisplayName => "Extracting Materia";

        public override string[]? Commands { get; init; } = ["extract"];
        public override string? CommandDescription { get; init; } = "Extract's materia from equipment";

        protected override string[] AddonsToClose { get; } = ["Materialize", "MaterializeDialog", "SelectYesno", "SelectString"];

        internal override void Start()
        {
            if (!QuestManager.IsQuestComplete(66174))
            {
                ADLogging.Info("Materia Extraction requires having completed quest: Forging the Spirit");
            }
            else
            {
                base.Start();

                this.stoppingCategory = this.ActionConfig.AutoExtractAll ? 6 : 0;
            }
        }

        internal override void Stop()
        {
            this.currentCategory  = 0;
            this.switchedCategory = false;
            base.Stop();
        }

        private int currentCategory = 0;
        private int stoppingCategory;
        private bool switchedCategory = false;

        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (Plugin.States.HasFlag(PluginState.Navigating) || InDungeon) this.Stop();

            if (!EzThrottler.Throttle("Extract", 250))
                return;

            if (Conditions.Instance()->Mounted)
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
                return;
            }

            Plugin.action = "Extracting Materia";

            if (InventoryManager.Instance()->GetEmptySlotsInBag() < 1)
            {
                this.Stop();
                return;
            }

            if (PlayerHelper.IsOccupied)
                return;

            if (GenericHelpers.TryGetAddonByName("MaterializeDialog", out AtkUnitBase* addonMaterializeDialog) && GenericHelpers.IsAddonReady(addonMaterializeDialog))
            {
                ADLogging.Debug("AutoExtract - Confirming MaterializeDialog");
                new AddonMaster.MaterializeDialog(addonMaterializeDialog).Materialize();
                return;
            }

            if (!GenericHelpers.TryGetAddonByName("Materialize", out AtkUnitBase* addonMaterialize))
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 14);
            }
            else if (GenericHelpers.IsAddonReady(addonMaterialize))
            {
                if (this.currentCategory <= this.stoppingCategory)
                {
                    AtkComponentList* list = addonMaterialize->GetNodeById(12)->GetAsAtkComponentList();

                    if (list == null) return;

                    AtkTextNode* spiritbondTextNode = list->UldManager.NodeList[2]->GetComponent()->GetTextNodeById(5)->GetAsAtkTextNode();
                    AtkTextNode* categoryTextNode   = addonMaterialize->GetNodeById(4)->GetAsAtkComponentDropdownList()->UldManager.NodeList[1]->GetAsAtkComponentCheckBox()->GetTextNodeById(3)->GetAsAtkTextNode();

                    if (spiritbondTextNode == null || categoryTextNode == null) return;

                    //switch to Category, if not on it
                    if (!this.switchedCategory)
                    {
                        ADLogging.Debug($"AutoExtract - Switching to Category: {this.currentCategory}");
                        AddonHelper.FireCallBack(addonMaterialize, false, 1, this.currentCategory);
                        this.switchedCategory = true;
                        return;
                    }

                    if (spiritbondTextNode->NodeText.ToString().Replace(" ", string.Empty) == "100%")
                    {
                        ADLogging.Debug($"AutoExtract - Extracting Materia");
                        AddonHelper.FireCallBack(addonMaterialize, true, 2, 0);
                        return;
                    }
                    else
                    {
                        this.currentCategory++;
                        this.switchedCategory = false;
                    }
                }
                else
                {
                    addonMaterialize->Close(true);
                    ADLogging.Info("Extract Materia Finished");
                    this.Stop();
                }
            }
        }
    }
}
