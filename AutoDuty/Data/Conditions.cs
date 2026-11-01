using AutoDuty.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using System.Linq;
using AutoDuty.Data;

// ReSharper disable UnusedType.Global

namespace AutoDuty.Data;

using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Newtonsoft.Json;
using System.Globalization;
using System.Linq;
using System.Numerics;

public abstract class PathActionCondition
{
    public const ConditionType PARSE_KEY = ConditionType.None;

    public static readonly Dictionary<ConditionType, Type> PARSE_KEYS;

    [JsonIgnore]
    public virtual ConditionType ParseKey => PARSE_KEY;

    static PathActionCondition()
    {
        PARSE_KEYS = [];
        foreach (Type type in typeof(PathActionCondition).Assembly.GetTypes().Where(x => 
                                                                                        typeof(PathActionCondition).IsAssignableFrom(x) && x is { IsAbstract: false, IsInterface: false } && x != typeof(PathActionCondition)).ToList())
            PARSE_KEYS.Add((ConditionType) (type.GetField(nameof(PARSE_KEY))!.GetValue(null)!), type);
    }

    public static readonly Dictionary<string, Func<object, object, bool>> operations = new()
                                                                                       {
                                                                                           { ">", (x,  y) => Convert.ToSingle(x) > Convert.ToSingle(y) },
                                                                                           { ">=", (x, y) => Convert.ToSingle(x) >= Convert.ToSingle(y) },
                                                                                           { "<", (x,  y) => Convert.ToSingle(x) < Convert.ToSingle(y) },
                                                                                           { "<=", (x, y) => Convert.ToSingle(x) <= Convert.ToSingle(y) },
                                                                                           { "==", (x, y) => x                   == y },
                                                                                           { "!=", (x, y) => x                   != y }
                                                                                       };

    public static PathActionCondition? ConditionSelection()
    {
        ConditionType newConditionType = ConditionType.None;
        if (ImGuiEx.EnumCombo(Loc.Get("BuildTab.AddNewCondition"), ref newConditionType))
            if (newConditionType != ConditionType.None)
                return newConditionType switch
                {
                    ConditionType.Distance => new PathActionConditionDistance(),
                    ConditionType.ItemCount => new PathActionConditionItemCount(),
                    ConditionType.ObjectData => new PathActionConditionObjectData(),
                    ConditionType.Job => new PathActionConditionJob(),
                    ConditionType.ActionStatus => new PathActionConditionActionStatus(),
                    ConditionType.VariantPath => new PathActionConditionVariantPath(),
                    ConditionType.ConditionFlag => new PathActionConditionConditionFlag(),
                    ConditionType.Collision => new PathActionConditionCollision(),
                    ConditionType.ToDo => new PathActionConditionToDo(),
                    ConditionType.Not => new PathActionConditionNot(),
                    ConditionType.Or => new PathActionConditionOr() ,
                    ConditionType.And => new PathActionConditionAnd(),
                    _ => throw new ArgumentOutOfRangeException()
                };

        return null;
    }

    public abstract bool IsFulfilled();
    public abstract void DrawConfig();

    public abstract IEnumerable<(Vector4 color, string text)> DrawStepEntry();
}

public class PathActionConditionNot : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.Not;
    public override ConditionType ParseKey => PARSE_KEY;

    public PathActionCondition? condition;

    public override bool IsFulfilled() => 
        !this.condition?.IsFulfilled() ?? true;

    public override void DrawConfig()
    {
        if(this.condition == null)
        {
            PathActionCondition? pathActionCondition = PathActionCondition.ConditionSelection();
            if(pathActionCondition != null)
                this.condition = pathActionCondition;
        }
        if(this.condition != null)
        {
            ImGui.Text(this.condition.ParseKey.ToLocalizedString());
            ImGui.SameLine();

            float indent = ImGui.GetCursorPosX();
            ImGui.Indent(indent);
            this.condition.DrawConfig();
            ImGui.Unindent(indent);
        }
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), "Not?");

        foreach ((Vector4 color, string text) step in this.condition.DrawStepEntry())
            yield return step;
    }
}

public class PathActionConditionJob : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.Job;
    public override  ConditionType ParseKey => PARSE_KEY;

    public JobWithRole job = JobWithRole.All;

    public override bool IsFulfilled() =>
        this.job.HasJob(PlayerHelper.GetJob());

    public override void DrawConfig() =>
        JobWithRoleHelper.DrawCategory(JobWithRole.All, ref this.job);

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{ConditionType.Job.ToLocalizedString()} ");
        yield return (new Vector4(1, 165 / 255f, 0, 1), this.job.ToLocalizedString());
    }
}

public class PathActionConditionActionStatus : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.ActionStatus;
    public override  ConditionType ParseKey => PARSE_KEY;

    public ActionType type = ActionType.Action;
    public uint       id;
    public uint       statusCode;

    public override bool IsFulfilled()
    {
        unsafe
        {
            return ActionManager.Instance()->GetActionStatus(this.type, this.id) == this.statusCode;
        }
    }

    public override void DrawConfig()
    {
        ImGuiEx.EnumCombo("Action Type", ref this.type);
        ImGui.SameLine();
        ImGui.InputUInt("Action ID", ref this.id);
        ImGui.SameLine();
        ImGui.InputUInt("Status Code", ref this.statusCode);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{ConditionType.ActionStatus.ToLocalizedString()} ");
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{this.type.ToCustomString()} {this.id} {this.statusCode}");
    }
}

public class PathActionConditionCollision : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.Collision;
    public override  ConditionType ParseKey => PARSE_KEY;

    public InstanceType type = InstanceType.CollisionBox;
    public ulong        id;

    [JsonIgnore]
    private string textInput = string.Empty;

    public override bool IsFulfilled()
    {
        unsafe
        {
            static V* FindPtr<K, V>(ref StdMap<K, Pointer<V>> map, K key) where K : unmanaged, IComparable where V : unmanaged =>
                map.TryGetValuePointer(key, out Pointer<V>* ptr) && ptr != null ? ptr->Value : null;

            LayoutManager*                           layout = LayoutWorld.Instance()->ActiveLayout;
            StdMap<ulong, Pointer<ILayoutInstance>>* insts  = layout != null ? FindPtr(ref layout->InstancesByType, this.type) : null;
            ILayoutInstance*                         inst   = insts  != null ? FindPtr(ref *insts,                  this.id) : null;
            return inst != null && inst->IsColliderActive();
        }
    }

    public override void DrawConfig()
    {
        ImGuiEx.EnumCombo("Instance Type", ref this.type);
        ImGui.SameLine();

        if(this.textInput.Length == 0)
            this.textInput = this.id.ToString("X");

        if(ImGui.InputText("Layout Instance ID", ref this.textInput))
            if (ulong.TryParse(this.textInput, NumberStyles.HexNumber, null, out ulong parsedId))
                this.id = parsedId;
            else
                this.textInput = string.Empty;
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{PARSE_KEY.ToLocalizedString()} ");
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{this.type.ToCustomString()} {this.id:X}");
    }
}


public class PathActionConditionItemCount : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.ItemCount;
    public override  ConditionType ParseKey => PARSE_KEY;

    public uint   itemId;
    public uint   quantity;
    public string operatorValue = operations.Keys.First();

    public override bool IsFulfilled()
    {
        if (!operations.TryGetValue(this.operatorValue, out Func<object, object, bool>? operationFunc))
            return false;
        int itemCount = InventoryHelper.ItemCount(this.itemId);
        return operationFunc(itemCount, this.quantity);
    }

    public override void DrawConfig()
    {
        ImGuiEx.InputUint("itemId", ref this.itemId);
        ImGui.SameLine();
        ImGuiEx.Combo("Operation", ref this.operatorValue, operations.Keys);
        ImGui.SameLine();
        ImGuiEx.InputUint("Quantity", ref this.quantity);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{ConditionType.ItemCount.ToLocalizedString()} ");
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{this.itemId} {this.operatorValue} {this.quantity}");
    }
}

public class PathActionConditionObjectData : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.ObjectData;
    public override  ConditionType ParseKey => PARSE_KEY;

    public uint               baseId;
    public ObjectDataProperty property;
    public int                value;


    public override bool IsFulfilled()
    {
        IGameObject? gameObject = null;
        if ((gameObject = ObjectHelper.GetObjectByDataId(this.baseId)) != null)
        {
            unsafe
            {
                GameObject* csObj = gameObject.Struct();

                return this.property switch
                {
                    ObjectDataProperty.EventState => csObj->EventState          == (byte)this.value,
                    ObjectDataProperty.IsTargetable => csObj->GetIsTargetable() == (this.value != 0),
                    _ => false
                };
            }
        }

        return false;
    }

    public override void DrawConfig()
    {
        ImGuiEx.InputUint("BaseId", ref this.baseId);
        ImGui.SameLine();
        ImGuiEx.EnumCombo("Property", ref this.property);
        ImGui.SameLine();
        ImGui.InputInt("Value", ref this.value);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{ConditionType.ObjectData.ToLocalizedString()} ");
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{this.baseId} {this.property.ToLocalizedString()} {this.value}");
    }
}

public class PathActionConditionDistance : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.Distance;
    public override  ConditionType ParseKey => PARSE_KEY;

    public DistanceLocationTypes origin = DistanceLocationTypes.Location;
    public uint                  originId;
    public Vector3               originLoc;


    public DistanceLocationTypes target = DistanceLocationTypes.Player;
    public uint                  targetId;
    public Vector3               targetLoc;

    public string operatorValue = operations.Keys.First();

    public float distance = 1f;

    public override bool IsFulfilled()
    {
        unsafe
        {
            Vector3 originVec = this.origin switch
            {
                DistanceLocationTypes.Player => Player.GameObject->Position,
                DistanceLocationTypes.Object => ObjectHelper.GetObjectByDataId(this.originId)?.Position ?? Vector3.PositiveInfinity,
                DistanceLocationTypes.Location => this.originLoc,
                _ => throw new ArgumentOutOfRangeException()
            };

            Vector3 targetVec = this.target switch
            {
                DistanceLocationTypes.Player => Player.GameObject->Position,
                DistanceLocationTypes.Object => ObjectHelper.GetObjectByDataId(this.targetId)?.Position ?? Vector3.NegativeInfinity,
                DistanceLocationTypes.Location => this.targetLoc,
                _ => throw new ArgumentOutOfRangeException()
            };

            float offset = Vector3.Distance(originVec, targetVec);

            return operations.TryGetValue(this.operatorValue, out Func<object, object, bool>? operationFunc) &&
                   operationFunc(offset, this.distance);
        }
    }

    public override void DrawConfig()
    {
        ImGuiEx.EnumCombo("origin", ref this.origin);

        switch (this.origin)
        {
            case DistanceLocationTypes.Object:
                ImGui.SameLine();
                ImGuiEx.InputUint("ID##origin_baseID", ref this.originId);
                break;
            case DistanceLocationTypes.Location:
                ImGui.SameLine();
                ImGui.PushItemWidth(50f);
                float x = this.originLoc.X;
                ImGui.InputFloat("X##origin_X", ref x);
                ImGui.SameLine();
                float y = this.originLoc.Y;
                ImGui.InputFloat("Y##origin_Y", ref y);
                ImGui.SameLine();
                float z = this.originLoc.Z;
                ImGui.InputFloat("Z##origin_Z", ref z);
                this.originLoc = new Vector3(x, y, z);
                ImGui.PopItemWidth();
                ImGui.SameLine();
                if (ImGui.Button("Position##originPosition"))
                    this.originLoc = Player.Position;
                break;
            case DistanceLocationTypes.Player:
            default:
                break;
        }


        ImGuiEx.EnumCombo("target", ref this.target);

        switch (this.target)
        {
            case DistanceLocationTypes.Object:
                ImGui.SameLine();
                ImGuiEx.InputUint("ID##target_baseID", ref this.targetId);
                break;
            case DistanceLocationTypes.Location:
                ImGui.PushItemWidth(50f);
                ImGui.SameLine();
                float x = this.targetLoc.X;
                ImGui.InputFloat("X##target_X", ref x);
                ImGui.SameLine();
                float y = this.targetLoc.Y;
                ImGui.InputFloat("Y##target_Y", ref y);
                ImGui.SameLine();
                float z = this.targetLoc.Z;
                ImGui.InputFloat("Z##target_Z", ref z);
                this.targetLoc = new Vector3(x, y, z);
                ImGui.PopItemWidth();
                ImGui.SameLine();
                if (ImGui.Button("Position##targetPosition"))
                    this.targetLoc = Player.Position;
                break;
            case DistanceLocationTypes.Player:
            default:
                break;
        }

        ImGuiEx.Combo("Operation", ref this.operatorValue, operations.Keys);
        ImGui.SameLine();
        ImGui.InputFloat("Distance", ref this.distance);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), ConditionType.Distance.ToLocalizedString());
    }
}

public class PathActionConditionConditionFlag : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.ConditionFlag;
    public override  ConditionType ParseKey => PARSE_KEY;

    public ConditionFlag flag;

    public override bool IsFulfilled() => 
        Svc.Condition[this.flag];

    public override void DrawConfig()
    {
        Dictionary<ConditionFlag, string> dictionary = Enum.GetValues<ConditionFlag>().Distinct().
                                                            ToDictionary(value => value, value => $"({(int)value}) {value}");
        ImGuiEx.EnumCombo("ConditionFlag", ref this.flag, names: dictionary);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{ConditionType.ConditionFlag.ToLocalizedString()} ");
        yield return (new Vector4(1, 165 / 255f, 0, 1), this.flag.ToString());
    }
}


public class PathActionConditionVariantPath : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.VariantPath;
    public override  ConditionType ParseKey => PARSE_KEY;

    public List<byte> pathIndices = [];

    public override bool IsFulfilled() =>
        this.pathIndices.Contains(Plugin.VariantPath);

    public override void DrawConfig()
    {
        for (int i = 0; i < this.pathIndices.Count; i++)
        {
            byte x = this.pathIndices[i];
            ImGui.InputByte($"###Path{i}", ref x, 1);
            this.pathIndices[i] = x;
        }
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            this.pathIndices.Add((byte)(this.pathIndices.Count == 0 ? 1 : this.pathIndices.Last() + 1));
        ImGui.SameLine();

        using ImRaii.DisabledDisposable _ = ImRaii.Disabled(this.pathIndices.Count == 0);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Minus))
            this.pathIndices.RemoveAt(this.pathIndices.Count - 1);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{ConditionType.VariantPath.ToLocalizedString()} ");
        yield return (new Vector4(1, 165 / 255f, 0, 1), string.Join(", ", this.pathIndices));
    }
}

public class PathActionConditionToDo : PathActionCondition
{
    public new const ConditionType PARSE_KEY = ConditionType.ToDo;
    public override  ConditionType ParseKey => PARSE_KEY;

    public byte index        = 0;
    public int  count = 0;

    public string operatorValue = operations.Keys.First();

    public override unsafe bool IsFulfilled()
    {
        ContentDirector* cd = EventFramework.Instance()->GetContentDirector();
        if (cd != null)
        {
            StdVector<DirectorTodo>* todo = cd->GetDirectorTodos();
            if (todo != null)
                if (this.index < todo->Count)
                {
                    DirectorTodo item = (*todo)[this.index];
                    
                    return operations.TryGetValue(this.operatorValue, out Func<object, object, bool>? operationFunc) &&
                           operationFunc(item.CurrentCount, this.count);
                }
        }

        return false;
    }

    public override unsafe void DrawConfig()
    {
        if(ImGui.BeginCombo("Objective", this.index.ToString()))
        {
            ContentDirector* cd = EventFramework.Instance()->GetContentDirector();
            if (cd != null)
            {
                StdVector<DirectorTodo>* todo = cd->GetDirectorTodos();
                if (todo != null)
                    for (int i = 0; i < todo->Count; i++)
                    {
                        DirectorTodo item = (*todo)[i];
                        if(ImGui.Selectable($"{i}: {item.Text} - {item.CurrentCount}/{item.NeededCount}"))
                            this.index = (byte)i;
                    }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGuiEx.Combo("Operation", ref this.operatorValue, operations.Keys);
        ImGui.SameLine();
        ImGui.InputInt("Count", ref this.count);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        yield return (new Vector4(1, 165 / 255f, 0, 1), $"{ConditionType.ToDo.ToLocalizedString()} ");
    }
}

public abstract class PathActionConditionLogicCollection : PathActionCondition
{
    public List<PathActionCondition> conditions = [];

    public override void DrawConfig()
    {
        for (int index = 0; index < this.conditions.Count; index++)
        {
            PathActionCondition condition = this.conditions[index];

            using ImRaii.IdDisposable _ = ImRaii.PushId($"BuildTab_Condition_Logic_{index}");
            ImGui.Text(condition.ParseKey.ToLocalizedString());
            ImGui.SameLine();

            float indent = ImGui.GetCursorPosX();

            ImGui.Indent(indent);
            condition.DrawConfig();
            ImGui.Separator();
            ImGui.Unindent(indent);
        }

        PathActionCondition? pathActionCondition = PathActionCondition.ConditionSelection();
        if (pathActionCondition != null)
            this.conditions.Add(pathActionCondition);

        ImGui.SameLine();
        using ImRaii.DisabledDisposable __ = ImRaii.Disabled(this.conditions.Count == 0);
        if (ImGuiComponents.IconButton("ConditionLogic_Del", FontAwesomeIcon.Minus))
            this.conditions.RemoveAt(this.conditions.Count - 1);
    }

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        foreach (PathActionCondition condition in this.conditions)
            foreach ((Vector4 color, string text) step in condition.DrawStepEntry())
                yield return step;
    }
}

public class PathActionConditionOr : PathActionConditionLogicCollection
{
    public new const ConditionType PARSE_KEY = ConditionType.Or;
    public override  ConditionType ParseKey => PARSE_KEY;

    public override bool IsFulfilled() =>
        this.conditions.Count > 0 && this.conditions.Any(x => x.IsFulfilled());
}

public class PathActionConditionAnd : PathActionConditionLogicCollection
{
    public new const ConditionType PARSE_KEY = ConditionType.And;
    public override  ConditionType ParseKey => PARSE_KEY;

    public override bool IsFulfilled() =>
        this.conditions.Count > 0 && this.conditions.All(x => x.IsFulfilled());

    public override IEnumerable<(Vector4 color, string text)> DrawStepEntry()
    {
        //yield return (new Vector4(1, 165 / 255f, 0, 1), ConditionType.And.ToLocalizedString());
        foreach (PathActionCondition condition in this.conditions)
            foreach ((Vector4 color, string text) step in condition.DrawStepEntry())
                yield return step;
    }
}