using ECommons.DalamudServices;
using ECommons.EzIpcManager;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Globalization;
using System.Numerics;
using static ECommons.IPC.ECommonsIPC;

// ReSharper disable InconsistentNaming
#nullable disable

namespace AutoDuty.IPC
{
    using System;
    using System.Collections.Generic;
    using ECommons.GameFunctions;
    using Helpers;
    using Dalamud.Plugin;
    using ECommons.IPC.Subscribers.RotationSolverReborn;
    using WrathCombo.API;
    using WrathCombo.API.Enum;

    internal static class AutoRetainer_IPCSubscriber
    {
        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("AutoRetainer");

        internal static bool IsBusy() => 
            AutoRetainer.IsBusy();
        internal static bool AreAnyRetainersAvailableForCurrentChara() => 
            AutoRetainer.AreAnyRetainersAvailableForCurrentChara();

        internal static void AbortAllTasks() =>
            AutoRetainer.AbortAllTasks();

        internal static void EnableMultiMode() =>
            AutoRetainer.EnableMultiMode();

        internal static void EnqueueGCInitiation() =>
            AutoRetainer.EnqueueInitiation();

        public static bool RetainersAvailable()
        {
            if (Configuration.EnableAutoRetainer && IsEnabled)
            {
                long? remaining = AutoRetainer.GetClosestRetainerVentureSecondsRemaining(Player.CID);
                Svc.Log.Debug($"AutoRetainer IPC - Closest Retainer Venture Remaining Time: {remaining}");
                return remaining.HasValue && remaining < Configuration.AutoRetainer_RemainingTime;
            }

            return false;
        }
    }

    internal static class BossMod_IPCSubscriber
    {
        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("BossMod") || IPCSubscriber_Common.IsReady("BossModReborn");

        public static bool HasModuleByDataId(uint id) => BossMod.HasModuleByDataId(id);
        public static void DisableModule(string moduleName, bool disable)
        {
            if(Configuration.AutoManageBossModAISettings)
            {
                Svc.Log.Debug($"BossMod IPC - Disabling Module: {moduleName}, Disable: {disable}");
                BossMod.DisableModule(moduleName, disable);
            }
        }

        public static void AddPreset(string name, string preset)
        {
            if (BossMod.Presets_Get(name) == null)
                Svc.Log.Debug($"BossMod Adding Preset: {name} {BossMod.Presets_Create(preset, true)}");
        }

        public static void RefreshPreset(string name, string preset)
        {
            if (BossMod.Presets_Get(name) != null)
                BossMod.Presets_Delete(name);
            AddPreset(name, preset);
        }

        public static void SetPreset(string name, string preset)
        {
            if (Configuration.AutoManageBossModAISettings)
                if (BossMod.Presets_GetActive() != name)
                {
                    Svc.Log.Debug($"BossMod Setting Preset: {name}");
                    AddPreset(name, preset);
                    BossMod.Presets_SetActive(name);
                }
        }

        public static void DisablePresets()
        {
            if (Configuration.AutoManageBossModAISettings)
                if (BossMod.Presets_GetActive() != null)
                {
                    Svc.Log.Debug($"BossMod Disabling Presets");
                    BossMod.Presets_ClearActive();
                }
        }

        public static void SetRange(float range)
        {
            if (Configuration.AutoManageBossModAISettings)
            {
                Svc.Log.Debug($"BossMod Setting Range to: {range}");

                BossMod.Presets_AddTransientStrategy("AutoDuty",         "BossMod.Autorotation.MiscAI.StayCloseToTarget", "range", MathF.Round(range, 1).ToString(CultureInfo.InvariantCulture));
                BossMod.Presets_AddTransientStrategy("AutoDuty Passive", "BossMod.Autorotation.MiscAI.StayCloseToTarget", "range", MathF.Round(range, 1).ToString(CultureInfo.InvariantCulture));
            }
        }

        public enum DestinationStrategy { None, Pathfind, Explicit }

        public static void SetMovement(bool on)
        {
            if (Configuration.AutoManageBossModAISettings)
            {
                Svc.Log.Debug($"BossMod Setting Movement: {on}");

                string destinationStrategy = (on ? DestinationStrategy.Pathfind : DestinationStrategy.None).ToString();

                BossMod.Presets_AddTransientStrategy("AutoDuty",         "BossMod.Autorotation.MiscAI.NormalMovement", "Destination", destinationStrategy);
                BossMod.Presets_AddTransientStrategy("AutoDuty Passive", "BossMod.Autorotation.MiscAI.NormalMovement", "Destination", destinationStrategy);
            }
        }

        public static void SetPositional(Positional positional)
        {
            if (Configuration.AutoManageBossModAISettings)
            {
                Svc.Log.Debug($"BossMod Setting Positional: {positional}");

                BossMod.Presets_AddTransientStrategy("AutoDuty Passive", "BossMod.Autorotation.MiscAI.GoToPositional", "Positional", positional.ToString());
            }
        }

        public static void InBoss(bool boss)
        {
            if (Configuration.AutoManageBossModAISettings)
            {
                string role = boss ? "None" : nameof(Role.Tank);

                BossMod.Presets_AddTransientStrategy("AutoDuty",         "BossMod.Autorotation.MiscAI.StayCloseToPartyRole", "Role", role);
                BossMod.Presets_AddTransientStrategy("AutoDuty Passive", "BossMod.Autorotation.MiscAI.StayCloseToPartyRole", "Role", role);
            }
        }
    }

    
    internal static class YesAlready_IPCSubscriber
    {
        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("YesAlready");

        public static bool IsPluginEnabled => YesAlready.IsPluginEnabled();

        public static void SetState(bool on) => 
            YesAlready.SetPluginEnabled(on);
    }

    internal static class Gearsetter_IPCSubscriber
    {
        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("Gearsetter");

        internal static List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)> GetRecommendationsForGearset(byte gearset) =>
            Gearsetter.GetRecommendationsForGearset(gearset);
    }

    internal static class Stylist_IPCSubscriber
    {
        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("Stylist");
        internal static void UpdateCurrentGearsetEx(bool? moveItemsFromInventory, bool? shouldEquip) =>
            Stylist.UpdateCurrentGearsetEx(moveItemsFromInventory, shouldEquip);

        internal static bool IsBusy    => Stylist.IsBusy();
    }


    internal static class VNavmesh_IPCSubscriber
    {
        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("vnavmesh");

        internal static void  Path_Stop()                                 => Vnavmesh.Stop();
        internal static bool  Nav_IsReady                                 => Vnavmesh.IsReady();
        internal static bool  SimpleMove_PathfindInProgress               => Vnavmesh.PathfindInProgress();
        internal static bool  Path_IsRunning                              => Vnavmesh.IsRunning();
        internal static void  Path_MoveTo(List<Vector3> points, bool fly) => Vnavmesh.MoveTo(points, fly);
        internal static bool  GetNav_Rebuild()  => Vnavmesh.Rebuild();
        internal static float Nav_BuildProgress => Vnavmesh.BuildProgress();
        internal static bool SimpleMove_PathfindAndMoveTo(Vector3 position, bool canFly) =>
            Vnavmesh.PathfindAndMoveTo(position, canFly);
        internal static int      Path_NumWaypoints                                   => Vnavmesh.NumWaypoints();
        internal static float    Path_GetTolerance                                   => Vnavmesh.GetTolerance();
        internal static void     Path_SetTolerance(float tolerance)                  => Vnavmesh.SetTolerance(tolerance);
        internal static bool     Path_GetAlignCamera                                 => Vnavmesh.GetAlignCamera();
        internal static void     Path_SetAlignCamera(bool        align)              => Vnavmesh.SetAlignCamera(align);
        internal static Vector3? Query_Mesh_PointOnFloor(Vector3 p, bool a, float b) => Vnavmesh.PointOnFloor(p, a, b);

        internal static void SetMovementAllowed(bool move)
        {
            if (Vnavmesh.GetMovementAllowed() != move)
                Vnavmesh.SetMovementAllowed(move);
        }
    }

    internal static class PandorasBox_IPCSubscriber
    {
        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("PandorasBox");

        internal static void SetFeatureEnabled(string feature, bool enabled) => PandorasBox.SetFeatureEnabled(feature, enabled);
        internal static bool? GetFeatureEnabled(string feature) => PandorasBox.GetFeatureEnabled(feature);
    }

    public static class Wrath_IPCSubscriber
    {
        private static Guid? _curLease;

        internal static bool IsEnabled => IPCSubscriber_Common.IsReady("WrathCombo");
        
        /// <summary>
        ///     Checks if the current job has a Single and Multi-Target combo configured
        ///     that are enabled in Auto-Mode.
        /// </summary>
        /// <returns>
        ///     If the user's current job is fully ready for Auto-Rotation.
        /// </returns>
        internal static bool IsCurrentJobAutoRotationReady => WrathIPCWrapper.IsCurrentJobAutoRotationReady();


        private static bool DoThing(Func<SetResult> action)
        {
            SetResult result = action();
            bool      check  = result.CheckResult();
            if (!check && result == SetResult.InvalidLease)
                check = action().CheckResult();
            return check;
        }

        private static bool CheckResult(this SetResult result)
        {
            switch (result)
            {
                case SetResult.Okay:
                case SetResult.OkayWorking:
                    return true;
                case SetResult.InvalidLease:
                    _curLease = null;
                    Register();
                    return false;
                case SetResult.BlacklistedLease:
                    Configuration.AutoManageRotationPluginState = false;
                    Windows.Configuration.Save();
                    return false;
                case SetResult.IPCDisabled:
                case SetResult.Duplicate:
                case SetResult.PlayerNotAvailable:
                case SetResult.InvalidConfiguration:
                case SetResult.InvalidValue:
                case SetResult.IGNORED:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result, null);
            }
        }

        internal static bool SetJobAutoReady() => 
            Register() && DoThing(() => WrathIPCWrapper.SetCurrentJobAutoRotationReady(_curLease!.Value));

        internal static void SetAutoMode(bool on)
        {
            if (Register())
            {
                bool autoRotationState = DoThing(() => WrathIPCWrapper.SetAutoRotationState(_curLease!.Value, on));
                if (autoRotationState && on)
                {
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.InCombatOnly,       false);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.AutoRez,            true);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.AutoRezDPSJobs,     true);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.IncludeNPCs,        true);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.OnlyAttackInCombat, false);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.AutoCleanse,        true);

                    DPSRotationMode dpsConfig = Plugin.currentPlayerItemLevelAndClassJob.Value.GetCombatRole() == CombatRole.Tank ?
                                                    Configuration.Wrath_TargetingTank :
                                                    Configuration.Wrath_TargetingNonTank;
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.DPSRotationMode,    dpsConfig);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.HealerRotationMode, HealerRotationMode.Lowest_Current);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.DPSAlwaysHardTarget, true);
                    WrathIPCWrapper.SetAutoRotationConfigState(_curLease.Value, AutoRotationConfigOption.HealerAlwaysHardTarget, true);
                }
            }
        }

        private static bool Register()
        {
            if (_curLease == null)
            {
                _curLease = WrathIPCWrapper.RegisterForLeaseWithCallback("AutoDuty", "AutoDuty", null);

                if (_curLease == null && IsEnabled)
                {
                    Configuration.AutoManageRotationPluginState = false;
                    Windows.Configuration.Save();
                }
            }
            return _curLease != null;
        }

        internal static void CancelActions(int reason, string s)
        {
            switch ((CancellationReason) reason)
            {
                case CancellationReason.WrathUserManuallyCancelled:
                    Configuration.AutoManageRotationPluginState = false;
                    Windows.Configuration.Save();
                    break;
                case CancellationReason.LeaseePluginDisabled:
                case CancellationReason.WrathPluginDisabled:
                case CancellationReason.LeaseeReleased:
                case CancellationReason.AllServicesSuspended:
                case CancellationReason.JobChanged:
                default:
                    break;
            }

            _curLease = null;
            Svc.Log.Info($"Wrath lease cancelled via {(CancellationReason) reason} for: {s}");
        }

        internal static void Release()
        {
            if (_curLease.HasValue)
            {
                WrathIPCWrapper.ReleaseControl(_curLease.Value);
                _curLease = null;
            }
        }
    }

    public static class RSR_IPCSubscriber
    {
        public static string GetHostileTypeDescription(RotationSolverRebornIPC.TargetHostileType type) =>
            type switch
            {
                RotationSolverRebornIPC.TargetHostileType.AllTargetsCanAttack => "All Targets Can Attack aka Tank/Autoduty Mode",
                RotationSolverRebornIPC.TargetHostileType.TargetsHaveTarget => "Targets Have A Target",
                RotationSolverRebornIPC.TargetHostileType.AllTargetsWhenSoloInDuty => "All Targets When Solo In Duty",
                RotationSolverRebornIPC.TargetHostileType.AllTargetsWhenSolo => "All Targets When Solo",
                _ => "Unknown Target Type"
            };

        internal static         bool                 IsEnabled => IPCSubscriber_Common.IsReady("RotationSolver");

        public static void RotationAuto()
        {
            RotationSolverReborn.OtherCommand(RotationSolverRebornIPC.OtherCommandType.Settings, $"HostileType {Configuration.RSR_TargetHostileType}");
            RotationSolverReborn.OtherCommand(RotationSolverRebornIPC.OtherCommandType.Settings, "FriendlyPartyNpcHealRaise3 true");
            RotationSolverReborn.OtherCommand(RotationSolverRebornIPC.OtherCommandType.Settings, "AutoOffAfterCombat false");
            RotationSolverReborn.AutodutyChangeOperatingMode(RotationSolverRebornIPC.StateCommandType.AutoDuty, Plugin.currentPlayerItemLevelAndClassJob.Value.GetCombatRole() == CombatRole.Tank ?
                                                                                                                    Configuration.RSR_TargetingTypeTank :
                                                                                                                    Configuration.RSR_TargetingTypeNonTank);
        }

        public static void RotationStop() => RotationSolverReborn.ChangeOperatingMode(RotationSolverRebornIPC.StateCommandType.Off);
    }

    internal class IPCSubscriber_Common
    {
        internal static bool IsReady(string pluginName) => DalamudReflector.TryGetDalamudPlugin(pluginName, out _, false, true);

        internal static Version Version(string pluginName) => DalamudReflector.TryGetDalamudPlugin(pluginName, out IDalamudPlugin dalamudPlugin, false, true) ? dalamudPlugin.GetType().Assembly.GetName().Version : new Version(0, 0, 0, 0);

        internal static void DisposeAll(EzIPCDisposalToken[] _disposalTokens)
        {
            foreach (EzIPCDisposalToken token in _disposalTokens)
                try
                {
                    token.Dispose();
                }
                catch (Exception ex)
                {
                    Svc.Log.Error($"Error while unregistering IPC: {ex}");
                }
        }
    }
}
