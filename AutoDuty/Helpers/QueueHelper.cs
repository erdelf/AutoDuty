using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using global::AutoDuty.Managers;

namespace AutoDuty.Helpers
{
    using Dalamud.Utility.Signatures;
    using FFXIVClientStructs.Interop;
    using FFXIVClientStructs.STD;
    using Multibox;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Lumina.Excel.Sheets;
    using static Data.Classes;

    internal unsafe class QueueHelper : ActiveHelperBase<QueueHelper>
    {
        public QueueHelper()
        {
            Svc.Hook.InitializeFromAttributes(this);
        }

        internal static void InvokeAcceptOnly()
        {
            _dutyMode = DutyMode.None;
            Svc.Log.Info("Queueing: Accepting only");
            Instance.Start();
            Plugin.action = Loc.Get("Overlay.Actions.QueueingWaiting");
        }

        internal static void Invoke(Content? content, DutyMode dutyMode)
        {
            if (State != ActionState.Running && content != null && dutyMode != DutyMode.None && (!_dutyMode.HasAnyFlag(DutyMode.Regular, DutyMode.Trial, DutyMode.Raid) || AutoDuty.Configuration.Unsynced || AutoDuty.Configuration.OverridePartyValidation))
            {
                _dutyMode = dutyMode;
                _content = content;
                Svc.Log.Info($"Queueing: {dutyMode}: {content.Name}");

                Instance.Start();
                Plugin.action = Loc.Get("Overlay.Actions.Queueing", _dutyMode, content.Name ?? "");
            }
        }

        protected override string Name        => nameof(QueueHelper);
        protected override string DisplayName => $"Queueing {_dutyMode}: {_content?.Name}";

        internal override void Stop()
        {
            if (State == ActionState.Running)
                Svc.Log.Info($"Done Queueing: {_dutyMode}: {_content?.Name}");
            _content                     = null;
            this._allConditionsMetToJoin = false;
            this._turnedOffTrustMembers  = false;
            this._turnedOnConfigMembers     = false;
            _dutyMode                    = DutyMode.None;

            base.Stop();
        }

        private static Content? _content = null;
        private static DutyMode _dutyMode = DutyMode.None;
        private AddonContentsFinder* _addonContentsFinder = null;
        private bool _allConditionsMetToJoin = false;
        private bool _turnedOffTrustMembers = false;
        private bool _turnedOnConfigMembers = false;

        private static bool ContentsFinderConfirm()
        {
            if (GenericHelpers.TryGetAddonByName("ContentsFinderConfirm", out AtkUnitBase* addonContentsFinderConfirm) && GenericHelpers.IsAddonReady(addonContentsFinderConfirm))
            {
                Svc.Log.Debug("Queue Helper - Confirming DutyPop");
                AddonHelper.FireCallBack(addonContentsFinderConfirm, true, 8);
                return true;
            }
            return false;
        }

        private void QueueTrust()
        {
            if (TrustHelper.State == ActionState.Running) 
                return;

            AgentDawn* agentDawn = AgentDawn.Instance();
            if (!TrustHelper.LevelsSetFor(_content))
            {
                TrustHelper.GetLevels(_content);
                return;
            }

            if (!agentDawn->IsAddonReady())
            {
                if (!EzThrottler.Throttle("OpenDawn", 5000) || !AgentHUD.Instance()->IsMainCommandEnabled(82)) 
                    return;

                Svc.Log.Debug("Queue Helper - Opening Dawn");
                RaptureAtkModule.Instance()->OpenDawn(_content.RowId);
                return;
            }

            if (agentDawn->Data->ContentData.ExpansionCount < (_content!.ExVersion - 2))
            {
                Svc.Log.Debug($"Queue Helper - You do not have expansion: {_content.ExVersion} unlocked stopping");
                this.Stop();
                return;
            }

            if(!_content.CanTrustRun())
            {
                Svc.Log.Debug("Queue Helper - Trust can't run, stopping QueueHelper");
                this.Stop();
                Plugin.Stage = Stage.Stopped;
                return;
            }

            if ((byte) agentDawn->SelectedContentId != _content.DawnRowId)
            {
                Svc.Log.Debug($"Queue Helper - Clicking: {_content.EnglishName} at {_content.RowId} with dawn {_content.DawnRowId} instead of {agentDawn->SelectedContentId}");
                RaptureAtkModule.Instance()->OpenDawn(_content.RowId);
            }
            else if (!this._turnedOffTrustMembers)
            {
                if (EzThrottler.Throttle("_turnedOffTrustMembers", 500))
                {
                    agentDawn->Data->PartyData.ClearParty();
                    agentDawn->UpdateAddon();
                    SchedulerHelper.ScheduleAction("_turnedOffTrustMembers", () => this._turnedOffTrustMembers = true, 250);
                }
            }
            else if (!this._turnedOnConfigMembers)
            {
                if (EzThrottler.Throttle("_turnedOnConfigMembers", 500))
                {
                    TrustHelper.ResetTrustIfInvalid();
                    AgentDawnInterface.DawnMemberEntry* curMembers = agentDawn->Data->MemberData.GetMembers(agentDawn->Data->MemberData.CurrentMembersIndex);
                    TrustMemberName?[]                  members    = AutoDuty.Configuration.SelectedTrustMembers;
                    if (members.Any(x => x is null || TrustHelper.Members[(TrustMemberName)x!].Level < _content.ClassJobLevelRequired))
                    {
                        Svc.Log.Info("Not all trust members selected. Selecting automatically now");
                        TrustHelper.SetLevelingTrustMembers(_content, LevelingMode.Trust_Solo);
                    }
                    
                    members.OrderBy(x => TrustHelper.Members[(TrustMemberName)x!].Role)
                           .Each(member =>
                                 {
                                     if (member != null)
                                     {
                                         byte                               index       = TrustHelper.Members[(TrustMemberName)member].Index;
                                         AgentDawnInterface.DawnMemberEntry memberEntry = curMembers[index];

                                         agentDawn->Data->PartyData.AddMember(index, &memberEntry);
                                     }
                                 });

                    agentDawn->UpdateAddon();
                    SchedulerHelper.ScheduleAction("_turnedOnConfigMembers", () => this._turnedOnConfigMembers = true, 250);
                }
            }
            else if(EzThrottler.Throttle("ClickRegisterButton", 10000))
            {
                Svc.Log.Debug($"Queue Helper - Clicking: Register For Duty");
                agentDawn->RegisterForDuty();
            }
        }

        private void QueueSupport()
        {
            AgentDawnStory* agentDawnStory = AgentDawnStory.Instance();
            if (!agentDawnStory->IsAddonReady())
            {
                if (!EzThrottler.Throttle("OpenDawnStory", 5000) || !AgentHUD.Instance()->IsMainCommandEnabled(91)) return;
                
                Svc.Log.Debug("Queue Helper - Opening DawnStory");
                RaptureAtkModule.Instance()->OpenDawnStory(_content.Id);
                return;
            }

            if (agentDawnStory->Data->ContentData.ExpansionCount <= _content!.ExVersion)
            {
                Svc.Log.Debug($"Queue Helper - You do not have expansion: {_content.ExVersion} unlocked. stopping");
                this.Stop();
                return;
            }

            if (agentDawnStory->Data->ContentData.ContentEntries[agentDawnStory->Data->ContentData.SelectedContentEntry].ContentFinderConditionId != _content.RowId)
            {
                Svc.Log.Debug($"Queue Helper - Clicking: {_content.EnglishName} {_content.RowId}");// instead of {agentDawnStory->Data->ContentData.ContentEntries[agentDawnStory->Data->ContentData.SelectedContentEntry].ContentFinderConditionId}");

                RaptureAtkModule.Instance()->OpenDawnStory(_content.RowId);
            }
            else if(EzThrottler.Throttle("ClickRegisterButton", 10000))
            {
                Svc.Log.Debug($"Queue Helper - Clicking: Register For Duty");
                AgentDawnStory.Instance()->RegisterForDuty();
            }
        }

        public static bool ShouldBeUnSynced() =>
            AutoDuty.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist ? 
                (Plugin.PlaylistCurrentEntry?.unsynced == true && Plugin.PlaylistCurrentEntry?.DutyMode.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial) == true) : 
                AutoDuty.Configuration.Unsynced && AutoDuty.Configuration.DutyModeEnum.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);

        private void QueueRegular()
        {
            bool unsync = ShouldBeUnSynced();
            Svc.Log.Debug($"Sync check: {unsync}");
            if (ContentsFinder.Instance()->IsUnrestrictedParty != unsync)
            {
                Svc.Log.Debug("Queue Helper - Setting UnrestrictedParty");
                ContentsFinder.Instance()->IsUnrestrictedParty = unsync;
                return;
            }

            GenericHelpers.TryGetAddonByName("ContentsFinder", out this._addonContentsFinder);
            if (!this._allConditionsMetToJoin && (this._addonContentsFinder == null || !GenericHelpers.IsAddonReady((AtkUnitBase*)this._addonContentsFinder)))
            {
                if (!AgentHUD.Instance()->IsMainCommandEnabled(33))
                    return;
                Svc.Log.Debug($"Queue Helper - Opening ContentsFinder to {_content!.Name}");
                AgentContentsFinder.Instance()->OpenRegularDuty(_content.ContentFinderCondition);
                return;
            }

            if (this._addonContentsFinder->DutyList->Items.LongCount == 0)
                return;

            StdVector<Pointer<AtkComponentTreeListItem>> vectorDutyListItems           = this._addonContentsFinder->DutyList->Items;
            List<AtkComponentTreeListItem>               listAtkComponentTreeListItems = [];
            if (vectorDutyListItems.Count == 0)
                return;
            
            vectorDutyListItems.ForEach(pointAtkComponentTreeListItem => listAtkComponentTreeListItems.Add(*(pointAtkComponentTreeListItem.Value)));

            if (!this._allConditionsMetToJoin && AgentContentsFinder.Instance()->SelectedDuty.Id != _content!.ContentFinderCondition)
            {
                Svc.Log.Debug($"Queue Helper - Opening ContentsFinder to {_content.Name} because we have the wrong selection of {listAtkComponentTreeListItems[(int)this._addonContentsFinder->DutyList->SelectedItemIndex].Renderer->GetTextNodeById(5)->GetAsAtkTextNode()->NodeText.ToString().Replace("...", "")}");
                AgentContentsFinder.Instance()->OpenRegularDuty(_content.ContentFinderCondition);
                EzThrottler.Throttle("QueueHelper", 500, true);
                return;
            }

            string? selectedDutyName = this._addonContentsFinder->AtkValues[18].GetValueAsString().Replace("\u0002\u001a\u0002\u0002\u0003", string.Empty).Replace("\u0002\u001a\u0002\u0001\u0003", string.Empty).Replace("\u0002\u001f\u0001\u0003", "\u2013");
            if (selectedDutyName != _content!.Name && !string.IsNullOrEmpty(selectedDutyName))
            {
                Svc.Log.Debug($"Queue Helper - We have {selectedDutyName} selected, not {_content.Name}, Clearing.");
                AddonHelper.FireCallBack((AtkUnitBase*)this._addonContentsFinder, true, 12, 1);
                return;
            }

            if (string.IsNullOrEmpty(selectedDutyName))
            {
                Svc.Log.Debug("Queue Helper - Checking Duty");
                SelectDuty(this._addonContentsFinder);
                return;
            }

            if (selectedDutyName == _content.Name)
            {
                this._allConditionsMetToJoin = true;
                Svc.Log.Debug("Queue Helper - All Conditions Met, Clicking Join");
                AddonHelper.FireCallBack((AtkUnitBase*)this._addonContentsFinder, true, 12, 0);

                if(MultiboxUtility.Config is { MultiBox: true, Host: true })
                    MultiboxUtility.Server.Queue();
                return;
            }
            Svc.Log.Debug("end");
        }

        public delegate bool QueueNoviceTutorialDelegate(uint tutorialRowId);

        [Signature("E8 ?? ?? ?? ?? 48 8B 07 48 8B CF C7 47 ?? ?? ?? ?? ?? FF 50 28 40 B6 01")]
        public QueueNoviceTutorialDelegate QueueNoviceTutorial;

        private void QueueNovice()
        {
            if (_content == null)
                return;
            Tutorial? tutorial = NoviceHelper.GetTutorialFromTerritory(_content.TerritoryType);
            if (!tutorial.HasValue)
                return;
            this.DebugLog("Queueing for tutorial " + tutorial.Value.RowId);
            this.QueueNoviceTutorial(tutorial.Value.RowId);
        }

        protected override void HelperUpdate(IFramework framework)
        {
            if (InDungeon || _dutyMode != DutyMode.None && (_content == null || Svc.ClientState.TerritoryType == _content?.TerritoryType)) 
                this.Stop();

            if (!EzThrottler.Throttle("QueueHelper", 250)|| !PlayerHelper.IsReadyFull || ContentsFinderConfirm() || Conditions.Instance()->InDutyQueue) return;

            switch (_dutyMode)
            {
                case DutyMode.Regular:
                case DutyMode.Trial:
                case DutyMode.Raid:
                    try
                    {
                        this.QueueRegular();
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Error(ex.ToString());
                    }
                    break;
                case DutyMode.Support:
                    this.QueueSupport();
                    break;
                case DutyMode.Trust:
                    this.QueueTrust();
                    break;
                case DutyMode.NoviceHall:
                    this.QueueNovice();
                    break;
            }
        }

        private static uint HeadersCount(int before, List<AtkComponentTreeListItem> list)
        {
            uint count = 0;
            try
            {
                for (int i = 0; i < before; i++)
                {
                    if (list[i].UIntValues[0] == 0 || list[i].UIntValues[0] == 1)
                        count++;
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex.ToString());
            }

            return count;
        }

        private static void SelectDuty(AddonContentsFinder* addonContentsFinder)
        {
            if (addonContentsFinder == null) return;
            
            StdVector<Pointer<AtkComponentTreeListItem>>                    vectorDutyListItems           = addonContentsFinder->DutyList->Items;
            List<AtkComponentTreeListItem> listAtkComponentTreeListItems = [];
            vectorDutyListItems.ForEach(pointAtkComponentTreeListItem => listAtkComponentTreeListItems.Add(*(pointAtkComponentTreeListItem.Value)));
            AddonHelper.FireCallBack((AtkUnitBase*)addonContentsFinder, true, 3, HeadersCount(addonContentsFinder->DutyList->SelectedItemIndex, listAtkComponentTreeListItems) + 1); // - (HeadersCount(addonContentsFinder->DutyList->SelectedItemIndex, listAtkComponentTreeListItems) + 1));
        }
    }
}
