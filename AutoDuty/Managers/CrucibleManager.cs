using AutoDuty.Helpers;
using ECommons;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;

namespace AutoDuty.Managers
{
    using Dalamud.Memory;
    using FFXIVClientStructs.FFXIV.Client.UI;
    using Lumina.Excel.Sheets;
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using static Data.Classes;
    using Screens = CrucibleUi.Screens;

    public class CrucibleManager(TaskManager _taskManager)
    {
        internal const           uint   LaudaTerritory = 148u;
        internal static readonly uint[] LaudaDataIds   = [1059316u, 1059339u, 1059759u];

        private static readonly Vector3 LaudaPosition = new(25.50f, -6.00f, 67.52f);

        private static readonly Dictionary<uint, uint> BoardByDuty = new()
                                                                     {
                                                                         [1088] = 1,
                                                                         [1089] = 2,
                                                                         [1090] = 3,
                                                                         [1091] = 4,
                                                                         [1092] = 5
                                                                     };

        private readonly CrucibleTeamSetup teamSetup = new();
        private readonly CrucibleMenus     menus     = new();

        private bool onBoard;
        private int  boardStep;

        internal string Status => this.menus.Status;

        internal static bool IsCrucibleTerritory(uint territoryType) =>
            ContentHelper.DictionaryContent.TryGetValue(territoryType, out Content? content) && content.DutyModes.HasFlag(DutyMode.Crucible);

        internal static void GotoLauda() =>
            GotoHelper.Invoke(LaudaTerritory, [LaudaPosition], 0.25f, 4f);

        internal unsafe void RegisterCrucible(Content content)
        {
            if (!BoardByDuty.TryGetValue(content.ContentFinderCondition, out uint board))
            {
                _taskManager.Enqueue(() => Svc.Log.Error($"No Crucible board for {content.Name} (CFC {content.ContentFinderCondition})"), "RegisterCrucible");
                return;
            }

            _taskManager.Enqueue(() => Svc.Log.Info($"Queueing Crucible: {content.Name} (board {board})"), "RegisterCrucible");
            _taskManager.Enqueue(() => Plugin.action = $"Queueing Crucible: {content.Name}",              "RegisterCrucible");

            if (!PlayerHelper.IsValid)
            {
                _taskManager.Enqueue(() => PlayerHelper.IsValid, "RegisterCrucible", new TaskManagerConfiguration(int.MaxValue));
                _taskManager.EnqueueDelay(2000);
            }

            _taskManager.Enqueue(() => this.boardStep = 0, "RegisterCrucible-OpenBoard");
            _taskManager.Enqueue(() => this.OpenBoard(board), "RegisterCrucible-OpenBoard", new TaskManagerConfiguration(30000));

            _taskManager.Enqueue(() => this.teamSetup.Start(AutoDuty.Configuration.Meta.Crucible.TeamMode), "RegisterCrucible-Team-Setup");
            _taskManager.Enqueue(() =>
                                 {
                                     bool done = this.teamSetup.Update();
                                     Plugin.action = this.teamSetup.Status;
                                     return done;
                                 }, "RegisterCrucible-Team-Setup", new TaskManagerConfiguration(300000));
            _taskManager.Enqueue(() =>
                                 {
                                     if (this.teamSetup.Error == null)
                                         return;
                                     Svc.Log.Error($"Crucible team setup failed: {this.teamSetup.Error}");
                                     _taskManager.Abort();
                                     Plugin.Stage = Stage.Stopped;
                                 }, "RegisterCrucible-TeamCheck");

            AtkUnitBase* addon = null;
            _taskManager.Enqueue(() => GenericHelpers.TryGetAddonByName(CrucibleUi.BoardLayout, out addon) && GenericHelpers.IsAddonReady(addon), "RegisterCrucible-Challenge");
            _taskManager.Enqueue(() =>
                                 {
                                     Screens.StageDetail.Confirm(addon);
                                     this.challengedAt = DateTime.UtcNow;
                                 }, "RegisterCrucible-Challenge");
            _taskManager.Enqueue(this.Commence, "RegisterCrucible-Commence");
        }

        private DateTime challengedAt;

        private unsafe bool Commence()
        {
            if (CrucibleUi.TryReady("ContentsFinderConfirm", out AtkUnitBase* confirm))
            {
                AddonHelper.FireCallBack(confirm, true, 8);
                return true;
            }

            if (DateTime.UtcNow - this.challengedAt <= TimeSpan.FromSeconds(5) && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* smallTeam))
            {
                Svc.Log.Info("[Crucible] Team is smaller than ten; confirming the challenge anyway");
                Screens.Prompt.Yes(smallTeam);
                this.challengedAt = DateTime.MinValue;
            }

            return false;
        }

        private static string? challengeText;

        private static string ChallengeText =>
            challengeText ??= Svc.Data.GetExcelSheet<RawRow>(name: "custom/009/CtsXbmEntrance_00976").TryGetRow(1, out RawRow row)
                                  ? row.ReadStringColumn(1).ExtractText().TrimEnd('.', '。', ' ')
                                  : string.Empty;

        private static string? questAlternativeText;

        private static string QuestAlternativeText =>
            questAlternativeText ??= Svc.Data.GetExcelSheet<RawRow>(name: "CustomTalk").TryGetRow(721872, out RawRow row)
                                         ? row.ReadStringColumn(64).ExtractText()
                                         : string.Empty;

        private static unsafe void ChooseChallenge(AtkUnitBase* menu)
        {
            int index = ChallengeText.Length > 0 ? CrucibleUi.SelectStringIndex(menu, ChallengeText) : -1;
            if (index < 0)
            {
                Svc.Log.Warning($"[Crucible] Lauda's menu has no \"{ChallengeText}\" option");
                return;
            }

            AddonHelper.FireCallBack(menu, true, index);
        }

        private unsafe bool OpenBoard(uint board)
        {
            if (CrucibleUi.IsOpen(CrucibleUi.TeamWindow))
                return true;

            if (!EzThrottler.Throttle("CrucibleOpenBoard", 300))
                return false;

            if (CrucibleUi.TryReady(CrucibleUi.BoardList, out AtkUnitBase* list))
            {
                if (this.boardStep == 0)
                {
                    Screens.StageList.Highlight(list, board);
                    this.boardStep = 1;
                }
                else
                {
                    Screens.StageList.Open(list, board);
                    this.boardStep = 0;
                    EzThrottler.Throttle("CrucibleOpenBoard", 1000, true);
                }

                return false;
            }

            if (CrucibleUi.TryReady("SelectIconString", out AtkUnitBase* iconMenu))
            {
                AddonSelectIconString * select = (AddonSelectIconString*) iconMenu;
                ref PopupMenu           iconPopMenu = ref select->PopupMenu.PopupMenu;
                if (iconPopMenu.EntryNames == null)
                    return false;

                for (int i = 0; i < iconPopMenu.EntryCount; i++)
                {
                    if (iconPopMenu.EntryNames[i].Value == null)
                        continue;

                    string entry = MemoryHelper.ReadSeStringNullTerminated((nint)iconPopMenu.EntryNames[i].Value).TextValue;
                    
                    if(QuestAlternativeText.Length > 0 && entry.Contains(QuestAlternativeText, StringComparison.OrdinalIgnoreCase))
                    {
                        AddonHelper.FireCallBack(iconMenu, true, i);
                        break;
                    }
                }

                EzThrottler.Throttle("CrucibleOpenBoard", 600, true);
                return false;
            }

            if (CrucibleUi.TryReady("SelectString", out AtkUnitBase* menu))
            {
                ChooseChallenge(menu);
                EzThrottler.Throttle("CrucibleOpenBoard", 600, true);
                return false;
            }

            if (GenericHelpers.TryGetAddonByName("Talk", out AtkUnitBase* talk) && GenericHelpers.IsAddonReady(talk))
            {
                AddonHelper.ClickTalk();
                return false;
            }

            if (!PlayerHelper.IsReady)
                return false;

            if (ObjectHelper.GetObjectByDataIds(o => o.IsTargetable, LaudaDataIds) is not { } lauda)
                return false;

            ObjectHelper.InteractWithObject(lauda, false);
            EzThrottler.Throttle("CrucibleOpenBoard", 2000, true);
            return false;
        }

        internal void Update()
        {
            if (!Player.Available)
                return;

            CrucibleTeam.UpdateCache();

            bool running = (Plugin.States.HasFlag(PluginState.Looping) || Plugin.States.HasFlag(PluginState.Navigating)) && !Plugin.States.HasFlag(PluginState.Paused);
            bool board   = running && IsCrucibleTerritory(Svc.ClientState.TerritoryType);

            if (!board)
            {
                if (this.onBoard)
                    this.menus.Reset();
                this.onBoard = false;
                return;
            }

            this.onBoard = true;
            this.menus.Update();
        }
    }
}
