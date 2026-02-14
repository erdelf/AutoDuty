using AutoDuty.Data;
using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using static AutoDuty.Helpers.ObjectHelper;
using static AutoDuty.Helpers.PlayerHelper;
#pragma warning disable CA1822
#pragma warning disable IDE0060
// ReSharper disable UnusedParameter.Global
// ReSharper disable UnusedMember.Global

namespace AutoDuty.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ECommons.ExcelServices;
    using FFXIVClientStructs.FFXIV.Client.Game.Object;
    using System.Reflection;

    internal class ActionsManager(AutoDuty plugin, TaskManager taskManager)
    {
        public readonly List<(string, string, string)> actionsList =
        [
            ("<-- Comment -->","comment?","Adds a Comment to the path; AutoDuty will do nothing but display them.\nExample: <-- Trash Pack #1 -->"),
            ("Wait","how long?", "Adds a Wait (for x milliseconds) step to the path; after moving to the position, AutoDuty will wait x milliseconds.\nExample: Wait|0.02, 23.85, -394.89|8000"),
            ("WaitFor","for?","Adds a WaitFor (Condition) step to the path; after moving to the position, AutoDuty will wait for a condition from the following list:\nCombat - waits until in combat\nIsReady - waits until the player is ready\nIsValid - waits until the player is valid\nIsOccupied - waits until the player is occupied\nBNpcInRadius - waits until a battle npc either spawns or path's into the radius specified\nExample: WaitFor|-12.12, 18.76, -148.05|Combat"),
            ("Boss","Loot Loc, Loot Range", "Adds a Boss step to the path; after (and while) moving to the position, AutoDuty will attempt to find the boss object. If not found, AD will wait 10s at the position for the boss to spawn and will then Invoke the Boss Action.\nExample: Boss|-2.91, 2.90, -204.68|"),
            ("Interactable","interact with?", "Adds an Interactable step to the path; after moving to within 2y of the position, AutoDuty will interact with the object specified (recommended to input DataId) until either the object is no longer targetable, you meet certain conditions, or a YesNo/Talk addon appears.\nExample: Interactable|21.82, 7.10, 27.40|1004346 (Goblin Pathfinder)"),
            ("TreasureCoffer","false", "Adds a TreasureCoffer flag to the path; AutoDuty will loot any treasure coffers automatically if it gets within interact range of one (while Config Loop Option is on), this is just a flag to mark the positions of Treasure Coffers.\nNote: AutoDuty will ignore this Path entry when Looting is disabled entirely or Boss Loot Only is enabled.\nExample: TreasureCoffer|3.21, 6.06, -97.63|"),
            ("SelectYesno","yes or no?", "Adds a SelectYesNo step to the path; after moving to the position, AutoDuty will click Yes or No on this addon.\nExample: SelectYesno|9.41, 1.94, -311.25|Yes"),
            ("SelectString", "list index", "Adds a SelectString step to the path; after moving to the position, AutoDuty will pick the indexed string.\nExample: SelectYesno|908.24, 327.26, -561.96|1"),
            ("MoveToObject","Object Name?", "Adds a MoveToObject step to the path; AutoDuty will will move the object specified (recommend input DataId)"),
            ("DutySpecificCode","step #?", "Adds a DutySpecificCode step to the path; after moving to the position, AutoDuty will invoke the Duty Specific Action for this TerritoryType and the step # specified.\nExample: DutySpecificCode|174.68, 102.00, -66.46|1"),
            ("BossMod", "on / off", "Adds a BossMod step to the path; after moving to the position, AutoDuty will turn BossMod on or off.\nExample: BossMod|-132.08, -342.25, 1.98|Off"),
            ("Rotation", "on / off", "Adds a Rotation step to the path; after moving to the position, AutoDuty will turn Rotation Plugin on or off.\nExample: Rotation|-132.08, -342.25, 1.98|Off"),
            ("Target", "Target what?", "Adds a Target step to the path; after moving to the position, AutoDuty will Target the object specified (recommend inputing DataId)."),
            ("AutoMoveFor", "how long?", "Adds an AutoMoveFor step to the path; AutoDuty will turn on Standard Mode and Auto Move for the time specified in milliseconds (or until player is not ready).\nExample: AutoMoveFor|-18.21, 1.61, 114.16|3000"),
            ("ChatCommand","Command with args?", "Adds a ChatCommand step to the path; after moving to the position, AutoDuty will execute the Command specified.\nExample: ChatCommand|-5.86, 164.00, 501.72|/bmrai follow Alisaie"),
            ("StopForCombat","true/false", "Adds a StopForCombat step to the path; after moving to the position, AutoDuty will turn StopForCombat on or off.\nExample: StopForCombat|-1.36, 5.76, -108.78|False"),
            ("Revival", "false", "Adds a Revive flag to the path; this is just a flag to mark the positions of Revival Points, AutoDuty will ignore this step during navigation.\nUse this if the Revive Teleporter does not take you directly to the arena of the last boss you killed, such as Sohm Al.\nExample: Revival|33.57, -202.93, -70.30|"),
            ("ForceAttack",  "false", "Adds a ForceAttack step to the path; after moving to the position, AutoDuty will ForceAttack the closest mob.\nExample: ForceAttack|-174.24, 6.56, -301.67|"),
            ("Jump", "automove for how long before", "Adds a Jump step to the path; after AutoMoving, AutoDuty will jump.\nExample: Jump|0, 0, 0|200"),
            ("JumpTo", "jump where? [how long before jump?]", "Move towards point with no mesh then jump\nExample: JumpTo|720.55, 57.24, 8.89|720.637, 57.242, 9.762[|100]"),
            //("PausePandora", "Which feature | how long"),
            ("CameraFacing", "Face which Coords?", "Adds a CameraFacing step to the path; after moving to the position, AutoDuty will face the coordinates specified.\nExample: CameraFacing|720.66, 57.24, 9.18|722.05, 62.47, 15.55"),
            ("ClickTalk", "false", "Adds a ClickTalk step to the path; after moving to the position, AutoDuty will click the talk addon."),
            ("ConditionAction","condition;args,action;args", "Adds a ConditionAction step to the path; after moving to the position, AutoDuty will check the condition specified and invoke Action."),
            ("ModifyIndex", "what number (0-based)", "Adds a ModifyIndex step to the path; after moving to the position, AutoDuty will modify the index to the number specified."),
            ("Action", "", "Run any action"),
            ("BLULoad", "enable?;which spell", "Enables or disables a spell from the current BLU loadout")
        ];

        public void InvokeAction(PathAction action)
        {
            try
            {
                if (action != null)
                {
                    Type?       thisType   = this.GetType();
                    MethodInfo? actionTask = thisType.GetMethod(action.Name, ReflectionHelper.ALL, [typeof(PathAction)]);
                    taskManager.Enqueue(() => actionTask?.Invoke(this, [action]), $"InvokeAction-{actionTask?.Name}");
                }
                else
                {
                    Svc.Log.Error("no action");
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex.ToString());
            }
        }

        public static void Follow(PathAction action) => FollowHelper.SetFollow(GetObjectByName(action.Arguments[0]));

        public static void SetBMSettings(PathAction action) => AutoDuty.SetBMSettings(bool.TryParse(action.Arguments[0], out bool defaultSettings) && defaultSettings);

        public unsafe void ConditionAction(PathAction action)
        {
            string[]? conditionActionArray = [..action.Arguments];

            switch (action.Arguments.Count)
            {
                // There are 4 paths that uses conditionaction before the argument array was split, 
                // so we need to handle that case until they can be modified to use properly split arguments and retested
                case 0:
                    return;
                case 1:
                {
                    if (!action.Arguments[0].Any(x => x.Equals('&'))) 
                        return;
                    conditionActionArray = action.Arguments[0].Split("&");
                    break;
                }
            }

            Plugin.action = $"ConditionAction: {conditionActionArray[0]}, {conditionActionArray[1]}";

            string? condition = conditionActionArray[0];
            string[] conditionArray = [];
            if (condition.Any(x => x.EqualsAny(';')))
                conditionArray = condition.Split(";");
            string? actions = conditionActionArray[1];
            string[] actionArray = [];
            if (actions.Any(x => x.EqualsAny(';')))
                actionArray = actions.Split(";");
            bool invokeAction = false;
            Dictionary<string, Func<object, object, bool>>? operation = new()
                                                                        {
                                                                            { ">", (x,  y) => Convert.ToSingle(x) > Convert.ToSingle(y) },
                                                                            { ">=", (x, y) => Convert.ToSingle(x) >= Convert.ToSingle(y) },
                                                                            { "<", (x,  y) => Convert.ToSingle(x) < Convert.ToSingle(y) },
                                                                            { "<=", (x, y) => Convert.ToSingle(x) <= Convert.ToSingle(y) },
                                                                            { "==", (x, y) => x                   == y },
                                                                            { "!=", (x, y) => x                   != y }
                                                                        };
            string? operatorValue;
            bool    operationResult;

            switch (conditionArray[0])
            {
                case "GetDistanceToPlayer":
                    {
                        if (conditionArray.Length < 4) return;
                        if (!conditionArray[1].TryGetVector3(out Vector3 vector3))
                            return;
                        if (!float.TryParse(conditionArray[3], out float distance)) 
                            return;
                        if (!(operatorValue = conditionArray[2]).EqualsAny(operation.Keys)) 
                            return;
                        float getDistance = GetDistanceToPlayer(vector3);

                        // ReSharper disable once AssignmentInConditionalExpression
                        if ((operationResult = operation[operatorValue](getDistance, distance)))
                            invokeAction = true;
                        Svc.Log.Info($"Condition: {getDistance}{operatorValue}{distance} = {operationResult}");
                        break;
                    }
                case "ObjectDistanceToPoint":
                    {
                        if (conditionArray.Length < 5) 
                            return;
                        if (!conditionArray[2].TryGetVector3(out Vector3 vector3)) 
                            return;
                        if (!float.TryParse(conditionArray[4], out float distance)) 
                            return;
                        if (!(operatorValue = conditionArray[3]).EqualsAny(operation.Keys)) 
                            return;
                        IGameObject? targetObject = null;
                        if ((targetObject = GetObjectByDataId(uint.TryParse(conditionArray[1], out uint dataId) ? dataId : 0)) == null) 
                            return;
                        float getDistance = Vector3.Distance(vector3, targetObject.Position);
                        if (operationResult = operation[operatorValue](getDistance, distance))
                            invokeAction = true;
                        Svc.Log.Info($"Condition: {getDistance}{operatorValue}{distance} = {operationResult}");
                        break;
                    }
                case "ItemCount":
                    if (conditionArray.Length < 4) 
                        return;
                    if (!uint.TryParse(conditionArray[1], out uint itemId)) 
                        return;
                    if (!uint.TryParse(conditionArray[3], out uint quantity)) 
                        return;
                    if (!operation.TryGetValue(operatorValue = conditionArray[2], out Func<object, object, bool>? operationFunc)) 
                        return;
                    int itemCount = InventoryHelper.ItemCount(itemId);
                    if (operationResult = operationFunc(itemCount, quantity))
                        invokeAction = true;
                    Svc.Log.Info($"Condition: {itemCount}{operatorValue}{quantity} = {operationResult}");
                    break;
                case "ObjectData":
                    if (conditionArray.Length > 3)
                    {
                        IGameObject? gameObject = null;
                        if ((gameObject = GetObjectByDataId(uint.TryParse(conditionArray[1], out uint dataId) ? dataId : 0)) != null)
                        {
                            GameObject* csObj = gameObject.Struct();
                            switch (conditionArray[2])
                            {
                                case "EventState":
                                    if (csObj->EventState == (int.TryParse(conditionArray[3], out int es) ? es : -1))
                                        invokeAction = true;
                                    break;
                                case "IsTargetable":
                                    if (csObj->GetIsTargetable() == (bool.TryParse(conditionArray[3], out bool it) && it))
                                        invokeAction = true;
                                    break;
                            }
                        }
                    }
                    break;
                case "Job":
                    if (conditionArray.Length > 1)
                        if (Enum.TryParse(conditionArray[1], out JobWithRole jwr))
                            if (jwr.HasJob(GetJob()))
                                invokeAction = true;
                    break;
                case "ActionStatus":
                    if (Enum.TryParse(conditionArray[1], out ActionType type))
                        if (uint.TryParse(conditionArray[2], out uint id))
                            if (uint.TryParse(conditionArray[3], out uint status))
                                if (ActionManager.Instance()->GetActionStatus(type, id) == status)
                                    invokeAction = true;
                    break;
            }
            if (invokeAction)
            {
                string?      actionActual    = actionArray[0];
                List<string> actionArguments = [..actionArray.Length > 1 ? actionArray[1..] : [string.Empty]];
                Svc.Log.Debug($"ConditionAction: Invoking Action: {actionActual} with Arguments: {actionArguments}");
                this.InvokeAction(new PathAction() { Name = actionActual, Arguments = actionArguments });
            }
        }

        public void BossMod(PathAction action) => 
            BossMod_IPCSubscriber.SetMovement(action.Arguments[0].Equals("on", StringComparison.InvariantCultureIgnoreCase));

        public void ModifyIndex(PathAction action)
        {
            if (!int.TryParse(action.Arguments[0], out int _index))
                return;

            if (action.Arguments[0][0] is '+' or '-')
                Plugin.indexer += _index;
            else
                Plugin.indexer = _index;

            Plugin.Stage = Stage.Reading_Path;
        }

        private bool autoManageRotationPluginState = false;
        public  void Rotation(PathAction action) => 
            this.Rotation(action.Arguments[0].Equals("on", StringComparison.InvariantCultureIgnoreCase));

        public void Rotation(bool on, bool rotationPlugins = true)
        {
            if (!on)
            {
                if (Configuration.AutoManageRotationPluginState)
                {
                    this.autoManageRotationPluginState          = true;
                    Configuration.AutoManageRotationPluginState = false;
                }

                if(rotationPlugins)
                    Plugin.SetRotationPluginSettings(false, true);
            }
            else
            {
                if (this.autoManageRotationPluginState)
                    Configuration.AutoManageRotationPluginState = true;

                if(rotationPlugins)
                    Plugin.SetRotationPluginSettings(true, true);
            }
        }

        public void StopForCombat(PathAction action)
        {
            if (!Player.Available)
                return;

            bool boolTrueFalse = action.Arguments[0].Equals("true", StringComparison.InvariantCultureIgnoreCase);
            Plugin.action = $"StopForCombat: {action.Arguments[0]}";

            this.StopForCombat(boolTrueFalse, action.Arguments.Count <= 1 || action.Arguments[1] != "noWait");
        }

        public void StopForCombat(bool stop, bool waitAfter = true)
        {
            if (!Player.Available)
                return;

            Plugin.stopForCombat = stop;
            taskManager.Enqueue(() => BossMod_IPCSubscriber.SetMovement(stop), "StopForCombat");
            if (stop && waitAfter)
                this.Wait(500);
        }


        public unsafe void ForceAttack(PathAction action)
        {
            int tot = action.Arguments[0].IsNullOrEmpty() ? 10000 : int.TryParse(action.Arguments[0], out int time) ? time : 0;
            if (action.Arguments[0].IsNullOrEmpty())
                action.Arguments[0] = "10000";
            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 16), "ForceAttack-GA16");
            taskManager.Enqueue(() => Svc.Targets.Target != null,                                        "ForceAttack-GA1", new TaskManagerConfiguration(500));
            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 1),  "ForceAttack-GA1");
            taskManager.Enqueue(() => InCombat,                                                          "ForceAttack-WaitForCombat", new TaskManagerConfiguration(tot));
        }

        public unsafe void Jump(PathAction action)
        {
            Plugin.action = $"Jumping";

            if (int.TryParse(action.Arguments[0], out int wait) && wait > 0)
            {
                taskManager.Enqueue(() => Chat.ExecuteCommand("/automove on"), "Jump");
                taskManager.Enqueue(() => EzThrottler.Throttle("AutoMove", Convert.ToInt32(wait)), "Jump");
                taskManager.Enqueue(() => EzThrottler.Check("AutoMove"), "Jump", new TaskManagerConfiguration(Convert.ToInt32(wait)));
            }

            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2), "Jump");

            if (wait > 0)
            {
                taskManager.Enqueue(() => EzThrottler.Throttle("AutoMove", Convert.ToInt32(100)), "Jump");
                taskManager.Enqueue(() => EzThrottler.Check("AutoMove"), "AutoMove", new TaskManagerConfiguration(Convert.ToInt32(100)));
                taskManager.Enqueue(() => Chat.ExecuteCommand("/automove off"), "Jump");
            }
        }

        public unsafe void JumpTo(PathAction action)
        {
            Plugin.action = $"Jumping To {action.Arguments[0]}";
            if (!action.Arguments[0].TryGetVector3(out Vector3 position))
                return;
            int wait = 100;
            if (action.Arguments.Count > 1 && int.TryParse(action.Arguments[1], out int parsedWait) && parsedWait > 0)
                wait = parsedWait;
            taskManager.Enqueue(() => VNavmesh_IPCSubscriber.Path_MoveTo([position], false), "Start-JumpTo-Move");
            if (wait > 0)
            {
                taskManager.Enqueue(() => EzThrottler.Throttle("JumpTo", Convert.ToInt32(wait)), "JumpTo-Wait");
                taskManager.Enqueue(() => EzThrottler.Check("JumpTo"), "JumpTo-Wait", new TaskManagerConfiguration(Convert.ToInt32(wait)));
            }
            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2), "JumpTo");
            taskManager.Enqueue(() => MovementHelper.Move(position, useMesh:false), "Finish-JumpTo-Move");
        }

        public void ChatCommand(PathAction action)
        {
            if (!Player.Available)
                return;
            Plugin.action = $"ChatCommand: {action.Arguments[0]}";
            taskManager.Enqueue(() => Chat.ExecuteCommand(action.Arguments[0]), "ChatCommand");
            taskManager.Enqueue(() => Plugin.action = "");
        }

        public void AutoMoveFor(PathAction action)
        {
            if (!Player.Available)
                return;
            Plugin.action = $"AutoMove For {action.Arguments[0]}";
            uint movementMode = Svc.GameConfig.UiControl.TryGetUInt("MoveMode", out uint mode) ? mode : 0;
            taskManager.Enqueue(() => { if (movementMode == 1) Svc.GameConfig.UiControl.Set("MoveMode", 0); }, "AutoMove-MoveMode");
            taskManager.Enqueue(() => Chat.ExecuteCommand("/automove on"), "AutoMove-On");
            taskManager.Enqueue(() => EzThrottler.Throttle("AutoMove", Convert.ToInt32(action.Arguments[0])), "AutoMove-Throttle");
            taskManager.Enqueue(() => EzThrottler.Check("AutoMove") || !IsReady, "AutoMove-CheckThrottleOrNotReady", new TaskManagerConfiguration(Convert.ToInt32(action.Arguments[0])));
            taskManager.Enqueue(() => { if (movementMode == 1) Svc.GameConfig.UiControl.Set("MoveMode", 1); }, "AutoMove-MoveMode2");
            taskManager.Enqueue(() => IsReady, "AutoMove-WaitIsReady", new TaskManagerConfiguration(int.MaxValue));
            taskManager.Enqueue(() => Chat.ExecuteCommand("/automove off"), "AutoMove-Off");
        }

        public void Wait(PathAction action)
        {
            Plugin.action = $"Wait: {action.Arguments[0]}";
            this.Wait(Convert.ToInt32(action.Arguments[0]));
            taskManager.Enqueue(() => Plugin.action = "");
        }

        public unsafe void Wait(int ms)
        {
            if (Plugin.stopForCombat)
                taskManager.Enqueue(() => !Player.Character->InCombat, "Wait", new TaskManagerConfiguration(int.MaxValue));
            taskManager.Enqueue(() => EzThrottler.Throttle("Wait", ms), "Wait");
            taskManager.Enqueue(() => EzThrottler.Check("Wait"),                                          "Wait", new TaskManagerConfiguration(ms));
            if (Plugin.stopForCombat)
                taskManager.Enqueue(() => !Player.Character->InCombat, "Wait", new TaskManagerConfiguration(int.MaxValue));
        }

        public unsafe void WaitFor(PathAction action)
        {
            Plugin.action = $"WaitFor: {action.Arguments[0]}";
            string[]? waitForWhats = action.Arguments[0].Split(';');
            switch (waitForWhats[0])
            {
                case "Combat":
                    taskManager.Enqueue(() => Player.Character->InCombat, "WaitFor-Combat");
                    break;
                case "OOC":
                    taskManager.Enqueue(() => Player.Character->InCombat, "WaitFor-Combat-500", new TaskManagerConfiguration(500));
                    taskManager.Enqueue(() => !Player.Character->InCombat, "WaitFor-OOC", new TaskManagerConfiguration(int.MaxValue));
                    break;
                case "IsValid":
                    taskManager.Enqueue(() => !IsValid, "WaitFor-NotIsValid-500", new TaskManagerConfiguration(500));
                    taskManager.Enqueue(() => IsValid, "WaitFor-IsValid", new TaskManagerConfiguration(int.MaxValue));
                    break;
                case "IsOccupied":
                    taskManager.Enqueue(() => !IsOccupied, "WaitFor-NotIsOccupied-500", new TaskManagerConfiguration(500));
                    taskManager.Enqueue(() => IsOccupied, "WaitFor-IsOccupied", new TaskManagerConfiguration(int.MaxValue));
                    break;
                case "IsReady":
                    taskManager.Enqueue(() => !IsReady, "WaitFor-NotIsReady-500", new TaskManagerConfiguration(500));
                    taskManager.Enqueue(() => IsReady, "WaitFor-IsReady", new TaskManagerConfiguration(int.MaxValue));
                    break;
                case "DistanceTo":
                    if (waitForWhats.Length < 3)
                        return;
                    if (waitForWhats[1].TryGetVector3(out Vector3 position)) return;
                    if (float.TryParse(waitForWhats[2], out float distance)) return;

                    taskManager.Enqueue(() => Vector3.Distance(Player.Position, position) <= distance, $"WaitFor-DistanceTo({position})<={distance}", new TaskManagerConfiguration(int.MaxValue));
                    break;
                case "ConditionFlag":
                    if (waitForWhats.Length < 3)
                        return;
                    ConditionFlag conditionFlag = Enum.TryParse(waitForWhats[1], out ConditionFlag condition) ? condition : ConditionFlag.None;
                    bool active = bool.TryParse(waitForWhats[2], out active) && active;

                    if (conditionFlag == ConditionFlag.None) return;

                    taskManager.Enqueue(() => Svc.Condition[conditionFlag] == !active, $"WaitFor-{conditionFlag}=={!active}-500", new TaskManagerConfiguration(500));
                    taskManager.Enqueue(() => Svc.Condition[conditionFlag] == active,  $"WaitFor-{conditionFlag}=={!active}",     new TaskManagerConfiguration(int.MaxValue));
                    break;
                case "BNpcInRadius":
                    if (waitForWhats.Length == 1)
                        return;
                    taskManager.Enqueue(() => !(GetObjectsByRadius(int.TryParse(waitForWhats[1], out int radius) ? radius : 0)?.Count > 0), $"WaitFor-BNpcInRadius{waitForWhats[1]}");
                    taskManager.Enqueue(() => IsReady, "WaitFor", new TaskManagerConfiguration(int.MaxValue));
                    break;
            }
            taskManager.Enqueue(() => Plugin.action = "");

        }

        private bool CheckPause() => plugin.Stage == Stage.Paused;

        public void ExitDuty(PathAction action)
        {
            taskManager.Enqueue(ExitDutyHelper.Invoke, "ExitDuty-Invoke");
            taskManager.Enqueue(() => ExitDutyHelper.State != ActionState.Running, "ExitDuty-WaitExitDutyRunning");
        }

        public unsafe bool IsAddonReady(nint addon) => addon > 0 && GenericHelpers.IsAddonReady((AtkUnitBase*)addon);

        public void SelectYesno(PathAction action)
        {
            taskManager.Enqueue(() => Plugin.action = $"SelectYesno: {action.Arguments[0]}", "SelectYesno");
            taskManager.Enqueue(() => AddonHelper.ClickSelectYesno(action.Arguments[0].ToUpper().Equals("YES")), "SelectYesno");
            taskManager.EnqueueDelay(500);
            taskManager.Enqueue(() => !IsCasting, "SelectYesno");
            taskManager.Enqueue(() => Plugin.action = "");
        }
        public void SelectString(PathAction action)
        {


            taskManager.Enqueue(() => Plugin.action = $"SelectString: {action.Arguments[0]}, {action.Note}", "SelectString");
            taskManager.Enqueue(() => AddonHelper.ClickSelectString(Convert.ToInt32(action.Arguments[0])), "SelectString");
            taskManager.EnqueueDelay(500);
            taskManager.Enqueue(() => !IsCasting, "SelectString");
            taskManager.Enqueue(() => Plugin.action = "");
        }

        public unsafe void MoveToObject(PathAction action)
        {
            if (!TryGetObjectIdRegex(action.Arguments[0], out string? objectDataId)) return;

            IGameObject? gameObject = null;
            Plugin.action = $"MoveToObject: {objectDataId}";

            taskManager.Enqueue(() => TryGetObjectByDataId(uint.Parse(objectDataId), out gameObject), "MoveToObject-GetGameObject");
            taskManager.Enqueue(() => MovementHelper.Move(gameObject), "MoveToObject-Move", new TaskManagerConfiguration(int.MaxValue));
            taskManager.Enqueue(() => Plugin.action = "");
        }

        public void TreasureCoffer(PathAction _) => 
            this.Wait(250);

        private bool TargetCheck(IGameObject? gameObject)
        {
            if (gameObject is not { IsTargetable: true } || !gameObject.IsValid() || (Svc.Targets.Target?.Equals(gameObject) ?? false))
                return true;

            if (EzThrottler.Check("TargetCheck"))
            {
                EzThrottler.Throttle("TargetCheck", 25);
                Svc.Targets.Target = gameObject;
            }
            return false;
        }

        public unsafe void Target(PathAction action)
        {
            if (!TryGetObjectIdRegex(action.Arguments[0], out string? objectDataId)) return;

            IGameObject? gameObject = null;
            Plugin.action = $"Target: {objectDataId}";

            taskManager.Enqueue(() => TryGetObjectByDataId(uint.Parse(objectDataId), out gameObject), "Target-GetGameObject");
            taskManager.Enqueue(() => this.TargetCheck(gameObject),                                   "Target-Check");
            taskManager.Enqueue(() => Plugin.action = "");
        }

        public void ClickTalk(PathAction action) => 
            taskManager.Enqueue(AddonHelper.ClickTalk, "ClickTalk");

        private static unsafe bool InteractableCheck(IGameObject? gameObject)
        {
            if (Conditions.Instance()->Mounted || Conditions.Instance()->RidingPillion)
                return true;

            if (Player.Available && IsCasting)
                return false;

            if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno) && !AddonHelper.ClickSelectYesno(true))
                return false;
            else if (AddonHelper.ClickSelectYesno(true))
                return true;

            if (GenericHelpers.TryGetAddonByName("SelectString", out AtkUnitBase* addonSelectString) && GenericHelpers.IsAddonReady(addonSelectString))
                return true;

            if (GenericHelpers.TryGetAddonByName("Talk", out AtkUnitBase* addonTalk) && GenericHelpers.IsAddonReady(addonTalk) && !AddonHelper.ClickTalk())
                return false;
            else if (AddonHelper.ClickTalk())
                return true;

            if (gameObject is not { IsTargetable: true } || !gameObject.IsValid() || !IsValid)
                return true;

            if (EzThrottler.Throttle("Interactable", 1000))
            {
                if (!TryGetObjectByDataId(gameObject?.BaseId ?? 0, igo => igo.IsTargetable, out gameObject)) return true;
                
                if (GetBattleDistanceToPlayer(gameObject!) > 2f)
                {
                    MovementHelper.Move(gameObject, 0.25f, 2f, false);
                }
                else
                {
                    Svc.Log.Debug($"InteractableCheck: Interacting with {gameObject!.Name} at {gameObject.Position} which is {GetDistanceToPlayer(gameObject)} away, because game object is not null: {gameObject != null} and IsTargetable: {gameObject!.IsTargetable} and IsValid: {gameObject.IsValid()}");
                    if (VNavmesh_IPCSubscriber.Path_IsRunning)
                        VNavmesh_IPCSubscriber.Path_Stop();
                    InteractWithObject(gameObject);
                };
            }

            return false;
        }
        private unsafe void Interactable(IGameObject? gameObject)
        {
            taskManager.Enqueue(() => BossMod_IPCSubscriber.SetMovement(false));
            taskManager.Enqueue(() => InteractableCheck(gameObject), "Interactable-InteractableCheck");
            taskManager.Enqueue(() => IsCasting, "Interactable-WaitIsCasting", new TaskManagerConfiguration(500));
            taskManager.Enqueue(() => !IsCasting, "Interactable-WaitNotIsCasting");
            taskManager.Enqueue(() => BossMod_IPCSubscriber.SetMovement(true));
            taskManager.EnqueueDelay(100);
            taskManager.Enqueue(() =>
            {
                bool boolAddonSelectYesno = GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno);
                bool boolAddonSelectString = GenericHelpers.TryGetAddonByName("SelectString", out AtkUnitBase* addonSelectString) && GenericHelpers.IsAddonReady(addonSelectString);
                bool boolAddonTalk = GenericHelpers.TryGetAddonByName("Talk", out AtkUnitBase* addonTalk) && GenericHelpers.IsAddonReady(addonTalk);

                if (!boolAddonSelectYesno && !boolAddonTalk && (!(gameObject?.IsTargetable ?? false)        ||
                                                                Conditions.Instance()->Mounted              ||
                                                                Conditions.Instance()->RidingPillion        ||
                                                                Svc.Condition[ConditionFlag.BetweenAreas]   ||
                                                                Svc.Condition[ConditionFlag.BetweenAreas51] ||
                                                                Svc.Condition[ConditionFlag.BeingMoved]     ||
                                                                Svc.Condition[ConditionFlag.Jumping61]      ||
                                                                Svc.Condition[ConditionFlag.CarryingItem]   ||
                                                                Svc.Condition[ConditionFlag.CarryingObject] ||
                                                                Svc.Condition[ConditionFlag.Occupied]       ||
                                                                Svc.Condition[ConditionFlag.Occupied30]     ||
                                                                Svc.Condition[ConditionFlag.Occupied33]     ||
                                                                Svc.Condition[ConditionFlag.Occupied38]     ||
                                                                Svc.Condition[ConditionFlag.Occupied39]     ||
                                                                boolAddonSelectString                       ||
                                                                gameObject?.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj))
                {
                    Plugin.action = "";
                }
                else
                {
                    if (TryGetObjectByDataId(gameObject?.BaseId ?? 0, out gameObject))
                    {
                        Svc.Log.Debug($"Interactable - Looping because {gameObject?.Name} is still Targetable: {gameObject?.IsTargetable} and we did not change conditions,  Position: {gameObject?.Position} Distance: {GetDistanceToPlayer(gameObject!.Position)}");
                        this.Interactable(gameObject);
                    }
                }
            }, "Interactable-LoopCheck");
        }

        public unsafe void Interactable(PathAction action)
        {
            List<uint> dataIds = [];
            string objectDataId;
            if (action.Arguments.Count > 1)
                action.Arguments.Each(x => dataIds.Add(TryGetObjectIdRegex(x, out objectDataId) ? uint.TryParse(objectDataId, out uint dataId) ? dataId : 0 : 0));
            else
                dataIds.Add(TryGetObjectIdRegex(action.Arguments[0], out objectDataId) ? 
                                uint.TryParse(objectDataId, out uint dataId) ? 
                                    dataId : 
                                    0 : 
                                0);

            if (dataIds.All(x => x.Equals(0u))) 
                return;

            IGameObject? gameObject = null;
            Plugin.action = $"Interactable";
            taskManager.Enqueue(() => Player.Character->InCombat && Plugin.stopForCombat || 
                                      (gameObject = Svc.Objects.Where(x => x.BaseId.EqualsAny(dataIds) && x.IsTargetable).OrderBy(GetDistanceToPlayer).FirstOrDefault()) != null, "Interactable-GetGameObjectUnlessInCombat");
            taskManager.Enqueue(() => { Plugin.action = $"Interactable: {gameObject?.BaseId}"; }, "Interactable-SetActionVar");
            taskManager.Enqueue(() =>
            {
                if (Player.Character->InCombat && Plugin.stopForCombat)
                {
                    taskManager.Abort();
                    taskManager.Enqueue(() => !Player.Character->InCombat, "Interactable-InCombatWait", new TaskManagerConfiguration(int.MaxValue));
                    this.Interactable(action);
                }
                else if (gameObject == null)
                {
                    taskManager.Abort();
                }
            }, "Interactable-InCombatCheck");
            taskManager.Enqueue(() => gameObject?.IsTargetable ?? true, "Interactable-WaitGameObjectTargetable");
            taskManager.Enqueue(() => this.Interactable(gameObject),       "Interactable-InteractableLoop");
        }

        private static bool TryGetObjectIdRegex(string input, out string output) => 
            (RegexHelper.ObjectIdRegex().IsMatch(input) ? 
                 output = RegexHelper.ObjectIdRegex().Match(input).Captures.First().Value : 
                 output = string.Empty) != string.Empty;

        private static bool BossCheck()
        {
            if (!Svc.Condition[ConditionFlag.InCombat])
                return true;

            if (EzThrottler.Throttle("PositionalChecker", 25) && ReflectionHelper.Avarice_Reflection.PositionalChanged(out Positional positional))
                BossMod_IPCSubscriber.SetPositional(positional);
            
            return false;
        }

        private static unsafe bool BossMoveCheck(Vector3 bossV3)
        {
            if (Plugin.bossObject != null && Plugin.bossObject.Struct()->InCombat)
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                return true;
            }
            return MovementHelper.Move(bossV3);
        }

        private void BossLoot(List<IGameObject>? gameObjects, int index)
        {
            if (gameObjects is not { Count: >= 1 })
            {
                taskManager.EnqueueDelay(1000);
                return;
            }

            taskManager.Enqueue(() => MovementHelper.Move(gameObjects[index], 0.25f, 1f), "BossLoot-MoveToChest");
            this.Wait(250);
            
            taskManager.Enqueue(() =>
            {
                index++;
                if (gameObjects.Count > index)
                    this.BossLoot(gameObjects, index);
                else
                    taskManager.EnqueueDelay(1000);
            }, "BossLoot-LoopOrDelay");
        }

        public void Boss(PathAction action)
        {
            Svc.Log.Info($"Starting Action Boss: {Plugin.bossObject?.Name.TextValue ?? "null"}");
            int index = 0;
            List<IGameObject>? treasureCofferObjects = null;
            Plugin.skipTreasureCoffer = false;
            this.StopForCombat(true, false);
            taskManager.Enqueue(() => BossMoveCheck(action.Position), "Boss-MoveCheck");
            if (Plugin.bossObject == null)
                taskManager.Enqueue(() => (Plugin.bossObject = GetBossObject()) != null, "Boss-GetBossObject");
            taskManager.Enqueue(() => Plugin.action      = $"Boss: {Plugin.bossObject?.Name.TextValue ?? ""}", "Boss-SetActionVar");
            taskManager.Enqueue(() => Svc.Targets.Target = Plugin.bossObject,                                  "Boss-SetTarget");
            taskManager.Enqueue(() => Svc.Condition[ConditionFlag.InCombat],                                   "Boss-WaitInCombat");
            taskManager.Enqueue(() => BossCheck(),                                                             "Boss-BossCheck", new TaskManagerConfiguration(int.MaxValue));
            taskManager.Enqueue(() => { Plugin.bossObject = null; },                                           "Boss-ClearBossObject");

            if (Configuration.LootTreasure)
            {
                
                taskManager.EnqueueDelay(1000);

                float lootRange = 50f;

                if (action.Arguments.Count > 0 && action.Arguments[0].TryGetVector3(out Vector3 lootPos))
                    lootRange = 5;
                else
                    lootPos = action.Position.LengthSquared() > 0.1f ? action.Position : Player.Position;

                if (action.Arguments.Count > 1)
                    if (float.TryParse(action.Arguments[1], out float lootRangeTmp))
                        lootRange = lootRangeTmp;

                taskManager.Enqueue(() => treasureCofferObjects = GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)?.
                                                                  Where(x => BelowDistanceToPoint(x.Position, lootPos, lootRange, 10)).ToList(), "Boss-GetTreasureChestsBounded");
                taskManager.Enqueue(() => this.BossLoot(treasureCofferObjects, index), "Boss-LootCheck");
            }
        }

        public void BLULoad(PathAction action)
        {
            if(GetJob() == Job.BLU)
                if (bool.TryParse(action.Arguments[0], out bool enable))
                    if (byte.TryParse(action.Arguments[1], out byte spell))
                    {
                        taskManager.Enqueue(() => !Svc.Condition.Any(ConditionFlag.InCombat, ConditionFlag.Casting));

                        if(enable)
                            taskManager.Enqueue(() => BLUHelper.SpellLoadoutIn(spell));
                        else
                            taskManager.Enqueue(() => BLUHelper.SpellLoadoutOut(spell));
                    }
        }

        public unsafe void Action(PathAction action)
        {
            Svc.Log.Debug($"Action: {action.Arguments[0]}");
            if (Enum.TryParse(action.Arguments[0], out ActionType type))
            {
                Svc.Log.Debug($"Action: {type} {action.Arguments[1]}");
                if (uint.TryParse(action.Arguments[1], out uint id))
                {
                    if (ActionManager.Instance()->GetActionStatus(type, id) != 573)
                    {
                        taskManager.Enqueue(() => ActionManager.Instance()->GetActionStatus(type, id) == 0, "Action_WaitTillReady");
                        taskManager.Enqueue(() => ActionManager.Instance()->UseAction(type, id),            "Action_UsingAction");
                    }
                }
            }
        }

        public void PausePandora(PathAction _)
        {
            return;
            //disable for now until we have a need other than interact objects
            //if (PandorasBox_IPCSubscriber.IsEnabled)
            //_taskManager.Enqueue(() => PandorasBox_IPCSubscriber.PauseFeature(featureName, int.Parse(intMs)));
        }

        public void Revival(PathAction _) => 
            taskManager.Enqueue(() => Plugin.action = "");

        public void CameraFacing(PathAction action)
        {
            if (action != null)
            {
                string[] v = action.Arguments[0].Split(", ");
                if (v.Length == 3)
                {
                    Vector3 facingPos = new(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                    Plugin.overrideCamera.Face(facingPos);
                }
            }
        }

        public enum OID : uint
        {
            Blue = 0x1E8554,
            Red = 0x1E8A8C,
            Green = 0x1E8A8D,
        }

        private string? GlobalStringStore;

        private unsafe void PraeFrameworkUpdateMount(IFramework _)
        {
            if (!EzThrottler.Throttle("PraeUpdate", 50))
                return;

            List<IGameObject>? objects = GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc);

            if (objects != null)
            {
                IGameObject? protoArmOrDoor = objects.FirstOrDefault(x => x is { IsTargetable: true, BaseId: 14566 or 14616 } && GetDistanceToPlayer(x) <= 25);
                if (protoArmOrDoor != null)
                    Svc.Targets.Target = protoArmOrDoor;
            }

            if (Svc.Condition[ConditionFlag.Mounted] && Svc.Targets.Target != null && Svc.Targets.Target.IsHostile())
            {
                Vector2 dir = Vector2.Normalize(new Vector2(Svc.Targets.Target.Position.X, Svc.Targets.Target.Position.Z) - new Vector2(Player.Position.X, Player.Position.Z));
                float rot = (float)Math.Atan2(dir.X, dir.Y);

                if(Player.Available)
                    Player.Object!.Struct()->SetRotation(rot);

                Vector3 targetPosition = Svc.Targets.Target.Position;
                ActionManager.Instance()->UseActionLocation(ActionType.Action, 1128, Player.GameObject->GetGameObjectId(), &targetPosition);
            }
        }


        private static readonly uint[] praeGaiusIds = [9020u, 14453u, 14455u];
        private void PraeFrameworkUpdateGaius(IFramework _)
        {
            if (!EzThrottler.Throttle("PraeUpdate", 50) || !IsReady || Svc.Targets.Target != null && praeGaiusIds.Contains(Svc.Targets.Target.BaseId))
                return;

            List<IGameObject>? objects = GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc);

            IGameObject? gaius = objects?.FirstOrDefault(x => x.IsTargetable && praeGaiusIds.Contains(x.BaseId));
            if (gaius != null)
                Svc.Targets.Target = gaius;
        }


        public unsafe void DutySpecificCode(PathAction action)
        {
            IGameObject? gameObject = null;
            switch (Svc.ClientState.TerritoryType)
            {
                //Prae
                case 1044:
                    switch (action.Arguments[0])
                    {
                        case "1":
                            Plugin.FrameworkUpdateInDuty += this.PraeFrameworkUpdateMount;
                            this.Interactable(new PathAction { Arguments = ["2012819"] });
                            break;
                        case "2":
                            Plugin.FrameworkUpdateInDuty -= this.PraeFrameworkUpdateMount;
                            break;
                        case "3":
                            Plugin.FrameworkUpdateInDuty += this.PraeFrameworkUpdateGaius;
                            break;
                    }
                    break;
                //Sastasha
                //Blue -  2000213
                //Red -  2000214
                //Green - 2000215
                case 1036:
                    switch (action.Arguments[0])
                    {
                        case "1":
                            taskManager.Enqueue(() => (gameObject = GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)?.FirstOrDefault(a => a.IsTargetable && (OID)a.BaseId is OID.Blue or OID.Red or OID.Green)) != null, "DutySpecificCode");
                            taskManager.Enqueue(() =>
                            {
                                if (gameObject != null)
                                    switch ((OID)gameObject.BaseId)
                                    {
                                        case OID.Blue:
                                            this.GlobalStringStore = "2000213";
                                            break;
                                        case OID.Red:
                                            this.GlobalStringStore = "2000214";
                                            break;
                                        case OID.Green:
                                            this.GlobalStringStore = "2000215";
                                            break;
                                    }
                            }, "DutySpecificCode");
                            break;
                        case "2":
                            taskManager.Enqueue(() => this.Interactable(new PathAction() { Arguments = [this.GlobalStringStore ?? ""] }), "DutySpecificCode");
                            break;
                        case "3":
                            taskManager.Enqueue(() => (gameObject = GetObjectByDataId(2000216)) != null, "DutySpecificCode");
                            taskManager.Enqueue(() => MovementHelper.Move(gameObject, 0.25f, 2.5f), "DutySpecificCode");
                            taskManager.EnqueueDelay(1000);
                            taskManager.Enqueue(() => InteractWithObject(gameObject), "DutySpecificCode");
                            break;
                        default: break;
                    }
                    break;
                //Mount Rokkon
                case 1137:
                    switch (action.Arguments[0])
                    {
                        case "5":
                            taskManager.Enqueue(() => (gameObject = GetObjectByDataId(16140)) != null, "DutySpecificCode");
                            taskManager.Enqueue(() => MovementHelper.Move(gameObject, 0.25f, 2.5f), "DutySpecificCode");
                            taskManager.EnqueueDelay(1000);
                            taskManager.Enqueue(() => InteractWithObject(gameObject), "DutySpecificCode");
                            if (IsValid)
                            {
                                taskManager.Enqueue(() => InteractWithObject(gameObject), "DutySpecificCode");
                                taskManager.Enqueue(() => AddonHelper.ClickSelectString(0));
                            }
                            break;
                        case "6":
                            taskManager.Enqueue(() => (gameObject = GetObjectByDataId(16140)) != null, "DutySpecificCode");
                            taskManager.Enqueue(() => MovementHelper.Move(gameObject, 0.25f, 2.5f), "DutySpecificCode");
                            taskManager.EnqueueDelay(1000);
                            if (IsValid)
                            {
                                taskManager.Enqueue(() => InteractWithObject(gameObject), "DutySpecificCode");
                                taskManager.Enqueue(() => AddonHelper.ClickSelectString(1));
                            }
                            break;
                        case "12":
                            taskManager.Enqueue(() => Chat.ExecuteCommand("/rotation Settings AoEType Off"), "DutySpecificCode");
                            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 16), "DutySpecificCode");
                            taskManager.Enqueue(() => EzThrottler.Throttle("DutySpecificCode", Convert.ToInt32(500)));
                            taskManager.Enqueue(() => EzThrottler.Check("DutySpecificCode"), "DutySpecificCode", new TaskManagerConfiguration(Convert.ToInt32(500)));
                            taskManager.Enqueue(() => Chat.ExecuteCommand("/mk ignore1"), "DutySpecificCode");
                            taskManager.Enqueue(() => EzThrottler.Throttle("DutySpecificCode", Convert.ToInt32(100)));
                            taskManager.Enqueue(() => EzThrottler.Check("DutySpecificCode"), "DutySpecificCode", new TaskManagerConfiguration(Convert.ToInt32(100)));

                            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 16), "DutySpecificCode");
                            taskManager.Enqueue(() => EzThrottler.Throttle("DutySpecificCode", Convert.ToInt32(500)));
                            taskManager.Enqueue(() => EzThrottler.Check("DutySpecificCode"), "DutySpecificCode", new TaskManagerConfiguration(Convert.ToInt32(500)));
                            taskManager.Enqueue(() => Chat.ExecuteCommand("/mk ignore2"), "DutySpecificCode");
                            taskManager.Enqueue(() => EzThrottler.Throttle("DutySpecificCode", Convert.ToInt32(100)));
                            taskManager.Enqueue(() => EzThrottler.Check("DutySpecificCode"), "DutySpecificCode", new TaskManagerConfiguration(Convert.ToInt32(100)));

                            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 16), "DutySpecificCode");
                            taskManager.Enqueue(() => EzThrottler.Throttle("DutySpecificCode", Convert.ToInt32(500)));
                            taskManager.Enqueue(() => EzThrottler.Check("DutySpecificCode"), "DutySpecificCode", new TaskManagerConfiguration(Convert.ToInt32(500)));
                            taskManager.Enqueue(() => Chat.ExecuteCommand("/mk attack1"), "DutySpecificCode");
                            break;
                        case "13":
                            taskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 16), "DutySpecificCode");
                            taskManager.Enqueue(() => EzThrottler.Throttle("DutySpecificCode", Convert.ToInt32(500)));
                            taskManager.Enqueue(() => EzThrottler.Check("DutySpecificCode"), "DutySpecificCode", new TaskManagerConfiguration(Convert.ToInt32(500)));
                            taskManager.Enqueue(() => Chat.ExecuteCommand("/mk attack1"), "DutySpecificCode");
                            break;

                        default: break;
                    }
                    break;
                //Xelphatol
                case 1113:
                    switch (action.Arguments[0])
                    {
                        case "1":
                            taskManager.Enqueue(() => TryGetObjectByDataId(2007400, out gameObject), "DutySpecificCode");
                            taskManager.Enqueue(() =>
                                {
                                    if (!EzThrottler.Throttle("DSC", 500) || Player.Character->IsCasting) return false;

                                    if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno) && !AddonHelper.ClickSelectYesno(true))
                                        return false;
                                    else if (AddonHelper.ClickSelectYesno(true))
                                        return true;

                                    if (gameObject == null) return true;

                                    if (GetBattleDistanceToPlayer(gameObject) > 2.5f)
                                    {
                                        MovementHelper.Move(gameObject, 0.25f, 2.5f);
                                    }
                                    else
                                    {
                                        MovementHelper.Stop();
                                        InteractWithObject(gameObject);
                                    }

                                    return false;
                                }, "DSC-Xelphatol-ClickTailWind");
                            break;
                        case "2":
                            taskManager.Enqueue(() => TryGetObjectByDataId(2007401, out gameObject), "DutySpecificCode");
                            taskManager.Enqueue(() =>
                            {
                                if (!EzThrottler.Throttle("DSC", 500) || Player.Character->IsCasting) return false;

                                if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno) && !AddonHelper.ClickSelectYesno(true))
                                    return false;
                                else if (AddonHelper.ClickSelectYesno(true))
                                    return true;

                                if (gameObject == null) return true;

                                if (GetBattleDistanceToPlayer(gameObject) > 2.5f)
                                {
                                    MovementHelper.Move(gameObject, 0.25f, 2.5f);
                                }
                                else
                                {
                                    MovementHelper.Stop();
                                    InteractWithObject(gameObject);
                                }

                                return false;
                            }, "DSC-Xelphatol-ClickTailWind");
                            break;
                        default:
                            break;
                    }
                    break;
                default: break;
            }
        }
    }
}
