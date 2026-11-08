namespace AutoDuty.Configurations;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.IPC.Subscribers.AutoRetainer;
using ECommons.IPC.Subscribers.Lifestream;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Helpers;
using IPC;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using static Helpers.RepairNPCHelper;
using static Windows.ConfigTab;

[Flags]
public enum LoopActionCategory
{
    All         = 0,
    Global      = 1 << 0,
    Pre         = 1 << 1,
    Termination = 1 << 2
}

public abstract class LoopActionConfig
{
    public static readonly Dictionary<LoopActionCategory, List<Tuple<Type, string>>> actionCategories = [];

    static LoopActionConfig()
    {
        Type     baseType = typeof(LoopActionConfig<>);
        Assembly assembly = typeof(LoopActionConfig<>).Assembly;

        foreach (Type type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract || type == typeof(LoopActionConfigBare))
                continue;

            Type? current = type;

            while (current != null)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == baseType)
                    break;

                if (current.BaseType is { IsGenericType: true } && current.BaseType.GetGenericTypeDefinition() == baseType) 
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(current.TypeHandle);

                    string             name     = type.GetProperty(nameof(LoopActionConfig<>.Name), BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null) as string ?? string.Empty;
                    LoopActionCategory category = (LoopActionCategory)(type.GetProperty(nameof(LoopActionConfig<>.LoopActionCategory), BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null) ?? LoopActionCategory.Global);

                    Tuple<Type, string> item = new(type, name);

                    foreach (LoopActionCategory value in Enum.GetValues<LoopActionCategory>())
                        if (category.HasFlag(value))
                        {
                            if (!actionCategories.ContainsKey(value))
                                actionCategories[value] = [];

                            actionCategories[value].Add(item);
                        }

                    break;
                }
                current = current.BaseType;
            }
        }
    }

    public abstract bool Locked { get; }
    public abstract void Run(ref bool queue);
    public abstract void OnGUI(bool   openWhenEnabled = true);
    public abstract bool OnGUIButton();
}

[JsonObject(MemberSerialization.OptIn)]
public abstract class LoopActionConfig<C> : LoopActionConfig where C : LoopActionConfig<C>, new()
{
    public static LoopActionCategory LoopActionCategory { get; set; } = LoopActionCategory.Global;
    public static string             Name               { get; set; } = null!;

    public abstract string         OverlayName     { get; }
    public virtual  ExternalPlugin RequiredPlugins => ExternalPlugin.None;
    public virtual  string         ActionText      => "Executing: " + Name;
    public override bool           Locked          => false;
    public virtual  string?        HelpText        => null;

    public override void Run(ref bool queue)
    {
        if(this.Enabled)
        {
            Plugin.action = this.ActionText;
            this.RunInternal(ref queue);
        }
    }

    protected abstract void RunInternal(ref bool queue);

    [JsonProperty] public bool Enabled { get; set; } = true;
    [JsonProperty] public bool EnabledOnlyWhenQueuing { get; set; }

    protected virtual bool HasConfig => true;
    private           bool configOpen;

    private bool init;

    public override void OnGUI(bool openWhenEnabled = true)
    {
        if (!this.init)
        {
            this.init       = true;
            this.configOpen = openWhenEnabled && this.Enabled && this.HasConfig;
        }


        bool enabled = this.Enabled;

        ImGuiHelper.EndUnconditionally __ = ImGuiHelper.RequiresPlugin(this.RequiredPlugins, "Plugins_Required", inline: true);

        using ImRaii.DisabledDisposable _ = ImRaii.Disabled(this.Locked);

        if(ImGui.Checkbox("##Enabled", ref enabled))
        {
            this.Enabled = enabled;
            ConfigurationProfileV2.Save();
        }

        ImGui.SameLine();

        if (this.HasConfig)
        {
            ImGui.SetItemAllowOverlap();
            ImGui.Selectable($"##LoopAction{Name}", ref this.configOpen);
            ImGui.SameLine(0, 0);
            ImGuiHelper.DrawIcon(this.configOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight);
            ImGui.SameLine(0, 0);
            ImGui.AlignTextToFramePadding();
        }

        ImGui.Text(Name);
        __.Dispose();
        if(this.HelpText != null)
            ImGuiHelper.HelpMarker(this.HelpText);

        using ImRaii.DisabledDisposable ___ = ImRaii.Disabled(!__.Success);

        if (this.configOpen)
        {
            ImGui.Separator();
            ImGui.Indent();
            this.OnGuiSettings();
            ImGui.Unindent();
            ImGui.Separator();
        }
    }

    public override bool OnGUIButton()
    {
        if (!this.Enabled)
            return false;

        ImGui.SameLine(0, 5);

        if (ImGui.GetWindowSize().X > 200 ? ImGuiEx.ButtonWrapped(this.OverlayName) : ImGui.Button(this.OverlayName))
        {
            bool queue = true;
            this.Run(ref queue);
        }

        if(this.HelpText != null && ImGui.IsItemHovered())
            using (ImRaii.Tooltip())
            {
                using (ImRaii.TextWrapPos(ImGui.GetFontSize() * 35.0f))
                    ImGui.Text(this.HelpText);
            }

        return true;
    }

    public abstract void OnGuiSettings();
}

public class LoopActionConfigBare : LoopActionConfig<LoopActionConfigBare>
{
    static LoopActionConfigBare() => 
        Name = string.Empty;

    public override    string OverlayName                 { get; } = null!;
    protected override void   RunInternal(ref bool queue) => throw new NotImplementedException();
    public override    void   OnGuiSettings()             => throw new NotImplementedException();
}

public abstract class ActiveLoopActionConfig<T,C> : LoopActionConfig<C> where T : ActiveHelperBase<T,C>, new() 
                                                                        where C : ActiveLoopActionConfig<T,C>, new()
{
    public ActiveHelperBase<T,C> Helper => ActiveHelper.GetHelper<T,C>();

    static ActiveLoopActionConfig() => 
        Name = ActiveHelper.GetHelper<T,C>().DisplayName;

    public virtual bool ShouldRun() => true;

    protected override void RunInternal(ref bool queue)
    {
        if (this.ShouldRun())
            ActiveHelper.EnqueueActiveHelper<T,C>(this);
    }
}

#region Active Helpers
public class AutoEquipLoopActionConfig : ActiveLoopActionConfig<AutoEquipHelper, AutoEquipLoopActionConfig>
{
    static AutoEquipLoopActionConfig() =>
        LoopActionCategory = LoopActionCategory.Pre;

    public override string OverlayName => Loc.Get("Overlay.Button.Equip");

    [JsonProperty] public GearsetUpdateSource RecommendedGearSource { get; set; } = GearsetUpdateSource.Vanilla;

    [JsonProperty] public bool GearsetterOldToInventory { get; set; }

    public override void OnGuiSettings()
    {
        
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 * 2);
        if (ImGui.BeginCombo("##AutoEquipRecommendedSource", this.RecommendedGearSource.ToCustomString()))
        {
            foreach (GearsetUpdateSource updateSource in Enum.GetValues(typeof(GearsetUpdateSource)))
                using (ImGuiHelper.RequiresPlugin(updateSource switch
                       {
                           GearsetUpdateSource.Gearsetter => ExternalPlugin.Gearsetter,
                           GearsetUpdateSource.Stylist => ExternalPlugin.Stylist,
                           _ => ExternalPlugin.None
                       }, "GearSet", inline: true))
                {
                    if (ImGui.Selectable(updateSource.ToCustomString(), this.RecommendedGearSource == updateSource, flags: ImGuiSelectableFlags.AllowItemOverlap))
                    {
                        this.RecommendedGearSource = updateSource;
                        ConfigurationProfileV2.Save();
                    }
                }

            ImGui.EndCombo();
        }

        ImGuiComponents.HelpMarker(Loc.Get("LoopActions.AutoEquip.GearSourceHelp"));

        ImGui.Indent();
        if (this.RecommendedGearSource == GearsetUpdateSource.Gearsetter)
            using (ImRaii.Disabled(!Gearsetter_IPCSubscriber.IsEnabled))
            {
                ImGui.Indent();
                bool oldToInventory = this.GearsetterOldToInventory;
                if (ImGui.Checkbox(Loc.Get("LoopActions.AutoEquip.MoveOldToInventory"), ref oldToInventory))
                {
                    this.GearsetterOldToInventory = oldToInventory;
                    ConfigurationProfileV2.Save();
                }

                ImGuiComponents.HelpMarker(Loc.Get("LoopActions.AutoEquip.MoveOldToInventoryHelp"));
                ImGui.Unindent();
            }

        if (!Gearsetter_IPCSubscriber.IsEnabled && !Stylist_IPCSubscriber.IsEnabled)
            ImGui.Text(Loc.Get("LoopActions.AutoEquip.RequiresGearsetterOrStylist"));

        if (this.RecommendedGearSource == GearsetUpdateSource.Gearsetter && !Gearsetter_IPCSubscriber.IsEnabled || this.RecommendedGearSource == GearsetUpdateSource.Stylist && !Stylist_IPCSubscriber.IsEnabled)
        {
            this.RecommendedGearSource = GearsetUpdateSource.Vanilla;
            ConfigurationProfileV2.Save();
        }

        ImGui.Unindent();
    }
}

public class RepairLoopActionConfig : ActiveLoopActionConfig<RepairHelper, RepairLoopActionConfig>
{
    static RepairLoopActionConfig() =>
        LoopActionCategory = LoopActionCategory.Pre;

    public override string OverlayName => Loc.Get("Overlay.Button.Repair");

    [JsonProperty] public uint           AutoRepairPct      { get; set; } = 50;
    [JsonProperty] public bool           AutoRepairSelf     { get; set; }
    [JsonProperty] public RepairNpcData? PreferredRepairNPC { get; set; }

    private static string preferredNPCSearchInput = "";

    public override bool ShouldRun() => base.ShouldRun() && InventoryHelper.CanRepair(this.AutoRepairPct);

    public override void OnGuiSettings()
    {
        if (ImGui.RadioButton(Loc.Get("LoopActions.Repair.Self"), this.AutoRepairSelf))
        {
            this.AutoRepairSelf = true;
            ConfigurationProfileV2.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.Get("LoopActions.Repair.SelfHelp"));
        ImGui.SameLine();

        if (ImGui.RadioButton(Loc.Get("LoopActions.Repair.CityNpc"), !this.AutoRepairSelf))
        {
            this.AutoRepairSelf = false;
            ConfigurationProfileV2.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.Get("LoopActions.Repair.CityNpcHelp"));
        ImGui.Indent();
        ImGui.Text(Loc.Get("LoopActions.Repair.TriggerAt"));
        ImGui.SameLine();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        int autoRepairPct = (int)this.AutoRepairPct;
        if (ImGui.SliderInt("##Repair@", ref autoRepairPct, 0, 99, "%d%%"))
        {
            this.AutoRepairPct = Math.Clamp((uint)autoRepairPct, 0, 99);
            ConfigurationProfileV2.Save();
        }

        ImGui.PopItemWidth();
        if (!this.AutoRepairSelf)
        {
            ImGui.Text(Loc.Get("LoopActions.Repair.PreferredRepairNPC"));
            ImGuiComponents.HelpMarker(Loc.Get("LoopActions.Repair.PreferredRepairNPCHelp"));
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##PreferredRepair", this.PreferredRepairNPC != null ?
                                     $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(this.PreferredRepairNPC.Name.ToLowerInvariant())} ({Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(this.PreferredRepairNPC.TerritoryType)?.PlaceName.ValueNullable?.Name.ToString()})  ({MapHelper.ConvertWorldXZToMap(this.PreferredRepairNPC.Position.ToVector2(), Svc.Data.GetExcelSheet<TerritoryType>().GetRow(this.PreferredRepairNPC.TerritoryType).Map.Value!).X.ToString("0.0", CultureInfo.InvariantCulture)}, {MapHelper.ConvertWorldXZToMap(this.PreferredRepairNPC.Position.ToVector2(), Svc.Data.GetExcelSheet<TerritoryType>().GetRow(this.PreferredRepairNPC.TerritoryType).Map.Value).Y.ToString("0.0", CultureInfo.InvariantCulture)})" :
                                     Loc.Get("LoopActions.Repair.GrandCompanyInn")))
            {
                if (ImGui.Selectable(Loc.Get("LoopActions.Repair.GrandCompanyInn"), this.PreferredRepairNPC == null))
                {
                    this.PreferredRepairNPC = null;
                    ConfigurationProfileV2.Save();
                }

                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputTextWithHint("##PreferredRepairSearch", Loc.Get("LoopActions.Repair.NPCSearchHint"), ref preferredNPCSearchInput, 100);

                foreach (RepairNpcData repairNPC in RepairNPCs.Where(x =>
                                                                         string.IsNullOrEmpty(preferredNPCSearchInput)                                         ||
                                                                         x.Name.Contains(preferredNPCSearchInput, StringComparison.InvariantCultureIgnoreCase) ||
                                                                         (Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(x.TerritoryType)?.PlaceName.ValueNullable?.Name.ToString().Contains(preferredNPCSearchInput, StringComparison.InvariantCultureIgnoreCase) ?? false)))
                {
                    if (repairNPC.TerritoryType <= 0)
                    {
                        ImGui.Text(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(repairNPC.Name.ToLowerInvariant()));
                        continue;
                    }

                    TerritoryType? territoryType = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(repairNPC.TerritoryType);

                    if (territoryType == null) continue;

                    if (ImGui.Selectable($"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(repairNPC.Name.ToLowerInvariant())} ({territoryType.Value.PlaceName.ValueNullable?.Name.ToString()})  ({MapHelper.ConvertWorldXZToMap(repairNPC.Position.ToVector2(), territoryType.Value.Map.Value!).X.ToString("0.0", CultureInfo.InvariantCulture)}, {MapHelper.ConvertWorldXZToMap(repairNPC.Position.ToVector2(), territoryType.Value.Map.Value!).Y.ToString("0.0", CultureInfo.InvariantCulture)})", this.PreferredRepairNPC == repairNPC))
                    {
                        this.PreferredRepairNPC = repairNPC;
                        ConfigurationProfileV2.Save();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.PopItemWidth();
        }
        ImGui.Unindent();
    }
}

public class AutoRetainerLoopActionConfig : ActiveLoopActionConfig<AutoRetainerHelper, AutoRetainerLoopActionConfig>
{
    public override ExternalPlugin RequiredPlugins => ExternalPlugin.AutoRetainer;
    public override string OverlayName => Loc.Get("Overlay.Button.AutoRetainer");

    [JsonProperty] public SummoningBellLocations PreferredSummoningBellEnum { get; set; } = SummoningBellLocations.Inn;
    [JsonProperty] public long                   AutoRetainerRemainingTime  { get; set; }

    public override void OnGuiSettings()
    {
        ImGui.Text(Loc.Get("LoopActions.AutoRetainer.PreferredSummoningBell"));
        ImGuiComponents.HelpMarker(Loc.Get("LoopActions.AutoRetainer.PreferredSummoningBellHelp"));
        if (ImGui.BeginCombo("##PreferredBell", this.PreferredSummoningBellEnum.ToLocalizedString()))
        {
            foreach (SummoningBellLocations summoningBells in Enum.GetValues(typeof(SummoningBellLocations)))
                if (ImGui.Selectable(summoningBells.ToLocalizedString()))
                {
                    this.PreferredSummoningBellEnum = summoningBells;
                    ConfigurationProfileV2.Save();
                }

            ImGui.EndCombo();
        }

        ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
        ImGui.AlignTextToFramePadding();
        ImGui.Text(Loc.Get("LoopActions.AutoRetainer.WaitingUpTo"));
        ImGui.SameLine();

        long retainerRemainingTime = this.AutoRetainerRemainingTime;
        if (MakeSliderOrInputLong(ref retainerRemainingTime, "AutoRetainerTimeWaiting", 0L, 300L, 1L, 100L))
        {
            this.AutoRetainerRemainingTime = Math.Max(retainerRemainingTime, 0L);
            ConfigurationProfileV2.Save();
        }
        ImGui.SameLine();
        ImGui.Text(Loc.Get("LoopActions.AutoRetainer.Seconds"));
        ImGui.PopItemWidth();
    }
}

public class AutoRetainerMultiModeLoopActionConfig : ActiveLoopActionConfig<AutoRetainerMultiModeHelper, AutoRetainerMultiModeLoopActionConfig>
{
    public override ExternalPlugin RequiredPlugins => ExternalPlugin.AutoRetainer | ExternalPlugin.Lifestream;
    public override       string        OverlayName   => Loc.Get("Overlay.Button.AutoRetainerMulti");

    [JsonProperty] public MultiModeType MultiModeType { get; set; } = MultiModeType.Everything;

    public override void OnGuiSettings()
    {
        if (ImGui.BeginCombo("##MultiModeType", this.MultiModeType.ToLocalizedString()))
        {
            foreach (MultiModeType multiModeType in Enum.GetValues<MultiModeType>())
                if (ImGui.Selectable(multiModeType.ToLocalizedString()))
                {
                    this.MultiModeType = multiModeType;
                    ConfigurationProfileV2.Save();
                }

            ImGui.EndCombo();
        }
    }
}

public class DiscardItemsLoopActionConfig : ActiveLoopActionConfig<DiscardHelper, DiscardItemsLoopActionConfig>
{
    protected override bool           HasConfig       => false;
    public override    ExternalPlugin RequiredPlugins => ExternalPlugin.AutoRetainer;
    public override string OverlayName => Loc.Get("Overlay.Button.Discard");

    public override void OnGuiSettings() => 
        throw new NotImplementedException();
}

public class TripleTriadUseLoopActionConfig : ActiveLoopActionConfig<TripleTriadCardUseHelper, TripleTriadUseLoopActionConfig>
{
    protected override bool   HasConfig   => false;
    public override    string OverlayName => Loc.Get("Overlay.Button.TripleTriadUse");

    public override void OnGuiSettings() => 
        throw new NotImplementedException();
}

public class TripleTriadSellLoopActionConfig : ActiveLoopActionConfig<TripleTriadCardSellHelper, TripleTriadSellLoopActionConfig>
{
    public override string OverlayName => Loc.Get("Overlay.Button.TripleTriadSell");

    [JsonProperty] public int TripleTriadSellMinItemCount { get; set; } = 1;
    [JsonProperty] public int TripleTriadSellMinSlotCount { get; set; } = 1;

    public override bool ShouldRun() =>
        base.ShouldRun() && TripleTriadCardSellHelper.CardsInInventory(this);

    public override void OnGuiSettings()
    {
        ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);

        ImGui.Text(Loc.Get("LoopActions.TripleTriadSell.SlotsOccupied"));
        ImGui.SameLine();
        float curX = ImGui.GetCursorPosX();

        int minSlotCount = this.TripleTriadSellMinSlotCount;
        if (MakeSliderOrInput(ref minSlotCount, "TripleTriadSellingMinSlot", 1, 5))
        {
            this.TripleTriadSellMinSlotCount = Math.Max(minSlotCount, 1);
            ConfigurationProfileV2.Save();
        }

        ImGui.Text(Loc.Get("LoopActions.TripleTriadSell.CardCount"));
        ImGui.SameLine();
        ImGui.SetCursorPosX(curX);

        int tripleTriadSellMinItemCount = this.TripleTriadSellMinItemCount;
        if (MakeSliderOrInput(ref tripleTriadSellMinItemCount, "TripleTriadSellingMinItem", 1, 99, inputStepFast: 10))
        {
            this.TripleTriadSellMinItemCount = Math.Max(tripleTriadSellMinItemCount, 1);
            ConfigurationProfileV2.Save();
        }
        ImGui.PopItemWidth();
    }
}

public class ArmoireLoopActionConfig : ActiveLoopActionConfig<ArmoireHelper, ArmoireLoopActionConfig>
{
    public override ExternalPlugin RequiredPlugins => ExternalPlugin.GlamourLog;
    public override string?        HelpText        => Loc.Get("LoopActions.Armoire.Help");
    public override string         OverlayName     => Loc.Get("Overlay.Button.Armoire");

    protected override bool HasConfig => false;

    public override void OnGuiSettings() =>
        throw new NotImplementedException();
}

public class GlamourLoopActionConfig : ActiveLoopActionConfig<GlamourChestHelper, GlamourLoopActionConfig>
{
    public override ExternalPlugin RequiredPlugins => ExternalPlugin.GlamourLog;
    public override string?        HelpText        => Loc.Get("LoopActions.Glamour.Help");
    public override string         OverlayName     => Loc.Get("Overlay.Button.Glamour");

    protected override bool HasConfig => false;

    public override void OnGuiSettings() => 
        throw new NotImplementedException();
}

public class ExtractLoopActionConfig : ActiveLoopActionConfig<ExtractHelper, ExtractLoopActionConfig>
{
    public override string OverlayName => Loc.Get("Overlay.Button.Extract");

    [JsonProperty] public bool AutoExtractAll { get; set; }

    public override void OnGuiSettings()
    {
        ImGui.SameLine(0, 10);
        if (ImGui.RadioButton(Loc.Get("LoopActions.Extract.Equipped"), !this.AutoExtractAll))
        {
            this.AutoExtractAll = false;
            ConfigurationProfileV2.Save();
        }
        ImGui.SameLine(0, 5);
        if (ImGui.RadioButton(Loc.Get("LoopActions.Extract.All"), this.AutoExtractAll))
        {
            this.AutoExtractAll = true;
            ConfigurationProfileV2.Save();
        }
    }
}

public class GCTurnInLoopActionConfig : ActiveLoopActionConfig<GCTurninHelper, GCTurnInLoopActionConfig>
{
    public override ExternalPlugin RequiredPlugins => ExternalPlugin.AutoRetainer;
    public override string         OverlayName     => Loc.Get("Overlay.Button.TurnIn");

    [JsonProperty] public bool SlotsLeftBool { get; set; }
    [JsonProperty] public int  SlotsLeft     { get; set; } = 5;
    [JsonProperty] public bool UseTicket     { get; set; }

    public override unsafe bool ShouldRun() => base.ShouldRun() && (!this.SlotsLeftBool || InventoryManager.Instance()->GetEmptySlotsInBag() <= this.SlotsLeft) && PlayerHelper.GetGrandCompanyRank() > 5;

    public override void OnGuiSettings()
    {
        bool gcTurninSlotsLeftBool = this.SlotsLeftBool;
        if (ImGui.Checkbox(Loc.Get("LoopActions.GCTurnIn.InventorySlotsLeft"), ref gcTurninSlotsLeftBool))
        {
            this.SlotsLeftBool = gcTurninSlotsLeftBool;
            ConfigurationProfileV2.Save();
        }

        ImGui.SameLine(0);
        using (ImRaii.Disabled(!this.SlotsLeftBool))
        {
            int gcTurninSlotsLeft = this.SlotsLeft;
            
            if (MakeSliderOrInput(ref gcTurninSlotsLeft, "##Slots", 0, 140))
            {
                this.SlotsLeft = Math.Clamp(gcTurninSlotsLeft, 0, 140);
                ConfigurationProfileV2.Save();
            }
        }

        bool gcTurninUseTicket = this.UseTicket;
        if (ImGui.Checkbox(Loc.Get("LoopActions.GCTurnIn.UseGCAetheryteTicket"), ref gcTurninUseTicket))
        {
            this.UseTicket = gcTurninUseTicket;
            ConfigurationProfileV2.Save();
        }
    }
}

public class DesynthLoopActionConfig : ActiveLoopActionConfig<DesynthHelper, DesynthLoopActionConfig>
{
    public override string OverlayName => Loc.Get("Overlay.Button.Desynth");

    [JsonProperty] public bool  SkillUp                   { get; set; }
    [JsonProperty] public int   SkillUpLimit              { get; set; } = 50;
    [JsonProperty] public bool  NQOnly                    { get; set; }
    [JsonProperty] public bool  NoGearset                 { get; set; } = true;
    [JsonProperty] public bool  ProtectGearsetterUpgrades { get; set; }
    [JsonProperty] public ulong Categories                { get; set; } = 0x1;
    public override void OnGuiSettings()
    {
        bool desynthSkillUp = this.SkillUp;
        if (ImGui.Checkbox(Loc.Get("LoopActions.Desynth.OnlySkillUps"), ref desynthSkillUp))
        {
            this.SkillUp = desynthSkillUp;
            ConfigurationProfileV2.Save();
        }

        if (this.SkillUp)
        {
            ImGui.Indent();
            ImGui.Text(Loc.Get("LoopActions.Desynth.ItemLevelLimit"));
            ImGuiComponents.HelpMarker(Loc.Get("LoopActions.Desynth.ItemLevelLimitHelp"));
            ImGui.SameLine();
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);

            int autoDesynthSkillUp = this.SkillUpLimit;
            if (ImGui.SliderInt("##AutoDesynthSkillUpLimit", ref autoDesynthSkillUp, 0, 50))
            {
                this.SkillUpLimit = Math.Clamp(autoDesynthSkillUp, 0, 50);
                ConfigurationProfileV2.Save();
            }
            ImGui.PopItemWidth();
            ImGui.Unindent();
        }

        bool desynthNQOnly = this.NQOnly;
        if (ImGui.Checkbox($"{Loc.Get("LoopActions.Desynth.NQOnly")}##Desynth{nameof(this.NQOnly)}", ref desynthNQOnly))
        {
            this.NQOnly = desynthNQOnly;
            ConfigurationProfileV2.Save();
        }

        bool desynthNoGearset = this.NoGearset;
        if (ImGui.Checkbox($"{Loc.Get("LoopActions.Desynth.ProtectGearsets")}##Desynth{nameof(this.NoGearset)}", ref desynthNoGearset))
        {
            this.NoGearset = desynthNoGearset;
            ConfigurationProfileV2.Save();
        }

        if (desynthNoGearset)
        {
            ImGui.Indent();
            using (ImGuiHelper.RequiresPlugin(ExternalPlugin.Gearsetter, "DesynthGearsetter", inline: true))
            {
                bool gearsetterUpgrades = this.ProtectGearsetterUpgrades;
                if (ImGui.Checkbox($"{Loc.Get("LoopActions.Desynth.ProtectGearsetterUpgrades")}##Desynth{nameof(this.ProtectGearsetterUpgrades)}", ref gearsetterUpgrades))
                {
                    this.ProtectGearsetterUpgrades = gearsetterUpgrades;
                    ConfigurationProfileV2.Save();
                }
            }

            ImGuiComponents.HelpMarker(Loc.Get("LoopActions.Desynth.ProtectGearsetterUpgradesHelp"));
            ImGui.Unindent();
        }


        if (ImGui.CollapsingHeader(Loc.Get("LoopActions.Desynth.Categories")))
        {
            ImGui.Indent();
            AgentSalvage.SalvageItemCategory[] values = Enum.GetValues<AgentSalvage.SalvageItemCategory>();
            for (int index = 0; index < values.Length; index++)
            {
                bool   x            = Bitmask.IsBitSet(this.Categories, index);
                string categoryName = values[index].ToLocalizedString();
                if (ImGui.Checkbox(categoryName + $"##DesynthCategory{index}", ref x))
                {
                    ulong autoDesynthCategories = this.Categories;
                    if (x)
                        Bitmask.SetBit(ref autoDesynthCategories, index);
                    else
                        Bitmask.ResetBit(ref autoDesynthCategories, index);
                    this.Categories = autoDesynthCategories;
                    ConfigurationProfileV2.Save();
                }
            }
            ImGui.Unindent();
        }
    }
}

public class CofferOpenLoopActionConfig : ActiveLoopActionConfig<CofferHelper, CofferOpenLoopActionConfig>
{
    public override string? HelpText    => Loc.Get("LoopActions.Coffers.Help");
    public override string  OverlayName => Loc.Get("Overlay.Button.Coffers");

    [JsonProperty] public byte?                    Gearset      { get; set; }
    [JsonProperty] public bool                     UseBlacklist { get; set; }
    [JsonProperty] public Dictionary<uint, string> Blacklist    { get; set; } = [];


    private static string                     autoOpenCoffersNameInput    = "";
    private static KeyValuePair<uint, string> autoOpenCoffersSelectedItem = new(0, "");

    public override void OnGuiSettings()
    {
        unsafe
        {
            ImGui.Text(Loc.Get("LoopActions.Coffers.OpenCoffersWithGearset"));
            ImGui.AlignTextToFramePadding();
            ImGui.SameLine();

            RaptureGearsetModule* module = RaptureGearsetModule.Instance();

            if (this.Gearset != null && !module->IsValidGearset((int)this.Gearset))
            {
                this.Gearset = null;
                ConfigurationProfileV2.Save();
            }


            if (ImGui.BeginCombo("##CofferGearsetSelection", this.Gearset != null ? module->GetGearset(this.Gearset.Value)->NameString : Loc.Get("LoopActions.Coffers.CurrentGearset")))
            {
                if (ImGui.Selectable(Loc.Get("LoopActions.Coffers.CurrentGearset"), this.Gearset == null))
                {
                    this.Gearset = null;
                    ConfigurationProfileV2.Save();
                }

                foreach (RaptureGearsetModule.GearsetEntry gearsetEntry in module->Entries)
                {
                    if (module->IsValidGearset(gearsetEntry.Id) && ImGui.Selectable($"{gearsetEntry.Id + 1}: {gearsetEntry.NameString}", this.Gearset == gearsetEntry.Id))
                    {
                        this.Gearset = gearsetEntry.Id;
                        ConfigurationProfileV2.Save();
                    }
                }

                ImGui.EndCombo();
            }

            bool openCoffersBlacklistUse = this.UseBlacklist;
            if (ImGui.Checkbox(Loc.Get("LoopActions.Coffers.UseBlacklist"), ref openCoffersBlacklistUse))
            {
                this.UseBlacklist = openCoffersBlacklistUse;
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("LoopActions.Coffers.UseBlacklistHelp"));
            if (this.UseBlacklist)
            {
                if (ImGui.BeginCombo(Loc.Get("LoopActions.Coffers.SelectCoffer"), autoOpenCoffersSelectedItem.Value))
                {
                    ImGui.InputTextWithHint(Loc.Get("LoopActions.Coffers.CofferName"), Loc.Get("LoopActions.Coffers.CofferNameHint"), ref autoOpenCoffersNameInput, 1000);
                    foreach (KeyValuePair<uint, Item> item in Items.Where(x => CofferHelper.ValidCoffer(x.Value, this) && x.Value.Name.ToString().Contains(autoOpenCoffersNameInput, StringComparison.InvariantCultureIgnoreCase)))
                        if (ImGui.Selectable($"{item.Value.Name.ToString()}"))
                            autoOpenCoffersSelectedItem = new KeyValuePair<uint, string>(item.Key, item.Value.Name.ToString());
                    ImGui.EndCombo();
                }

                ImGui.SameLine(0, 5);
                using (ImRaii.Disabled(autoOpenCoffersSelectedItem.Value.IsNullOrEmpty()))
                {
                    if (ImGui.Button(Loc.Get("LoopActions.Coffers.AddCoffer")))
                    {
                        if (!this.Blacklist.TryAdd(autoOpenCoffersSelectedItem.Key, autoOpenCoffersSelectedItem.Value))
                        {
                            this.Blacklist.Remove(autoOpenCoffersSelectedItem.Key);
                            this.Blacklist.Add(autoOpenCoffersSelectedItem.Key, autoOpenCoffersSelectedItem.Value);
                        }

                        autoOpenCoffersSelectedItem = new KeyValuePair<uint, string>(0, "");
                        ConfigurationProfileV2.Save();
                    }
                }

                if (!ImGui.BeginListBox("##CofferBlackList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * this.Blacklist.Count) + 5)))
                    return;

                foreach (KeyValuePair<uint, string> item in this.Blacklist)
                {
                    ImGui.Selectable($"{item.Value}");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        this.Blacklist.Remove(item);
                        ConfigurationProfileV2.Save();
                    }
                }

                ImGui.EndListBox();
            }
        }
    }
}


public class ShopLoopActionConfig : ActiveLoopActionConfig<ShopHelper, ShopLoopActionConfig>
{
    public override string? HelpText    => Loc.Get("LoopActions.Shop.Help");
    public override string  OverlayName => Loc.Get("Overlay.Button.Shop");

    public override ExternalPlugin RequiredPlugins { get; } = ExternalPlugin.Knightshopper;

    [JsonProperty] public Knightshopper_IPCSubscriber.CurrencyId Currency { get; set; } = Knightshopper_IPCSubscriber.CurrencyId.Tomestone;

    public override void OnGuiSettings()
    {
        Knightshopper_IPCSubscriber.CurrencyId currency = this.Currency;
        if(ImGuiEx.EnumCombo(Loc.Get("LoopActions.Shop.CurrencySelection"), ref currency, Enum.GetValues<Knightshopper_IPCSubscriber.CurrencyId>().ToDictionary(ci => ci, ci => ci.ToCustomString())))
        {
            this.Currency = currency;
            ConfigurationProfileV2.Save();
        }
    }
}

#endregion

#region Custom
public class RetireLoopActionConfig : LoopActionConfig<RetireLoopActionConfig>
{
    static RetireLoopActionConfig()
    {
        Name = "Retire";
        LoopActionCategory = LoopActionCategory.Pre;
    }

    public override string OverlayName => Loc.Get("Overlay.Button.Retire");

    [JsonProperty] public RetireLocation RetireLocationEnum { get; set; } = RetireLocation.Inn;
    [JsonProperty] public bool AdjustForDutyModes { get; set; } = true;

    protected override void RunInternal(ref bool queue)
    {
        if (this.AdjustForDutyModes && Configuration.Meta.DutyModeEnum is DutyMode.Crucible)
            return;

        Plugin.taskManager.Enqueue(() => Svc.Log.Debug($"Retire Between Loop Action"));

        switch (this.RetireLocationEnum)
        {
            case { } when this.AdjustForDutyModes && Configuration.Meta.DutyModeEnum is DutyMode.Squadron:
            case RetireLocation.GC_Barracks:
                Plugin.taskManager.Enqueue(() => GotoBarracksHelper.Invoke(), "Loop-GotoBarracksInvoke");
                break;
            case RetireLocation.Inn:
                Plugin.taskManager.Enqueue(() => GotoInnHelper.Invoke(), "Loop-GotoInnInvoke");
                break;
            case RetireLocation.Lifestream_Auto:
                Plugin.taskManager.Enqueue(() => Lifestream_IPCSubscriber.Teleport(PropertyType.Auto), "Loop-LifestreamAutoTeleport");
                break;
            case RetireLocation.Apartment:
            case RetireLocation.Personal_Home:
            case RetireLocation.FC_Estate:
            default:
                Svc.Log.Info($"{(Housing)this.RetireLocationEnum} {this.RetireLocationEnum}");
                Plugin.taskManager.Enqueue(() => GotoHousingHelper.Invoke((Housing)this.RetireLocationEnum), "Loop-GotoHousingInvoke");
                break;
        }

        Plugin.taskManager.EnqueueDelay(50);
        Plugin.taskManager.Enqueue(() => GotoHousingHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running && !Lifestream_IPCSubscriber.IsBusy,
                                   "Loop-WaitGotoComplete", new TaskManagerConfiguration(int.MaxValue));
    }

    public override void OnGuiSettings()
    {
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##RetireLocation", this.RetireLocationEnum.ToLocalizedString()))
        {
            foreach (RetireLocation retireLocation in Enum.GetValues(typeof(RetireLocation)))
            {
                bool disabled = retireLocation == RetireLocation.FC_Estate && (!Lifestream_IPCSubscriber.IsEnabled || !Lifestream_IPCSubscriber.IsEstateRegistered(true)) ||
                                retireLocation == RetireLocation.Personal_Home && (!Lifestream_IPCSubscriber.IsEnabled || !Lifestream_IPCSubscriber.IsEstateRegistered(false));

                using ImRaii.DisabledDisposable _ = ImRaii.Disabled(disabled);

                ImGuiHelper.EndUnconditionally? x = retireLocation is RetireLocation.FC_Estate or RetireLocation.Personal_Home or RetireLocation.Lifestream_Auto ? ImGuiHelper.RequiresPlugin(ExternalPlugin.Lifestream, "LoopActionRetireEnum", inline: true) : null;

                if (ImGui.Selectable(retireLocation.ToLocalizedString() + (disabled ? " | " + Loc.Get("LoopActions.Retire.LifestreamNotRegistered") : string.Empty), this.RetireLocationEnum == retireLocation))
                {
                    this.RetireLocationEnum = retireLocation;
                    ConfigurationProfileV2.Save();
                }

                x?.Dispose();
            }

            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();

        bool adjustForDutyModes = this.AdjustForDutyModes;
        if (ImGui.Checkbox(Loc.Get("LoopActions.Retire.AdjustForDutyModes"), ref adjustForDutyModes))
        {
            this.AdjustForDutyModes = adjustForDutyModes;
            ConfigurationProfileV2.Save();
        }
        ImGuiComponents.HelpMarker(Loc.Get("LoopActions.Retire.AdjustForDutyModesHelp"));
    }
}

public class ConsumeItemsLoopActionConfig : LoopActionConfig<ConsumeItemsLoopActionConfig>
{
    static ConsumeItemsLoopActionConfig()
    {
        Name = "Consume Items";
        LoopActionCategory = LoopActionCategory.Pre;
    }

    public override string? HelpText    => Loc.Get("LoopActions.ConsumeItems.AutoConsumeHelp");
    public override string  OverlayName => Loc.Get("Overlay.Button.ConsumeItems");

    [JsonProperty] public bool AutoConsumeIgnoreStatus { get; set; } = false;
    [JsonProperty] public int AutoConsumeTime { get; set; } = 29;
    [JsonProperty] public List<KeyValuePair<ushort, ConsumableItem>> AutoConsumeItemsList { get; set; } = [];

    [JsonObject(MemberSerialization.OptOut)]
    public class ConsumableItem
    {
        public uint ItemId;
        public string Name = string.Empty;
        public bool CanBeHq;
        public ushort StatusId;
    }

    private static List<ConsumableItem> ConsumableItems { get; } =
    [
        ..Svc.Data.GetExcelSheet<Item>()
             .Where(x => !x.Name.ToString().IsNullOrEmpty() && x.ItemUICategory.ValueNullable?.RowId is 44 or 45 or 46 && x.ItemAction.ValueNullable?.Data[0] is 48 or 49)
             .Select(x => new ConsumableItem
                          {
                              StatusId = x.ItemAction.Value!.Data[0],
                              ItemId   = x.RowId,
                              Name     = x.Name.ToString(),
                              CanBeHq  = x.CanBeHq
                          }),
        new() { StatusId = 1086, ItemId = 14945, Name = "Squadron Enlistment Manual", CanBeHq       = false },
        new() { StatusId = 1080, ItemId = 14948, Name = "Squadron Battle Manual", CanBeHq           = false },
        new() { StatusId = 1081, ItemId = 14949, Name = "Squadron Survival Manual", CanBeHq         = false },
        new() { StatusId = 1082, ItemId = 14950, Name = "Squadron Engineering Manual", CanBeHq      = false },
        new() { StatusId = 1083, ItemId = 14951, Name = "Squadron Spiritbonding Manual", CanBeHq    = false },
        new() { StatusId = 1084, ItemId = 14952, Name = "Squadron Rationing Manual", CanBeHq        = false },
        new() { StatusId = 1085, ItemId = 14953, Name = "Squadron Gear Maintenance Manual", CanBeHq = false }
    ];

    private static string consumableItemsItemNameInput = "";
    private static ConsumableItem consumableItemsSelectedItem = new();

    public ConsumeItemsLoopActionConfig() => this.EnabledOnlyWhenQueuing = true;

    protected override void RunInternal(ref bool queue)
    {
        Plugin.taskManager.Enqueue(() => Svc.Log.Debug($"AutoConsume Action"));
        this.AutoConsumeItemsList.Each(x =>
        {
            bool isAvailable = InventoryHelper.IsItemAvailable(x.Value.ItemId, x.Value.CanBeHq);
            if (isAvailable)
            {
                if (this.AutoConsumeIgnoreStatus)
                    Plugin.taskManager.Enqueue(() => InventoryHelper.UseItemUntilAnimationLock(x.Value.ItemId, x.Value.CanBeHq), $"AutoConsume - {x.Value.Name} is available: {isAvailable}");
                else
                    Plugin.taskManager.Enqueue(() => InventoryHelper.UseItemUntilStatus(x.Value.ItemId, x.Key, this.AutoConsumeTime * 60, x.Value.CanBeHq),
                                               $"AutoConsume - {x.Value.Name} is available: {isAvailable}");
            }

            Plugin.taskManager.EnqueueDelay(50);
            Plugin.taskManager.Enqueue(() => PlayerHelper.IsReadyFull, "AutoConsume-WaitPlayerIsReadyFull");
            Plugin.taskManager.EnqueueDelay(250);
        });
    }

    public override void OnGuiSettings()
    {
        ImGui.Columns(2, "##AutoConsumeColumns");
        //ImGui.SameLine(0, 5);
        bool preAutoConsumeIgnoreStatus = this.AutoConsumeIgnoreStatus;
        if (ImGui.Checkbox(Loc.Get("LoopActions.ConsumeItems.IgnoreStatus"), ref preAutoConsumeIgnoreStatus))
        {
            this.AutoConsumeIgnoreStatus = preAutoConsumeIgnoreStatus;
            ConfigurationProfileV2.Save();
        }

        ImGuiComponents.HelpMarker(Loc.Get("LoopActions.ConsumeItems.IgnoreStatusHelp"));
        ImGui.NextColumn();
        //ImGui.SameLine(0, 5);

        ImGui.PushItemWidth(80 * ImGuiHelpers.GlobalScale);

        using (ImRaii.Disabled(this.AutoConsumeIgnoreStatus))
        {
            int consumeTime = this.AutoConsumeTime;
            if (ImGui.InputInt(Loc.Get("LoopActions.ConsumeItems.MinTimeRemaining"), ref consumeTime, 1))
            {
                this.AutoConsumeTime = Math.Clamp(consumeTime, 0, 59);
                ConfigurationProfileV2.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("LoopActions.ConsumeItems.MinTimeRemainingHelp"));
        }

        ImGui.PopItemWidth();
        ImGui.Columns(1);
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 115 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##SelectAutoConsumeItem", consumableItemsSelectedItem.Name))
        {
            ImGui.InputTextWithHint(Loc.Get("LoopActions.ConsumeItems.ItemName"), Loc.Get("LoopActions.ConsumeItems.ItemNameHint"), ref consumableItemsItemNameInput, 1000);
            foreach (ConsumableItem? item in ConsumableItems.Where(x => x.Name.Contains(consumableItemsItemNameInput, StringComparison.InvariantCultureIgnoreCase))!)
                if (ImGui.Selectable($"{item.Name}"))
                    consumableItemsSelectedItem = item;

            ImGui.EndCombo();
        }

        ImGui.PopItemWidth();

        ImGui.SameLine(0, 5);
        using (ImRaii.Disabled(consumableItemsSelectedItem == null))
        {
            if (ImGui.Button(Loc.Get("LoopActions.ConsumeItems.AddItem")))
            {
                if (this.AutoConsumeItemsList.Any(x => x.Key == consumableItemsSelectedItem!.StatusId))
                    this.AutoConsumeItemsList.RemoveAll(x => x.Key == consumableItemsSelectedItem!.StatusId);

                this.AutoConsumeItemsList.Add(new KeyValuePair<ushort, ConsumableItem>(consumableItemsSelectedItem!.StatusId, consumableItemsSelectedItem));
                ConfigurationProfileV2.Save();
            }
        }

        using (ImRaii.ListBox("##ConsumableItemList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeightWithSpacing() * this.AutoConsumeItemsList.Count + 5)))
        {
            bool boolRemoveItem = false;
            KeyValuePair<ushort, ConsumableItem> removeItem = new();
            foreach (KeyValuePair<ushort, ConsumableItem> item in this.AutoConsumeItemsList)
            {
                ImGui.Selectable($"{item.Value.Name}");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    boolRemoveItem = true;
                    removeItem = item;
                }
            }

            if (boolRemoveItem)
            {
                this.AutoConsumeItemsList.Remove(removeItem);
                ConfigurationProfileV2.Save();
            }

        }
    }
}

public class ExecuteCommandsLoopActionConfig : LoopActionConfig<ExecuteCommandsLoopActionConfig>
{
    static ExecuteCommandsLoopActionConfig()
    {
        Name = "Execute Commands";
        LoopActionCategory = LoopActionCategory.Pre | LoopActionCategory.Termination;
    }

    public override string? HelpText    => Loc.Get("LoopActions.ExecuteCommands.ExecuteCommandsHelp", "/echo test");
    public override string  OverlayName => Loc.Get("Overlay.Button.ExecuteCommands");

    [JsonProperty] public List<string> CustomCommands  { get; set; } = [];

    private static string loopCommand = string.Empty;

    protected override void RunInternal(ref bool queue)
    {
        Plugin.taskManager.Enqueue(() => Svc.Log.Debug($"ExecutingCommandsPreLoop, executing {this.CustomCommands.Count} commands"));
        this.CustomCommands.Each(x => Plugin.taskManager.Enqueue(() => Chat.ExecuteCommand(x), "Run-ExecuteCommands"));
    }

    public override void OnGuiSettings()
    {
        List<string> preCustomCommands  = this.CustomCommands;
        MakeCommands("LoopActions.ExecuteCommands.ExecuteCommands", ref preCustomCommands, ref loopCommand, "Commands");
        this.CustomCommands  = preCustomCommands;
    }

    private static void MakeCommands(string checkbox, ref List<string> commands, ref string curCommand, string id)
    {
        ImGui.Indent();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 185 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint($"##Commands{checkbox}_{id}", "enter command starting with /", ref curCommand, 500, ImGuiInputTextFlags.EnterReturnsTrue))
            if (!curCommand.IsNullOrEmpty() && curCommand[0] == '/' && (ImGui.IsKeyDown(ImGuiKey.Enter) || ImGui.IsKeyDown(ImGuiKey.KeypadEnter)))
            {
                commands.Add(curCommand);
                curCommand = string.Empty;
                ConfigurationProfileV2.Save();
            }

        ImGui.PopItemWidth();

        ImGui.SameLine(0, 5);
        using (ImRaii.Disabled(curCommand.IsNullOrEmpty() || curCommand[0] != '/'))
        {
            if (ImGui.Button($"Add Command##CommandButton{checkbox}_{id}"))
            {
                commands.Add(curCommand);
                ConfigurationProfileV2.Save();
            }
        }

        if (!ImGui.BeginListBox($"##CommandList{checkbox}_{id}", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * commands.Count) + 5)))
            return;

        bool removeItem = false;
        int  removeAt   = 0;

        foreach ((string Value, int Index) item in commands.Select((Value, Index) => (Value, Index)))
        {
            ImGui.Selectable($"{item.Value}##Selectable{checkbox}_{id}");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                removeItem = true;
                removeAt   = item.Index;
            }
        }

        if (removeItem)
        {
            commands.RemoveAt(removeAt);
            ConfigurationProfileV2.Save();
        }

        ImGui.EndListBox();
        ImGui.Unindent();
    }
}

public class WaitLoopActionConfig : LoopActionConfig<WaitLoopActionConfig>
{
    static WaitLoopActionConfig() => 
        Name = "Wait";

    [Flags]
    public enum WaitPlugins
    {
        None       = 0,
        Lifestream = 1 << 0,
        SND        = 1 << 1,
    }

    public override string OverlayName => Loc.Get("Overlay.Button.Wait");

    [JsonProperty] public int  WaitTime          { get; set; } = 1000;
    [JsonProperty] public WaitPlugins WaitOnPlugins { get; set; } = WaitPlugins.None;

    protected override void RunInternal(ref bool queue)
    {
        Plugin.taskManager.EnqueueDelay(this.WaitTime);

        if(this.WaitOnPlugins.HasFlag(WaitPlugins.Lifestream))
            Plugin.taskManager.Enqueue(() => !Lifestream_IPCSubscriber.IsBusy);
        if(this.WaitOnPlugins.HasFlag(WaitPlugins.SND))
            Plugin.taskManager.Enqueue(() => !SND_IPCSubscriber.AnyMacroRunning);
    }

    public override void OnGuiSettings()
    {
        int waitTime = this.WaitTime;
        if (ImGui.InputInt(Loc.Get("LoopActions.Wait.WaitTime"), ref waitTime, 10, 100))
        {
            if (waitTime < 0)
                waitTime = 0;

            this.WaitTime = waitTime;
            ConfigurationProfileV2.Save();
        }

        ImGui.AlignTextToFramePadding();
        WaitPlugins waitPlugins = this.WaitOnPlugins;

        if (ImGui.BeginCombo("##WaitOnPlugins", waitPlugins.ToString()))
        {
            foreach (WaitPlugins plugin in Enum.GetValues<WaitPlugins>())
                if (plugin is not WaitPlugins.None && ImGui.Selectable(plugin.ToString(), waitPlugins.HasFlag(plugin)))
                {
                    if (this.WaitOnPlugins.HasFlag(plugin))
                        this.WaitOnPlugins &= ~plugin;
                    else
                        this.WaitOnPlugins |= plugin;

                    ConfigurationProfileV2.Save();
                }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Text(Loc.Get("LoopActions.Wait.WaitOnPlugins"));
    }
}

public class PlaylistPreLoopActionConfig : LoopActionConfig<PlaylistPreLoopActionConfig>
{
    static PlaylistPreLoopActionConfig() =>
            Name = "Playlist Pre Loop";

    public override string? HelpText    => Loc.Get("LoopActions.PlaylistPreLoop.Help");

    public override    string OverlayName { get; } = null!;
    public override    bool   Locked      => true;
    protected override bool   HasConfig   => false;

    protected override unsafe void RunInternal(ref bool queue)
    {
        if (ConfigurationMain.Instance.GetCurrentConfig.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist && Plugin.PlaylistCurrentEntry != null)
            if (Plugin.PlaylistCurrentEntry.gearset.HasValue && RaptureGearsetModule.Instance()->IsValidGearset(Plugin.PlaylistCurrentEntry.gearset.Value))
            {
                Plugin.taskManager.Enqueue(() => RaptureGearsetModule.Instance()->EquipGearset(Plugin.PlaylistCurrentEntry.gearset.Value));
                Plugin.taskManager.Enqueue(() => PlayerHelper.IsReadyFull);
            }
    }

    public override void OnGuiSettings() =>
        throw new NotImplementedException();
}

public class PlaylistSwitchLoopActionConfig : LoopActionConfig<PlaylistSwitchLoopActionConfig>
{
    static PlaylistSwitchLoopActionConfig() =>
            Name = "Playlist Duty Switch";

    public override string? HelpText    => Loc.Get("LoopActions.PlaylistSwitchLoop.Help");
    public override string  OverlayName { get; } = null!;

    public override    bool   Locked    => true;
    protected override bool   HasConfig => false;

    public PlaylistSwitchLoopActionConfig() => 
        this.EnabledOnlyWhenQueuing = true;

    protected override unsafe void RunInternal(ref bool queue)
    {
        if (ConfigurationMain.Instance.GetCurrentConfig.Meta.AutoDutyModeEnum != AutoDutyMode.Playlist)
            return;

        PlaylistEntry? currentEntry = Plugin.PlaylistCurrentEntry;
        if (currentEntry != null && ++currentEntry.curCount < currentEntry.count && !currentEntry.TerminationConfig.ShouldStop)
        {
            Svc.Log.Debug($"repeating the duty once more: {currentEntry.curCount + 1} of {currentEntry.count}");
        }
        else
        {
            Svc.Log.Debug("next playlist entry");
            Plugin.playlistIndex++;

            if (Plugin.playlistIndex >= Plugin.PlaylistCurrent.Entries.Count)
            {
                Svc.Log.Debug("playlist done");
                queue                = false;
                Plugin.playlistIndex = 0;
            }
            else
            {
                Plugin.PlaylistCurrentEntry!.curCount = 0;

                Svc.Log.Debug($"entry with gearset {Plugin.PlaylistCurrentEntry.gearset}");

                if (Plugin.PlaylistCurrentEntry.gearset.HasValue && RaptureGearsetModule.Instance()->IsValidGearset(Plugin.PlaylistCurrentEntry.gearset.Value))
                {
                    static void GearSwitch()
                    {
                        Plugin.taskManager.InsertMulti(
                                                     new TaskManagerTask(() => RaptureGearsetModule.Instance()->EquipGearset(Plugin.PlaylistCurrentEntry!.gearset!.Value)),
                                                     new TaskManagerTask(() => PlayerHelper.IsReadyFull),
                                                     new TaskManagerTask(() =>
                                                                         {
                                                                             if (RaptureGearsetModule.Instance()->CurrentGearsetIndex != Plugin.PlaylistCurrentEntry!.gearset!.Value)
                                                                                 Plugin.taskManager.Insert(GearSwitch);
                                                                         }));
                    }
                    Plugin.taskManager.Enqueue(GearSwitch);
                }
            }
        }
    }

    public override void OnGuiSettings() => 
        throw new NotImplementedException();
}

public class PlaySoundLoopActionConfig : LoopActionConfig<PlaySoundLoopActionConfig>
{
    static PlaySoundLoopActionConfig()
    {
        Name = "Play Sound";
        LoopActionCategory = LoopActionCategory.Termination;
    }

    public override string OverlayName => Loc.Get("Overlay.Button.PlaySound");

    [JsonProperty] public bool   CustomSound       { get; set; }
    [JsonProperty] public float  CustomSoundVolume { get; set; } = 0.5f;
    [JsonProperty] public Sounds SoundEnum         { get; set; } = Sounds.None;
    [JsonProperty] public string SoundPath         { get; set; } = string.Empty;

    private static readonly Sounds[] validSounds = [.. ((Sounds[])Enum.GetValues(typeof(Sounds))).Where(s => s is not Sounds.None and not Sounds.Unknown)];

    protected override void RunInternal(ref bool queue) =>
        //todo: wtf are these configs if nothing of that is done
        Plugin.taskManager.Enqueue(() => SoundHelper.StartSound(this.SoundEnum));

    public override void OnGuiSettings()
    {
        if (ImGuiEx.IconButton(FontAwesomeIcon.Play, "##ConfigSoundTest", new Vector2(ImGui.GetItemRectSize().Y)))
            SoundHelper.StartSound(this.SoundEnum);

        ImGui.SameLine(0, 10);
        ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##ConfigEndSoundMethod", this.SoundEnum.ToName()))
        {
            foreach (Sounds sound in validSounds)
                if (ImGui.Selectable(sound.ToName()))
                {
                    this.SoundEnum = sound;
                    SoundHelper.StartSound(sound);
                    ConfigurationProfileV2.Save();
                }

            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
    }
}
#endregion