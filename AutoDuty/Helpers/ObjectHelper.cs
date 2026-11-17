using AutoDuty.IPC;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Dalamud.Game.ClientState.Objects.SubKinds;
    using ECommons.ExcelServices;
    using ECommons.GameFunctions;
    using ECommons.PartyFunctions;
    using FFXIVClientStructs.FFXIV.Client.Game.Object;
    using FFXIVClientStructs.FFXIV.Client.UI.Info;
    using Lumina.Excel.Sheets;
    using Buddy = Buddy;
    using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

    internal static class ObjectHelper
    {
        internal static bool TryGetObjectByDataId(uint dataId, out IGameObject? gameObject) => (gameObject = Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(x => x.BaseId == dataId)) != null;
        internal static bool TryGetObjectByDataId(uint dataId, Func<IGameObject, bool> condition, out IGameObject? gameObject) => (gameObject = Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(x => x.BaseId == dataId && condition(x))) != null;

        internal static List<IGameObject>? GetObjectsByObjectKind(ObjectKind objectKind) => [.. Svc.Objects.OrderBy(GetDistanceToPlayer).Where(o => o.ObjectKind == objectKind)];

        internal static IGameObject? GetObjectByObjectKind(ObjectKind objectKind) => Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(o => o.ObjectKind == objectKind);

        internal static List<IGameObject>? GetObjectsByRadius(float radius) => [.. Svc.Objects.OrderBy(GetDistanceToPlayer).Where(o => GetDistanceToPlayer(o) <= radius)];
        internal static List<IGameObject>? GetObjectsByRadius(Vector3 position, float radius) => [.. Svc.Objects.OrderBy(p => GetDistanceToPosSquared(position, p.Position)).Where(o => GetDistanceToPosSquared(position, o.Position) <= radius*radius)];

        internal static IGameObject? GetObjectByRadius(float radius) => Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(o => GetDistanceToPlayer(o) <= radius);

        internal static List<IGameObject>? GetObjectsByName(string name) => [.. Svc.Objects.OrderBy(GetDistanceToPlayer).Where(o => o.Name.TextValue.Equals(name, StringComparison.CurrentCultureIgnoreCase))];

        internal static IGameObject? GetObjectByName(string name) => Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(o => o.Name.TextValue.Equals(name, StringComparison.CurrentCultureIgnoreCase));

        internal static IGameObject? GetObjectByDataId(uint                     id)                      => Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(o => o.BaseId == id);
        internal static IGameObject? GetObjectByDataIds(Func<IGameObject, bool> condition, params uint[] ids) => Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(o => ids.Contains(o.BaseId) && condition(o));
        internal static IGameObject? GetObjectByDataIds(params uint[] ids) => Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(o => ids.Contains(o.BaseId));

        internal static List<IGameObject>? GetObjectsByPartialName(string name) => [.. Svc.Objects.OrderBy(GetDistanceToPlayer).Where(o => o.Name.TextValue.Contains(name, StringComparison.CurrentCultureIgnoreCase))];

        internal static IGameObject? GetObjectByPartialName(string name) => Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(o => o.Name.TextValue.Contains(name, StringComparison.CurrentCultureIgnoreCase));

        internal static List<IGameObject>? GetObjectsByNameAndRadius(string objectName) => 
            [.. Svc.Objects.OrderBy(GetDistanceToPlayer).Where(g => g.Name.TextValue.Equals(objectName, StringComparison.CurrentCultureIgnoreCase) && Vector3.Distance(Player.Position, g.Position) <= 10)];

        internal static IGameObject? GetObjectByNameAndRadius(string objectName) => 
            Svc.Objects.OrderBy(GetDistanceToPlayer).FirstOrDefault(g => g.Name.TextValue.Equals(objectName, StringComparison.CurrentCultureIgnoreCase) && Vector3.Distance(Player.Position, g.Position) <= 10);

        internal static IBattleChara? GetBossObject(int radius = 100, Vector3? position = null) => (position.HasValue ? GetObjectsByRadius(position.Value, radius) : GetObjectsByRadius(radius))?.OfType<IBattleChara>().FirstOrDefault(IsBoss);

        private static readonly Dictionary<string, uint> bossNameIds = new(StringComparer.OrdinalIgnoreCase);

        internal static string[] BossNamesForStep(PathAction step)
        {
            string hint = step.Note.Trim();
            if (hint.Length == 0) return [];
            if (!bossNameIds.TryGetValue(hint, out uint id))
            {
                id = Svc.Data.GetExcelSheet<BNpcName>(Dalamud.Game.ClientLanguage.English)
                    .FirstOrDefault(row => string.Equals(row.Singular.ToString(), hint, StringComparison.OrdinalIgnoreCase)).RowId;
                if (id == 0)
                    id = Svc.Data.GetExcelSheet<BNpcName>().FirstOrDefault(row =>
                        string.Equals(row.Singular.ToString(), hint, StringComparison.OrdinalIgnoreCase)).RowId;
                bossNameIds[hint] = id;
            }
            return id == 0 ? [] : new[] { hint, Svc.Data.GetExcelSheet<BNpcName>().GetRowOrDefault(id)?.Singular.ToString() ?? "" }
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
        }

        internal static IBattleChara[] BossCandidatesForStep(PathAction step, IEnumerable<IBattleChara> objects, int radius = 100)
        {
            Vector3 origin = step.Position == Vector3.Zero ? Player.Position : step.Position;
            var nearby = objects.Where(o => o.ObjectKind == ObjectKind.BattleNpc
                && Vector3.DistanceSquared(origin, o.Position) <= radius * radius && MathF.Abs(origin.Y - o.Position.Y) <= 10)
                .OrderBy(o => Vector3.DistanceSquared(origin, o.Position)).ToArray();
            var names = BossNamesForStep(step);
            if (names.Length > 0)
                return nearby.Where(o => names.Any(name => string.Equals(name, o.Name.TextValue, StringComparison.OrdinalIgnoreCase))).ToArray();
            // For unnamed legacy steps use only the nearest ranked/module boss. Never arbitrary adds.
            return nearby.Where(IsBoss).Take(1).ToArray();
        }

        internal static IBattleChara? GetBossForStep(PathAction step, int radius = 100) =>
            BossCandidatesForStep(step, Svc.Objects.OfType<IBattleChara>(), radius)
                .FirstOrDefault(o => o.CurrentHp > 0 && !o.IsDead && o.IsTargetable);

        internal readonly record struct BossObjective(string Text, int Current, int Needed);
        internal static unsafe BossObjective[] BossObjectives()
        {
            var director = FFXIVClientStructs.FFXIV.Client.Game.Event.EventFramework.Instance()->GetContentDirector();
            var todos = director == null ? null : director->GetDirectorTodos();
            if (todos == null) return [];
            var result = new List<BossObjective>();
            for (int i = 0; i < todos->Count; i++)
            {
                var item = (*todos)[i];
                result.Add(new(item.Text.ToString(), item.CurrentCount, item.NeededCount));
            }
            return result.ToArray();
        }

        internal static unsafe float GetDistanceToPosSquared(Vector3 pos, Vector3 target) => Vector3.DistanceSquared(pos, target);
        internal static unsafe float GetDistanceToPlayer(IGameObject gameObject) => GetDistanceToPlayer(gameObject.Position);

        internal static unsafe float GetDistanceToPlayer(Vector3 v3) => Vector3.Distance(v3, Player.GameObject->Position);

        internal static unsafe bool BelowDistanceToPlayer(Vector3 v3, float maxDistance, float maxHeightDistance) => BelowDistanceToPoint(v3, Player.GameObject->Position, maxDistance, maxHeightDistance);

        internal static bool BelowDistanceToPoint(Vector3 target, Vector3 origin, float maxDistance, float maxHeightDistance) => Vector3.Distance(target, origin) < maxDistance &&
                                                                                                                     MathF.Abs(target.Y - origin.Y) < maxHeightDistance;
        /// <summary>
        ///     Converts a GameObject pointer to an IGameObject from the object table.
        /// </summary>
        /// <param name="ptr">The GameObject pointer to convert.</param>
        /// <returns>An IGameObject if found in the object table; otherwise, null.</returns>
        public static unsafe IGameObject? GetObjectFrom(GameObject* ptr) =>
            ptr == null ? null : Svc.Objects.FirstOrDefault(x => x.Address == (IntPtr)ptr);

        internal static unsafe IGameObject? GetPartyMemberFromRole(string role)
        {
            if (Player.Object != null && Player.Object.ClassJob.Value.GetJobRole().ToString().Contains(role, StringComparison.InvariantCultureIgnoreCase)) 
                return Player.Object;

            if (Svc.Party.PartyId != 0) 
                return Svc.Party.FirstOrDefault(x => x.ClassJob.Value.GetJobRole().ToString().Contains(role, StringComparison.InvariantCultureIgnoreCase))?.GameObject;

            IEnumerable<Buddy.BuddyMember>? buddies = UIState.Instance()->Buddy.BattleBuddies.ToArray().Where(x => x.DataId != 0);
            foreach (Buddy.BuddyMember buddy in buddies)
            {
                IGameObject? gameObject = Svc.Objects.FirstOrDefault(x => x.EntityId == buddy.EntityId);

                if (gameObject == null) 
                    continue;

                ClassJob? classJob = ((ICharacter)gameObject).ClassJob.ValueNullable;

                if (classJob == null) 
                    continue;

                if (classJob.Value.GetJobRole().ToString().Contains(role, StringComparison.InvariantCultureIgnoreCase))
                    return gameObject;
            }
            return null;
        }

        internal static unsafe IGameObject? GetTankPartyMember() => GetPartyMemberFromRole("Tank");

        internal static unsafe IGameObject? GetHealerPartyMember() => GetPartyMemberFromRole("Healer");

        //RotationSolver
        internal static unsafe float GetBattleDistanceToPlayer(IGameObject gameObject)
        {
            if (gameObject == null) return float.MaxValue;
            IPlayerCharacter? player = Player.Object;
            if (player == null) return float.MaxValue;

            float distance = Vector3.Distance(player.Position, gameObject.Position) - player.HitboxRadius;
            distance -= gameObject.HitboxRadius;
            return distance;
        }

        internal static BNpcBase? GetObjectNPC(IGameObject gameObject) => Svc.Data.GetExcelSheet<BNpcBase>()?.GetRow(gameObject.BaseId) ?? null;

        //From RotationSolver
        internal static bool IsBossFromIcon(IGameObject gameObject) => GetObjectNPC(gameObject)?.Rank is 1 or 2 or 6;

        internal static bool IsBoss(IGameObject gameObject) => IsBossFromIcon(gameObject) || BossMod_IPCSubscriber.HasModuleByDataId(gameObject.BaseId);

        internal static unsafe void InteractWithObject(IGameObject? gameObject, bool face = true)
        {
            try
            {
                if (gameObject is not { IsTargetable: true }) 
                    return;
                if (face) 
                    Plugin.overrideCamera.Face(gameObject.Position);
                GameObject* gameObjectPointer = (GameObject*)gameObject.Address;
                TargetSystem.Instance()->InteractWithObject(gameObjectPointer, false);
            }
            catch (Exception ex)
            {
                Svc.Log.Info($"InteractWithObject: Exception: {ex}");
            }
        }
        internal static unsafe AtkUnitBase* InteractWithObjectUntilAddon(IGameObject? gameObject, string addonName)
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(addonName, out AtkUnitBase* addon) && GenericHelpers.IsAddonReady(addon))
                return addon;

            if (EzThrottler.Throttle("InteractWithObjectUntilAddon"))
                InteractWithObject(gameObject);
            
            return null;
        }

        internal static unsafe bool InteractWithObjectUntilNotValid(IGameObject? gameObject)
        {
            if (gameObject == null || !PlayerHelper.IsValid)
                return true;

            if (EzThrottler.Throttle("InteractWithObjectUntilNotValid"))
                InteractWithObject(gameObject);
            
            return false;
        }

        internal static unsafe bool InteractWithObjectUntilNotTargetable(IGameObject? gameObject)
        {
            if (gameObject is not { IsTargetable: true })
                return true;

            if (EzThrottler.Throttle("InteractWithObjectUntilNotTargetable"))
                InteractWithObject(gameObject);

            return false;
        }

        internal static unsafe bool PartyValidation()
        {
            if (UniversalParty.Length < 4)
                return false;

            bool healer = false;
            bool tank = false;
            int dpsCount = 0;

            InfoProxyPartyMember* instance = InfoProxyPartyMember.Instance();
            
            for (uint i = 0; i < instance->GetEntryCount(); i++)
            {
                InfoProxyCommonList.CharacterData* characterData = instance->GetEntry(i);
                
                switch(((Job)characterData->Job).GetCombatRole())
                {
                    case CombatRole.Tank:
                        tank = true;
                        break;
                    case CombatRole.Healer:
                        healer = true;
                        break;
                    case CombatRole.DPS:
                        dpsCount++;
                        break;
                    case CombatRole.NonCombat:
                    default:
                        break;
                }
            }
            return (tank && healer && dpsCount > 1);
        }
    }
}
