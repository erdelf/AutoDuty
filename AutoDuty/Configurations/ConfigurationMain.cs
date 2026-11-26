namespace AutoDuty.Configurations;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Windows;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using Helpers;
using Logging;
using Multibox;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NightmareUI.Censoring;
using Formatting = Newtonsoft.Json.Formatting;

[JsonObject(MemberSerialization.OptIn)]
public class ConfigurationMain
{
    public const           string CONFIGNAME_BARE = "Bare";
    public static readonly Guid   CONFIGGUID_BARE = Guid.AllBitsSet;

    public static ConfigurationMain Instance { get; set; } = null!;

    [JsonProperty]
    public string DefaultConfigName { get; set; } = CONFIGNAME_BARE;

    [JsonProperty]
    internal string Language { get; set; } = LocalizationManager.BASE_LANGUAGE;

    private Guid activeProfileGuid = Guid.AllBitsSet;

    public Guid        ActiveProfileGuid => this.activeProfileGuid;
    public ProfileData ActiveProfile     => this.profileByGuid[this.activeProfileGuid];
    public string      ActiveProfileName => this.ActiveProfile.Name;

    public bool Initialized { get; private set; } = false;

    [JsonProperty]
    private readonly HashSet<ProfileData> profileData = [];

    private readonly Dictionary<string, ProfileData> profileByName = [];
    private readonly Dictionary<ulong, ProfileData>  profileByCID  = [];
    private readonly Dictionary<Guid, ProfileData>   profileByGuid = [];

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
    public readonly Dictionary<string, Guid> profileByPluginName = [];

    public ProfileData? GetProfileByGuid(Guid guid) => this.profileByGuid.GetValueOrDefault(guid);

    public void ActivatePluginProfile(string name, bool on)
    {
        if(!on)
        {
            if (this.profileByPluginName.TryGetValue(name, out Guid guid) && this.ActiveProfileGuid == guid)
                this.SetProfileToDefault();
        }
        else
        {
            if (!this.profileByPluginName.TryGetValue(name, out Guid _))
            {
                ConfigurationProfileV2? configClone = this.profileByName.TryGetValue(this.DefaultConfigName, out ProfileData? defaultProfile) ?
                                                          defaultProfile.Config.JSONClone(JsonSerializerSettings) :
                                                          this.profileByGuid[CONFIGGUID_BARE].Config.JSONClone(JsonSerializerSettings);

                int    count = -1;
                string profileName;
                do
                {
                    count++;
                    profileName = name + (count > 0 ? $" ({count})" : string.Empty);
                } while (this.ConfigNames.Contains(profileName));

                ProfileData profile = configClone == null ? this.CreateProfile(profileName) : this.CreateProfile(profileName, configClone);
                this.profileByPluginName[name] = profile.guid;
            }

            if (this.profileByPluginName.TryGetValue(name, out Guid pluginProfileGuid))
                this.SetProfile(pluginProfileGuid);
        }
    }


    [JsonProperty]
    public Dictionary<ulong, CrucibleCharacterData> crucibleConfigByCID = [];

    public const string PLAYLISTNAME_EPHEMERAL = "Ephemeral";
    [JsonProperty]
    public List<Playlist> Playlists { get; set; } = [];

    [JsonProperty]
    public Dictionary<ulong, int> dutyCountSinceReset = [];
    [JsonProperty]
    public DateTime dutyCountResetDate;

    [JsonProperty]
    public StatData stats = new();

    [JsonProperty]
    public HashSet<string> DoNotUpdatePathFiles = [];

    [JsonObject(MemberSerialization.OptOut)]
    public class StatData
    {
        public int                  dungeonsRun;
        public List<DutyDataRecord> dutyRecords = [];
        public TimeSpan             timeSpent   = TimeSpan.Zero;

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
    public IEnumerable<Guid> ConfigGuids => this.profileByGuid.Keys;
     
    public ProfileData GetCurrentProfile
    {
        get
        {
            if (!this.profileByGuid.TryGetValue(this.ActiveProfileGuid, out ProfileData? profiles))
            {
                this.SetProfileToDefault();
                return this.GetCurrentProfile;
            }

            return profiles;
        }
    }

    public ConfigurationProfileV2 GetCurrentConfig => this.GetCurrentProfile.Config;

    public static void Initialization()
    {
        Instance = EzConfig.Init<ConfigurationMain>();

        bool reinit = false;
        if (Instance.profileData.Count == 0)
        {
            string v1ConfigPath = Path.Combine(EzConfig.GetPluginConfigDirectory(), "AutoDutyConfig.json");
            if (File.Exists(v1ConfigPath))
            {
                reinit = true;
                EzConfig.Set(Instance = EzConfig.LoadConfiguration<ConfigurationMain>("AutoDutyConfig.json"));
                Instance.Migrate();
            }
        }

        Instance.Init();

        if(reinit)
            Save();
    }

    public void Init()
    {
        DebugLog("Found profiles: " + this.profileData.Count + "\n" + string.Join("\n", this.profileData.Select(p => p.Name)) + "\nDefault: " + this.DefaultConfigName);
        
        if (this.profileData.Count == 0)
        {
            this.profileData.Add(new ProfileData { Name = "Default", guid = Guid.NewGuid(), Config = new ConfigurationProfileV2() });
            this.DefaultConfigName = "Default";
        }

        void RegisterProfileData(ProfileData profile)
        {
            if (profile.CIDs.Count != 0)
                foreach (ulong cid in profile.CIDs)
                    this.profileByCID[cid] = profile;

            this.profileByName[profile.Name] = profile;

            if(profile.guid == Guid.Empty)
                profile.guid = Guid.NewGuid();

            this.profileByGuid[profile.guid] = profile;
        }

        foreach (ProfileData profile in this.profileData)
            if(profile.Name != CONFIGNAME_BARE && profile.guid != Guid.AllBitsSet)
                RegisterProfileData(profile);

        ProfileData profileBare = new()
                                  {
                                      Name   = CONFIGNAME_BARE,
                                      guid   = Guid.AllBitsSet,
                                      Config = new ConfigurationProfileV2()
                                  };
        profileBare.Config.Loop.Pre.Enabled     = false;
        profileBare.Config.Loop.Between.Enabled = false;
        profileBare.Config.Loop.Termination.Enabled = false;
        profileBare.Config.DutyConfig.LootTreasure  = false;

        RegisterProfileData(profileBare);

        this.SetProfileToDefault();

        if(this.Playlists.Count == 0)
            this.Playlists.Add(new Playlist());

        this.Playlists[0] = new Playlist { Name = PLAYLISTNAME_EPHEMERAL };

        Plugin.PlaylistCurrent = this.Playlists[0].JSONClone();
        MainTab.playlistName   = PLAYLISTNAME_EPHEMERAL;
    }

    private void Migrate()
    {
        static void DebugLog(string message) =>
            ConfigurationMain.DebugLog("[Migration]: " + message);

        DebugLog("Migrating configs...");

    #pragma warning disable CS0618 // Type or member is obsolete
    #region V1
        DebugLog("Migrating V1 configs..");

        string v1ConfigPath = Path.Combine(EzConfig.GetPluginConfigDirectory(), "AutoDutyConfig.json");
        if (File.Exists(v1ConfigPath))
        {
            DebugLog("Found V1 config file");
            try
            {
                string json;
                using (StreamReader streamReader = new(v1ConfigPath, Encoding.UTF8))
                    json = streamReader.ReadToEnd();

                JObject? configMain = JsonConvert.DeserializeObject<JObject>(json);
                //XmlDocument? configMain = JsonConvert.DeserializeXmlNode(json);

                if (configMain?["profileData"] is JArray profileList)
                {
                    DebugLog($"Found {profileList.Count} profiles");

                    foreach (JToken profileData in profileList)
                    {
                        string?        profileName = (string?) profileData["Name"];
                        Obsolete.Configuration? oldConfig   = EzConfig.DefaultSerializationFactory.Deserialize<Obsolete.Configuration>(profileData["Config"].ToString());
                        DebugLog($"Found profile: {profileName}");
                        if (oldConfig != null)
                        {
                            DebugLog("Migrating profile");
                            ConfigurationProfileV2 migratedConfig = this.MigrateConfigurationV1ToV2(oldConfig);
                            this.profileData.First(pf => pf.Name == profileName).Config = migratedConfig;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Failed to migrate V1 configs: {ex.Message}");
            }
        }
    #endregion

        if (this.profileData.Count != 0)
            return;

    #region Legacy
        if (Svc.PluginInterface.ConfigFile.Exists)
        {
            try
            {
                Obsolete.Configuration? configuration = EzConfig.DefaultSerializationFactory.Deserialize<Obsolete.Configuration>(File.ReadAllText(Svc.PluginInterface.ConfigFile.FullName, Encoding.UTF8));
                if (configuration != null)
                {
                    ConfigurationProfileV2 migratedConfig = this.MigrateConfigurationV1ToV2(configuration);
                    this.CreateProfile("Migrated", migratedConfig);
                    this.SetProfileAsDefault();
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Failed to migrate Legacy config: {ex.Message}");
            }
        }
    #endregion
    #pragma warning restore CS0618 // Type or member is obsolete
    }

    public bool SetProfile(string name)
    {
        DebugLog("Changing profile to: " + name);
        return this.profileByName.TryGetValue(name, out ProfileData? profile) && this.SetProfile(profile.guid);
    }

    public bool SetProfile(Guid guid)
    {
        DebugLog("Changing profile to: " + guid);
        if (this.profileByGuid.ContainsKey(guid))
        {
            this.activeProfileGuid = guid;
            return true;
        }
        return false;
    }

    public void SetProfileAsDefault()
    {
        if (this.profileByGuid.ContainsKey(this.ActiveProfileGuid))
        {
            this.DefaultConfigName = this.ActiveProfileName;
            Save();
        }
    }

    public void SetProfileToDefault()
    {
        this.SetProfile(CONFIGGUID_BARE);
        Svc.Framework.RunOnTick(() =>
                                {
                                    DebugLog($"Setting to default profile for {(Player.Available ? Player.Name : "Unknown")} ({Player.CID}) {PlayerHelper.IsValid}");

                                    if (Player.Available && this.profileByCID.TryGetValue(Player.CID, out ProfileData? charProfile))
                                        if (this.SetProfile(charProfile.guid))
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

    public ProfileData CreateNewProfile() =>
        this.CreateProfile("Profile" + (this.profileByName.Count - 1).ToString(CultureInfo.InvariantCulture));

    public ProfileData CreateProfile(string name) =>
        this.CreateProfile(name, new ConfigurationProfileV2());

    public ProfileData CreateProfile(string name, ConfigurationProfileV2 config, bool setActive = true)
    {
        DebugLog($"Creating new Profile: {name}");

        ProfileData profile = new()
                              {
                                  Name   = name,
                                  guid   = Guid.NewGuid(),
                                  Config = config
                              };

        this.profileData.Add(profile);
        this.profileByName.Add(name, profile);
        this.profileByGuid.Add(profile.guid, profile);

        if(setActive)
            this.SetProfile(name);

        return profile;
    }

    public void DuplicateCurrentProfile()
    {
        string name;
        int    counter = 0;

        string templateName = this.ActiveProfileName.EndsWith("_Copy") ? this.ActiveProfileName : $"{this.ActiveProfileName}_Copy";

        do
            name = counter++ > 0 ? $"{templateName}{counter}" : templateName;
        while (this.profileByName.ContainsKey(name));

        ConfigurationProfileV2 newConfig = this.GetCurrentConfig.JSONClone(JsonSerializerSettings);
        if (newConfig != null)
            this.CreateProfile(name, newConfig);
    }

    public void RemoveCurrentProfile()
    {
        DebugLog("Removing " + this.ActiveProfileName);
        this.profileData.Remove(this.GetCurrentProfile);
        this.profileByName.Remove(this.ActiveProfileName);
        this.profileByGuid.Remove(this.ActiveProfileGuid);
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

        Save();

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

                                    if (this.profileByCID.TryGetValue(cid, out ProfileData? oldProfile))
                                        oldProfile.CIDs.Remove(cid);

                                    this.GetCurrentProfile.CIDs.Add(cid);
                                    this.profileByCID.Add(cid, this.GetCurrentProfile);
                                    this.charByCID[cid] = new CharData
                                                          {
                                                              CID   = cid,
                                                              Name  = Player.Name,
                                                              World = Player.CurrentWorldName
                                                          };
                                    Save();

                                    LevelingHelper.ResetLevelingDuties();
                                });

    public void RemoveCharacterDefault() =>
        Svc.Framework.RunOnTick(() =>
                                {
                                    if (!PlayerHelper.IsValid)
                                        return;

                                    ulong cid = Player.CID;

                                    this.profileByGuid[this.ActiveProfileGuid].CIDs.Remove(cid);
                                    this.profileByCID.Remove(cid);

                                    Save();
                                });

    public static void DebugLog(string message) => 
        ADLogging.Debug($"Configuration Main: {message}");

    public static void Save()
    {
        if (!ConfigOverrideHelper.HasOverrides)
            EzConfig.Save();
    }

#pragma warning disable CS0618 // Type or member is obsolete
    private ConfigurationProfileV2 MigrateConfigurationV1ToV2(Obsolete.Configuration oldConfig)
#pragma warning restore CS0618 // Type or member is obsolete
    {
        ConfigurationProfileV2 newConfig = new();

        

        // Meta Config
        newConfig.Meta.AutoDutyModeEnum               = oldConfig.AutoDutyModeEnum;
        newConfig.Meta.LoopTimes                      = oldConfig.LoopTimes;
        newConfig.Meta.DutyModeEnum                   = oldConfig.DutyModeEnum;
        newConfig.Meta.Unsynced                       = oldConfig.Unsynced;
        newConfig.Meta.HideUnavailableDuties          = oldConfig.HideUnavailableDuties;
        newConfig.Meta.PreferTrustOverSupportLeveling = oldConfig.PreferTrustOverSupportLeveling;
        newConfig.Meta.SquadronAssignLowestMembers    = oldConfig.SquadronAssignLowestMembers;
        newConfig.Meta.ShowMainWindowOnStartup        = oldConfig.ShowMainWindowOnStartup;
        newConfig.Meta.PathSelectionsByPath           = oldConfig.PathSelectionsByPath;

        // Log Config
        newConfig.Log.AutoScroll    = oldConfig.AutoScroll;
        newConfig.Log.LogEventLevel = oldConfig.LogEventLevel;

        // Overlay Config
        newConfig.Overlay.Show            = oldConfig.ShowOverlay;
        newConfig.Overlay.HideWhenStopped = oldConfig.HideOverlayWhenStopped;
        newConfig.Overlay.Lock            = oldConfig.LockOverlay;
        newConfig.Overlay.NoBG            = oldConfig.OverlayNoBG;
        newConfig.Overlay.AnchorBottom    = oldConfig.OverlayAnchorBottom;
        newConfig.Overlay.ShowDutyLoopText       = oldConfig.ShowDutyLoopText;
        newConfig.Overlay.ShowActionText         = oldConfig.ShowActionText;
        newConfig.Meta.UseSliderInputs        = oldConfig.UseSliderInputs;

        newConfig.Overlay.GoToActions = oldConfig.GotoButton;
        newConfig.Overlay.LoopActions =
        [
            new GCTurnInLoopActionConfig
            {
                Enabled       = oldConfig.autoGCTurnin,
                SlotsLeftBool = oldConfig.AutoGCTurninSlotsLeftBool,
                SlotsLeft     = oldConfig.AutoGCTurninSlotsLeft,
                UseTicket     = oldConfig.AutoGCTurninUseTicket
            },
            new DesynthLoopActionConfig
            {
                Enabled      = oldConfig.DesynthButton,
                SkillUp      = oldConfig.AutoDesynthSkillUp,
                SkillUpLimit = oldConfig.AutoDesynthSkillUpLimit,
                NQOnly       = oldConfig.AutoDesynthNQOnly,
                NoGearset    = oldConfig.AutoDesynthNoGearset,
                Categories   = oldConfig.AutoDesynthCategories
            },
            new ExtractLoopActionConfig
            {
                Enabled        = oldConfig.ExtractButton,
                AutoExtractAll = oldConfig.AutoExtractAll
            },
            new RepairLoopActionConfig
            {
                Enabled            = oldConfig.RepairButton,
                AutoRepairPct      = oldConfig.AutoRepairPct,
                AutoRepairSelf     = oldConfig.AutoRepairSelf,
                PreferredRepairNPC = oldConfig.PreferredRepairNPC
            },
            new AutoEquipLoopActionConfig
            {
                Enabled                  = oldConfig.EquipButton,
                RecommendedGearSource    = oldConfig.AutoEquipRecommendedGearSource,
                GearsetterOldToInventory = oldConfig.AutoEquipRecommendedGearGearsetterOldToInventory
            },
            new CofferOpenLoopActionConfig
            {
                Enabled      = oldConfig.CofferButton,
                Gearset      = oldConfig.AutoOpenCoffersGearset,
                UseBlacklist = oldConfig.AutoOpenCoffersBlacklistUse,
                Blacklist    = oldConfig.AutoOpenCoffersBlacklist
            },
            new TripleTriadUseLoopActionConfig(),
            new TripleTriadSellLoopActionConfig
            {
                TripleTriadSellMinItemCount = oldConfig.TripleTriadSellMinItemCount,
                TripleTriadSellMinSlotCount = oldConfig.TripleTriadSellMinSlotCount
            },
        ];

        
        // Duty Config
        newConfig.DutyConfig.AutoExitDuty                  = oldConfig.AutoExitDuty;
        newConfig.DutyConfig.OnlyExitWhenDutyDone          = oldConfig.OnlyExitWhenDutyDone;
        newConfig.DutyConfig.AutoManageRotationPluginState = oldConfig.AutoManageRotationPluginState;
        newConfig.DutyConfig.RotationPlugin                = oldConfig.rotationPlugin;

        // Wrath Config
        newConfig.DutyConfig.Wrath.AutoSetupJobs    = oldConfig.Wrath_AutoSetupJobs;
        newConfig.DutyConfig.Wrath.TargetingTank    = oldConfig.Wrath_TargetingTank;
        newConfig.DutyConfig.Wrath.TargetingNonTank = oldConfig.Wrath_TargetingNonTank;

        // RSR Config
        newConfig.DutyConfig.RSR.TargetHostileType    = oldConfig.RSR_TargetHostileType;
        newConfig.DutyConfig.RSR.TargetingTypeTank    = oldConfig.RSR_TargetingTypeTank;
        newConfig.DutyConfig.RSR.TargetingTypeNonTank = oldConfig.RSR_TargetingTypeNonTank;

        // Boss Mod Config
        newConfig.DutyConfig.AutoManageBossModAISettings           = oldConfig.AutoManageBossModAISettings;
        newConfig.DutyConfig.BossMod.UpdatePresetsAutomatically    = oldConfig.BM_UpdatePresetsAutomatically;
        newConfig.DutyConfig.BossMod.MaxDistanceToTargetRoleBased  = oldConfig.MaxDistanceToTargetRoleBased;
        newConfig.DutyConfig.BossMod.MaxDistanceToTargetFloat      = oldConfig.MaxDistanceToTargetFloat;
        newConfig.DutyConfig.BossMod.MaxDistanceToTargetAoEFloat   = oldConfig.MaxDistanceToTargetAoEFloat;
        newConfig.DutyConfig.BossMod.PositionalRoleBased           = oldConfig.PositionalRoleBased;
        newConfig.DutyConfig.BossMod.MaxDistanceToTargetRoleMelee  = oldConfig.MaxDistanceToTargetRoleMelee;
        newConfig.DutyConfig.BossMod.MaxDistanceToTargetRoleRanged = oldConfig.MaxDistanceToTargetRoleRanged;

        // Positional
        newConfig.DutyConfig.BossMod.PositionalEnum    = oldConfig.PositionalEnum;
        newConfig.DutyConfig.BossMod.PositionalAvarice = oldConfig.positionalAvarice;

        // Navigation and Loot
        newConfig.DutyConfig.AutoManageVnavAlignCamera  = oldConfig.AutoManageVnavAlignCamera;
        newConfig.DutyConfig.LootTreasure               = oldConfig.LootTreasure;
        newConfig.DutyConfig.LootMethodEnum             = oldConfig.LootMethodEnum;
        newConfig.DutyConfig.LootBossTreasureOnly       = oldConfig.LootBossTreasureOnly;
        newConfig.DutyConfig.TreasureCofferScanDistance = oldConfig.TreasureCofferScanDistance;

        // Stuck Config
        newConfig.DutyConfig.Stuck.RebuildNavmeshOnStuck          = oldConfig.RebuildNavmeshOnStuck;
        newConfig.DutyConfig.Stuck.RebuildNavmeshAfterStuckXTimes = oldConfig.RebuildNavmeshAfterStuckXTimes;
        newConfig.DutyConfig.Stuck.MinStuckTime                   = oldConfig.MinStuckTime;
        newConfig.DutyConfig.Stuck.StuckOnStep                    = oldConfig.StuckOnStep;
        newConfig.DutyConfig.Stuck.StuckReturnX                   = oldConfig.StuckReturnX;
        newConfig.DutyConfig.Stuck.StuckReturn                    = oldConfig.StuckReturn;

        // Path and Render
        newConfig.DutyConfig.PathDrawEnabled          = oldConfig.PathDrawEnabled;
        newConfig.DutyConfig.PathDrawStepCount        = oldConfig.PathDrawStepCount;
        newConfig.DutyConfig.DisableRenderWhileActive = oldConfig.DisableRenderWhileActive;

        // Plugin Alternatives
        newConfig.DutyConfig.OverridePartyValidation        = oldConfig.OverridePartyValidation;
        newConfig.DutyConfig.UsingAlternativeRotationPlugin = oldConfig.UsingAlternativeRotationPlugin;
        newConfig.DutyConfig.UsingAlternativeMovementPlugin = oldConfig.UsingAlternativeMovementPlugin;
        newConfig.DutyConfig.UsingAlternativeBossPlugin     = oldConfig.UsingAlternativeBossPlugin;

        // W2W Config
        newConfig.DutyConfig.TreatUnsyncAsW2W = oldConfig.TreatUnsyncAsW2W;
        newConfig.DutyConfig.W2WJobs          = oldConfig.W2WJobs;

        // Leveling
        newConfig.DutyConfig.LevelingListExperimentalEntries = oldConfig.LevelingListExperimentalEntries;

        // Loop Config - Pre Loop
        newConfig.Loop.Pre.Enabled                                          = oldConfig.EnablePreLoopActions;

        newConfig.Loop.Pre.Actions =
        [
            new ExecuteCommandsLoopActionConfig
            {
                Enabled        = oldConfig.ExecuteCommandsPreLoop,
                CustomCommands = oldConfig.CustomCommandsPreLoop
            },
            new PlaylistPreLoopActionConfig(),
            new ConsumeItemsLoopActionConfig
            {
                Enabled                 = oldConfig.AutoConsume,
                AutoConsumeIgnoreStatus = oldConfig.AutoConsumeIgnoreStatus,
                AutoConsumeTime         = oldConfig.AutoConsumeTime,
                AutoConsumeItemsList    = oldConfig.AutoConsumeItemsList
            },
            new AutoEquipLoopActionConfig
            {
                Enabled                  = oldConfig.AutoEquipRecommendedGear,
                RecommendedGearSource    = oldConfig.AutoEquipRecommendedGearSource,
                GearsetterOldToInventory = oldConfig.AutoEquipRecommendedGearGearsetterOldToInventory
            },
            new RepairLoopActionConfig
            {
                Enabled            = oldConfig.AutoRepair,
                AutoRepairPct      = oldConfig.AutoRepairPct,
                AutoRepairSelf     = oldConfig.AutoRepairSelf,
                PreferredRepairNPC = oldConfig.PreferredRepairNPC
            },
            new RetireLoopActionConfig
            {
                Enabled            = oldConfig.RetireMode,
                RetireLocationEnum = oldConfig.RetireLocationEnum
            }
        ];

        // Loop Config - Between Loop
        newConfig.Loop.Between.Enabled                        = oldConfig.EnableBetweenLoopActions;
        newConfig.Loop.Between.ExecuteLastLoop                = oldConfig.ExecuteBetweenLoopActionLastLoop;

        newConfig.Loop.Between.Actions =
        [
            new WaitLoopActionConfig
            {
                WaitTime = oldConfig.WaitTimeBeforeAfterLoopActions * 1000
            },
            new ExecuteCommandsLoopActionConfig
            {
                Enabled        = oldConfig.ExecuteCommandsBetweenLoop,
                CustomCommands = oldConfig.CustomCommandsBetweenLoop
            },
            new CofferOpenLoopActionConfig
            {
                Enabled = oldConfig.AutoOpenCoffers,
                Gearset = oldConfig.AutoOpenCoffersGearset,
                UseBlacklist = oldConfig.AutoOpenCoffersBlacklistUse,
                Blacklist = oldConfig.AutoOpenCoffersBlacklist
            },
            new AutoRetainerLoopActionConfig
            {
                Enabled                    = oldConfig is { EnableAutoRetainer: true, EnableAutoRetainerMultiMode: false },
                PreferredSummoningBellEnum = oldConfig.PreferredSummoningBellEnum,
                AutoRetainerRemainingTime  = oldConfig.AutoRetainer_RemainingTime
            },
            new AutoRetainerMultiModeLoopActionConfig
            {
                Enabled = oldConfig.EnableAutoRetainerMultiMode,
                MultiModeType = oldConfig.AutoRetainerMultiModeType
            },
            new PlaylistSwitchLoopActionConfig(),
            new AutoEquipLoopActionConfig
            {
                Enabled                  = oldConfig.AutoEquipRecommendedGear,
                RecommendedGearSource    = oldConfig.AutoEquipRecommendedGearSource,
                GearsetterOldToInventory = oldConfig.AutoEquipRecommendedGearGearsetterOldToInventory
            },
            new GlamourLoopActionConfig
            {
                Enabled = oldConfig.GlamourChestEntrust
            },
            new ArmoireLoopActionConfig
            {
                Enabled = oldConfig.ArmoireEntrust
            },
            new RepairLoopActionConfig
            {
                Enabled            = oldConfig.AutoRepair,
                AutoRepairPct      = oldConfig.AutoRepairPct,
                AutoRepairSelf     = oldConfig.AutoRepairSelf,
                PreferredRepairNPC = oldConfig.PreferredRepairNPC
            },
            new ExtractLoopActionConfig
            {
                Enabled = oldConfig.AutoExtract,
                AutoExtractAll = oldConfig.AutoExtractAll
            },
            new DesynthLoopActionConfig
            {
                Enabled = oldConfig.AutoDesynth,
                SkillUp = oldConfig.AutoDesynthSkillUp,
                SkillUpLimit = oldConfig.AutoDesynthSkillUpLimit,
                NQOnly = oldConfig.AutoDesynthNQOnly,
                NoGearset = oldConfig.AutoDesynthNoGearset,
                Categories = oldConfig.AutoDesynthCategories
            },
            new GCTurnInLoopActionConfig
            {
                Enabled = oldConfig.AutoGCTurnin,
                SlotsLeftBool = oldConfig.AutoGCTurninSlotsLeftBool,
                SlotsLeft = oldConfig.AutoGCTurninSlotsLeft,
                UseTicket = oldConfig.AutoGCTurninUseTicket
            },
            new TripleTriadUseLoopActionConfig
            {
                Enabled = oldConfig.TripleTriadRegister
            },
            new TripleTriadSellLoopActionConfig
            {
                Enabled = oldConfig.TripleTriadSell,
                TripleTriadSellMinItemCount = oldConfig.TripleTriadSellMinItemCount,
                TripleTriadSellMinSlotCount = oldConfig.TripleTriadSellMinSlotCount
            },
            new DiscardItemsLoopActionConfig
            {
                Enabled = oldConfig.DiscardItems
            },
            new RetireLoopActionConfig
            {
                Enabled            = oldConfig.RetireMode,
                RetireLocationEnum = oldConfig.RetireLocationEnum
            },
            new ConsumeItemsLoopActionConfig
            {
                Enabled                 = oldConfig.AutoConsume,
                AutoConsumeIgnoreStatus = oldConfig.AutoConsumeIgnoreStatus,
                AutoConsumeTime         = oldConfig.AutoConsumeTime,
                AutoConsumeItemsList    = oldConfig.AutoConsumeItemsList
            },
        ];

        // Loop Config - Termination
        newConfig.Loop.Termination.Enabled                       = oldConfig.EnableTerminationActions;
        newConfig.Loop.Termination.StopLevel                     = oldConfig.StopLevel;
        newConfig.Loop.Termination.StopLevelInt                  = oldConfig.StopLevelInt;
        newConfig.Loop.Termination.StopNoRestedXP                = oldConfig.StopNoRestedXP;
        newConfig.Loop.Termination.StopItemQty                   = oldConfig.StopItemQty;
        newConfig.Loop.Termination.StopItemAll                   = oldConfig.StopItemAll;
        newConfig.Loop.Termination.StopItemQtyItemDictionary     = oldConfig.StopItemQtyItemDictionary;
        newConfig.Loop.Termination.StopItemQtyInt                = oldConfig.StopItemQtyInt;
        newConfig.Loop.Termination.StopWhenDutyGathered          = oldConfig.StopWhenDutyGathered;
        newConfig.Loop.Termination.TerminationBLUSpellsEnabled   = oldConfig.TerminationBLUSpellsEnabled;
        newConfig.Loop.Termination.TerminationBLUSpells          = oldConfig.TerminationBLUSpells;
        newConfig.Loop.Termination.TerminationBLUSpellsAll       = oldConfig.TerminationBLUSpellsAll;
        newConfig.Loop.Termination.TerminationInventoryFree      = oldConfig.TerminationInventoryFree;
        newConfig.Loop.Termination.TerminationInventoryFreeSlots = oldConfig.TerminationInventoryFreeSlots;
        newConfig.Loop.Termination.TerminationiLvl               = oldConfig.TerminationiLvl;
        newConfig.Loop.Termination.TerminationiLvlInt            = oldConfig.TerminationiLvlInt;

        newConfig.Loop.Termination.Actions =
        [
            new ExecuteCommandsLoopActionConfig
            {
                Enabled        = oldConfig.ExecuteCommandsTermination,
                CustomCommands = oldConfig.CustomCommandsTermination
            },
            new PlaySoundLoopActionConfig
            {
                Enabled              = oldConfig.PlayEndSound,
                CustomSound          = oldConfig.CustomSound,
                CustomSoundVolume    = oldConfig.CustomSoundVolume,
                SoundEnum            = oldConfig.SoundEnum,
                SoundPath            = oldConfig.SoundPath
            }
        ];

        newConfig.Loop.Termination.TerminationMethodEnum         = oldConfig.TerminationMethodEnum;
        newConfig.Loop.Termination.TerminationKeepActive         = oldConfig.TerminationKeepActive;

        // Other Config
        newConfig.SelectedTrustMembers = oldConfig.SelectedTrustMembers;
        
        return newConfig;
    }

    public static JsonSerializerSettings JsonSerializerSettings { get; } = new()
                                                                           {
                                                                               Formatting                     = Formatting.Indented,
                                                                               DefaultValueHandling           = DefaultValueHandling.Include,
                                                                               Converters                     = [new StringEnumConverter(new DefaultNamingStrategy())],
                                                                               TypeNameHandling               = TypeNameHandling.Auto,
                                                                               TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                                                                               Culture                        = CultureInfo.InvariantCulture,
                                                                               SerializationBinder            = new AutoDutySerializationBinder(),
                                                                               ObjectCreationHandling         = ObjectCreationHandling.Replace
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

            #pragma warning disable CS0618 // Type or member is obsolete
                Type? type = typeof(Obsolete.Configuration).Assembly.GetType(typeName);
            #pragma warning restore CS0618 // Type or member is obsolete
                if (type != null)
                    return type;

                type = typeof(ConfigurationProfileV2).Assembly.GetType(typeName);
                if (type != null)
                    return type;
            }

            return base.BindToType(assemblyName, typeName);
        }
    }

    [JsonObject(MemberSerialization.OptOut)]
    public class ProfileData
    {
        public required string                 Name;
        public required Guid                   guid;
        public          HashSet<ulong>         CIDs = [];
        public required ConfigurationProfileV2 Config;
    }

    public class AutoDutySerializationFactory : DefaultSerializationFactory, ISerializationFactory
    {
        public override string DefaultConfigFileName { get; } = "AutoDutyConfigV2.json";

        public new string Serialize(object config) =>
            base.Serialize(config);

        public override byte[] SerializeAsBin(object config) =>
            Encoding.UTF8.GetBytes(this.Serialize(config));
    }
}