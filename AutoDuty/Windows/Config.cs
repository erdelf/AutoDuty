using AutoDuty.Helpers;
using AutoDuty.IPC;
using AutoDuty.Managers;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Data;
using ECommons.Configuration;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.PartyFunctions;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Properties;
using System.Numerics;
using System.Text;
using ECommons.IPC.Subscribers.RotationSolverReborn;
using Multibox;
using NightmareUI.Censoring;
using Achievement = Lumina.Excel.Sheets.Achievement;
using Buddy = FFXIVClientStructs.FFXIV.Client.Game.UI.Buddy;
using Map = Lumina.Excel.Sheets.Map;
using Vector2 = FFXIVClientStructs.FFXIV.Common.Math.Vector2;

[JsonObject(MemberSerialization.OptIn)]
public class ConfigurationMain
{
    public const string CONFIGNAME_BARE = "Bare";

    public static ConfigurationMain Instance { get; set; } = null!;

    [JsonProperty]
    public string DefaultConfigName { get; set; } = CONFIGNAME_BARE;

    public string Language { get; set; } = "en-US";

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
    public StatData stats = new();

    [JsonObject(MemberSerialization.OptOut)]
    public class StatData
    {
        public          int                  dungeonsRun;
        public readonly List<DutyDataRecord> dutyRecords = [];
        public          TimeSpan             timeSpent   = TimeSpan.Zero;
    }


    [JsonProperty]
    //Dev Options
    internal bool updatePathsOnStartup = true;
    public bool UpdatePathsOnStartup
    {
        get => !Plugin.isDev || this.updatePathsOnStartup;
        set => this.updatePathsOnStartup = value;
    }

    internal bool multiBox = false;
    public bool MultiBox
    {
        get => this.multiBox;
        set
        {
            if (this.multiBox == value)
                return;
            this.multiBox = value;

            MultiboxUtility.Set(this.multiBox);
        }
    }

    [JsonProperty]
    internal bool host = false;
    [JsonProperty]
    internal bool multiBoxSynchronizePath = true;

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
            if (LocalizationManager.CurrentLanguage != GetCurrentConfig.Language)
                LocalizationManager.SetLanguage(GetCurrentConfig.Language);
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
            bool isInternal = (assemblyName?.StartsWith("AutoDuty") ?? false);

            if (isInternal)
            {
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
    public string Language = "en-US";

    
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
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => Plugin.Overlay.IsOpen = !value || Plugin.states.HasAnyFlag(PluginState.Looping, PluginState.Navigating), () => Plugin.Overlay != null);
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
    public RotationSolverRebornIPC.TargetingType     RSR_TargetingTypeTank    = RotationSolverRebornIPC.TargetingType.HighMaxHP;
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
        get => this.stuckReturn && !ConfigurationMain.Instance.MultiBox;
        set => this.stuckReturn = value;
    }

    public bool PathDrawEnabled   = false;
    public int  PathDrawStepCount = 5;

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

        return ConfigurationMain.Instance.MultiBox || unsync.Value && this.TreatUnsyncAsW2W;
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
    public bool                                        EnableTerminationActions    = true;
    public bool                                        StopLevel                   = false;
    public int                                         StopLevelInt                = 1;
    public bool                                        StopNoRestedXP              = false;
    public bool                                        StopItemQty                 = false;
    public bool                                        StopItemAll                 = false;
    public Dictionary<uint, KeyValuePair<string, int>> StopItemQtyItemDictionary   = [];
    public int                                         StopItemQtyInt              = 1;
    public bool                                        TerminationBLUSpellsEnabled = false;
    public List<uint>                                  TerminationBLUSpells        = [];
    public bool                                        TerminationBLUSpellsAll     = false;
    public bool                                        ExecuteCommandsTermination  = false;
    public List<string>                                CustomCommandsTermination   = [];
    public bool                                        PlayEndSound                = false;
    public bool                                        CustomSound                 = false;
    public float                                       CustomSoundVolume           = 0.5f;
    public Sounds                                      SoundEnum                   = Sounds.None;
    public string                                      SoundPath                   = "";
    public TerminationMode                             TerminationMethodEnum       = TerminationMode.Do_Nothing;
    public bool                                        TerminationKeepActive       = true;
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
        using (ImRaii.IEndObject configCombo = ImRaii.Combo("##ConfigCombo", ConfigurationMain.Instance.ActiveProfileName))
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
        using ImRaii.IEndObject _ = ImRaii.Disabled(bareProfile);

        //Language Selector
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(Loc.Get("ConfigTab.Language"));
        ImGui.SameLine();

        string currentLang = Configuration.Language;
        string[] languages = LocalizationManager.AvailableLanguages;
        int currentIndex = Array.IndexOf(languages, currentLang);

        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##Language", ref currentIndex, languages, languages.Length))
        {
            Configuration.Language = languages[currentIndex];
            LocalizationManager.SetLanguage(Configuration.Language);
            Configuration.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Get("ConfigTab.LanguageHelp"));

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
            bool devHeader = ImGui.Selectable(Loc.Get("ConfigTab.Dev.Header"), devHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (devHeader)
                devHeaderSelected = !devHeaderSelected;

            if (devHeaderSelected)
            {
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Dev.UpdatePathsOnStartup") + "##DevUpdatePathsOnStartup", ref ConfigurationMain.Instance.updatePathsOnStartup))
                    Configuration.Save();

                if (ImGui.Button(Loc.Get("ConfigTab.Dev.PrintModList") + "##DevPrintModList")) 
                    Svc.Log.Info(string.Join("\n", PluginInterface.InstalledPlugins.Where(pl => pl.IsLoaded).GroupBy(pl => pl.Manifest.InstalledFromUrl).OrderByDescending(g => g.Count()).Select(g => g.Key+"\n\t"+string.Join("\n\t", g.Select(pl => pl.Name)))));
                unsafe
                {
                    ImGuiEx.Text(Loc.Get("ConfigTab.Dev.InvitedBy") + InfoProxyPartyInvite.Instance()->InviterName + " | " + InfoProxyPartyInvite.Instance()->InviterWorldId);
                }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.DutySupport.Header") + "##DevDutySupport")) //ImGui.Button("check duty support?"))
                    if(GenericHelpers.TryGetAddonMaster<AddonMaster.DawnStory>(out AddonMaster.DawnStory? m))
                        if (m.IsAddonReady)
                        {
                            ImGuiEx.Text(Loc.Get("ConfigTab.Dev.DutySupport.Selected") + m.Reader.CurrentSelection);

                            ImGuiEx.Text(Loc.Get("ConfigTab.Dev.DutySupport.Cnt") + m.Reader.EntryCount);
                            foreach (AddonMaster.DawnStory.Entry? x in m.Entries)
                            {
                                ImGuiEx.Text($"{x.Name} / {x.ReaderEntry.Callback} / {x.Index}");
                                if (ImGuiEx.HoveredAndClicked() && x.Status != 2)
                                    x.Select();
                            }
                        }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Squadron.Header") + "##DevSquadron"))
                    unsafe
                    {
                        if (GenericHelpers.TryGetAddonByName("GcArmyCapture", out AtkUnitBase* armyCaptureAtk) && GenericHelpers.IsAddonReady(armyCaptureAtk))
                        {
                            ImGui.Indent();
                            if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Squadron.Duties") + "##DevSquadronDuties"))
                            {
                                ReaderGCArmyCapture armyCapture = new(armyCaptureAtk);
                                ImGui.Text($"{armyCapture.PlayerCharLvl} ({armyCapture.PlayerCharIlvl}) {armyCapture.PlayerCharName}");
                                ImGui.Columns(6);

                                ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Enabled"));
                                ImGui.NextColumn();
                                ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Completed"));
                                ImGui.NextColumn();

                                ImGui.NextColumn();
                                ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Name"));
                                ImGui.NextColumn();
                                ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Level"));
                                ImGui.NextColumn();
                                ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Synced"));
                                ImGui.NextColumn();


                                foreach (ReaderGCArmyCapture.DungeonInfo dungeon in armyCapture.Entries)
                                {
                                    bool unk0 = dungeon.Unk0;
                                    ImGui.Checkbox(string.Empty, ref unk0);
                                    ImGui.NextColumn();
                                    bool unk1 = dungeon.Completed;
                                    ImGui.Checkbox(string.Empty, ref unk1);
                                    ImGui.NextColumn();
                                    ImGui.Text(dungeon.Unk2.ToString());
                                    ImGui.NextColumn();
                                    ImGui.Text(dungeon.Name.TextValue);
                                    ImGui.NextColumn();
                                    ImGui.Text(dungeon.Level);
                                    ImGui.NextColumn();
                                    bool synced = dungeon.Synced;
                                    ImGui.Checkbox(string.Empty, ref synced);
                                    ImGui.NextColumn();
                                }
                                ImGui.Columns(1);
                            }
                            if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Squadron.AvailableMembers") + "##DevGCArmyMembers"))
                                if (GenericHelpers.TryGetAddonByName("GcArmyMemberList", out AtkUnitBase* armyMemberListAtk) && GenericHelpers.IsAddonReady(armyMemberListAtk))
                                {
                                    ReaderGCArmyMemberList armyMemberList = new(armyMemberListAtk);

                                    ImGui.Columns(13);


                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Selected"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Name"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Class"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.ClassId"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Level"));
                                    ImGui.NextColumn();
                                    ImGui.NextColumn();
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Physical"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Mental"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Tactical"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Chemistry"));
                                    ImGui.NextColumn();
                                    ImGui.Text(Loc.Get("ConfigTab.Dev.Squadron.Columns.Tactics"));
                                    ImGui.NextColumn();


                                    foreach (ReaderGCArmyMemberList.MemberInfo? member in armyMemberList.Entries)
                                    {
                                        ImGui.Text(member.Unk0.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Selected.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Name);
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Class);
                                        ImGui.NextColumn();
                                        ImGui.Text($"{member.ClassId} ({(ReaderGCArmyMemberList.SquadronClassType)(byte) member.ClassId})");
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Level.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Unk3.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Unk4.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Physical.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Mental.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Tactical.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Chemistry.ToString());
                                        ImGui.NextColumn();
                                        ImGui.Text(member.Tactics.ToString());
                                        ImGui.NextColumn();
                                    }

                                    ImGui.Columns(1);
                                }

                            ImGui.Unindent();
                        }
                    }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.TTCards.Header") + "##DevAvailableTTShop"))
                    unsafe
                    {
                        if (GenericHelpers.TryGetAddonByName("TripleTriadCoinExchange", out AtkUnitBase* exchangeAddon))
                            if (exchangeAddon->IsReady)
                            {
                                ReaderTripleTriadCoinExchange exchange = new(exchangeAddon);

                                ImGuiEx.Text(Loc.Get("ConfigTab.Dev.TTCards.Cnt") + exchange.EntryCount);
                                foreach (ReaderTripleTriadCoinExchange.CardEntry? x in exchange.Entries)
                                {
                                    ImGuiEx.Text($"({x.Id}) {x.Name} | {x.Count} | {x.Value} | {x.InDeck}");
                                    if (ImGuiEx.HoveredAndClicked())
                                    {
                                        //x.Select();
                                    }
                                }
                            }
                    }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Partycheck.Header") + "##DevPartyInfo"))
                {
                    ImGui.Indent();
                    unsafe
                    {
                        ImGui.Text(Loc.Get("ConfigTab.Dev.Partycheck.PartySize") + Svc.Party.Count);
                    
                        bool healer   = false;
                        bool tank     = false;
                        int  dpsCount = 0;

                        foreach (IPartyMember? item in Svc.Party)
                        {
                            ImGui.Text($"{item.ClassJob.ValueNullable?.Role} {((Job) item.ClassJob.RowId)} {((Job)item.ClassJob.RowId).GetCombatRole()}");
                            switch (item.ClassJob.ValueNullable?.Role)
                            {
                                case 1:
                                    tank = true;
                                    break;
                                case 2:
                                case 3:
                                    dpsCount++;
                                    break;
                                case 4:
                                    healer = true;
                                    break;
                                default:
                                    break;
                            }
                        }
                        ImGui.NewLine();
                        ImGui.Text(Loc.Get("ConfigTab.Dev.Partycheck.Valid") + (tank && healer && dpsCount > 1));
                        ImGui.NewLine();
                        foreach (UniversalPartyMember member in UniversalParty.Members)
                        {
                            ImGui.Text($"{member.ClassJob} {member.ClassJob.GetCombatRole()}");
                        }
                        ImGui.NewLine();

                        foreach (PartyMember member in GroupManager.Instance()->MainGroup.PartyMembers)
                        {
                            ImGui.Text($"{(Job) member.ClassJob} {((Job) member.ClassJob).GetCombatRole()}");
                        }
                        ImGui.NewLine();
                        try
                        {
                            InfoProxyPartyMember* instance = InfoProxyPartyMember.Instance();
                            ImGui.Text(instance->EntryCount.ToString());
                            ImGui.Text(instance->GetEntryCount().ToString());

                            for (uint i = 0; i < instance->GetEntryCount(); i++)
                            {
                                InfoProxyCommonList.CharacterData* characterData = instance->GetEntry(i);
                                ImGui.Text($"{(Job) characterData->Job} {((Job)characterData->Job).GetCombatRole()}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Svc.Log.Error(ex.ToString());
                        }
                    }
                    ImGui.Unindent();
                }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.BLU.Header") + "##DevBlueLoadout"))
                {
                    unsafe
                    {
                        Span<uint> blu = ActionManager.Instance()->BlueMageActions;
                        foreach (uint u in blu)
                        {
                            if (u != 0)
                            {
                                if (BLUHelper.spellsById.TryGetValue(BLUHelper.NormalToAoz(u), out BLUHelper.BLUSpell? spell))
                                {
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.Text($"{u}: {BLUHelper.NormalToAoz(u)} {spell.Entry} {spell.Name}");
                                    ImGui.SameLine();
                                    if (ImGui.Button(Loc.Get("ConfigTab.Dev.BLU.Unload") + $"##DevBlueLoadoutUnload_{u}"))
                                        BLUHelper.SpellLoadoutOut(spell.Entry);
                                }

                            } else
                            {
                                ImGui.Text(Loc.Get("ConfigTab.Dev.BLU.NothingLoaded"));
                                ImGui.SameLine();
                                if (ImGui.Button(Loc.Get("ConfigTab.Dev.BLU.Load") + "##DevBlueLoadoutLoad"))
                                {
                                    BLUHelper.SpellLoadoutIn((byte)Random.Shared.Next(1, 124));
                                    return;
                                }
                            }
                        }
                    }
                }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.RetainerPrices.Header") + "##DevRetainerPricesCheck"))
                {
                    unsafe
                    {
                        ImGui.Indent();


                        InventoryContainer* container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.RetainerMarket);
                        Span<ulong>         prices    = InventoryManager.Instance()->RetainerMarketPrices;
                        ImGui.Columns(2);

                        for (int i = 0; i < container->Size; i++)
                        {
                            InventoryItem item = container->Items[i];
                            ImGui.Text(item.Quantity.ToString());
                            ImGui.NextColumn();
                            ulong price = prices[i];
                            ImGuiEx.Text(price.ToString());
                            ImGui.NextColumn();
                        }

                        ImGui.Columns();
                        ImGui.Unindent();
                    }
                }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Buddy.Header") + "##DevBuddyChecks"))
                {
                    unsafe
                    {
                        Span<Buddy.BuddyMember> span = new(UIState.Instance()->Buddy.DutyHelperInfo.DutyHelpers, 7);
                        ImGui.Columns(3);
                        foreach (Buddy.BuddyMember member in span)
                        {
                            ImGui.Text(member.DataId.ToString());
                            ImGui.NextColumn();
                            ImGui.Text(member.EntityId.ToString());
                            ImGui.NextColumn();
                            ImGui.Text(member.Synced.ToString());
                            ImGui.NextColumn();
                        }
                        ImGui.Columns(1);
                        ImGui.Text($"Party {Svc.Party.Length}");
                        ImGui.Text($"Party {PartyHelper.GetPartyMembers().Count}");
                        ImGui.Text($"Party {PartyHelper.GetPartyMembers().Skip(1).FirstOrDefault()?.ObjectKind}");
                        ImGui.Text($"Party {PartyHelper.GetPartyMembers().Skip(1).FirstOrDefault()?.BaseId}");
                        ImGui.Text($"Party {PartyHelper.GetPartyMembers().Skip(2).FirstOrDefault()?.BaseId}");
                        ImGui.Text($"Party {PartyHelper.GetPartyMembers().Skip(3).FirstOrDefault()?.BaseId}");
                        ImGui.Text($"Buddies: {Svc.Buddies.Length}");
                        ImGui.Text($"Buddies: {Svc.Buddies.Count}");
                        ImGui.Text($"Pet: {Svc.Buddies.PetBuddy?.GameObject?.Name}");
                        ImGui.Text($"Pet: {Svc.Buddies.PetBuddy?.GameObject?.Name}");
                        ImGui.Text($"Companion: {Svc.Buddies.CompanionBuddy?.GameObject?.Name}");
                    }
                }


                if(ImGui.Button(Loc.Get("ConfigTab.Dev.Actions.CheckAction") + "##DevCheckActionStatus"))
                    unsafe
                    {
                        Svc.Log.Warning(ActionManager.Instance()->GetActionStatus(ActionType.Action, 23282).ToString());
                    }
                ImGui.SameLine();
                if (ImGui.Button(Loc.Get("ConfigTab.Dev.Actions.CheckAction2") + "##DevCheckActionStatus2"))
                    unsafe
                    {
                        Svc.Log.Warning(ActionManager.Instance()->GetActionStatus(ActionType.Action, 23277).ToString());
                    }

                if (ImGui.Button(Loc.Get("ConfigTab.Dev.Actions.Return") + "##DevReturnButton"))
                    unsafe
                    {
                        VNavmesh_IPCSubscriber.Path_Stop();
                        ActionManager.Instance()->UseAction(ActionType.Action, 6);
                    }

                if (ImGui.Button(Loc.Get("ConfigTab.Dev.Actions.RotationOn") + "##DevRotationOn")) 
                    Plugin.SetRotationPluginSettings(true, ignoreConfig: true, ignoreTimer: true);

                ImGui.SameLine();
                if (ImGui.Button(Loc.Get("ConfigTab.Dev.Actions.RotationOff") + "##DevRotationoff"))
                {
                    Plugin.SetRotationPluginSettings(false, ignoreConfig: true, ignoreTimer: true);
                    if(Wrath_IPCSubscriber.IsEnabled)
                        Wrath_IPCSubscriber.Release();
                }

                if (ImGui.Button(Loc.Get("ConfigTab.Dev.Actions.BetweenLoopActions") + "##DevBetweenLoops"))
                {
                    Plugin.CurrentTerritoryContent =  ContentHelper.DictionaryContent.Values.First();
                    Plugin.states                  |= PluginState.Other;
                    Plugin.LoopTasks(false);
                }

                if (ImGui.Button(Loc.Get("ConfigTab.Dev.Actions.BossLootTest") + "##DevBossLootTest"))
                {
                    IEnumerable<IGameObject> treasures = ObjectHelper.GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)?.
                                                                      Where(x => ObjectHelper.BelowDistanceToPoint(x.Position, Player.Position, 50, 10)) ?? [];
                    Svc.Log.Debug(treasures.Count() + "\n" + string.Join("\n", treasures.Select(igo => igo.Position.ToString())));
                }

                if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Teleport.Header") + "##DevTPPlay"))
                {
                    ImGui.Indent();
                    if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Teleport.Warps") + "##DevWarps"))
                    {
                        ImGui.Indent();
                        foreach (Warp warp in Svc.Data.GameData.GetExcelSheet<Warp>()!)
                        {
                            if (warp.TerritoryType.RowId != 152)
                                continue;

                            if (ImGui.CollapsingHeader($"{warp.Name} {warp.Question} to {warp.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString()}##{warp.RowId}"))
                                if (warp.PopRange.ValueNullable is { } level)
                                {
                                    ImGui.Text($"{level.X} {level.Y} {level.Z} in {level.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ToString()}");
                                    ImGui.Text($"{(new Vector3(level.X, level.Y, level.Z) - Player.Position)}");
                                }
                        }

                        ImGui.Unindent();
                    }

                    if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.Dev.Teleport.LevelTest")))
                        foreach ((Level lvl, Vector3, Vector3) level in Svc.Data.GameData.GetExcelSheet<Level>()!.Where(lvl => lvl.Territory.RowId == 152)
                                                                           .Select(lvl => (lvl, (new Vector3(lvl.X, lvl.Y, lvl.Z))))
                                                                           .Select(tuple => (tuple.lvl, tuple.Item2, (tuple.Item2 - Player.Position))).OrderBy(lvl => lvl.Item3.LengthSquared()))
                            ImGui.Text($"{level.lvl.RowId} {level.Item2} {level.Item3} {string.Join(" | ", level.lvl.Object.GetType().GenericTypeArguments.Select(t => t.FullName))}: {level.lvl.Object.RowId}");

                    ImGuiEx.Text($"{typeof(Achievement).Assembly.GetTypes().Where(x => x.FullName?.StartsWith("Lumina.Excel.Sheets") ?? false).
                                                        Select(x => (x, x.GetProperties().Where(f => f.PropertyType.Name == "RowRef`1" && f.PropertyType.GenericTypeArguments[0].FullName == typeof(Map).FullName))).
                                                        Where(x => x.Item2.Any()).
                                                        Select(x => $"{x.x} references {x.Item2.Select(pi => pi.Name).Print(", ")}").Print("\n")}");
                    ImGui.Unindent();
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
                        if (ImGui.Button(Configuration.PositionalEnum.ToCustomString()))
                            ImGui.OpenPopup("PositionalPopup");
            
                        if (ImGui.BeginPopup("PositionalPopup"))
                        {
                            foreach (Positional positional in Enum.GetValues(typeof(Positional)))
                                if (ImGui.Selectable(positional.ToCustomString(), Configuration.PositionalEnum == positional))
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
                if(ImGui.InputInt(Loc.Get("ConfigTab.Duty.RebuildNavmeshTimes"), ref rebuildX, 1))
                {
                    Configuration.RebuildNavmeshAfterStuckXTimes = (byte) Math.Clamp(rebuildX, byte.MinValue+1, byte.MaxValue);
                    Configuration.Save();
                }
            }

            if (ImGui.Checkbox(Loc.Get("ConfigTab.Duty.UseReturnWhenStuck"), ref Configuration.stuckReturn))
                Configuration.Save();

            if (Configuration.StuckReturn)
            {
                ImGui.SameLine();
                int returnX = Configuration.StuckReturnX;
                if (ImGui.InputInt(Loc.Get("ConfigTab.Duty.ReturnTimes"), ref returnX, 1))
                {
                    Configuration.StuckReturnX = (byte)Math.Clamp(returnX, byte.MinValue + 1, byte.MaxValue);
                    Configuration.Save();
                }
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
                JobWithRoleHelper.DrawCategory(JobWithRole.All, ref Configuration.W2WJobs);
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
                MakeCommands(Loc.Get("ConfigTab.PreLoop.ExecuteCommands"), ref Configuration.ExecuteCommandsPreLoop, ref Configuration.CustomCommandsPreLoop, ref preLoopCommand, "CommandsPreLoop");

                ImGui.Separator();

                ImGui.TextColored(ImGuiHelper.VersionColor,
                                  string.Format(Loc.Get("ConfigTab.PreLoop.BetweenLoopNote"), Configuration.EnableBetweenLoopActions ? Loc.Get("ConfigTab.PreLoop.Enable").ToLower() : "disabled"));

                if (ImGui.Checkbox(Loc.Get("ConfigTab.PreLoop.RetireTo"), ref Configuration.RetireMode))
                    Configuration.Save();

                using (ImRaii.Disabled(!Configuration.RetireMode))
                {
                    ImGui.SameLine(0, 5);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.BeginCombo("##RetireLocation", Configuration.RetireLocationEnum.ToLocalizedString("RetireLocation")))
                    {
                        foreach (RetireLocation retireLocation in Enum.GetValues(typeof(RetireLocation)))
                            if (ImGui.Selectable(retireLocation.ToLocalizedString("RetireLocation"), Configuration.RetireLocationEnum == retireLocation))
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

                MakeCommands(Loc.Get("ConfigTab.BetweenLoop.ExecuteCommands"), ref Configuration.ExecuteCommandsBetweenLoop, ref Configuration.CustomCommandsBetweenLoop, ref betweenLoopCommand, "CommandsBetweenLoop");

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

                            for (int i = 0; i < module->NumGearsets; i++)
                            {
                                RaptureGearsetModule.GearsetEntry* gearset = module->GetGearset(i);
                                if(ImGui.Selectable(gearset->NameString, Configuration.AutoOpenCoffersGearset == gearset->Id))
                                {
                                    Configuration.AutoOpenCoffersGearset = gearset->Id;
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

                    if (ImGui.Checkbox($"{Loc.Get("ConfigTab.BetweenLoop.ProtectGearsets")}##Desynth{nameof(Configuration.AutoDesynthNoGearset)}", ref Configuration.AutoDesynthNoGearset))
                        Configuration.Save();

                    if (ImGui.CollapsingHeader(Loc.Get("ConfigTab.BetweenLoop.DesynthCategories")))
                    {
                        ImGui.Indent();
                        AgentSalvage.SalvageItemCategory[] values = Enum.GetValues<AgentSalvage.SalvageItemCategory>();
                        for (int index = 0; index < values.Length; index++)
                        {
                            bool   x            = Bitmask.IsBitSet(Configuration.AutoDesynthCategories, index);
                            string categoryName = values[index].ToLocalizedString("DesynthCategory");
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
                ImGui.SetColumnWidth(0, 200 * ImGuiHelpers.GlobalScale);
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
                    if (ImGui.BeginCombo("##PreferredBell", Configuration.PreferredSummoningBellEnum.ToLocalizedString("SummoningBellLocations")))
                    {
                        foreach (SummoningBellLocations summoningBells in Enum.GetValues(typeof(SummoningBellLocations)))
                            if (ImGui.Selectable(summoningBells.ToLocalizedString("SummoningBellLocations")))
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
                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Termination.StopAtLevelHelp"));
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopNoRestedXP"), ref Configuration.StopNoRestedXP))
                    Configuration.Save();

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Termination.StopNoRestedXPHelp"));
                if (ImGui.Checkbox(Loc.Get("ConfigTab.Termination.StopAtItemQty"), ref Configuration.StopItemQty))
                    Configuration.Save();

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Termination.StopAtItemQtyHelp"));
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

                MakeCommands(Loc.Get("ConfigTab.Termination.ExecuteCommandsOnTermination"), ref Configuration.ExecuteCommandsTermination,  ref Configuration.CustomCommandsTermination, ref terminationCommand, "CommandsTermination");

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
                if (ImGui.BeginCombo("##ConfigTerminationMethod", Configuration.TerminationMethodEnum.ToLocalizedString("TerminationMode")))
                {
                    foreach (TerminationMode terminationMode in Enum.GetValues(typeof(TerminationMode)))
                        if (terminationMode != TerminationMode.Kill_PC || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                            if (ImGui.Selectable(terminationMode.ToLocalizedString("TerminationMode"), Configuration.TerminationMethodEnum == terminationMode))
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

            TransportType transportType = MultiboxUtility.TransportType;
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

            bool multiBox = ConfigurationMain.Instance.multiBox;
            if (ImGui.Checkbox(nameof(ConfigurationMain.MultiBox), ref multiBox))
            {
                ConfigurationMain.Instance.MultiBox = multiBox;
                Configuration.Save();
            }

            using(ImRaii.Disabled(ConfigurationMain.Instance.MultiBox))
            {
                ImGui.Indent();
                if(ImGuiEx.EnumCombo(Loc.Get("ConfigTab.Multiboxing.TransportType"), ref transportType))
                {
                    MultiboxUtility.TransportType = transportType;
                    Configuration.Save();
                }

                ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Multiboxing.TransportTypeHelp"));

                switch (transportType)
                {
                    case TransportType.NamedPipe:
                    {
                        string pipeName = MultiboxUtility.PipeName;
                        if(ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.PipeName"), ref pipeName))
                        {
                            MultiboxUtility.PipeName = pipeName;
                            Configuration.Save();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetPipeName"))
                        {
                            MultiboxUtility.PipeName = "AutoDutyPipe";
                                Configuration.Save();
                        }

                        if (!ConfigurationMain.Instance.host)
                        {
                            string serverName = MultiboxUtility.ServerName;
                            if (ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.ServerName"), ref serverName))
                            {
                                MultiboxUtility.ServerName = serverName;
                                Configuration.Save();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerName"))
                            {
                                MultiboxUtility.ServerName = ".";
                                    Configuration.Save();
                            }
                        }

                        break;
                    }
                    case TransportType.Tcp:
                    {
                        if (!ConfigurationMain.Instance.host)
                        {
                            string serverAddress = MultiboxUtility.ServerAddress;
                            if (ImGui.InputText(Loc.Get("ConfigTab.Multiboxing.ServerAddress"), ref serverAddress))
                            {
                                MultiboxUtility.ServerAddress = serverAddress;
                                Configuration.Save();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerAddress"))
                            {
                                MultiboxUtility.ServerAddress = "127.0.0.1";
                                    Configuration.Save();
                            }
                        }

                        int serverPort = MultiboxUtility.ServerPort;
                        if (ImGui.InputInt(Loc.Get("ConfigTab.Multiboxing.ServerPort"), ref serverPort))
                        {
                            MultiboxUtility.ServerPort = serverPort;
                            Configuration.Save();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"{Loc.Get("ConfigTab.Multiboxing.Reset")}##MultiboxResetServerPort"))
                        {
                            MultiboxUtility.ServerPort = 1716;
                                Configuration.Save();
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Multiboxing.Host")}##MultiboxHost", ref ConfigurationMain.Instance.host))
                    Configuration.Save();

                ImGui.Unindent();
            }

            if (ImGui.Checkbox($"{Loc.Get("ConfigTab.Multiboxing.SynchronizePaths")}##MultiboxSynchronizePaths", ref ConfigurationMain.Instance.multiBoxSynchronizePath))
                Configuration.Save();
            ImGuiComponents.HelpMarker(Loc.Get("ConfigTab.Multiboxing.SynchronizePathsHelp"));

            if (ConfigurationMain.Instance.MultiBox)
            {
                ImGui.Indent();
                ImGuiEx.Text(string.Format(Loc.Get("ConfigTab.Multiboxing.Blocking"), MultiboxUtility.stepBlock));

                if(ConfigurationMain.Instance.host)
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
            if (ImGui.Checkbox($"{checkbox}{(execute ? ":" : string.Empty)} ", ref execute))
                Configuration.Save();

            ImGuiComponents.HelpMarker($"{checkbox}.\nFor example, /echo test");

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
                    Configuration.SoundEnum = sound;
                    UIGlobals.PlaySoundEffect((uint)sound);
                    Configuration.Save();
                }

            ImGui.EndCombo();
        }
    }
}
