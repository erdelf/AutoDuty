using AutoDuty.IPC;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using System;
    using FFXIVClientStructs.FFXIV.Client.UI.Agent;
    using FFXIVClientStructs.FFXIV.Common.Math;
    using Multibox;

    internal static class DeathHelper
    {
        public static  int             deathCount = 0;
        private static PlayerLifeState deathState = PlayerLifeState.Alive;
        internal static PlayerLifeState DeathState
        {
            get => deathState;
            set
            {
                if (Plugin.Stage == Stage.Stopped)
                    return;

                if(deathState != value)
                    MultiboxUtility.IsDead(value == PlayerLifeState.Dead);

                switch (value)
                {
                    case PlayerLifeState.Dead:
                    {
                        if (value != deathState)
                        {
                            DebugLog("Player is Dead changing state to Dead");
                            SchedulerHelper.ScheduleAction(nameof(OnDeath), OnDeath, 500, false);
                            deathCount++;
                        }

                        break;
                    }
                    case PlayerLifeState.Revived:
                        SchedulerHelper.DescheduleAction(nameof(OnDeath));
                        DebugLog("Player is Revived changing state to Revived");

                        if (CrucibleManager.IsCrucibleTerritory(Plugin.currentTerritoryType))
                        {
                            if (VNavmesh_IPCSubscriber.Path_IsRunning)
                                VNavmesh_IPCSubscriber.Path_Stop();
                            BossMod_IPCSubscriber.SetMovement(true);
                            Plugin.Stage = Stage.Idle;
                            Plugin.Stage = Stage.Reading_Path;
                            break;
                        }

                        oldIndex              = Plugin.indexer;
                        findShortcutStartTime = Environment.TickCount;
                        shortcutUseLocation   = null;
                        FindShortcut();
                        break;
                    case PlayerLifeState.Alive:
                    default:
                        break;
                }
                deathState = value;
            }
        }

        private static unsafe void OnDeath()
        {
            if (!Player.IsDead)
            {
                DebugLog("Player is Alive, stopping OnDeath");
                SchedulerHelper.DescheduleAction(nameof(OnDeath));
            }

            Plugin.DutyData?.StopForCombat = true;
            Plugin.skipTreasureCoffer = true;

            if (VNavmesh_IPCSubscriber.Path_IsRunning)
                VNavmesh_IPCSubscriber.Path_Stop();

            if (Plugin.taskManager.IsBusy)
                Plugin.taskManager.Abort();
            
            if (Configuration.Meta.DutyModeEnum.EqualsAny(DutyMode.Regular, DutyMode.Trial, DutyMode.Raid, DutyMode.Variant))
            {
                bool yesNo = GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno);
                bool dead  = PartyHelper.PartyDead();

                if (dead)
                {
                    if(yesNo)
                        AddonHelper.ClickSelectYesno();
                    else if (!AgentRevive.Instance()->IsAddonShown()) 
                        AgentRevive.Instance()->ShowAddon();
                } else if(yesNo && !AgentRevive.Instance()->IsAddonShown())
                {
                    AddonHelper.ClickSelectYesno();
                }
            }
        }

        private static int          oldIndex = 0;
        private static IGameObject? GameObject => ObjectHelper.GetObjectByDataIds(2000700, 2000789);
        private static int          findShortcutStartTime = 0;
        private static Vector3?     shortcutUseLocation;

        private static int FindWaypoint()
        {
            if (Plugin.indexer != -1)
            {
                ContentPathsManager.ContentPathContainer container    = ContentPathsManager.DictionaryPaths[Plugin.currentTerritoryType];
                ContentPathsManager.DutyPath             dutyPath     = container.Paths[Plugin.currentPath];
                bool                                     revivalFound = dutyPath.RevivalFound;

                bool isBoss = Plugin.Actions[Plugin.indexer].Name.Equals("Boss");
                if (!revivalFound)
                    if (Plugin.indexer > 0 && isBoss)
                        return Plugin.indexer;

                ADLogging.Info($"Finding Revival Point starting at {Plugin.indexer}. Using Revival Action: {revivalFound}");
                for (int i = Plugin.indexer; i >= 0; i--)
                {
                    string name = Plugin.Actions[i].Name;


                    bool found = name.Equals("Revival", StringComparison.InvariantCultureIgnoreCase);

                    if(!found && !isBoss)
                        found = name.Equals("Boss", StringComparison.InvariantCultureIgnoreCase);

                    if (found && i != Plugin.indexer)
                    {
                        int waypoint = isBoss ? i : i + 1;
                        ADLogging.Debug($"Revival Point: {i}");
                        return waypoint;
                    }
                }
            }

            return 0;
        }

        private static void FindShortcut()
        {
            if (GameObject is not { IsTargetable: true })
            {
                if (Environment.TickCount <= (findShortcutStartTime + 5000))
                {
                    ADLogging.Debug($"OnRevive: Searching for shortcut");
                    SchedulerHelper.ScheduleAction("FindShortcut", FindShortcut, 500);
                    return;
                }

                ADLogging.Debug($"OnRevive: Couldn't find shortcut");
                Plugin.indexer = 0;
                //Stop();
                //return;
            } else
            {
                ADLogging.Debug("OnRevive: Found shortcut");
            }

            Svc.Framework.Update += OnRevive;
        }

        internal static void Stop()
        {
            Svc.Framework.Update -= OnRevive;
            if (VNavmesh_IPCSubscriber.Path_IsRunning)
                VNavmesh_IPCSubscriber.Path_Stop();
            BossMod_IPCSubscriber.SetMovement(true);
            Plugin.Stage = Stage.Idle;
            Plugin.Stage = Stage.Reading_Path;
            ADLogging.Debug("DeathHelper - Player is Alive, and we are done with Revived Actions, changing state to Alive");
            deathState               = PlayerLifeState.Alive;
            Plugin.skipTreasureCoffer = false;
        }

        private static unsafe void OnRevive(IFramework _)
        {
            if (!EzThrottler.Throttle("OnRevive", 500) || (!PlayerHelper.IsReady && !Conditions.Instance()->OccupiedInQuestEvent) || PlayerHelper.IsCasting) 
                return;

            if (PlayerHelper.HasStatusAny([43, 44], 90) || PlayerHelper.HasStatusAny([148, 1140]))
            {
                Plugin.indexer = oldIndex;
                Stop();
                return ;
            }

            IGameObject? shortcutObject       = GameObject;

            if (!(shortcutObject?.IsTargetable ?? false) || (shortcutUseLocation != null && ObjectHelper.GetDistanceToPlayer(shortcutUseLocation.Value) > 5))
            {
                ADLogging.Debug("OnRevive: Done");
                if(Plugin.indexer == 0)
                    Plugin.indexer = FindWaypoint();
                Stop();
                return;
            }

            if (oldIndex == Plugin.indexer)
                Plugin.indexer = FindWaypoint();

            float distanceToPlayer = ObjectHelper.GetDistanceToPlayer(shortcutObject);
            if (distanceToPlayer > 2)
            {
                MovementHelper.Move(shortcutObject, 0.25f, 2);
                ADLogging.Debug($"OnRevive: Moving to {shortcutObject.Name} at: {shortcutObject.Position} which is {distanceToPlayer} away");
            }
            else
            {
                shortcutUseLocation = Player.Position;
                ADLogging.Debug($"OnRevive: Interacting with {shortcutObject.Name} until SelectYesno Addon appears, and ClickingYes");
                ObjectHelper.InteractWithObjectUntilAddon(shortcutObject, "SelectYesno");
                AddonHelper.ClickSelectYesno();
            }
        }

        private static void DebugLog(string s) => 
            ADLogging.Debug($"DeathHelper: {s}");
    }
}
