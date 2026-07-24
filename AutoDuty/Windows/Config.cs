using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using Serilog.Events;
using System.Globalization;
using static AutoDuty.Helpers.RepairNPCHelper;
using static AutoDuty.Windows.ConfigTab;

namespace AutoDuty.Windows;

using Dalamud.Game.ClientState.Objects.Types;
using Data;
using ECommons.Configuration;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.IPC.Subscribers.RotationSolverReborn;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Multibox;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using NightmareUI.Censoring;
using Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Achievement = Lumina.Excel.Sheets.Achievement;
using Vector2 = FFXIVClientStructs.FFXIV.Common.Math.Vector2;

[JsonObject(MemberSerialization.OptIn)]
public class ConfigurationMain
{
    public const string CONFIGNAME_BARE = "Bare";

    public static ConfigurationMain Instance { get; set; } = null!;

    [JsonProperty]
    public string DefaultConfigName { get; set; } = CONFIGNAME_BARE;

    [JsonProperty]
    internal string Language { get; set; } = LocalizationManager.BASE_LANGUAGE;

    [JsonProperty]
    private string activeProfileName = CONFIGNAME_BARE;
    
    public  string ActiveProfileName => this.activeProfileName;

    public bool Initialized { get; private set; } = false;

    [JsonProperty]
    private readonly HashSet<ProfileData> profileData = [];

    private readonly Dictionary<string, ProfileData> profileByName = [];
    private readonly Dictionary<ulong, string> profileByCID = [];

    [JsonProperty]
    public readonly Dictionary<ulong, CharData> charByCID = [];

    [JsonObject(MemberSerialization.OptOut)]
    public struct CharData
    {
        public required ulong  CID;
        public          string Name;
        public          string World;

        public readonly string GetName() =>
            this.Name.Length != 0 ? Censor.Character(this.Name, this.World) : this.CID.ToString();

        public readonly override int GetHashCode() => 
            this.CID.GetHashCode();
    }

    [JsonProperty]
    public Dictionary<ulong, int> dutyCountSinceReset = [];
    [JsonProperty]
    public DateTime dutyCountResetDate;

    [JsonProperty]
    public StatData stats = new();

    [JsonObject(MemberSerialization.OptOut)]
    public class StatData
    {
        public          int                  dungeonsRun;
        public readonly List<DutyDataRecord> dutyRecords = [];
        public          TimeSpan             timeSpent   = TimeSpan.Zero;

        public StatData Filter(Func<DutyDataRecord, bool> filter)
        {
            StatData newStat = new();

            newStat.dutyRecords.AddRange(this.dutyRecords.Where(filter));
            newStat.dungeonsRun = newStat.dutyRecords.Count;
            foreach (DutyDataRecord record in newStat.dutyRecords)
                newStat.timeSpent += record.Duration;
            return newStat;
        }
    }


    [JsonProperty]
    //Dev Options
    internal bool updatePathsOnStartup = true;
    public bool UpdatePathsOnStartup
    {
        get => !Plugin.isDev || this.updatePathsOnStartup;
        set => this.updatePathsOnStartup = value;
    }

    internal uint noviceQueue = 0;

    [JsonProperty]
    public MultiboxUtility.MultiboxConfiguration multibox = new();

    public IEnumerable<string> ConfigNames => this.profileByName.Keys;
     
    public ProfileData GetCurrentProfile
    {
        get
        {
            if (!this.profileByName.TryGetValue(this.ActiveProfileName, out ProfileData? profiles))
            {
                this.SetProfileToDefault();
                return this.GetCurrentProfile;
            }

            return profiles;
        }
    }

    public Configuration GetCurrentConfig => this.GetCurrentProfile.Config;

    public void Init()
    {
        if (this.profileData.Count == 0)
            if (Svc.PluginInterface.ConfigFile.Exists)
            {
                Configuration? configuration = EzConfig.DefaultSerializationFactory.Deserialize<Configuration>(File.ReadAllText(Svc.PluginInterface.ConfigFile.FullName, Encoding.UTF8));
                if (configuration != null)
                {
                    this.CreateProfile("Migrated", configuration);
                    this.SetProfileAsDefault();
                }
            }

        void RegisterProfileData(ProfileData profile)
        {
            if (profile.CIDs.Count != 0)
                foreach (ulong cid in profile.CIDs)
                    this.profileByCID[cid] = profile.Name;
            this.profileByName[profile.Name] = profile;
        }

        foreach (ProfileData profile in this.profileData)
            if(profile.Name != CONFIGNAME_BARE)
                RegisterProfileData(profile);

        RegisterProfileData(new ProfileData
                            {
                                Name = CONFIGNAME_BARE,
                                Config = new Configuration
                                         {
                                             EnablePreLoopActions     = false,
                                             EnableBetweenLoopActions = false,
                                             EnableTerminationActions = false,
                                             LootTreasure             = false
                                         }
                            });

        this.SetProfileToDefault();
    }

    public bool SetProfile(string name)
    {
        DebugLog("Changing profile to: " + name);
        if (this.profileByName.ContainsKey(name))
        {
            this.activeProfileName = name;
            EzConfig.Save();
            return true;
        }
        return false;
    }

    public void SetProfileAsDefault()
    {
        if (this.profileByName.ContainsKey(this.ActiveProfileName))
        {
            this.DefaultConfigName = this.ActiveProfileName;
            EzConfig.Save();
        }
    }

    public void SetProfileToDefault()
    {
        this.SetProfile(CONFIGNAME_BARE);
        Svc.Framework.RunOnTick(() =>
        {
            DebugLog($"Setting to default profile for {Player.Name} ({Player.CID}) {PlayerHelper.IsValid}");

            if (Player.Available && this.profileByCID.TryGetValue(Player.CID, out string? charProfile))
                if (this.SetProfile(charProfile))
                    return;

            DebugLog("No char default found. Using general default");
            if (!this.SetProfile(this.DefaultConfigName))
            {
                DebugLog("Fallback, using bare");
                this.DefaultConfigName = CONFIGNAME_BARE;
                this.SetProfile(CONFIGNAME_BARE);
            }

            this.Initialized = true;
        });
    }

    public void CreateNewProfile() => 
        this.CreateProfile("Profile" + (this.profileByName.Count - 1).ToString(CultureInfo.InvariantCulture));

    public void CreateProfile(string name) => 
        this.CreateProfile(name, new Configuration());

    public void CreateProfile(string name, Configuration config)
    {
        DebugLog($"Creating new Profile: {name}");

        ProfileData profile = new()
                           {
                               Name   = name,
                               Config = config
                           };

        this.profileData.Add(profile);
        this.profileByName.Add(name, profile);
        this.SetProfile(name);
    }

    public void DuplicateCurrentProfile()
    {
        string name;
        int    counter = 0;

        string templateName = this.ActiveProfileName.EndsWith("_Copy") ? this.ActiveProfileName : $"{this.ActiveProfileName}_Copy";

        do
            name = counter++ > 0 ? $"{templateName}{counter}" : templateName;
        while (this.profileByName.ContainsKey(name));

        string?        oldConfig = EzConfig.DefaultSerializationFactory.Serialize(this.GetCurrentConfig);
        if(oldConfig != null)
        {
            Configuration? newConfig = EzConfig.DefaultSerializationFactory.Deserialize<Configuration>(oldConfig);
            if(newConfig != null)
                this.CreateProfile(name, newConfig);
        }
    }

    public void RemoveCurrentProfile()
    {
        DebugLog("Removing " + this.ActiveProfileName);
        this.profileData.Remove(this.GetCurrentProfile);
        this.profileByName.Remove(this.ActiveProfileName);
        this.SetProfileToDefault();
    }

    public bool RenameCurrentProfile(string newName)
    {
        if (this.profileByName.ContainsKey(newName))
            return false;

        ProfileData config = this.GetCurrentProfile;
        this.profileByName.Remove(this.ActiveProfileName);
        this.profileByName[newName] = config;
        config.Name                 = newName;
        this.activeProfileName      = newName;

        EzConfig.Save();

        return true;
    }

    public ProfileData? GetProfile(string name) => 
        this.profileByName.GetValueOrDefault(name);

    public void SetCharacterDefault() =>
        Svc.Framework.RunOnTick(() =>
                                {

                                    if (!PlayerHelper.IsValid)
                                        return;

                                    ulong cid = Player.CID;

                                    if (this.profileByCID.TryGetValue(cid, out string? oldProfile))
                                        this.profileByName[oldProfile].CIDs.Remove(cid);

                                    this.GetCurrentProfile.CIDs.Add(cid);
                                    this.profileByCID.Add(cid, this.ActiveProfileName);
                                    this.charByCID[cid] = new CharData
                                                          {
                                                              CID   = cid,
                                                              Name  = Player.Name,
                                                              World = Player.CurrentWorldName
                                                          };
                                    EzConfig.Save();

                                    LevelingHelper.ResetLevelingDuties();
                                });

    public void RemoveCharacterDefault() =>
        Svc.Framework.RunOnTick(() =>
                                {
                                    if (!PlayerHelper.IsValid)
                                        return;

                                    ulong cid = Player.CID;

                                    this.profileByName[this.ActiveProfileName].CIDs.Remove(cid);
                                    this.profileByCID.Remove(cid);

                                    EzConfig.Save();
                                });

    public static void DebugLog(string message) => 
        Svc.Log.Debug($"Configuration Main: {message}");

    public static JsonSerializerSettings JsonSerializerSettings { get; } = new()
                                                                           {
                                                                               Formatting                     = Formatting.Indented,
                                                                               DefaultValueHandling           = DefaultValueHandling.Include,
                                                                               Converters                     = [new StringEnumConverter(new DefaultNamingStrategy())],
                                                                               TypeNameHandling               = TypeNameHandling.Auto,
                                                                               TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                                                                               Culture                        = CultureInfo.InvariantCulture,
                                                                               SerializationBinder            = new AutoDutySerializationBinder()
                                                                           };

    public class AutoDutySerializationBinder : DefaultSerializationBinder
    {
        
        public override Type BindToType(string? assemblyName, string typeName)
        {
            bool isInternal = assemblyName?.StartsWith("AutoDuty") ?? false;

            if (isInternal)
            {
                if (typeName.Contains("AutoDuty.Data.Classes+PathActionCondition"))
                    typeName = typeName.Replace("AutoDuty.Data.Classes+PathActionCondition", "AutoDuty.Data.PathActionCondition");

                Type? type = typeof(Configuration).Assembly.GetType(typeName);
                if (type != null)
                    return type;
            }

            return base.BindToType(assemblyName, typeName);
        }
    }
}

[JsonObject(MemberSerialization.OptOut)]
public class ProfileData
{
    public required string         Name;
    public          HashSet<ulong> CIDs = [];
    public required Configuration  Config;
}

public class AutoDutySerializationFactory : DefaultSerializationFactory, ISerializationFactory
{
    public override string DefaultConfigFileName { get; } = "AutoDutyConfig.json";

    public new string Serialize(object config) => 
        base.Serialize(config);

    public override byte[] SerializeAsBin(object config) => 
        Encoding.UTF8.GetBytes(this.Serialize(config));
}



[Serializable]
public class Configuration
{
    //Meta
    public HashSet<string>                                    DoNotUpdatePathFiles = [];
    public Dictionary<uint, Dictionary<string, JobWithRole>?> PathSelectionsByPath = [];

    //LogOptions
    public bool AutoScroll = true;
    public LogEventLevel LogEventLevel = LogEventLevel.Debug;

    //General Options
    internal AutoDutyMode autoDutyModeEnum = AutoDutyMode.Looping;
    public AutoDutyMode AutoDutyModeEnum
    {
        get => this.autoDutyModeEnum;
        set
        {
            this.autoDutyModeEnum          = value;
            Plugin.CurrentTerritoryContent = null;
            MainTab.DutySelected           = null;
            Plugin.LevelingModeEnum        = LevelingMode.None;
        }
    }


    public int LoopTimes = 1;
    internal DutyMode dutyModeEnum = DutyMode.Support;
    public DutyMode DutyModeEnum
    {
        get => this.AutoDutyModeEnum switch
        {
            AutoDutyMode.Playlist => Plugin.PlaylistCurrentEntry?.DutyMode ?? this.dutyModeEnum,
            AutoDutyMode.Looping or _ => this.dutyModeEnum
        };
        set
        {
            this.dutyModeEnum = value;
            Plugin.CurrentTerritoryContent = null;
            MainTab.DutySelected = null;
            Plugin.LevelingModeEnum = LevelingMode.None;
        }
    }


    
    public bool Unsynced                       = false;
    public bool HideUnavailableDuties          = false;
    public bool PreferTrustOverSupportLeveling = false;
    public bool SquadronAssignLowestMembers    = true;

    public bool ShowMainWindowOnStartup = false;

    
    #region OverlayConfig
    internal bool showOverlay = true;
    public bool ShowOverlay
    {
        get => this.showOverlay;
        set
        {
            this.showOverlay       = value;
            Plugin.Overlay?.IsOpen = value;
        }
    }
    internal bool hideOverlayWhenStopped = false;
    public bool HideOverlayWhenStopped
    {
        get => this.hideOverlayWhenStopped;
        set 
        {
            this.hideOverlayWhenStopped = value;
            if (Plugin.Overlay != null) 
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => Plugin.Overlay.IsOpen = !value || Plugin.States.HasAnyFlag(PluginState.Looping, PluginState.Navigating), () => Plugin.Overlay != null);
        }
    }
    internal bool lockOverlay = false;
    public bool LockOverlay
    {
        get => this.lockOverlay;
        set 
        {
            this.lockOverlay = value;
            if (value)
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () =>
                                                                    {
                                                                        if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                            Plugin.Overlay.Flags |= ImGuiWindowFlags.NoMove;
                                                                    }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () =>
                                                                    {
                                                                        if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                            Plugin.Overlay.Flags -= ImGuiWindowFlags.NoMove;
                                                                    }, () => Plugin.Overlay != null);
        }
    }
    internal bool overlayNoBG = false;
    public bool OverlayNoBG
    {
        get => this.overlayNoBG;
        set
        {
            this.overlayNoBG = value;
            if (value)
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () =>
                                                                    {
                                                                        if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) 
                                                                            Plugin.Overlay.Flags |= ImGuiWindowFlags.NoBackground;
                                                                    }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () =>
                                                                    {
                                                                        if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) 
                                                                            Plugin.Overlay.Flags -= ImGuiWindowFlags.NoBackground;
                                                                    }, () => Plugin.Overlay != null);
        }
    }

    public bool OverlayAnchorBottom    = false;
    public bool ShowDutyLoopText       = true;
    public bool ShowActionText         = true;
    public bool UseSliderInputs        = false;
    public bool OverrideOverlayButtons = true;
    public bool GotoButton             = true;
    public bool TurninButton           = true;
    public bool DesynthButton          = true;
    public bool ExtractButton          = true;
    public bool RepairButton           = true;
    public bool EquipButton            = true;
    public bool CofferButton           = true;
    public bool TTButton               = true;
    #endregion

    #region DutyConfig
    //Duty Config Options
    public bool           AutoExitDuty                  = true;
    public bool           OnlyExitWhenDutyDone          = false;
    public bool           AutoManageRotationPluginState = true;
    public RotationPlugin rotationPlugin                = RotationPlugin.All;

    #region Wrath
    public bool                                Wrath_AutoSetupJobs { get; set; } = true;
    public WrathCombo.API.Enum.DPSRotationMode Wrath_TargetingTank    = WrathCombo.API.Enum.DPSRotationMode.Highest_Max;
    public WrathCombo.API.Enum.DPSRotationMode Wrath_TargetingNonTank = WrathCombo.API.Enum.DPSRotationMode.Lowest_Current;
    #endregion

    #region RSR

    public RotationSolverRebornIPC.TargetHostileType RSR_TargetHostileType    = RotationSolverRebornIPC.TargetHostileType.AllTargetsCanAttack;
    public RotationSolverRebornIPC.TargetingType     RSR_TargetingTypeTank    = RotationSolverRebornIPC.TargetingType.HighHP;
    public RotationSolverRebornIPC.TargetingType     RSR_TargetingTypeNonTank = RotationSolverRebornIPC.TargetingType.LowHP;
    #endregion



    internal bool autoManageBossModAISettings   = true;
    public bool AutoManageBossModAISettings
    {
        get => this.autoManageBossModAISettings;
        set
        {
            this.autoManageBossModAISettings = value;
            this.HideBossModAIConfig         = !value;
        }
    }

    #region BossMod
    public bool HideBossModAIConfig           = false;
    public bool BM_UpdatePresetsAutomatically = true;


    internal bool maxDistanceToTargetRoleBased = true;
    public bool MaxDistanceToTargetRoleBased
    {
        get => this.maxDistanceToTargetRoleBased;
        set
        {
            this.maxDistanceToTargetRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("MaxDistanceToTargetRoleBasedBMRoleChecks", () => BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetFloat    = 2.6f;
    public float MaxDistanceToTargetAoEFloat = 12;

    internal bool positionalRoleBased = true;
    public bool PositionalRoleBased
    {
        get => this.positionalRoleBased;
        set
        {
            this.positionalRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("PositionalRoleBasedBMRoleChecks", () => BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetRoleMelee  = 2.6f;
    public float MaxDistanceToTargetRoleRanged = 10f;
    #endregion

    internal bool       positionalAvarice = true;
    public   Positional PositionalEnum    = Positional.Any;

    public bool       AutoManageVnavAlignCamera      = true;
    public bool       LootTreasure                   = true;
    public LootMethod LootMethodEnum                 = LootMethod.AutoDuty;
    public bool       LootBossTreasureOnly           = false;
    public int        TreasureCofferScanDistance     = 25;
    public bool       RebuildNavmeshOnStuck          = true;
    public byte       RebuildNavmeshAfterStuckXTimes = 5;
    public int        MinStuckTime                   = 500;
    public bool       StuckOnStep                    = true;
    public bool       stuckReturn                    = true;
    public int        StuckReturnX                   = 10;
    public bool StuckReturn
    {
        get => this.stuckReturn && !MultiboxUtility.Config.MultiBox;
        set => this.stuckReturn = value;
    }

    public bool PathDrawEnabled   = false;
    public int  PathDrawStepCount = 5;

    public bool DisableRenderWhileActive = false;

    public bool       OverridePartyValidation        = false;
    public bool       UsingAlternativeRotationPlugin = false;
    public bool       UsingAlternativeMovementPlugin = false;
    public bool       UsingAlternativeBossPlugin     = false;

    public bool        TreatUnsyncAsW2W = true;
    public JobWithRole W2WJobs          = JobWithRole.Tanks;

    public bool IsW2W(Job? job = null, bool? unsync = null)
    {
        job ??= PlayerHelper.GetJob();

        if (this.W2WJobs.HasJob(job.Value))
            return true;

        unsync ??= this.Unsynced && this.DutyModeEnum.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);

        return MultiboxUtility.Config.MultiBox || unsync.Value && this.TreatUnsyncAsW2W;
    }

    public bool LevelingListExperimentalEntries
    {
        get;
        set
        {
            if(field != value)
                LevelingHelper.ResetLevelingDuties();
            field = value;
        }
    } = false;

#endregion

    #region PreLoop
    public bool                                       EnablePreLoopActions     = true;
    public bool                                       ExecuteCommandsPreLoop   = false;
    public List<string>                               CustomCommandsPreLoop    = [];
    public bool                                       RetireMode               = false;
    public RetireLocation                             RetireLocationEnum       = RetireLocation.Inn;
    public List<Vector3>                              PersonalHomeEntrancePath = [];
    public List<Vector3>                              FCEstateEntrancePath     = [];
    public bool                                       AutoEquipRecommendedGear;
    public GearsetUpdateSource                        AutoEquipRecommendedGearSource;
    public bool                                       AutoEquipRecommendedGearGearsetterOldToInventory;
    public bool                                       AutoRepair              = false;
    public uint                                       AutoRepairPct           = 50;
    public bool                                       AutoRepairSelf          = false;
    public RepairNpcData?                             PreferredRepairNPC      = null;
    public bool                                       AutoConsume             = false;
    public bool                                       AutoConsumeIgnoreStatus = false;
    public int                                        AutoConsumeTime         = 29;
    public List<KeyValuePair<ushort, ConsumableItem>> AutoConsumeItemsList    = [];
    #endregion


    #region BetweenLoop
    public bool         EnableBetweenLoopActions         = true;
    public bool         ExecuteBetweenLoopActionLastLoop = false;
    public int          WaitTimeBeforeAfterLoopActions   = 0;
    public bool         ExecuteCommandsBetweenLoop       = false;
    public List<string> CustomCommandsBetweenLoop        = [];
    public bool         AutoExtract                      = false;

    public bool                     AutoOpenCoffers = false;
    public byte?                    AutoOpenCoffersGearset;
    public bool                     AutoOpenCoffersBlacklistUse;
    public Dictionary<uint, string> AutoOpenCoffersBlacklist = [];

    internal bool autoExtractAll = false;
    public bool AutoExtractAll
    {
        get => this.autoExtractAll;
        set => this.autoExtractAll = value;
    }
    internal bool autoDesynth = false;
    public bool AutoDesynth
    {
        get => this.autoDesynth;
        set
        {
            this.autoDesynth = value;
            if (value && !this.AutoDesynthSkillUp)
                this.AutoGCTurnin = false;
        }
    }
    internal bool autoDesynthSkillUp = false;
    public bool AutoDesynthSkillUp
    {
        get => this.autoDesynthSkillUp;
        set
        {
            this.autoDesynthSkillUp = value;
            if (!value && this.AutoGCTurnin)
                this.AutoDesynth = false;
        }
    }
    public int   AutoDesynthSkillUpLimit = 50;
    public bool  AutoDesynthNQOnly       = false;
    public bool  AutoDesynthNoGearset    = true;
    public ulong AutoDesynthCategories   = 0x1;

    internal bool autoGCTurnin            = false;
    public bool AutoGCTurnin
    {
        get => this.autoGCTurnin;
        set
        {
            this.autoGCTurnin = value;
            if (value && !this.AutoDesynthSkillUp)
                this.AutoDesynth = false;
        }
    }

    public int  AutoGCTurninSlotsLeft     = 5;
    public bool AutoGCTurninSlotsLeftBool = false;
    public bool AutoGCTurninUseTicket     = false;

    public bool ArmoireEntrust      = false;
    public bool GlamourChestEntrust = false;

    public bool TripleTriadRegister;
    public bool TripleTriadSell;
    public int  TripleTriadSellMinItemCount = 1;
    public int  TripleTriadSellMinSlotCount = 1;

    public bool DiscardItems;

    public bool                   EnableAutoRetainer         = false;
    public SummoningBellLocations PreferredSummoningBellEnum = 0;
    public long                   AutoRetainer_RemainingTime = 0L;
    #endregion

    #region Termination
    public bool                                        EnableTerminationActions      = true;
    public bool                                        StopLevel                     = false;
    public int                                         StopLevelInt                  = 1;
    public bool                                        StopNoRestedXP                = false;
    public bool                                        StopItemQty                   = false;
    public bool                                        StopItemAll                   = false;
    public Dictionary<uint, KeyValuePair<string, int>> StopItemQtyItemDictionary     = [];
    public int                                         StopItemQtyInt                = 1;
    public bool                                        StopWhenDutyGathered          = false;
    public bool                                        TerminationBLUSpellsEnabled   = false;
    public List<uint>                                  TerminationBLUSpells          = [];
    public bool                                        TerminationBLUSpellsAll       = false;
    public bool                                        TerminationInventoryFree      = false;
    public int                                         TerminationInventoryFreeSlots = 0;
    public bool                                        TerminationiLvl               = false;
    public int                                         TerminationiLvlInt            = 0;

    public bool                                        ExecuteCommandsTermination   = false;
    public List<string>                                CustomCommandsTermination    = [];
    public bool                                        PlayEndSound                 = false;
    public bool                                        CustomSound                  = false;
    public float                                       CustomSoundVolume            = 0.5f;
    public Sounds                                      SoundEnum                    = Sounds.None;
    public string                                      SoundPath                    = "";
    public TerminationMode                             TerminationMethodEnum        = TerminationMode.Do_Nothing;
    public bool                                        TerminationKeepActive        = true;
    #endregion

    public static void Save() => 
        EzConfig.Save();

    public TrustMemberName?[] SelectedTrustMembers = new TrustMemberName?[3];
}

public static class ConfigTab
{
    internal static string followName = "";

    private static Configuration              Configuration => AutoDuty.Configuration;
    private static string                     preLoopCommand     = string.Empty;
    private static string                     betweenLoopCommand = string.Empty;
    private static string                     terminationCommand = string.Empty;
    private static Dictionary<uint, Item>     Items { get; set; } = Svc.Data.GetExcelSheet<Item>()?.Where(x => !x.Name.ToString().IsNullOrEmpty()).ToDictionary(x => x.RowId, x => x) ?? [];
    private static string                     stopItemQtyItemNameInput = "";
    private static KeyValuePair<uint, string> stopItemQtySelectedItem  = new(0, "");

    private static string                     autoOpenCoffersNameInput    = "";
    private static KeyValuePair<uint, string> autoOpenCoffersSelectedItem = new(0, "");

    public class ConsumableItem
    {
        public uint ItemId;
        public string Name = string.Empty;
        public bool CanBeHq;
        public ushort StatusId;
    }

    private static List<ConsumableItem> ConsumableItems { get; } = [..Svc.Data.GetExcelSheet<Item>()
                                                                         .Where(x => !x.Name.ToString().IsNullOrEmpty() && 
                                                                                     x.ItemUICategory.ValueNullable?.RowId is 44 or 45 or 46 && x.ItemAction.ValueNullable?.Data[0] is 48 or 49)
                                                                         .Select(x => new ConsumableItem
                                                                                      {
                                                                                          StatusId = x.ItemAction.Value!.Data[0],
                                                                                          ItemId   = x.RowId,
                                                                                          Name     = x.Name.ToString(),
                                                                                          CanBeHq  = x.CanBeHq
                                                                                      })];

    private static string         consumableItemsItemNameInput = "";
    private static ConsumableItem consumableItemsSelectedItem  = new();

    private static string profileRenameInput = "";

    private static readonly Sounds[] validSounds = [..((Sounds[])Enum.GetValues(typeof(Sounds))).Where(s => s is not Sounds.None and not Sounds.Unknown)];

    private static bool overlayHeaderSelected      = false;
    private static bool multiboxHeaderSelected     = false;
    private static bool devHeaderSelected          = false;
    private static bool dutyConfigHeaderSelected   = false;
    private static bool bmaiSettingHeaderSelected  = false;
    private static bool wrathSettingHeaderSelected = false;
    private static bool rsrSettingHeaderSelected   = false;
    private static bool w2wSettingHeaderSelected   = false;
    private static bool advModeHeaderSelected      = false;
    private static bool preLoopHeaderSelected      = false;
    private static bool betweenLoopHeaderSelected  = false;
    private static bool terminationHeaderSelected  = false;

    public static void BuildManuals()
    {
        ConsumableItems.Add(new ConsumableItem { StatusId = 1086, ItemId = 14945, Name = "Squadron Enlistment Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1080, ItemId = 14948, Name = "Squadron Battle Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1081, ItemId = 14949, Name = "Squadron Survival Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1082, ItemId = 14950, Name = "Squadron Engineering Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1083, ItemId = 14951, Name = "Squadron Spiritbonding Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1084, ItemId = 14952, Name = "Squadron Rationing Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1085, ItemId = 14953, Name = "Squadron Gear Maintenance Manual", CanBeHq = false });
    }

    public static void Draw()
    {
        if (MainWindow.CurrentTabName != "Config")
            MainWindow.CurrentTabName = "Config";

        //Language Selector
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(Loc.Get("ConfigTab.Language"));
        ImGui.SameLine();

        string   currentLang  = ConfigurationMain.Instance.Language;
        string[] languages    = LocalizationManager.availableLanguages;
        int      currentIndex = Array.IndexOf(languages, currentLang);

        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##Language", ref currentIndex, languages, languages.Length))
        {
            LocalizationManager.SetLanguage(languages[currentIndex]);
            Configuration.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Get("ConfigTab.LanguageHelp"));

        ImGui.Separator();

        //Start of Profile Selection
        ImGui.AlignTextToFramePadding();
        ImGui.Text(Loc.Get("ConfigTab.Profile.CurrentlySelected"));
        ImGui.SameLine();
        if (ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.CONFIGNAME_BARE)
            ImGuiHelper.DrawIcon(FontAwesomeIcon.Lock);
        if (ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.Instance.DefaultConfigName)
            ImGuiHelper.DrawIcon(FontAwesomeIcon.CheckCircle);
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 180 * ImGuiHelpers.GlobalScale);
        ImGui.SetItemAllowOverlap();
        using (ImRaii.ComboDisposable configCombo = ImRaii.Combo("##ConfigCombo", ConfigurationMain.Instance.ActiveProfileName))
        {
            if (configCombo)
                foreach (string key in ConfigurationMain.Instance.ConfigNames)
                {
                    float selectableX = ImGui.GetCursorPosX();
                    if (key == ConfigurationMain.CONFIGNAME_BARE)
                        ImGuiHelper.DrawIcon(FontAwesomeIcon.Lock);
                    if (key == ConfigurationMain.Instance.DefaultConfigName)
                        ImGuiHelper.DrawIcon(FontAwesomeIcon.CheckCircle);

                    float textX = ImGui.GetCursorPosX();
                        
                    ImGui.SetCursorPosX(selectableX);
                    ImGui.SetItemAllowOverlap();
                    if (ImGui.Selectable($"###{key}ConfigSelectable", key == ConfigurationMain.Instance.ActiveProfileName))
                        ConfigurationMain.Instance.SetProfile(key);
                    ImGui.SameLine(textX);
                    ImGui.Text(key);

                    ProfileData? profile = ConfigurationMain.Instance.GetProfile(key);
                    if(profile?.CIDs.Count != 0)
                    {
                        ImGui.SameLine();
                        ImGuiEx.TextWrapped(ImGuiHelper.VersionColor, string.Join(", ", 
                                                                                  profile!.CIDs.Select(cid => ConfigurationMain.Instance.charByCID.TryGetValue(cid, out ConfigurationMain.CharData cd) ? 
                                                                                                                  cd.GetName() : 
                                                                                                                  cid.ToString())));
                    }
                }
        }

        ImGui.PopItemWidth();
        ImGui.SameLine();

        if (ImGui.IsPopupOpen("##RenameProfile"))
        {
            bool    open     = true;
            Vector2 textSize = ImGui.CalcTextSize(profileRenameInput);
            ImGui.SetNextWindowSize(new Vector2(textSize.X + 200, textSize.Y + 120) * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginPopupModal($"##RenameProfile", ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove))
            {
                ImGuiHelper.CenterNextElement(ImGui.CalcTextSize(Loc.Get("ConfigTab.Profile.NewProfileName")).X);
                ImGui.Text(Loc.Get("ConfigTab.Profile.NewProfileName"));
                ImGui.NewLine();
                ImGui.SameLine(50);
                ImGui.SetNextItemWidth((textSize.X + 100) * ImGuiHelpers.GlobalScale);

                ImGui.InputText("##RenameProfileInput", ref profileRenameInput, 100);
                ImGui.Spacing();
                ImGuiHelper.CenterNextElement(ImGui.CalcTextSize(Loc.Get("ConfigTab.Profile.ChangeProfileName")).X);
                if (ImGui.Button(Loc.Get("ConfigTab.Profile.ChangeProfileName")))
                    if (ConfigurationMain.Instance.RenameCurrentProfile(profileRenameInput))
                    {
                        open = false;
                        ImGui.CloseCurrentPopup();
                    }

                ImGui.EndPopup();
            }
        }



        bool bareProfile = ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.CONFIGNAME_BARE;

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            ConfigurationMain.Instance.CreateNewProfile();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.CreateNew"));

        ImGui.SameLine(0, 15f);
        using (ImRaii.Disabled(bareProfile))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Pen))
            {
                profileRenameInput = ConfigurationMain.Instance.ActiveProfileName;
                ImGui.OpenPopup("##RenameProfile");
            }
        }

        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.Rename"));

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            ConfigurationMain.Instance.DuplicateCurrentProfile();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.Duplicate"));

        ImGui.SameLine();
        using (ImRaii.Disabled(ImGui.GetIO().KeyCtrl ? 
                                   ConfigurationMain.Instance.GetCurrentProfile.CIDs.Contains(Player.CID) != ImGui.GetIO().KeyShift : 
                                   ConfigurationMain.Instance.DefaultConfigName == ConfigurationMain.Instance.ActiveProfileName))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.CheckCircle))
                if (ImGui.GetIO().KeyCtrl)
                    if (ImGui.GetIO().KeyShift)
                        ConfigurationMain.Instance.RemoveCharacterDefault();
                    else
                        ConfigurationMain.Instance.SetCharacterDefault();
                else
                    ConfigurationMain.Instance.SetProfileAsDefault();
        }

        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.MakeDefaultHelp"));


        ImGui.SameLine();
        using (ImRaii.Disabled(bareProfile || !ImGui.GetIO().KeyCtrl))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
                ConfigurationMain.Instance.RemoveCurrentProfile();
        }

        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip(Loc.Get("ConfigTab.Profile.DeleteHelp"));

        if (bareProfile)
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Profile.BareProfileNote"));
        using ImRaii.DisabledDisposable _ = ImRaii.Disabled(bareProfile);

        //Start of Window & Overlay Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool overlayHeader = ImGui.Selectable(Loc.Get("ConfigTab.Overlay.Header"), overlayHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();      
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (overlayHeader)
            overlayHeaderSelected = !overlayHeaderSelected;

        if (overlayHeaderSelected == true)
        {
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowOverlay"), ref Configuration.showOverlay))
            {
                Configuration.ShowOverlay = Configuration.showOverlay;
                Configuration.Save();
            }
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Overlay.ShowOverlayHelp"));
            if (Configuration.ShowOverlay)
            {
                ImGui.Indent();
                ImGui.Columns(2, "##OverlayColumns", false);

                //ImGui.SameLine(0, 53);
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.HideWhenStopped"), ref Configuration.hideOverlayWhenStopped))
                {
                    Configuration.HideOverlayWhenStopped = Configuration.hideOverlayWhenStopped;
                    Configuration.Save();
                }
                ImGui.NextColumn();
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.LockOverlay"), ref Configuration.lockOverlay))
                {
                    Configuration.LockOverlay = Configuration.lockOverlay;
                    Configuration.Save();
                }
                ImGui.NextColumn();
                //ImGui.SameLine(0, 57);

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowDutyLoopText"), ref Configuration.ShowDutyLoopText))
                    Configuration.Save();
                ImGui.NextColumn();
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.TransparentBG"), ref Configuration.overlayNoBG))
                {
                    Configuration.OverlayNoBG = Configuration.overlayNoBG;
                    Configuration.Save();
                }
                ImGui.NextColumn();
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowActionText"), ref Configuration.ShowActionText))
                    Configuration.Save();
                ImGui.NextColumn();
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.AnchorBottom"), ref Configuration.OverlayAnchorBottom))
                    Configuration.Save();
                ImGui.NextColumn();
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.OverrideButtons"), ref Configuration.OverrideOverlayButtons))
                    Configuration.Save();
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Overlay.OverrideButtonsHelp"));
                if (Configuration.OverrideOverlayButtons)
                {
                    ImGui.Indent();
                    ImGui.Columns(3, "##OverlayButtonColumns", false);
                    if (ImGui.Checkbox(Loc.Get("Overlay.Button.Goto"), ref Configuration.GotoButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox(Loc.Get("Overlay.Button.TurnIn"), ref Configuration.TurninButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox(Loc.Get("Overlay.Button.Desynth"), ref Configuration.DesynthButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox(Loc.Get("Overlay.Button.Extract"), ref Configuration.ExtractButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox(Loc.Get("Overlay.Button.Repair"), ref Configuration.RepairButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox(Loc.Get("Overlay.Button.Equip"), ref Configuration.EquipButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox(Loc.Get("Overlay.Button.Coffers"), ref Configuration.CofferButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox($"{Loc.Get("Overlay.Button.TripleTriad")}##TTButton", ref Configuration.TTButton))
                        Configuration.Save();
                    ImGui.Unindent();
                }
                ImGui.Unindent();
            }
            ImGui.Columns(1);
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.ShowMainWindowOnStartup"), ref Configuration.ShowMainWindowOnStartup))
                Configuration.Save();
            ImGui.SameLine();
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Overlay.SliderInputs"), ref Configuration.UseSliderInputs))
                Configuration.Save();
            
        }

        if (Plugin.isDev)
        {
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            bool devHeader = ImGui.Selectable("Dev###DevHeader", devHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (devHeader)
                devHeaderSelected = !devHeaderSelected;

            if (devHeaderSelected)
            {
                if (ImGui.Checkbox("Update Paths On Startup##DevUpdatePathsOnStartup", ref ConfigurationMain.Instance.updatePathsOnStartup))
                    Configuration.Save();

                if (ImGui.Button("Print Mod List##DevPrintModList")) 
                    Svc.Log.Info(string.Join("\n", PluginInterface.InstalledPlugins.Where(pl => pl.IsLoaded).GroupBy(pl => pl.Manifest.InstalledFromUrl).OrderByDescending(g => g.Count()).Select(g => g.Key+"\n\t"+string.Join("\n\t", g.Select(pl => pl.Name)))));

                if (ImGui.Button("Rotation On##DevRotationOn")) 
                    Plugin.SetRotationPluginSettings(true, ignoreConfig: true, ignoreTimer: true);

                ImGui.SameLine();
                if (ImGui.Button("Rotation Off##DevRotationOff"))
                {
                    Plugin.SetRotationPluginSettings(false, ignoreConfig: true, ignoreTimer: true);
                    if(Wrath_IPCSubscriber.IsEnabled)
                        Wrath_IPCSubscriber.Release();
                }

                if (ImGui.Button("Between Loop Actions##DevBetweenLoops"))
                {
                    Plugin.CurrentTerritoryContent =  ContentHelper.DictionaryContent.Values.First();
                    Plugin.States                  |= PluginState.Other;
                    Plugin.LoopTasks(false);
                }

                if (ImGui.Button("Boss Loot Test##DevBossLootTest"))
                {
                    IEnumerable<IGameObject> treasures = ObjectHelper.GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)?.
                                                                      Where(x => ObjectHelper.BelowDistanceToPoint(x.Position, Player.Position, 50, 10)) ?? [];
                    Svc.Log.Debug(treasures.Count() + "\n" + string.Join("\n", treasures.Select(igo => igo.Position.ToString())));
                }

                unsafe
                {
                    ImGui.Text($"In Area: " + GotoHousingHelper.InHousingArea(Housing.FC_Estate));
                    ImGui.Text($"Indoors: " + GotoHousingHelper.InPrivateHouse(Housing.FC_Estate));
                }

                if (ImGui.CollapsingHeader("Sheet Check"))
                {
                    ImGuiEx.Text($"{typeof(Achievement).Assembly.GetTypes().Where(x => x.FullName?.StartsWith("Lumina.Excel.Sheets") ?? false).
                                                        Select(x => (x, x.GetProperties().Where(f => f.PropertyType.Name == "RowRef`1" && f.PropertyType.GenericTypeArguments[0] == typeof(Tutorial)))).
                                                        Where(x => x.Item2.Any()).
                                                        Select(x => $"{x.x} references {x.Item2.Select(pi => pi.Name).Print(", ")}").Print("\n")}");
                }
            }
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool dutyConfigHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.Header"), dutyConfigHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (dutyConfigHeader)
            dutyConfigHeaderSelected = !dutyConfigHeaderSelected;

        if (dutyConfigHeaderSelected == true)
        {
            ImGui.Columns(2, "##DutyConfigHeaderColumns");
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoLeaveDuty"), ref Configuration.AutoExitDuty))
                Configuration.Save();
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoLeaveDutyHelp"));
            ImGui.NextColumn();
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BlockLeavingDuty"), ref Configuration.OnlyExitWhenDutyDone))
                Configuration.Save();
            //ImGuiComponents.HelpMarker("Blocks leaving dungeon before duty is completed");
            ImGui.Columns(1);
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoManageRotation"), ref Configuration.AutoManageRotationPluginState))
                Configuration.Save();
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoManageRotationHelp"));

            using (ImRaii.Disabled(!Configuration.AutoManageRotationPluginState))
            {
                ImGui.SameLine(0, 5);
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 * 2);
                if (ImGui.BeginCombo("##RotationPluginSelection", Configuration.rotationPlugin.ToCustomString()))
                {
                    foreach (RotationPlugin rotationPlugin in Enum.GetValues(typeof(RotationPlugin)).Cast<RotationPlugin>().Reverse())
                        using (rotationPlugin.HasFlag(RotationPlugin.All) ? _ : ImGuiHelper.RequiresPlugin(rotationPlugin switch
                               {
                                   RotationPlugin.BossMod => ExternalPlugin.BossMod,
                                   RotationPlugin.RotationSolverReborn => ExternalPlugin.RotationSolverReborn,
                                   RotationPlugin.WrathCombo => ExternalPlugin.WrathCombo,
                                   _ => throw new ArgumentOutOfRangeException()
                               }, "RotationPluginSelection", inline: true))
                        {
                            if (ImGui.Selectable(rotationPlugin.ToCustomString(), Configuration.rotationPlugin == rotationPlugin, ImGuiSelectableFlags.AllowItemOverlap))
                            {
                                Configuration.rotationPlugin = rotationPlugin;
                                Configuration.Save();
                            }
                        }

                    ImGui.EndCombo();
                }
            }


            if (Configuration.AutoManageRotationPluginState)
            {
                if (Configuration.rotationPlugin is RotationPlugin.WrathCombo or RotationPlugin.All && Wrath_IPCSubscriber.IsEnabled)
                    using (ImGuiHelper.RequiresPlugin(ExternalPlugin.WrathCombo, "WrathConfig", write: false))
                    {
                        ImGui.Indent();
                        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                        bool wrathSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.Wrath.Header"), wrathSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                        ImGui.PopStyleVar();
                        if (ImGui.IsItemHovered())
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (wrathSettingHeader)
                            wrathSettingHeaderSelected = !wrathSettingHeaderSelected;

                        if (wrathSettingHeaderSelected)
                        {
                            bool wrath_AutoSetupJobs = Configuration.Wrath_AutoSetupJobs;
                            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Wrath.AutoSetupJobs"), ref wrath_AutoSetupJobs))
                            {
                                Configuration.Wrath_AutoSetupJobs = wrath_AutoSetupJobs;
                                Configuration.Save();
                            }

                            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Wrath.AutoSetupJobsHelp"));

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.Wrath.TargetingTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigWrathTargetingTank", Configuration.Wrath_TargetingTank.ToCustomString()))
                            {
                                foreach (WrathCombo.API.Enum.DPSRotationMode targeting in Enum.GetValues<WrathCombo.API.Enum.DPSRotationMode>())
                                {
                                    if (targeting == WrathCombo.API.Enum.DPSRotationMode.Tank_Target)
                                        continue;

                                    if (ImGui.Selectable(targeting.ToCustomString(), Configuration.Wrath_TargetingTank == targeting))
                                    {
                                        Configuration.Wrath_TargetingTank = targeting;
                                        Configuration.Save();
                                    }
                                }

                                ImGui.EndCombo();
                            }

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.Wrath.TargetingNonTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigWrathTargetingNonTank", Configuration.Wrath_TargetingNonTank.ToCustomString()))
                            {
                                foreach (WrathCombo.API.Enum.DPSRotationMode targeting in Enum.GetValues<WrathCombo.API.Enum.DPSRotationMode>())
                                    if (ImGui.Selectable(targeting.ToCustomString(), Configuration.Wrath_TargetingNonTank == targeting))
                                    {
                                        Configuration.Wrath_TargetingNonTank = targeting;
                                        Configuration.Save();
                                    }

                                ImGui.EndCombo();
                            }

                            ImGui.Separator();
                        }

                        ImGui.Unindent();
                    }

                if (Configuration.rotationPlugin is RotationPlugin.RotationSolverReborn or RotationPlugin.All && RSR_IPCSubscriber.IsEnabled)
                    using (ImGuiHelper.RequiresPlugin(ExternalPlugin.RotationSolverReborn, "RSRConfig", write: false))
                    {
                        ImGui.Indent();
                        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                        bool rsrSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.RSR.Header"), rsrSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                        ImGui.PopStyleVar();
                        if (ImGui.IsItemHovered())
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (rsrSettingHeader)
                            rsrSettingHeaderSelected = !rsrSettingHeaderSelected;

                        if (rsrSettingHeaderSelected)
                        {
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.RSR.EngageSettings"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigRSREngage", RSR_IPCSubscriber.GetHostileTypeDescription(Configuration.RSR_TargetHostileType)))
                            {
                                foreach (RotationSolverRebornIPC.TargetHostileType hostileType in Enum.GetValues<RotationSolverRebornIPC.TargetHostileType>())
                                    if (ImGui.Selectable(RSR_IPCSubscriber.GetHostileTypeDescription(hostileType), hostileType == Configuration.RSR_TargetHostileType))
                                    {
                                        Configuration.RSR_TargetHostileType = hostileType;
                                        Configuration.Save();
                                    }

                                ImGui.EndCombo();
                            }


                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.RSR.TargetingTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigRSRTargetTank", Configuration.RSR_TargetingTypeTank.ToCustomString()))
                            {
                                foreach (RotationSolverRebornIPC.TargetingType targetingType in Enum.GetValues<RotationSolverRebornIPC.TargetingType>())
                                    if (ImGui.Selectable(targetingType.ToCustomString(), targetingType == Configuration.RSR_TargetingTypeTank))
                                    {
                                        Configuration.RSR_TargetingTypeTank = targetingType;
                                        Configuration.Save();
                                    }

                                ImGui.EndCombo();
                            }

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("ConfigTab.Duty.RSR.TargetingNonTank"));
                            ImGui.SameLine(0, 5);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * ImGuiHelpers.GlobalScale);
                            if (ImGui.BeginCombo("##ConfigRSRTargetNonTank", Configuration.RSR_TargetingTypeNonTank.ToCustomString()))
                            {
                                foreach (RotationSolverRebornIPC.TargetingType targetingType in Enum.GetValues<RotationSolverRebornIPC.TargetingType>())
                                    if (ImGui.Selectable(targetingType.ToCustomString(), targetingType == Configuration.RSR_TargetingTypeNonTank))
                                    {
                                        Configuration.RSR_TargetingTypeNonTank = targetingType;
                                        Configuration.Save();
                                    }

                                ImGui.EndCombo();
                            }

                            ImGui.Separator();
                        }

                        ImGui.Unindent();
                    }
            }

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoManageBMAI"), ref Configuration.autoManageBossModAISettings))
                Configuration.Save();
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoManageBMAIHelp"));

            if (Configuration.autoManageBossModAISettings)
            {
                ImGui.Indent();
                ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                bool bmaiSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.BMAI.Header"), bmaiSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                ImGui.PopStyleVar();
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (bmaiSettingHeader)
                    bmaiSettingHeaderSelected = !bmaiSettingHeaderSelected;
            
                if (bmaiSettingHeaderSelected == true)
                {
                    if (ImGui.Button(Loc.Get("ConfigTab.Duty.BMAI.UpdatePresets")))
                    {
                        BossMod_IPCSubscriber.RefreshPreset("AutoDuty", Resources.AutoDutyPreset);
                        BossMod_IPCSubscriber.RefreshPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
                    }
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BMAI.UpdatePresetsAuto"), ref Configuration.BM_UpdatePresetsAutomatically))
                        Configuration.Save();
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceRoleBased"), ref Configuration.maxDistanceToTargetRoleBased))
                    {
                        Configuration.MaxDistanceToTargetRoleBased = Configuration.maxDistanceToTargetRoleBased;
                        Configuration.Save();
                    }
                    using (ImRaii.Disabled(Configuration.MaxDistanceToTargetRoleBased))
                    {
                        ImGui.PushItemWidth(195 * ImGuiHelpers.GlobalScale);
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistance"), ref Configuration.MaxDistanceToTargetFloat, 1, 30))
                        {
                            Configuration.MaxDistanceToTargetFloat = Math.Clamp(Configuration.MaxDistanceToTargetFloat, 1, 30);
                            Configuration.Save();
                        }
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceAoE"), ref Configuration.MaxDistanceToTargetAoEFloat, 1, 10))
                        {
                            Configuration.MaxDistanceToTargetAoEFloat = Math.Clamp(Configuration.MaxDistanceToTargetAoEFloat, 1, 10);
                            Configuration.Save();
                        }
                        ImGui.PopItemWidth();
                    }
                    using (ImRaii.Disabled(!Configuration.MaxDistanceToTargetRoleBased))
                    {
                        ImGui.PushItemWidth(195 * ImGuiHelpers.GlobalScale);
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceMelee"), ref Configuration.MaxDistanceToTargetRoleMelee, 1, 30))
                        {
                            Configuration.MaxDistanceToTargetRoleMelee = Math.Clamp(Configuration.MaxDistanceToTargetRoleMelee, 1, 30);
                            Configuration.Save();
                        }
                        if (ImGui.SliderFloat(Loc.Get("ConfigTab.Duty.BMAI.MaxDistanceRanged"), ref Configuration.MaxDistanceToTargetRoleRanged, 1, 30))
                        {
                            Configuration.MaxDistanceToTargetRoleRanged = Math.Clamp(Configuration.MaxDistanceToTargetRoleRanged, 1, 30);
                            Configuration.Save();
                        }
                        ImGui.PopItemWidth();
                    }
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.BMAI.PositionalRoleBased"), ref Configuration.positionalRoleBased))
                    {
                        Configuration.PositionalRoleBased = Configuration.positionalRoleBased;
                        BMRoleChecks();
                        Configuration.Save();
                    }
                    using (ImRaii.Disabled(Configuration.positionalRoleBased))
                    {
                        ImGui.SameLine(0, 10);
                        if (ImGui.Button(Configuration.PositionalEnum.ToLocalizedString()))
                            ImGui.OpenPopup("PositionalPopup");
            
                        if (ImGui.BeginPopup("PositionalPopup"))
                        {
                            foreach (Positional positional in Enum.GetValues(typeof(Positional)))
                                if (ImGui.Selectable(positional.ToLocalizedString(), Configuration.PositionalEnum == positional))
                                {
                                    Configuration.PositionalEnum = positional;
                                    Configuration.Save();
                                }

                            ImGui.EndPopup();
                        }
                    }
                    if (ImGui.Button(Loc.Get("ConfigTab.Duty.BMAI.UseDefaultSettings")))
                    {
                        Configuration.maxDistanceToTargetRoleBased = true;
                        Configuration.positionalRoleBased = true;
                        Configuration.Save();
                    }
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.BMAI.UseDefaultSettingsHelp"));

                    ImGui.Separator();
                }
                ImGui.Unindent();
            }
            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.AutoManageVnavCamera"), ref Configuration.AutoManageVnavAlignCamera))
                Configuration.Save();
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.AutoManageVnavCameraHelp"));

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.LootTreasure"), ref Configuration.LootTreasure))
                Configuration.Save();

            if (Configuration.LootTreasure)
            {
                ImGui.Indent();
                ImGui.Text(Loc.Get("ConfigTab.Duty.SelectMethod"));
                ImGui.SameLine(0, 5);
                ImGui.PushItemWidth(200 * ImGuiHelpers.GlobalScale);
                if (ImGui.BeginCombo("##ConfigLootMethod", Configuration.LootMethodEnum.ToCustomString()))
                {
                    foreach (LootMethod lootMethod in Enum.GetValues(typeof(LootMethod)))
                        using (lootMethod == LootMethod.Pandora ? ImGuiHelper.RequiresPlugin(ExternalPlugin.Pandora, $"{lootMethod}_Looting", inline: true) : _)
                        {
                            if (ImGui.Selectable(lootMethod.ToCustomString(), Configuration.LootMethodEnum == lootMethod))
                            {
                                Configuration.LootMethodEnum = lootMethod;
                                Configuration.Save();
                            }
                        }

                    ImGui.EndCombo();
                }
                
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.LootBossTreasureOnly"), ref Configuration.LootBossTreasureOnly))
                    Configuration.Save();

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.LootBossTreasureOnlyHelp"));
                ImGui.PopItemWidth();
                ImGui.Unindent();
            }
            ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt(Loc.Get("ConfigTab.Duty.MinStuckTime"), ref Configuration.MinStuckTime, 10, 100))
            {
                Configuration.MinStuckTime = Math.Max(250, Configuration.MinStuckTime);
                Configuration.Save();
            }
            ImGui.Indent();

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.ResetStuckOnStep"), ref Configuration.StuckOnStep))
                Configuration.Save();

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.RebuildNavmeshOnStuck"), ref Configuration.RebuildNavmeshOnStuck))
                Configuration.Save();

            if (Configuration.RebuildNavmeshOnStuck)
            {
                ImGui.SameLine();
                int rebuildX = Configuration.RebuildNavmeshAfterStuckXTimes;
                if(ImGui.InputInt(Loc.Get("ConfigTab.Duty.RebuildNavmeshTimes") + "###RebuildTimesConfig", ref rebuildX, 1))
                {
                    Configuration.RebuildNavmeshAfterStuckXTimes = (byte) Math.Clamp(rebuildX, byte.MinValue + 2, byte.MaxValue);
                    Configuration.Save();
                }
            }

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.UseReturnWhenStuck") + "###UseReturnConfig", ref Configuration.stuckReturn))
                Configuration.Save();

            if (Configuration.StuckReturn)
            {
                ImGui.SameLine();
                int returnX = Configuration.StuckReturnX;
                if (ImGui.InputInt(Loc.Get("ConfigTab.Duty.ReturnTimes") + "###ReturnTimesConfig", ref returnX, 1))
                {
                    Configuration.StuckReturnX = (byte)Math.Clamp(returnX, byte.MinValue + 2, byte.MaxValue);
                    Configuration.Save();
                }
            }

            if (Configuration is { RebuildNavmeshOnStuck: true, stuckReturn: true } && 
                Configuration.RebuildNavmeshAfterStuckXTimes >= Configuration.StuckReturnX)
            {
                Configuration.StuckReturnX = Configuration.RebuildNavmeshAfterStuckXTimes + 1;
                Configuration.Save();
            }


            ImGui.Unindent();

            if(ImGui.Checkbox(Loc.Get("ConfigTab.Duty.DrawPath"), ref Configuration.PathDrawEnabled))
                Configuration.Save();
            ImGui.PopItemWidth();
            if (Configuration.PathDrawEnabled)
            {
                ImGui.Indent();
                ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt(Loc.Get("ConfigTab.Duty.DrawPathSteps"), ref Configuration.PathDrawStepCount, 1))
                {
                    Configuration.PathDrawStepCount = Math.Max(1, Configuration.PathDrawStepCount);
                    Configuration.Save();
                }
                ImGui.PopItemWidth();
                ImGui.Unindent();
            }

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.DisableRenderWhileActive"), ref Configuration.DisableRenderWhileActive))
            {
                if (!Configuration.DisableRenderWhileActive)
                    RenderDisableManager.RemoveRequest();
                else if (Plugin.States != PluginState.None)
                    RenderDisableManager.PlaceRequest();

                Configuration.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.DisableRenderWhileActiveHelp"));


            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            bool w2wSettingHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.W2W.Header"), w2wSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (w2wSettingHeader)
                w2wSettingHeaderSelected = !w2wSettingHeaderSelected;

            if (w2wSettingHeaderSelected)
            {
                if(ImGui.Checkbox(Loc.Get("ConfigTab.Duty.W2W.TreatUnsyncAsW2W"), ref Configuration.TreatUnsyncAsW2W))
                    Configuration.Save();
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.W2W.TreatUnsyncAsW2WHelp"));


                ImGui.BeginListBox("##W2WConfig", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 300));
                if(JobWithRoleHelper.DrawCategory(JobWithRole.All, ref Configuration.W2WJobs))
                    Configuration.Save();
                ImGui.EndListBox();
            }

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.OverridePartyValidation"), ref Configuration.OverridePartyValidation))
                Configuration.Save();
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.OverridePartyValidationHelp"));


            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            bool advModeHeader = ImGui.Selectable(Loc.Get("ConfigTab.Duty.Advanced.Header"), advModeHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (advModeHeader)
                advModeHeaderSelected = !advModeHeaderSelected;

            if (advModeHeaderSelected == true)
            {
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Advanced.UsingAltRotation"), ref Configuration.UsingAlternativeRotationPlugin))
                    Configuration.Save();
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Advanced.UsingAltRotationHelp"));

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Advanced.UsingAltMovement"), ref Configuration.UsingAlternativeMovementPlugin))
                    Configuration.Save();
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Advanced.UsingAltMovementHelp"));

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.Advanced.UsingAltBoss"), ref Configuration.UsingAlternativeBossPlugin))
                    Configuration.Save();
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Duty.Advanced.UsingAltBossHelp"));
            }
        }

        //Start of Pre-Loop Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool preLoopHeader = ImGui.Selectable(Loc.Get("ConfigTab.PreLoop.Header"), preLoopHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (preLoopHeader)
            preLoopHeaderSelected = !preLoopHeaderSelected;

        if (preLoopHeaderSelected == true)
        {
            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.PreLoop.Enable")}###PreLoopEnable", ref Configuration.EnablePreLoopActions))
                Configuration.Save();

            using (ImRaii.Disabled(!Configuration.EnablePreLoopActions))
            {
                ImGui.Separator();
                MakeCommands("ConfigTab.PreLoop.ExecuteCommands", ref Configuration.ExecuteCommandsPreLoop, ref Configuration.CustomCommandsPreLoop, ref preLoopCommand, "CommandsPreLoop");

                ImGui.Separator();

                ImGui.TextColored(ImGuiHelper.VersionColor,
                                  string.Format(Loc.Get("ConfigTab.PreLoop.BetweenLoopNote"), Configuration.EnableBetweenLoopActions ? Loc.Get("ConfigTab.PreLoop.Enable").ToLower() : "disabled"));

                if (ImGui.Checkbox(Loc.Get("ConfigTab.PreLoop.RetireTo"), ref Configuration.RetireMode))
                    Configuration.Save();

                using (ImRaii.Disabled(!Configuration.RetireMode))
                {
                    ImGui.SameLine(0, 5);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.BeginCombo("##RetireLocation", Configuration.RetireLocationEnum.ToLocalizedString()))
                    {
                        foreach (RetireLocation retireLocation in Enum.GetValues(typeof(RetireLocation)))
                            if (ImGui.Selectable(retireLocation.ToLocalizedString(), Configuration.RetireLocationEnum == retireLocation))
                            {
                                Configuration.RetireLocationEnum = retireLocation;
                                Configuration.Save();
                            }

                        ImGui.EndCombo();
                    }

                    if (Configuration is { RetireMode: true, RetireLocationEnum: RetireLocation.Personal_Home })
                    {
                        if (ImGui.Button(Loc.Get("ConfigTab.PreLoop.AddCurrentPosition")))
                        {
                            Configuration.PersonalHomeEntrancePath.Add(Player.Position);
                            Configuration.Save();
                        }

                        ImGuiComponents
                           .HelpMarker(Loc.Get("ConfigTab.PreLoop.HomePathHelp"));

                        using (ImRaii.ListBox("##PersonalHomeVector3List", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X,
                                                                                                       (ImGui.GetTextLineHeightWithSpacing() * Configuration.PersonalHomeEntrancePath.Count) + 5)))
                        {
                            bool removeItem = false;
                            int removeAt   = 0;

                            foreach ((Vector3 Value, int Index) item in Configuration.PersonalHomeEntrancePath.Select((Value, Index) => (Value, Index)))
                            {
                                ImGui.Selectable($"{item.Value}");
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    removeItem = true;
                                    removeAt   = item.Index;
                                }
                            }

                            if (removeItem)
                            {
                                Configuration.PersonalHomeEntrancePath.RemoveAt(removeAt);
                                Configuration.Save();
                            }
                        }
                    }

                    if (Configuration is { RetireMode: true, RetireLocationEnum: RetireLocation.FC_Estate })
                    {
                        if (ImGui.Button(Loc.Get("ConfigTab.PreLoop.AddCurrentPosition")))
                        {
                            Configuration.FCEstateEntrancePath.Add(Player.Position);
                            Configuration.Save();
                        }

                        ImGuiComponents
                           .HelpMarker(Loc.Get("ConfigTab.PreLoop.HomePathHelp"));

                        using (ImRaii.ListBox("##FCEstateVector3List", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.FCEstateEntrancePath.Count) + 5)))
                        {
                            bool removeItem = false;
                            int removeAt   = 0;

                            foreach ((Vector3 Value, int Index) item in Configuration.FCEstateEntrancePath.Select((value, index) => (Value: value, Index: index)))
                            {
                                ImGui.Selectable($"{item.Value}");
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    removeItem = true;
                                    removeAt   = item.Index;
                                }
                            }

                            if (removeItem)
                            {
                                Configuration.FCEstateEntrancePath.RemoveAt(removeAt);
                                Configuration.Save();
                            }
                        }
                    }
                }

                if (ImGui.Checkbox(Loc.Get("ConfigTab.PreLoop.AutoEquipGear"), ref Configuration.AutoEquipRecommendedGear))
                    Configuration.Save();

                using (ImRaii.Disabled(!Configuration.AutoEquipRecommendedGear))
                {
                    ImGui.SameLine(0, 5);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X/3*2);
                    if (ImGui.BeginCombo("##AutoEquipRecommendedSource", Configuration.AutoEquipRecommendedGearSource.ToCustomString()))
                    {
                        foreach (GearsetUpdateSource updateSource in Enum.GetValues(typeof(GearsetUpdateSource)))
                            using (updateSource == GearsetUpdateSource.Vanilla ? _ : ImGuiHelper.RequiresPlugin(updateSource == GearsetUpdateSource.Gearsetter ? ExternalPlugin.Gearsetter : ExternalPlugin.Stylist, "GearSet", inline: true))
                            {
                                if (ImGui.Selectable(updateSource.ToCustomString(), Configuration.AutoEquipRecommendedGearSource == updateSource, flags: ImGuiSelectableFlags.AllowItemOverlap))
                                {
                                    Configuration.AutoEquipRecommendedGearSource = updateSource;
                                    Configuration.Save();
                                }
                            }

                        ImGui.EndCombo();
                    }
                }

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.GearSourceHelp"));


                if (Configuration.AutoEquipRecommendedGear)
                {
                    ImGui.Indent();
                    if(Configuration.AutoEquipRecommendedGearSource == GearsetUpdateSource.Gearsetter)
                        using (ImRaii.Disabled(!Gearsetter_IPCSubscriber.IsEnabled))
                        {
                            ImGui.Indent();
                            if (ImGui.Checkbox(Loc.Get("ConfigTab.PreLoop.MoveOldToInventory"), ref Configuration.AutoEquipRecommendedGearGearsetterOldToInventory))
                                Configuration.Save();
                            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.MoveOldToInventoryHelp"));
                            ImGui.Unindent();
                        }

                    if (!Gearsetter_IPCSubscriber.IsEnabled && !Stylist_IPCSubscriber.IsEnabled) ImGui.Text(Loc.Get("ConfigTab.PreLoop.RequiresGearsetterOrStylist"));


                    if (Configuration.AutoEquipRecommendedGearSource == GearsetUpdateSource.Gearsetter && !Gearsetter_IPCSubscriber.IsEnabled ||
                        Configuration.AutoEquipRecommendedGearSource == GearsetUpdateSource.Stylist    && !Stylist_IPCSubscriber.IsEnabled)
                    {

                        Configuration.AutoEquipRecommendedGearSource = GearsetUpdateSource.Vanilla;
                        Configuration.Save();
                    }


                    ImGui.Unindent();
                }

                if (ImGui.Checkbox(Loc.Get("ConfigTab.PreLoop.AutoRepair"), ref Configuration.AutoRepair))
                    Configuration.Save();

                if (Configuration.AutoRepair)
                {
                    ImGui.SameLine();

                    if (ImGui.RadioButton(Loc.Get("ConfigTab.PreLoop.Self"), Configuration.AutoRepairSelf))
                    {
                        Configuration.AutoRepairSelf = true;
                        Configuration.Save();
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.SelfHelp"));
                    ImGui.SameLine();

                    if (ImGui.RadioButton(Loc.Get("ConfigTab.PreLoop.CityNpc"), !Configuration.AutoRepairSelf))
                    {
                        Configuration.AutoRepairSelf = false;
                        Configuration.Save();
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.CityNpcHelp"));
                    ImGui.Indent();
                    ImGui.Text(Loc.Get("ConfigTab.PreLoop.TriggerAt"));
                    ImGui.SameLine();
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    int autoRepairPct = (int)Configuration.AutoRepairPct;
                    if (ImGui.SliderInt("##Repair@", ref autoRepairPct, 0, 99, "%d%%"))
                    {
                        Configuration.AutoRepairPct = Math.Clamp((uint)autoRepairPct, 0, 99);
                        Configuration.Save();
                    }

                    ImGui.PopItemWidth();
                    if (!Configuration.AutoRepairSelf)
                    {
                        ImGui.Text(Loc.Get("ConfigTab.PreLoop.PreferredRepairNPC"));
                        ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.PreferredRepairNPCHelp"));
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.BeginCombo("##PreferredRepair",
                                             Configuration.PreferredRepairNPC != null ?
                                                 $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Configuration.PreferredRepairNPC.Name.ToLowerInvariant())} ({Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(Configuration.PreferredRepairNPC.TerritoryType)?.PlaceName.ValueNullable?.Name.ToString()})  ({MapHelper.ConvertWorldXZToMap(Configuration.PreferredRepairNPC.Position.ToVector2(), Svc.Data.GetExcelSheet<TerritoryType>().GetRow(Configuration.PreferredRepairNPC.TerritoryType).Map.Value!).X.ToString("0.0", CultureInfo.InvariantCulture)}, {MapHelper.ConvertWorldXZToMap(Configuration.PreferredRepairNPC.Position.ToVector2(), Svc.Data.GetExcelSheet<TerritoryType>().GetRow(Configuration.PreferredRepairNPC.TerritoryType).Map.Value).Y.ToString("0.0", CultureInfo.InvariantCulture)})" :
                                                 Loc.Get("ConfigTab.PreLoop.GrandCompanyInn")))
                        {
                            if (ImGui.Selectable(Loc.Get("ConfigTab.PreLoop.GrandCompanyInn"), Configuration.PreferredRepairNPC == null))
                            {
                                Configuration.PreferredRepairNPC = null;
                                Configuration.Save();
                            }

                            foreach (RepairNpcData repairNPC in RepairNPCs)
                            {
                                if (repairNPC.TerritoryType <= 0)
                                {
                                    ImGui.Text(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(repairNPC.Name.ToLowerInvariant()));
                                    continue;
                                }

                                TerritoryType? territoryType = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(repairNPC.TerritoryType);

                                if (territoryType == null) continue;

                                if (ImGui.Selectable($"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(repairNPC.Name.ToLowerInvariant())} ({territoryType.Value.PlaceName.ValueNullable?.Name.ToString()})  ({MapHelper.ConvertWorldXZToMap(repairNPC.Position.ToVector2(), territoryType.Value.Map.Value!).X.ToString("0.0", CultureInfo.InvariantCulture)}, {MapHelper.ConvertWorldXZToMap(repairNPC.Position.ToVector2(), territoryType.Value.Map.Value!).Y.ToString("0.0", CultureInfo.InvariantCulture)})", Configuration.PreferredRepairNPC == repairNPC))
                                {
                                    Configuration.PreferredRepairNPC = repairNPC;
                                    Configuration.Save();
                                }
                            }

                            ImGui.EndCombo();
                        }

                        ImGui.PopItemWidth();
                    }

                    ImGui.Unindent();
                }

                if (ImGui.Checkbox(Loc.Get("ConfigTab.PreLoop.AutoConsume"), ref Configuration.AutoConsume))
                    Configuration.Save();

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.AutoConsumeHelp"));
                if (Configuration.AutoConsume)
                {
                    ImGui.SameLine();
                    ImGui.Columns(3, "##AutoConsumeColumns");
                    //ImGui.SameLine(0, 5);
                    ImGui.NextColumn();
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.PreLoop.IgnoreStatus"), ref Configuration.AutoConsumeIgnoreStatus))
                        Configuration.Save();

                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.IgnoreStatusHelp"));
                    ImGui.NextColumn();
                    //ImGui.SameLine(0, 5);

                    ImGui.PushItemWidth(80 * ImGuiHelpers.GlobalScale);

                    using (ImRaii.Disabled(Configuration.AutoConsumeIgnoreStatus))
                    {
                        if (ImGui.InputInt(Loc.Get("ConfigTab.PreLoop.MinTimeRemaining"), ref Configuration.AutoConsumeTime, 1))
                        {
                            Configuration.AutoConsumeTime = Math.Clamp(Configuration.AutoConsumeTime, 0, 59);
                            Configuration.Save();
                        }

                        ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.PreLoop.MinTimeRemainingHelp"));
                    }

                    ImGui.PopItemWidth();
                    ImGui.Columns(1);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 115 * ImGuiHelpers.GlobalScale);
                    if (ImGui.BeginCombo("##SelectAutoConsumeItem", consumableItemsSelectedItem.Name))
                    {
                        ImGui.InputTextWithHint(Loc.Get("ConfigTab.PreLoop.ItemName"), Loc.Get("ConfigTab.PreLoop.ItemNameHint"), ref consumableItemsItemNameInput, 1000);
                        foreach (ConsumableItem? item in ConsumableItems.Where(x => x.Name.Contains(consumableItemsItemNameInput, StringComparison.InvariantCultureIgnoreCase))!)
                            if (ImGui.Selectable($"{item.Name}"))
                                consumableItemsSelectedItem = item;

                        ImGui.EndCombo();
                    }

                    ImGui.PopItemWidth();

                    ImGui.SameLine(0, 5);
                    using (ImRaii.Disabled(consumableItemsSelectedItem == null))
                    {
                        if (ImGui.Button(Loc.Get("ConfigTab.PreLoop.AddItem")))
                        {
                            if (Configuration.AutoConsumeItemsList.Any(x => x.Key == consumableItemsSelectedItem!.StatusId))
                                Configuration.AutoConsumeItemsList.RemoveAll(x => x.Key == consumableItemsSelectedItem!.StatusId);

                            Configuration.AutoConsumeItemsList.Add(new KeyValuePair<ushort, ConsumableItem>(consumableItemsSelectedItem!.StatusId, consumableItemsSelectedItem));
                            Configuration.Save();
                        }
                    }

                    using (ImRaii.ListBox("##ConsumableItemList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X,
                                                                                              (ImGui.GetTextLineHeightWithSpacing() * Configuration.AutoConsumeItemsList.Count) + 5)))
                    {
                        bool                                  boolRemoveItem = false;
                        KeyValuePair<ushort, ConsumableItem> removeItem     = new();
                        foreach (KeyValuePair<ushort, ConsumableItem> item in Configuration.AutoConsumeItemsList)
                        {
                            ImGui.Selectable($"{item.Value.Name}");
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                boolRemoveItem = true;
                                removeItem     = item;
                            }
                        }

                        if (boolRemoveItem)
                        {
                            Configuration.AutoConsumeItemsList.Remove(removeItem);
                            Configuration.Save();
                        }
                    }
                }
            }
        }

        //Between Loop Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool betweenLoopHeader = ImGui.Selectable(Loc.Get("ConfigTab.BetweenLoop.Header"), betweenLoopHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (betweenLoopHeader)
            betweenLoopHeaderSelected = !betweenLoopHeaderSelected;

        if (betweenLoopHeaderSelected == true)
        {
            ImGui.Columns(2, "##BetweenLoopHeaderColumns");

            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.BetweenLoop.Enable")}###BetweenLoopEnable", ref Configuration.EnableBetweenLoopActions))
                Configuration.Save();

            using (ImRaii.Disabled(!Configuration.EnableBetweenLoopActions))
            {
                ImGui.NextColumn();

                if (ImGui.Checkbox($"{Loc.Get("ConfigTab.BetweenLoop.RunOnLastLoop")}###BetweenLoopEnableLastLoop", ref Configuration.ExecuteBetweenLoopActionLastLoop))
                    Configuration.Save();

                ImGui.Columns(1);

                ImGui.Separator();
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcItemWidth());
                if (ImGui.InputInt(Loc.Get("ConfigTab.BetweenLoop.WaitTime"), ref Configuration.WaitTimeBeforeAfterLoopActions, 10, 100))
                {
                    if (Configuration.WaitTimeBeforeAfterLoopActions < 0) Configuration.WaitTimeBeforeAfterLoopActions = 0;
                    Configuration.Save();
                }
                ImGui.PopItemWidth();
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.BetweenLoop.WaitTimeHelp"));
                ImGui.Separator();

                MakeCommands("ConfigTab.BetweenLoop.ExecuteCommands", ref Configuration.ExecuteCommandsBetweenLoop, ref Configuration.CustomCommandsBetweenLoop, ref betweenLoopCommand, "CommandsBetweenLoop");

                if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.AutoExtract"), ref Configuration.AutoExtract))
                    Configuration.Save();

                if (Configuration.AutoExtract)
                {
                    ImGui.SameLine(0, 10);
                    if (ImGui.RadioButton(Loc.Get("ConfigTab.BetweenLoop.Equipped"), !Configuration.autoExtractAll))
                    {
                        Configuration.AutoExtractAll = false;
                        Configuration.Save();
                    }
                    ImGui.SameLine(0, 5);
                    if (ImGui.RadioButton(Loc.Get("ConfigTab.BetweenLoop.All"), Configuration.autoExtractAll))
                    {
                        Configuration.AutoExtractAll = true;
                        Configuration.Save();
                    }
                }

                if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.AutoOpenCoffers"), ref Configuration.AutoOpenCoffers))
                    Configuration.Save();

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.BetweenLoop.AutoOpenCoffersHelp"));
                if (Configuration.AutoOpenCoffers)
                    unsafe
                    {
                        ImGui.Indent();
                        ImGui.Text(Loc.Get("ConfigTab.BetweenLoop.OpenCoffersWithGearset"));
                        ImGui.AlignTextToFramePadding();
                        ImGui.SameLine();

                        RaptureGearsetModule* module = RaptureGearsetModule.Instance();
                        
                        if (Configuration.AutoOpenCoffersGearset != null && !module->IsValidGearset((int) Configuration.AutoOpenCoffersGearset))
                        {
                            Configuration.AutoOpenCoffersGearset = null;
                            Configuration.Save();
                        }


                        if (ImGui.BeginCombo("##CofferGearsetSelection", Configuration.AutoOpenCoffersGearset != null ? module->GetGearset(Configuration.AutoOpenCoffersGearset.Value)->NameString : Loc.Get("ConfigTab.BetweenLoop.CurrentGearset")))
                        {
                            if (ImGui.Selectable(Loc.Get("ConfigTab.BetweenLoop.CurrentGearset"), Configuration.AutoOpenCoffersGearset == null))
                            {
                                Configuration.AutoOpenCoffersGearset = null;
                                Configuration.Save();
                            }

                            foreach (RaptureGearsetModule.GearsetEntry gearsetEntry in module->Entries)
                            {
                                if (module->IsValidGearset(gearsetEntry.Id) && ImGui.Selectable($"{gearsetEntry.Id+1}: {gearsetEntry.NameString}", Configuration.AutoOpenCoffersGearset == gearsetEntry.Id))
                                {
                                    Configuration.AutoOpenCoffersGearset = gearsetEntry.Id;
                                    Configuration.Save();
                                }
                            }

                            ImGui.EndCombo();
                        }

                        if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.UseBlacklist"), ref Configuration.AutoOpenCoffersBlacklistUse))
                            Configuration.Save();

                        ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.BetweenLoop.UseBlacklistHelp"));
                        if (Configuration.AutoOpenCoffersBlacklistUse)
                        {
                            if (ImGui.BeginCombo(Loc.Get("ConfigTab.BetweenLoop.SelectCoffer"), autoOpenCoffersSelectedItem.Value))
                            {
                                ImGui.InputTextWithHint(Loc.Get("ConfigTab.BetweenLoop.CofferName"), Loc.Get("ConfigTab.BetweenLoop.CofferNameHint"), ref autoOpenCoffersNameInput, 1000);
                                foreach (KeyValuePair<uint, Item> item in 
                                         Items.Where(x => CofferHelper.ValidCoffer(x.Value) && x.Value.Name.ToString().Contains(autoOpenCoffersNameInput, StringComparison.InvariantCultureIgnoreCase)))
                                    if (ImGui.Selectable($"{item.Value.Name.ToString()}"))
                                        autoOpenCoffersSelectedItem = new KeyValuePair<uint, string>(item.Key, item.Value.Name.ToString());
                                ImGui.EndCombo();
                            }

                            ImGui.SameLine(0, 5);
                            using (ImRaii.Disabled(autoOpenCoffersSelectedItem.Value.IsNullOrEmpty()))
                            {
                                if (ImGui.Button(Loc.Get("ConfigTab.BetweenLoop.AddCoffer")))
                                {
                                    if (!Configuration.AutoOpenCoffersBlacklist.TryAdd(autoOpenCoffersSelectedItem.Key, autoOpenCoffersSelectedItem.Value))
                                    {
                                        Configuration.AutoOpenCoffersBlacklist.Remove(autoOpenCoffersSelectedItem.Key);
                                        Configuration.AutoOpenCoffersBlacklist.Add(autoOpenCoffersSelectedItem.Key, autoOpenCoffersSelectedItem.Value);
                                    }
                                    autoOpenCoffersSelectedItem = new KeyValuePair<uint, string>(0, "");
                                    Configuration.Save();
                                }
                            }
                            
                            if (!ImGui.BeginListBox("##CofferBlackList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.AutoOpenCoffersBlacklist.Count) + 5))) 
                                return;

                            foreach (KeyValuePair<uint, string> item in Configuration.AutoOpenCoffersBlacklist)
                            {
                                ImGui.Selectable($"{item.Value}");
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    Configuration.AutoOpenCoffersBlacklist.Remove(item);
                                    Configuration.Save();
                                }
                            }
                            ImGui.EndListBox();
                        }
                        
                        ImGui.Unindent();
                    }

                using (ImGuiHelper.RequiresPlugin(ExternalPlugin.AutoRetainer, "DiscardConfig", inline: true))
                {
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.DiscardItems"), ref Configuration.DiscardItems))
                        Configuration.Save();
                }
                if (!AutoRetainer_IPCSubscriber.IsEnabled)
                    if (Configuration.DiscardItems)
                    {
                        Configuration.DiscardItems = false;
                        Configuration.Save();
                    }


                ImGui.Columns(2, "##DesynthColumns");
                float columnY = ImGui.GetCursorPosY();

                if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.AutoDesynth"), ref Configuration.autoDesynth))
                {
                    Configuration.AutoDesynth = Configuration.autoDesynth;
                    Configuration.Save();
                }
                if (Configuration.AutoDesynth)
                {
                    ImGui.Indent();
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.OnlySkillUps"), ref Configuration.autoDesynthSkillUp))
                    {
                        Configuration.AutoDesynthSkillUp = Configuration.autoDesynthSkillUp;
                        Configuration.Save();
                    }
                    if (Configuration.AutoDesynthSkillUp)
                    {
                        ImGui.Indent();
                        ImGui.Text(Loc.Get("ConfigTab.BetweenLoop.ItemLevelLimit"));
                        ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.BetweenLoop.ItemLevelLimitHelp"));
                        ImGui.SameLine();
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.SliderInt("##AutoDesynthSkillUpLimit", ref Configuration.AutoDesynthSkillUpLimit, 0, 50))
                        {
                            Configuration.AutoDesynthSkillUpLimit = Math.Clamp(Configuration.AutoDesynthSkillUpLimit, 0, 50);
                            Configuration.Save();
                        }
                        ImGui.PopItemWidth();
                        ImGui.Unindent();
                    }

                    if (ImGui.Checkbox($"{Loc.Get("ConfigTab.BetweenLoop.Desynth.NQOnly")}##Desynth{nameof(Configuration.AutoDesynthNQOnly)}", ref Configuration.AutoDesynthNQOnly))
                        Configuration.Save();

                    if (ImGui.Checkbox($"{Loc.Get("ConfigTab.BetweenLoop.ProtectGearsets")}##Desynth{nameof(Configuration.AutoDesynthNoGearset)}", ref Configuration.AutoDesynthNoGearset))
                        Configuration.Save();

                    if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.BetweenLoop.DesynthCategories")))
                    {
                        ImGui.Indent();
                        AgentSalvage.SalvageItemCategory[] values = Enum.GetValues<AgentSalvage.SalvageItemCategory>();
                        for (int index = 0; index < values.Length; index++)
                        {
                            bool   x            = Bitmask.IsBitSet(Configuration.AutoDesynthCategories, index);
                            string categoryName = values[index].ToLocalizedString();
                            if (ImGui.Checkbox(categoryName + $"##DesynthCategory{index}", ref x))
                                if (x)
                                    Bitmask.SetBit(ref Configuration.AutoDesynthCategories, index);
                                else
                                    Bitmask.ResetBit(ref Configuration.AutoDesynthCategories, index);
                        }
                        ImGui.Unindent();
                    }

                    ImGui.Unindent();
                }

                ImGui.NextColumn();
                ImGui.SetCursorPosY(columnY);
                //ImGui.SameLine(0, 5);
                using (ImGuiHelper.RequiresPlugin(ExternalPlugin.AutoRetainer, "GCTurnin"))
                {
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.AutoGCTurnin"), ref Configuration.autoGCTurnin))
                    {
                        Configuration.AutoGCTurnin = Configuration.autoGCTurnin;
                        Configuration.Save();
                    }
                    if (Configuration.AutoGCTurnin)
                    {
                        ImGui.Indent();
                        if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.InventorySlotsLeft"), ref Configuration.AutoGCTurninSlotsLeftBool))
                            Configuration.Save();
                        ImGui.SameLine(0);
                        using (ImRaii.Disabled(!Configuration.AutoGCTurninSlotsLeftBool))
                        {
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                            if (Configuration.UseSliderInputs)
                            {
                                if (ImGui.SliderInt("##Slots", ref Configuration.AutoGCTurninSlotsLeft, 0, 140))
                                {
                                    Configuration.AutoGCTurninSlotsLeft = Math.Clamp(Configuration.AutoGCTurninSlotsLeft, 0, 140);
                                    Configuration.Save();
                                }
                            }
                            else
                            {
                                Configuration.AutoGCTurninSlotsLeft = Math.Clamp(Configuration.AutoGCTurninSlotsLeft, 0, 140);

                                if (ImGui.InputInt("##Slots", ref Configuration.AutoGCTurninSlotsLeft, 1))
                                {
                                    Configuration.AutoGCTurninSlotsLeft = Math.Clamp(Configuration.AutoGCTurninSlotsLeft, 0, 140);
                                    Configuration.Save();
                                }
                            }
                            ImGui.PopItemWidth();
                        }
                        if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.UseGCAetheryteTicket"), ref Configuration.AutoGCTurninUseTicket))
                            Configuration.Save();
                        ImGui.Unindent();
                    }
                }
                ImGui.Columns(1);

                if (!AutoRetainer_IPCSubscriber.IsEnabled)
                    if (Configuration.AutoGCTurnin)
                    {
                        Configuration.AutoGCTurnin = false;
                        Configuration.Save();
                    }

                ImGui.Columns(2, "TripleTriadColumns");
                if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.RegisterTripleTriadCards"), ref Configuration.TripleTriadRegister))
                    Configuration.Save();
                ImGui.NextColumn();
                if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.SellTripleTriadCards"), ref Configuration.TripleTriadSell))
                    Configuration.Save();

                if (Configuration.TripleTriadSell)
                {
                    ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);


                    ImGui.Text(Loc.Get("ConfigTab.BetweenLoop.SlotsOccupied"));
                    ImGui.SameLine();
                    float curX = ImGui.GetCursorPosX();
                    if (Configuration.UseSliderInputs  && ImGui.SliderInt("###TripleTriadSellingMinSlotSlider", ref Configuration.TripleTriadSellMinSlotCount, 1, 5) ||
                        !Configuration.UseSliderInputs && ImGui.InputInt("###TripleTriadSellingMinSlotInput", ref Configuration.TripleTriadSellMinSlotCount, step: 1, stepFast: 2))
                    {
                        Configuration.TripleTriadSellMinSlotCount = Math.Max(Configuration.TripleTriadSellMinSlotCount, 1);
                        Configuration.Save();
                    }

                    ImGui.Text(Loc.Get("ConfigTab.BetweenLoop.CardCount"));
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(curX);
                    if (Configuration.UseSliderInputs  && ImGui.SliderInt("###TripleTriadSellingMinItemSlider", ref Configuration.TripleTriadSellMinItemCount, 1, 99) ||
                        !Configuration.UseSliderInputs && ImGui.InputInt("###TripleTriadSellingMinItemInput", ref Configuration.TripleTriadSellMinItemCount, step: 1, stepFast: 10))
                    {
                        Configuration.TripleTriadSellMinItemCount = Math.Max(Configuration.TripleTriadSellMinItemCount, 1);
                        Configuration.Save();
                    }
                    ImGui.PopItemWidth();
                }

                ImGui.Columns(1);

                using (ImGuiHelper.RequiresPlugin(ExternalPlugin.GlamourLog, "EntrustGlamourLog"))
                {
                    ImGui.Columns(2);

                    if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.GlamourEntrust") + "##GlamourEntrust", ref Configuration.GlamourChestEntrust))
                        Configuration.Save();
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.BetweenLoop.GlamourEntrustHelp"));

                    ImGui.NextColumn();
                    float x = ImGui.GetCursorPosX() - 100f.Scale();

                    if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.ArmoireEntrust") + "##ArmoireEntrust", ref Configuration.ArmoireEntrust))
                        Configuration.Save();
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.BetweenLoop.ArmoireEntrustHelp"));
                    ImGui.Columns(1);

                    ImGui.SetCursorPosX(x);
                }

                using (ImGuiHelper.RequiresPlugin(ExternalPlugin.AutoRetainer, "AR", inline: true))
                {
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.BetweenLoop.EnableAutoRetainer"), ref Configuration.EnableAutoRetainer))
                        Configuration.Save();
                }
                if (Configuration.EnableAutoRetainer)
                {
                    ImGui.Indent();
                    ImGui.Text(Loc.Get("ConfigTab.BetweenLoop.PreferredSummoningBell"));
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.BetweenLoop.PreferredSummoningBellHelp"));
                    if (ImGui.BeginCombo("##PreferredBell", Configuration.PreferredSummoningBellEnum.ToLocalizedString()))
                    {
                        foreach (SummoningBellLocations summoningBells in Enum.GetValues(typeof(SummoningBellLocations)))
                            if (ImGui.Selectable(summoningBells.ToLocalizedString()))
                            {
                                Configuration.PreferredSummoningBellEnum = summoningBells;
                                Configuration.Save();
                            }

                        ImGui.EndCombo();
                    }

                    ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(Loc.Get("ConfigTab.BetweenLoop.WaitingUpTo"));
                    ImGui.SameLine();
                    if (Configuration.UseSliderInputs && ImGui.SliderLong("###AutoRetainerTimeWaitingSlider", ref Configuration.AutoRetainer_RemainingTime, 0L, 300L) ||
                        !Configuration.UseSliderInputs && ImGui.InputLong("###AutoRetainerTimeWaitingInput", ref Configuration.AutoRetainer_RemainingTime, step: 1L, stepFast: 10L))
                    {
                        Configuration.AutoRetainer_RemainingTime = Math.Max(Configuration.AutoRetainer_RemainingTime, 0L);
                        Configuration.Save();
                    }
                    ImGui.SameLine();
                    ImGui.Text(Loc.Get("ConfigTab.BetweenLoop.Seconds"));
                    ImGui.PopItemWidth();
                    ImGui.Unindent();
                }
                if (!AutoRetainer_IPCSubscriber.IsEnabled)
                    if (Configuration.EnableAutoRetainer)
                    {
                        Configuration.EnableAutoRetainer = false;
                        Configuration.Save();
                    }
            }
        }

        //Loop Termination Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        bool terminationHeader = ImGui.Selectable(Loc.Get("ConfigTab.Termination.Header"), terminationHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (terminationHeader)
            terminationHeaderSelected = !terminationHeaderSelected;
        if (terminationHeaderSelected == true)
        {
            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Termination.Enable")}###TerminationEnable", ref Configuration.EnableTerminationActions))
                Configuration.Save();
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Termination.Help"));
            using (ImRaii.Disabled(!Configuration.EnableTerminationActions))
            {
                ImGui.Separator();

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopAtLevel"), ref Configuration.StopLevel))
                    Configuration.Save();

                if (Configuration.StopLevel)
                {
                    ImGui.SameLine(0, 10);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (Configuration.UseSliderInputs)
                    {
                        if (ImGui.SliderInt("##Level", ref Configuration.StopLevelInt, 1, 100))
                        {
                            Configuration.StopLevelInt = Math.Clamp(Configuration.StopLevelInt, 1, 100);
                            Configuration.Save();
                        }
                    }
                    else
                    {
                        if (ImGui.InputInt("##Level", ref Configuration.StopLevelInt, 1, 5))
                        {
                            Configuration.StopLevelInt = Math.Clamp(Configuration.StopLevelInt, 1, 100);
                            Configuration.Save();
                        }
                    }
                    ImGui.PopItemWidth();
                }

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopNoRestedXP"), ref Configuration.StopNoRestedXP))
                    Configuration.Save();

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopAtILevel"), ref Configuration.TerminationiLvl))
                    Configuration.Save();

                if (Configuration.TerminationiLvl)
                {
                    ImGui.SameLine(0, 10);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (Configuration.UseSliderInputs)
                    {
                        if (ImGui.SliderInt("##ItemLevel", ref Configuration.TerminationiLvlInt, 1, 100))
                        {
                            Configuration.TerminationiLvlInt = Math.Clamp(Configuration.TerminationiLvlInt, 1, 100);
                            Configuration.Save();
                        }
                    }
                    else
                    {
                        if (ImGui.InputInt("##ItemLevel", ref Configuration.TerminationiLvlInt, 1, 5))
                        {
                            Configuration.TerminationiLvlInt = Math.Clamp(Configuration.TerminationiLvlInt, 1, 100);
                            Configuration.Save();
                        }
                    }
                    ImGui.PopItemWidth();
                }



                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopAtItemQty"), ref Configuration.StopItemQty))
                    Configuration.Save();

                if (Configuration.StopItemQty)
                {
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 125 * ImGuiHelpers.GlobalScale);
                    if (ImGui.BeginCombo(Loc.Get("ConfigTab.Termination.SelectItem"), stopItemQtySelectedItem.Value))
                    {
                        ImGui.InputTextWithHint(Loc.Get("ConfigTab.Termination.ItemName"), Loc.Get("ConfigTab.Termination.ItemNameHint"), ref stopItemQtyItemNameInput, 1000);
                        foreach (KeyValuePair<uint, Item> item in Items.Where(x => x.Value.Name.ToString().Contains(stopItemQtyItemNameInput, StringComparison.InvariantCultureIgnoreCase))!)
                            if (ImGui.Selectable($"{item.Value.Name.ToString()}"))
                                stopItemQtySelectedItem = new KeyValuePair<uint, string>(item.Key, item.Value.Name.ToString());
                        ImGui.EndCombo();
                    }
                    ImGui.PopItemWidth();
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 220 * ImGuiHelpers.GlobalScale);
                    if (ImGui.InputInt(Loc.Get("ConfigTab.Termination.Quantity"), ref Configuration.StopItemQtyInt, 1, 10))
                        Configuration.Save();

                    ImGui.SameLine(0, 5);
                    using (ImRaii.Disabled(stopItemQtySelectedItem.Value.IsNullOrEmpty()))
                    {
                        if (ImGui.Button(Loc.Get("ConfigTab.Termination.AddItem")))
                        {
                            if (!Configuration.StopItemQtyItemDictionary.TryAdd(stopItemQtySelectedItem.Key, new KeyValuePair<string, int>(stopItemQtySelectedItem.Value, Configuration.StopItemQtyInt)))
                            {
                                Configuration.StopItemQtyItemDictionary.Remove(stopItemQtySelectedItem.Key);
                                Configuration.StopItemQtyItemDictionary.Add(stopItemQtySelectedItem.Key, new KeyValuePair<string, int>(stopItemQtySelectedItem.Value, Configuration.StopItemQtyInt));
                            }
                            Configuration.Save();
                        }
                    }
                    ImGui.PopItemWidth();
                    if (!ImGui.BeginListBox("##ItemList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.StopItemQtyItemDictionary.Count) + 5)))
                        return;

                    foreach (KeyValuePair<uint, KeyValuePair<string, int>> item in Configuration.StopItemQtyItemDictionary)
                    {
                        ImGui.Selectable($"{item.Value.Key} (Qty: {item.Value.Value})");
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            Configuration.StopItemQtyItemDictionary.Remove(item);
                            Configuration.Save();
                        }
                    }
                    ImGui.EndListBox();
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopOnlyWhenAllItems"), ref Configuration.StopItemAll))
                        Configuration.Save();
                }

                using (ImGuiHelper.RequiresPlugin(ExternalPlugin.GlamourLog, "StopWhenDutyGatheredGlamourLog", inline: true))
                {
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopWhenDutyGathered"), ref Configuration.StopWhenDutyGathered))
                        Configuration.Save();
                    ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Termination.StopWhenDutyGatheredHelp"));
                }

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopBLUSpell"), ref Configuration.TerminationBLUSpellsEnabled))
                    Configuration.Save();

                if (Configuration.TerminationBLUSpellsEnabled)
                {
                    ImGui.Indent();

                    if(ImGui.BeginCombo("##TerminationBlueSpell", Loc.Get("ConfigTab.Termination.SelectBLUSpell")))
                    {
                        foreach (BLUHelper.BLUSpell bluSpell in BLUHelper.spells)
                        {
                            if (!BLUHelper.SpellUnlocked(bluSpell))
                                if (ImGui.Selectable($"({bluSpell.Entry}) {bluSpell.Name}"))
                                {
                                    Configuration.TerminationBLUSpells.Add(bluSpell.ID);
                                    Configuration.TerminationBLUSpells = [..Configuration.TerminationBLUSpells.OrderBy(sp => BLUHelper.spellsById[sp].Entry)];
                                    Configuration.Save();
                                }
                        }

                        ImGui.EndCombo();
                    }

                    if (ImGui.BeginListBox("##TerminationBluSpellList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.TerminationBLUSpells.Count) + 5)))
                    {
                        foreach (uint bluSpell in Configuration.TerminationBLUSpells)
                        {
                            BLUHelper.BLUSpell spell = BLUHelper.spellsById[bluSpell];
                            ImGui.Selectable($"({spell.Entry}) {spell.Name}");
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                Configuration.TerminationBLUSpells.Remove(bluSpell);
                                Configuration.Save();
                                return;
                            }
                        }
                        ImGui.EndListBox();
                    }
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopOnlyWhenAllSpells"), ref Configuration.TerminationBLUSpellsAll))
                        Configuration.Save();

                    ImGui.Unindent();
                }

                if(ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopWhenInventoryFull") + "###StopWhenInventoryFull", ref Configuration.TerminationInventoryFree))
                    Configuration.Save();

                if (Configuration.TerminationInventoryFree)
                {
                    ImGui.Indent();
                    ImGui.PushItemWidth(150f.Scale());

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(Loc.Get("ConfigTab.Termination.StopWhenInventoryFullSlots"));
                    ImGui.SameLine();
                    if(ImGui.InputInt("###StopWhenInventoryFullSlotsInput", ref Configuration.TerminationInventoryFreeSlots, 1, 5))
                    {
                        Configuration.TerminationInventoryFreeSlots = Math.Clamp(Configuration.TerminationInventoryFreeSlots, 0, 139);
                        Configuration.Save();
                    }

                    ImGui.PopItemWidth();
                    ImGui.Unindent();
                }


                MakeCommands("ConfigTab.Termination.ExecuteCommandsOnTermination", ref Configuration.ExecuteCommandsTermination,  ref Configuration.CustomCommandsTermination, ref terminationCommand, "CommandsTermination");

                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.PlaySoundOnCompletion"), ref Configuration.PlayEndSound)) //Heavily Inspired by ChatAlerts
                    Configuration.Save();
                if (Configuration.PlayEndSound)
                {
                    if (ImGuiEx.IconButton(FontAwesomeIcon.Play, "##ConfigSoundTest", new Vector2(ImGui.GetItemRectSize().Y)))
                        SoundHelper.StartSound(Configuration.PlayEndSound, Configuration.CustomSound, Configuration.SoundEnum);
                    ImGui.SameLine();
                    DrawGameSound();
                }

                ImGui.Text(Loc.Get("ConfigTab.Termination.OnCompletionOfAllLoops"));
                ImGui.SameLine(0, 10);
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.BeginCombo("##ConfigTerminationMethod", Configuration.TerminationMethodEnum.ToLocalizedString()))
                {
                    foreach (TerminationMode terminationMode in Enum.GetValues(typeof(TerminationMode)))
                        if (terminationMode != TerminationMode.Kill_PC || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                            if (ImGui.Selectable(terminationMode.ToLocalizedString(), Configuration.TerminationMethodEnum == terminationMode))
                            {
                                Configuration.TerminationMethodEnum = terminationMode;
                                Configuration.Save();
                            }

                    ImGui.EndCombo();
                }

                if (Configuration.TerminationMethodEnum is TerminationMode.Kill_Client or TerminationMode.Kill_PC or TerminationMode.Logout)
                {
                    ImGui.Indent();
                    if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.KeepTerminationActive"), ref Configuration.TerminationKeepActive))
                        Configuration.Save();
                    ImGui.Unindent();
                }
            }
        }

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

        ImGui.SetItemAllowOverlap();
        if (ImGui.Selectable(Loc.Get("ConfigTab.Multiboxing.Header"), multiboxHeaderSelected, ImGuiSelectableFlags.DontClosePopups))
            multiboxHeaderSelected = !multiboxHeaderSelected;

        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (multiboxHeaderSelected)
        {
            ImGui.TextColored(GradientColor.Get(ImGuiHelper.ExperimentalColor, ImGuiHelper.ExperimentalColor2, 500), Loc.Get("ConfigTab.Multiboxing.ExperimentalWarning"));

            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step1"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step2"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step3"));
            ImGui.Separator();

            uint text     = ImGui.GetColorU32(ImGuiCol.Text);
            uint disabled = ImGui.GetColorU32(ImGuiCol.TextDisabled);

            TransportType transportType = MultiboxUtility.Config.TransportType;
            using (ImRaii.PushColor(ImGuiCol.Text, transportType == TransportType.NamedPipe ? text : disabled))
            {
                ImGui.TextWrapped(Loc.Get("ConfigTab.Multiboxing.NamedPipes"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step4Pipes"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step5Pipes"));
            }
            ImGui.Separator();
            using (ImRaii.PushColor(ImGuiCol.Text, transportType == TransportType.Tcp ? text : disabled))
            {
                ImGui.TextWrapped(Loc.Get("ConfigTab.Multiboxing.TCP"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step4TCP"));
                ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step5TCP"));
                if(OperatingSystem.IsWindows())
                    ImGui.TextWrapped(Loc.Get("ConfigTab.Multiboxing.UACWarning"));
            }

            ImGui.Separator();
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step6"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step7"));
            ImGuiEx.TextWrapped(Loc.Get("ConfigTab.Multiboxing.Step8"));

            bool multiBox = MultiboxUtility.Config.MultiBox;
            if (ImGui.Checkbox(nameof(MultiboxUtility.Config.MultiBox), ref multiBox))
            {
                MultiboxUtility.Config.MultiBox = multiBox;
                Configuration.Save();
            }

            using(ImRaii.Disabled(MultiboxUtility.Config.MultiBox))
            {
                ImGui.Indent();
                if(ImGuiEx.EnumCombo(Loc.Get("ConfigTab.Multiboxing.TransportType"), ref transportType))
                {
                    MultiboxUtility.Config.TransportType = transportType;
                    Configuration.Save();
                }

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Multiboxing.TransportTypeHelp"));

                switch (transportType)
                {
                    case TransportType.NamedPipe:
                    {
                        string pipeName = MultiboxUtility.Config.PipeName;
                        if(ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.PipeName"), ref pipeName))
                        {
                            MultiboxUtility.Config.PipeName = pipeName;
                            Configuration.Save();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetPipeName"))
                        {
                            MultiboxUtility.Config.PipeName = "AutoDutyPipe";
                                Configuration.Save();
                        }

                        if (!MultiboxUtility.Config.Host)
                        {
                            string serverName = MultiboxUtility.Config.ServerName;
                            if (ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.ServerName"), ref serverName))
                            {
                                MultiboxUtility.Config.ServerName = serverName;
                                Configuration.Save();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerName"))
                            {
                                MultiboxUtility.Config.ServerName = ".";
                                    Configuration.Save();
                            }
                        }

                        break;
                    }
                    case TransportType.Tcp:
                    {
                        if (!MultiboxUtility.Config.Host)
                        {
                            string serverAddress = MultiboxUtility.Config.ServerAddress;
                            if (ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.ServerAddress"), ref serverAddress))
                            {
                                MultiboxUtility.Config.ServerAddress = serverAddress;
                                Configuration.Save();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerAddress"))
                            {
                                MultiboxUtility.Config.ServerAddress = "127.0.0.1";
                                    Configuration.Save();
                            }
                        }

                        int serverPort = MultiboxUtility.Config.ServerPort;
                        if (ImGui.InputInt(Loc.Get("ConfigTab.Multiboxing.ServerPort"), ref serverPort))
                        {
                            MultiboxUtility.Config.ServerPort = serverPort;
                            Configuration.Save();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerPort"))
                        {
                            MultiboxUtility.Config.ServerPort = 1716;
                                Configuration.Save();
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                bool host = MultiboxUtility.Config.Host;
                if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Multiboxing.Host")}##MultiboxHost", ref host))
                {
                    MultiboxUtility.Config.Host = host;
                    Configuration.Save();
                }

                ImGui.Unindent();
            }

            bool synchronizePath = MultiboxUtility.Config.SynchronizePath;
            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Multiboxing.SynchronizePaths")}##MultiboxSynchronizePaths", ref synchronizePath))
            {
                MultiboxUtility.Config.SynchronizePath = synchronizePath;
                Configuration.Save();
            }

            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Multiboxing.SynchronizePathsHelp"));

            if (MultiboxUtility.Config.MultiBox)
            {
                ImGui.Indent();
                ImGuiEx.Text(string.Format(Loc.Get("ConfigTab.Multiboxing.Blocking"), MultiboxUtility.stepBlock));

                if(MultiboxUtility.Config.Host)
                {
                    unsafe
                    {
                        ImGui.Separator();

                        if (ImGui.Checkbox(Loc.Get("ConfigTab.Multiboxing.ScrambleNames"), ref Censor.Config.Enabled))
                            Configuration.Save();

                        ImGui.Columns(5);

                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.Name"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.InParty"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.Job"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.BlockingStatus"));
                        ImGui.NextColumn();
                        ImGuiEx.Text(Loc.Get("ConfigTab.Multiboxing.LastHeard"));
                        ImGui.Separator();
                        ImGui.NextColumn();

                        InfoProxyPartyMember* partyMembers = InfoProxyPartyMember.Instance();

                        for (int i = 0; i < MultiboxUtility.Server.MAX_SERVERS; i++)
                        {
                            MultiboxUtility.Server.ClientInfo? info = MultiboxUtility.Server.clients[i];

                            if(info != null)
                            {
                                ImGuiEx.Text(Censor.Character(info.CName));
                                ImGui.NextColumn();
                                bool inParty = PartyHelper.IsPartyMember(info.CID);
                                ImGuiEx.Text(inParty ? ImGuiHelper.StateGoodColor : ImGuiHelper.StateBadColor, inParty ? Loc.Get("ConfigTab.Multiboxing.InPartyYes") : Loc.Get("ConfigTab.Multiboxing.InPartyNo"));
                                ImGui.NextColumn();
                                if(partyMembers != null)
                                {
                                    InfoProxyCommonList.CharacterData* data = partyMembers->GetEntryByContentId(info.CID);
                                    if(data != null)
                                    {
                                        Job job = (Job) data->Job;
                                        ImGuiEx.Text(job.GetCombatRole() switch
                                        {
                                            CombatRole.Tank => ImGuiHelper.RoleTankColor,
                                            CombatRole.Healer => ImGuiHelper.RoleHealerColor,
                                            CombatRole.DPS => ImGuiHelper.RoleDPSColor,
                                            _ => ImGuiHelper.StateBadColor
                                        }, job.ToCustomString());
                                    }
                                }

                                ImGui.NextColumn();
                                ImGuiEx.Text(MultiboxUtility.Server.stepConfirms[i].ToString());
                                ImGui.NextColumn();
                                double totalSeconds = DateTime.Now.Subtract(MultiboxUtility.Server.keepAlives[i]).TotalSeconds;
                                ImGuiEx.Text(totalSeconds < 10 ? ImGuiHelper.StateGoodColor : ImGuiHelper.StateBadColor, $"{totalSeconds:F3}s ago");
                                ImGui.NextColumn();
                            }
                            else
                            {
                                ImGui.Text(string.Format(Loc.Get("ConfigTab.Multiboxing.NoInfo"), i));
                                for (int j = 0; j < 5; j++)
                                    ImGui.NextColumn();
                            }
                        }
                        ImGui.Columns(1);

                        using(ImRaii.Disabled(!InDungeon))
                        {
                            if(ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.ResynchronizeStep")}##MultiboxSynchronizeStep"))
                                MultiboxUtility.Server.SendStepStart();
                        }
                        ImGui.Separator();
                    }
                }

                ImGui.Unindent();
            }
        }

        return;

        static void MakeCommands(string checkbox, ref bool execute, ref List<string> commands, ref string curCommand, string id)
        {
            if (ImGui.Checkbox(Loc.Get(checkbox), ref execute))
                Configuration.Save();

            ImGuiComponents.HelpMarker(Loc.Get(checkbox + "Help", "/echo test"));

            if (execute)
            {
                ImGui.Indent();
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 185 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputTextWithHint($"##Commands{checkbox}_{id}", "enter command starting with /", ref curCommand, 500, ImGuiInputTextFlags.EnterReturnsTrue))
                    if (!curCommand.IsNullOrEmpty() && curCommand[0] == '/' && (ImGui.IsKeyDown(ImGuiKey.Enter) || ImGui.IsKeyDown(ImGuiKey.KeypadEnter)))
                    {
                        Configuration.CustomCommandsPreLoop.Add(curCommand);
                        curCommand = string.Empty;
                        Configuration.Save();
                    }

                ImGui.PopItemWidth();
                    
                ImGui.SameLine(0, 5);
                using (ImRaii.Disabled(curCommand.IsNullOrEmpty() || curCommand[0] != '/'))
                {
                    if (ImGui.Button($"Add Command##CommandButton{checkbox}_{id}"))
                    {
                        commands.Add(curCommand);
                        Configuration.Save();
                    }
                }
                if (!ImGui.BeginListBox($"##CommandList{checkbox}_{id}", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * commands.Count) + 5))) 
                    return;

                bool removeItem = false;
                int removeAt   = 0;

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
                    Configuration.Save();
                }
                ImGui.EndListBox();
                ImGui.Unindent();
            }
        }
    }

    private static void DrawGameSound()
    {
        ImGui.SameLine(0, 10);
        ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##ConfigEndSoundMethod", Configuration.SoundEnum.ToName()))
        {
            foreach (Sounds sound in validSounds)
                if (ImGui.Selectable(sound.ToName()))
                {
                    unsafe
                    {
                        Configuration.SoundEnum = sound;
                        UIGlobals.PlaySoundEffect((uint)sound);
                        Configuration.Save();
                    }
                }

            ImGui.EndCombo();
        }
    }
}
