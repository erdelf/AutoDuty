using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Configurations;
    using Lumina.Excel;
    using static Data.Classes;

    internal static class TrustHelper
    {
        internal static unsafe uint GetLevelFromTrustWindow(AtkUnitBase* addon)
        {
            if (addon == null)
                return 0;

            AtkComponentNode* atkResNode       = addon->GetComponentNodeById(88);
            AtkResNode*       resNode          = atkResNode->Component->UldManager.NodeList[5];
            AtkResNode*       resNodeChildNode = resNode->GetComponent()->UldManager.NodeList[0];
            return Convert.ToUInt32(resNodeChildNode->GetAsAtkCounterNode()->NodeText.GetText());
        }


        internal static bool CanTrustRun(this Content content, bool checkTrustLevels = true)
        {
            if (!content.DutyModes.HasFlag(DutyMode.Trust))
                return false;

            if (!UIState.IsInstanceContentCompleted(content.Id) && !QuestManager.IsQuestComplete(content.UnlockQuest))
                return false;

            return !checkTrustLevels || CanTrustRunMembers(content);
        }

        internal static bool LevelsSetFor(Content content) => 
            !content.TrustMembers.Any(tm => tm is { LevelIsSet: false, Available: true });

        internal static bool LevelsSet() => 
            !Members.Values.Any(tm => tm is { LevelIsSet: false, Available: true });

        private static bool CanTrustRunMembers(Content content)
        {
            if (!LevelsSetFor(content))
                GetLevels(content);

            TrustMember?[] members = new TrustMember?[3];

            int index = 0;
            foreach (TrustMember member in content.TrustMembers)
                if (member.Level >= content.ClassJobLevelRequired && members.CanSelectMember(member, Player.Available ? Player.Job.GetCombatRole() : CombatRole.NonCombat))
                {
                    members[index++] = member;
                    if (index >= 3)
                        return true;
                }

            return false;
        }

        internal static bool SetLevelingTrustMembers(Content content, LevelingMode mode)
        {
            Job        playerJob  = PlayerHelper.GetJob();
            CombatRole playerRole = playerJob.GetCombatRole();

            if (Members.All(tm => tm.Value.Level >= tm.Value.LevelCap) && AutoDuty.Configuration.SelectedTrustMembers.All(tmn => tmn.HasValue))
            {
                bool test = true;

                for (int i = 0; i < 3 && test; i++)
                {
                    TrustMember?[] curMembers = [..AutoDuty.Configuration.SelectedTrustMembers.Select(tmn => Members[tmn!.Value])];
                    TrustMember    testMember = curMembers[i]!;
                    curMembers[i] = null;
                    test &= curMembers.CanSelectMember(testMember, playerRole);
                }

                if (test)
                {
                    ADLogging.Info("Leveling Trust Members retained from previous selection");
                    return true;
                }
            }

            AutoDuty.Configuration.SelectedTrustMembers = new TrustMemberName?[3];

            TrustMember?[] trustMembers = new TrustMember?[3];

            JobRole playerJobRole = Player.Available ? Player.ClassJob.ValueNullable?.GetJobRole() ?? JobRole.None : JobRole.None;

            ADLogging.Info("Leveling Trust Members set start");
            ADLogging.Info("Trustmembers available: " + content.TrustMembers.Count);

            int index = 0;

            try
            {
                TrustMember[] membersPossible = [.. content.TrustMembers.Where(tm => tm.Level >= content.ClassJobLevelRequired)
                              .OrderBy(tm => (mode != LevelingMode.Trust_Solo ? tm.Level : Members.First().Value.LevelCap - tm.Level) + (tm.Level < tm.LevelCap ? 0 : 100) + (playerRole == CombatRole.DPS ? playerJobRole == tm.Job?.GetJobRole() ? 0.5f : 0 : 0))];
                foreach (TrustMember member in membersPossible)
                {
                    ADLogging.Info("checking: " + member.Name);

                    if (trustMembers.CanSelectMember(member, playerRole))
                    {
                        ADLogging.Info("check successful");
                        trustMembers[index++] = member;

                        if (index >= 3)
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ADLogging.Error(ex.ToString());
            }

            if (trustMembers.All(tm => tm != null))
            {
                AutoDuty.Configuration.SelectedTrustMembers = [..trustMembers.Select(tm => tm?.MemberName)];
                ConfigurationProfileV2.Save();
                return true;
            }

            return false;
        }


        public static bool CanSelectMember(this TrustMember?[] trustMembers, TrustMember member, CombatRole playerRole) =>
            playerRole != CombatRole.NonCombat &&
            member.Available &&
            member.Role switch
            {
                TrustRole.DPS => playerRole == CombatRole.DPS && !trustMembers.Where(x => x != null).Any(x => x!.Role is TrustRole.DPS) ||
                                 playerRole != CombatRole.DPS && trustMembers.Where(x => x  != null).Count(x => x!.Role is TrustRole.DPS) < 2,
                TrustRole.Healer => playerRole != CombatRole.Healer && !trustMembers.Where(x => x != null).Any(x => x!.Role is TrustRole.Healer),
                TrustRole.Tank => playerRole   != CombatRole.Tank   && !trustMembers.Where(x => x != null).Any(x => x!.Role is TrustRole.Tank),
                TrustRole.AllRounder => true,
                _ => throw new ArgumentOutOfRangeException(member.Name, @"member is of invalid role.. somehow")
            };

        internal static readonly Dictionary<TrustMemberName, TrustMember> Members           = [];
        internal static readonly Dictionary<byte, TrustMember>            MembersByMemberId = [];

        internal static void PopulateTrustMembers()
        {
            ExcelSheet<DawnMemberUIParam>? dawnSheet = Svc.Data.GetExcelSheet<DawnMemberUIParam>();
            ExcelSheet<ClassJob>?          jobSheet  = Svc.Data.GetExcelSheet<ClassJob>();

            if (dawnSheet == null || jobSheet == null)
                return;

            void AddMember(TrustMemberName name, byte index, byte[] memberId, TrustRole role, Job classJob, uint levelInit = 71, uint levelCap = 100, uint unlockQuest = 0)
            {
                TrustMember member = new()
                                     {
                                         Index       = index,
                                         MemberIds   = memberId,
                                         Name        = dawnSheet.GetRow((uint)name)!.Name.ToString(),
                                         Role        = role,
                                         Job         = jobSheet.GetRow((uint)classJob)!,
                                         MemberName  = name,
                                         LevelInit   = levelInit,
                                         Level       = levelInit,
                                         LevelCap    = levelCap,
                                         LevelIsSet  = levelInit == levelCap,
                                         UnlockQuest = unlockQuest
                                     };
                Members.Add(name, member);

                foreach (byte id in memberId)
                    MembersByMemberId.Add(id, member);
            }

            AddMember(TrustMemberName.Alphinaud, 0, [0,6],      TrustRole.Healer,     Job.SGE);
            AddMember(TrustMemberName.Alisaie,   1, [1],        TrustRole.DPS,        Job.RDM);
            AddMember(TrustMemberName.Thancred,  2, [2],        TrustRole.Tank,       Job.GNB);
            AddMember(TrustMemberName.Urianger,  3, [3],        TrustRole.Healer,     Job.AST);
            AddMember(TrustMemberName.Yshtola,   4, [4],        TrustRole.DPS,        Job.BLM);
            AddMember(TrustMemberName.Ryne,      5, [5],        TrustRole.DPS,        Job.ROG,       71, 80);
            AddMember(TrustMemberName.Estinien,  5, [7],        TrustRole.DPS,        Job.DRG,     81);
            AddMember(TrustMemberName.Graha,     6, [8, 9, 10], TrustRole.AllRounder, Job.BLM,  81, unlockQuest: 69318);
            AddMember(TrustMemberName.Zero,      7, [103],      TrustRole.DPS,        Job.RPR,      90, 90);
            AddMember(TrustMemberName.Krile,     7, [11],       TrustRole.DPS,        Job.PCT, 91);
        }

        public static void ResetTrustIfInvalid()
        {
            if (!PlayerHelper.IsValid) return;

            if (AutoDuty.Configuration.SelectedTrustMembers.Count(x => x is not null) == 3)
            {
                CombatRole playerRole = Player.Job.GetCombatRole();

                TrustMember[] trustMembers = [.. AutoDuty.Configuration.SelectedTrustMembers.Select(name => Members[(TrustMemberName)name!])];

                int dps = trustMembers.Count(x => x.Role is TrustRole.DPS);
                int healers = trustMembers.Count(x => x.Role is TrustRole.Healer);
                int tanks = trustMembers.Count(x => x.Role is TrustRole.Tank);

                bool needsReset = playerRole switch
                {
                    CombatRole.DPS => dps == 2,
                    CombatRole.Healer => healers == 1,
                    CombatRole.Tank => tanks == 1,
                    _ => false
                } || trustMembers.Any(tm => tm.Level < Plugin.CurrentTerritoryContent?.ClassJobLevelRequired);

                if (needsReset)
                {
                    AutoDuty.Configuration.SelectedTrustMembers = new TrustMemberName?[3];
                    ConfigurationProfileV2.Save();
                }
            }
        }

        internal static void ClearCachedLevels() => Members.Each(x => x.Value.ResetLevel());

        internal static void ClearCachedLevels(Content content) => content?.TrustMembers.Each(x => { if (x.Level < x.LevelCap) x.ResetLevel(); });

        internal static void GetLevels(Content? content)
        {
            if (State == ActionState.Running) return;
                    
            _getLevelsContent = content;

            _getLevelsContent ??= Plugin.CurrentTerritoryContent;

            if (_getLevelsContent == null)
            {
                ADLogging.Debug($"Get Trust Levels: our content was null, returning");
                return;
            }
            if (_getLevelsContent.DawnIndex < 0)
            {
                ADLogging.Debug($"Get Trust Levels: our content was not dawn content, returning");
                return;
            }
            if (_getLevelsContent.TrustMembers.TrueForAll(tm => tm.LevelIsSet || !tm.Available))
            {
                ADLogging.Debug($"Get Trust Levels: we already have all our trust levels, returning");
                return;
            }

            if (!_getLevelsContent.CanTrustRun(false))
            {
                ADLogging.Debug($"Get Trust Levels: this content CanTrustRun is false, returning");
                return;
            }
            ADLogging.Info($"TrustHelper - Getting trust levels for expansion {_getLevelsContent.ExVersion} from {_getLevelsContent.EnglishName}");
            ADLogging.Info("Get Trust Levels: Level not set for: " + string.Join(" | ", _getLevelsContent.TrustMembers.Where(tm => tm is { LevelIsSet: false, Available: true }).Select(tm => tm.Name)));

            State = ActionState.Running;
            Svc.Framework.Update += GetLevelsUpdate;
            SchedulerHelper.ScheduleAction("CheckTrustLevelTimeout", () => Stop(), 2500);
        }

        private static unsafe void Stop(bool forceHide = false)
        {
            if (forceHide || (_getLevelsContent?.TrustMembers.TrueForAll(tm => tm.LevelIsSet || !tm.Available) ?? false))
                AgentModule.Instance()->GetAgentByInternalId(AgentId.Dawn)->Hide();
            Svc.Framework.Update -= GetLevelsUpdate;
            State = ActionState.None; 
            ADLogging.Info($"TrustHelper - Done getting trust levels for expansion {_getLevelsContent?.ExVersion}");
            _getLevelsContent = null;
            SchedulerHelper.DescheduleAction("CheckTrustLevelTimeout");
        }

        internal static ActionState State = ActionState.None;

        private static Content? _getLevelsContent = null;
        internal static unsafe void GetLevelsUpdate(IFramework framework)
        {
            if (_getLevelsContent == null || InDungeon || !AgentHUD.Instance()->IsMainCommandEnabled(82))
            {
                Stop();
                return;
            }

            if (!EzThrottler.Throttle("GetLevelsUpdate", 5) || !PlayerHelper.IsValid) return;

            AgentDawn* agentDawn = AgentDawn.Instance();
            if (!agentDawn->IsAddonReady())
            {
                if (!EzThrottler.Throttle("OpenDawn", 2500))
                    return;

                ADLogging.Debug("Trust Helper - Opening Dawn");
                RaptureAtkModule.Instance()->OpenDawn(_getLevelsContent.RowId);
                return;
            }

            if (agentDawn->Data->ContentData.ExpansionCount < (_getLevelsContent.ExVersion - 2))
            {
                ADLogging.Debug($"Trust Helper - You do not have expansion: {_getLevelsContent.ExVersion} unlocked. stopping");
                Stop();
                return;
            }

            AgentDawnInterface.DawnMemberData memberData = agentDawn->Data->MemberData;

            foreach (AgentDawnInterface.DawnMemberEntry entry in memberData.Members)
            {
                if((entry.MemberId != 0 || entry.ClassJob != 0) && MembersByMemberId.TryGetValue(entry.MemberId, out TrustMember? member) && !member.LevelIsSet)
                {
                    ADLogging.Debug($"TrustHelper - Setting {member.MemberName} aka {member.Name} {entry.MemberId} level to {entry.Level}");
                    member.SetLevel(entry.Level);
                    
                    if(LevelsSet())
                    {
                        Stop();
                        return;
                    }
                }
            }

            Stop();
        }
    }
}
