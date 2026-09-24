global using AutoDuty.Managers;
global using ECommons.GameHelpers;
global using static AutoDuty.AutoDuty;
global using static AutoDuty.Data.Classes;
global using static AutoDuty.Data.Enums;
global using static AutoDuty.Data.Extensions;
using AutoDuty.External;
using AutoDuty.Helpers;
using AutoDuty.IPC;
using AutoDuty.Properties;
using AutoDuty.Updater;
using AutoDuty.Windows;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Serilog.Events;
using System.Diagnostics;
using System.Numerics;

namespace AutoDuty;

using Configurations;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.DutyState;
using Data;
using ECommons.Automation.NeoTaskManager;
using ECommons.Configuration;
using ECommons.EzIpcManager;
using ECommons.IPC.Subscribers;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Multibox;
using Pictomancy;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Data.Classes;
using TaskManager = ECommons.Automation.NeoTaskManager.TaskManager;

// TODO:
// Scrapped interable list, going to implement an internal list that when a interactable step end in fail, the Dataid gets add to the list and is scanned for from there on out, if found we goto it and get it, then remove from list.
// Need to expand AutoRepair to include check for level and stuff to see if you are eligible for self repair. and check for dark matter

public sealed class AutoDuty : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    internal List<PathAction>           Actions       { get;       set; } = [];
    internal List<uint>                 Interactables { get;       set; } = [];
    internal int                        currentLoop                       = 0;
    internal KeyValuePair<ushort, Job?> currentPlayerItemLevelAndClassJob = new(0, null);

    internal Content? CurrentTerritoryContent
    {
        get => Configuration.Meta.AutoDutyModeEnum switch
        {
            AutoDutyMode.Playlist when this.States.HasFlag(PluginState.Looping) || !InDungeon => (this.PlaylistCurrent.Entries.Count >= 0 && this.playlistIndex < this.PlaylistCurrent.Entries.Count && this.playlistIndex >= 0) ?
                                                                                                     this.PlaylistCurrent.Entries[this.playlistIndex].Content : null,
            AutoDutyMode.Looping or _ => field
        };
        set
        {
            this.currentPlayerItemLevelAndClassJob = PlayerHelper.IsValid ? new KeyValuePair<ushort, Job?>(InventoryHelper.CurrentItemLevel, Player.Job) : new KeyValuePair<ushort, Job?>(0, null);
            field           = value;
        }
    } = null;

    internal byte VariantPath
    {
        get => Configuration.Meta.AutoDutyModeEnum switch
        {
            AutoDutyMode.Playlist when this.States.HasFlag(PluginState.Looping) || !InDungeon => (this.PlaylistCurrent.Entries.Count >= 0 && this.playlistIndex < this.PlaylistCurrent.Entries.Count && this.playlistIndex >= 0) ?
                                                                                                     this.PlaylistCurrent.Entries[this.playlistIndex].variantPathIndex : (byte) 0,
            AutoDutyMode.Looping or _ => field
        };
        set;
    }

    internal uint currentTerritoryType = 0;
    internal int  currentPath          = -1;

    internal Playlist PlaylistCurrent
    {
        get => field;
        set
        {
            if (field != value)
            {
                field = value;

                this.playlistIndex = 0;
            }
        }
    } = new();

    internal int      playlistIndex   = 0;

    internal PlaylistEntry? PlaylistCurrentEntry => this.playlistIndex >= 0 && this.playlistIndex < this.PlaylistCurrent.Entries.Count ?
                                                        this.PlaylistCurrent.Entries[this.playlistIndex] : null;

    internal bool SupportLevelingEnabled => this.LevelingModeEnum == LevelingMode.Support;
    internal bool TrustLevelingEnabled   => this.LevelingModeEnum.IsTrustLeveling();
    internal bool LevelingEnabled        => this.LevelingModeEnum != LevelingMode.None;

    internal static   string         Name   => "AutoDuty";
    internal static   AutoDuty       Plugin { get; private set; } = null!;
    internal readonly DirectoryInfo  pathsDirectory   = null!;
    internal readonly FileInfo       assemblyFileInfo = null!;
    internal readonly FileInfo       configFile       = null!;
    internal readonly DirectoryInfo? dalamudDirectory;
    internal          DirectoryInfo? assemblyDirectoryInfo;

    internal static   ConfigurationProfileV2 Configuration => ConfigurationMain.Instance.GetCurrentConfig;
    internal readonly WindowSystem           windowSystem = new("AutoDuty");

    internal DateTime runStartTime = DateTime.UnixEpoch;

    public   int   Version { get; set; }
    internal Stage previousStage = Stage.Stopped;

    internal Stage Stage
    {
        get;
        set
        {
            switch (value)
            {
                case Stage.Stopped:
                    this.StopAndResetAll();
                    break;
                case Stage.Paused:
                    this.previousStage = this.Stage;
                    if (VNavmesh_IPCSubscriber.Path_NumWaypoints > 0)
                        VNavmesh_IPCSubscriber.Path_Stop();
                    FollowHelper.SetFollow(null);
                    this.taskManager.StepMode =  true;
                    this.States               |= PluginState.Paused;
                    break;
                case Stage.Action:
                    this.ActionInvoke();
                    break;
                case Stage.Condition:
                    this.action = $"ConditionChange";
                    SchedulerHelper.ScheduleAction("ConditionChangeStageReadingPath", () => field = Stage.Reading_Path,
                                                   () => !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51] && !Svc.Condition[ConditionFlag.Jumping61]);
                    break;
                case Stage.Waiting_For_Combat:
                    BossMod_IPCSubscriber.SetRange(Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat);
                    break;
                case Stage.Reading_Path:
                    if (field is not Stage.Waiting_For_Combat and not Stage.Revived and not Stage.Looping and not Stage.Idle)
                        MultiboxUtility.MultiboxBlockingNextStep = true;
                    break;
                case Stage.Idle:
                    if (VNavmesh_IPCSubscriber.Path_NumWaypoints > 0)
                        VNavmesh_IPCSubscriber.Path_Stop();
                    break;
                case Stage.Looping:
                    if(this.runStartTime.Equals(DateTime.UnixEpoch))
                        this.runStartTime = DateTime.UtcNow;
                    goto case Stage.Moving;
                case Stage.Moving:
                case Stage.Dead:
                case Stage.Revived:
                case Stage.Interactable:
                default:
                    break;
            }

            if (value is not Stage.Paused)
            {
                this.taskManager.StepMode =  false;
                this.States               &= ~PluginState.Paused;
            }

            if (value is Stage.Stopped or Stage.Paused && !this.runStartTime.Equals(DateTime.UnixEpoch))
            {
                ConfigurationMain.Instance.stats.timeSpent += DateTime.UtcNow.Subtract(this.runStartTime);
                this.runStartTime                          =  DateTime.UnixEpoch;
                ConfigurationProfileV2.Save();
            }

            Svc.Log.Debug($"Stage from {field.ToCustomString()} to {value.ToCustomString()}");
            field = value;
        }
    } = Stage.Stopped;

    internal LevelingMode LevelingModeEnum
    {
        get => this.levelingModeEnum;
        set
        {
            if (value != LevelingMode.None)
            {
                Svc.Log.Debug($"Setting Leveling mode to {value}");
                Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(value);

                if (duty != null)
                {
                    this.levelingModeEnum     = value;
                    MainTab.DutySelected      = ContentPathsManager.DictionaryPaths[duty.TerritoryType];
                    this.CurrentTerritoryContent = duty;
                    MainTab.DutySelected.SelectPath(out this.currentPath);
                    Svc.Log.Debug($"Leveling Mode: Setting duty to {duty.Name}");
                }
                else
                {
                    MainTab.DutySelected         = null;
                    this.mainListClicked         = false;
                    this.CurrentTerritoryContent = null;
                    this.levelingModeEnum        = LevelingMode.None;
                    Svc.Log.Debug($"Leveling Mode: No appropriate leveling duty found");
                }
            }
            else
            {
                MainTab.DutySelected         = null;
                this.mainListClicked         = false;
                this.CurrentTerritoryContent = null;
                this.levelingModeEnum           = LevelingMode.None;
            }
        }
    }

    internal PluginState States
    {
        get;
        set
        {
            field = value;

            if (Configuration.DutyConfig.DisableRenderWhileActive)
                if(field == PluginState.None)
                    RenderDisableManager.RemoveRequest();
                else
                    RenderDisableManager.PlaceRequest();

        }
    } = PluginState.None;

    internal int           indexer         = -1;
    internal bool          mainListClicked = false;
    internal IBattleChara? bossObject;

    internal static IGameObject? ClosestObject => 
        Svc.Objects.Where(o => o.IsTargetable && o.ObjectKind.EqualsAny(ObjectKind.EventObj, ObjectKind.BattleNpc)).
            OrderBy(ObjectHelper.GetDistanceToPlayer).TryGetFirst(out IGameObject gameObject) ? gameObject : null;
    internal readonly OverrideCamera           overrideCamera = null!;
    internal          MainWindow               MainWindow { get; init; } = null!;
    internal          Overlay                  Overlay    { get; init; } = null!;
    internal static   bool                     InDungeon  => ContentHelper.DictionaryContent.ContainsKey(Svc.ClientState.TerritoryType);

    internal          bool                     skipTreasureCoffer = false;
    internal          string                   action             = "";
    internal          string                   pathFile           = "";
    internal readonly TaskManager              taskManager        = null!;
    internal          Job                      jobLastKnown;
    internal          DutyState                dutyState         = DutyState.None;
    internal          PathAction               pathAction        = new();
    internal readonly List<Classes.LogMessage> dalamudLogEntries = [];
    private           LevelingMode             levelingModeEnum  = LevelingMode.None;
    private const     string                   CommandName       = "/autoduty";
    private readonly  DirectoryInfo            configDirectory   = null!;
    public readonly   ActionsManager           actions           = null!;
    private readonly  SquadronManager          squadronManager   = null!;
    private readonly  VariantManager           variantManager    = null!;
    private readonly  CrucibleManager          crucibleManager   = null!;
    private readonly  OverrideAFK              overrideAfk       = null!;
    private readonly  IPCProvider              ipcProvider       = null!;

    private           IGameObject?                  treasureCofferGameObject;
    //private readonly TinyMessageBus _messageBusSend = new("AutoDutyBroadcaster");
    //private readonly TinyMessageBus _messageBusReceive = new("AutoDutyBroadcaster");
    private         bool           recentlyWatchedCutscene = false;
    private         bool           lootTreasure;
    private         SettingsActive settingsActive         = SettingsActive.None;
    private         SettingsActive bareModeSettingsActive = SettingsActive.None;
    private         DateTime       lastRotationSetTime    = DateTime.MinValue;
    public readonly bool           isDev;

    private readonly (string[], string, Action<string[]>)[] commands = null!;

    public DutyDataTemporary? DutyData { get; set; } = new();

    public AutoDuty()
    {
        try
        {
            Plugin = this;

            IPCBase.DefaultWrapper = SafeWrapper.IPCException;
            ECommonsMain.Init(PluginInterface, Plugin, Module.DalamudReflector, Module.ObjectFunctions);
            PctService.Initialize(PluginInterface);

            this.isDev = PluginInterface.IsDev;

            //EzConfig.Init<ConfigurationMain>();
            EzConfig.DefaultSerializationFactory = new ConfigurationMain.AutoDutySerializationFactory();

            //Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            this.configDirectory      = PluginInterface.ConfigDirectory;
            this.configFile            = PluginInterface.ConfigFile;
            this.dalamudDirectory      = this.configFile.Directory?.Parent;
            this.pathsDirectory        = new DirectoryInfo(this.configDirectory.FullName + "/paths");
            this.assemblyFileInfo      = PluginInterface.AssemblyLocation;
            this.assemblyDirectoryInfo = this.assemblyFileInfo.Directory;

            this.Version = 
                ((PluginInterface.IsDev     ? new Version(0,0,0, 356) :
                  PluginInterface.IsTesting ? PluginInterface.Manifest.TestingAssemblyVersion ?? PluginInterface.Manifest.AssemblyVersion : PluginInterface.Manifest.AssemblyVersion)!).Revision;

            if (!this.configDirectory.Exists)
                this.configDirectory.Create();
            if (!this.pathsDirectory.Exists) 
                this.pathsDirectory.Create();

            this.taskManager = new TaskManager(new TaskManagerConfiguration()
                                               {
                                                   AbortOnTimeout  = false,
                                                   TimeoutSilently = true,
                                                   TimeLimitMS = 10_000,
                                                   ShowDebug = true
                                               });

            TrustHelper.PopulateTrustMembers();
            ContentHelper.PopulateDuties();
            RepairNPCHelper.PopulateRepairNPCs();
            FileHelper.Init();

            ConfigurationMain.Initialization();

            Patcher.Patch(startup: true);

            LocalizationManager.Initialize();

            this.overrideAfk     = new OverrideAFK();
            this.ipcProvider     = new IPCProvider();
            this.squadronManager = new SquadronManager(this.taskManager);
            this.variantManager  = new VariantManager(this.taskManager);
            this.crucibleManager = new CrucibleManager(this.taskManager);
            this.actions         = new ActionsManager(Plugin, this.taskManager);
            this.overrideCamera   = new OverrideCamera();
            this.Overlay          = new Overlay();
            this.MainWindow       = new MainWindow();
            this.windowSystem.AddWindow(this.MainWindow);
            this.windowSystem.AddWindow(this.Overlay);

            if (Svc.ClientState.IsLoggedIn) 
                this.ClientStateOnLogin();
            
            ActiveHelper.InvokeAllHelpers();

            this.commands =
            [
                (["config", "cfg"], "opens config window / modifies config", argsArray =>
                                                                             {
                                                                                 if (argsArray.Length < 2)
                                                                                     this.OpenConfigUI();
                                                                                 else if (argsArray[1] == "list")
                                                                                     ConfigHelper.ListConfig();
                                                                                 else
                                                                                     ConfigHelper.ModifyConfig(argsArray[1], argsArray[2..]);
                                                                             }),
                (["start"], "starts autoduty when in a Duty", _ => this.Run(Svc.ClientState.TerritoryType, 1)),
                (["stop"], "stops everything", _ => Plugin.Stage = Stage.Stopped),
                (["pause"], "pause route", _ => Plugin.Stage     = Stage.Paused),
                (["resume"], "resume route", _ =>
                                             {
                                                 if (Plugin.Stage == Stage.Paused)
                                                 {
                                                     Plugin.taskManager.StepMode =  false;
                                                     Plugin.Stage                =  Plugin.previousStage;
                                                     Plugin.States               &= ~PluginState.Paused;
                                                 }
                                             }),
                (["queue"], "queues duty", argsArray =>
                                           {
                                               QueueHelper.Invoke(ContentHelper.DictionaryContent
                                                                               .FirstOrDefault(x => x.Value.Name!.Equals(string.Join(" ", argsArray).Replace("queue ", string.Empty), StringComparison.InvariantCultureIgnoreCase)).Value ??
                                                                  null,
                                                                  Configuration.Meta.DutyModeEnum);
                                           }),
                (["overlay"], "opens overlay", argsArray =>
                                               {
                                                   if (argsArray.Length == 1)
                                                   {
                                                       Configuration.Overlay.Show = true;
                                                       this.Overlay.IsOpen       = true;

                                                       if (!Plugin.States.HasAnyFlag(PluginState.Looping, PluginState.Navigating))
                                                           Configuration.Overlay.HideWhenStopped = false;
                                                   }
                                                   else
                                                   {
                                                       switch (argsArray[1].ToLower())
                                                       {
                                                           case "lock":
                                                               if (this.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                   this.Overlay.Flags -= ImGuiWindowFlags.NoMove;
                                                               else
                                                                   this.Overlay.Flags |= ImGuiWindowFlags.NoMove;
                                                               break;
                                                           case "nobg":
                                                               if (this.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground))
                                                                   this.Overlay.Flags -= ImGuiWindowFlags.NoBackground;
                                                               else
                                                                   this.Overlay.Flags |= ImGuiWindowFlags.NoBackground;
                                                               break;
                                                       }
                                                   }
                                               }),
                (["skipstep"], "skips the current step", _ =>
                                                         {
                                                             if (this.States.HasFlag(PluginState.Navigating))
                                                             {
                                                                 this.indexer++;
                                                                 this.Stage = Stage.Reading_Path;
                                                             }
                                                         }),
                (["movetoflag"], "moves to the flag map marker", _ => MapHelper.MoveToMapMarker()),
                (["ttfull"], "opens packs, registers cards and sells the rest", _ =>
                                                                                {
                                                                                    this.taskManager.Enqueue(CofferHelper.Invoke);
                                                                                    this.taskManager.Enqueue(() => CofferHelper.State == ActionState.None, new TaskManagerConfiguration(600_000));
                                                                                    this.taskManager.Enqueue(TripleTriadCardUseHelper.Invoke);
                                                                                    this.taskManager.EnqueueDelay(200);
                                                                                    this.taskManager.Enqueue(() => TripleTriadCardUseHelper.State == ActionState.None, new TaskManagerConfiguration(600_000));
                                                                                    this.taskManager.EnqueueDelay(200);
                                                                                    this.taskManager.Enqueue(TripleTriadCardSellHelper.Invoke);
                                                                                    this.taskManager.Enqueue(() => TripleTriadCardSellHelper.State == ActionState.None, new TaskManagerConfiguration(120_000));
                                                                                }),
                (["run"], "starts auto duty in territory type specified", argsArray =>
                                                                          {
                                                                              const string failPreMessage = "Run Error: Incorrect usage: ";

                                                                              const string failPostMessageLev =
                                                                                  "\nCorrect usage: /autoduty run LoopTimesInteger\nexample: /autoduty run 10";

                                                                              if (Plugin.LevelingEnabled)
                                                                              {
                                                                                  if (argsArray.Length < 2)
                                                                                  {
                                                                                      Svc.Log.Info($"{failPreMessage}Argument count must be at least 3, you inputted {argsArray.Length - 1}{failPostMessageLev}");
                                                                                      return;
                                                                                  }

                                                                                  if (!int.TryParse(argsArray[1], out int loopTimesLev))
                                                                                  {
                                                                                      Svc.Log.Info($"{failPreMessage}Argument 1 must be an integer, you inputted {argsArray[3]}{failPostMessageLev}");
                                                                                      return;
                                                                                  }

                                                                                  this.Run(0, loopTimesLev);
                                                                                  return;
                                                                              }

                                                                              const string failPostMessage =
                                                                                  "\nCorrect usage: /autoduty run DutyMode TerritoryTypeInteger LoopTimesInteger (optional)BareModeBool\nexample: /autoduty run Support 1036 10 true\nYou can get the TerritoryTypeInteger from /autoduty tt name of territory (will be logged and copied to clipboard)";

                                                                              if (argsArray.Length < 4)
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument count must be at least 3, you inputted {argsArray.Length - 1}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!Enum.TryParse(argsArray[1], true, out DutyMode dutyMode))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 1 must be a DutyMode enum Type, you inputted {argsArray[1]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!uint.TryParse(argsArray[2], out uint territoryType))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 2 must be an unsigned integer, you inputted {argsArray[2]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!int.TryParse(argsArray[3], out int loopTimes))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 3 must be an integer, you inputted {argsArray[3]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!ContentHelper.DictionaryContent.TryGetValue(territoryType, out Content? content))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 2 value was not in our ContentList or has no Path, you inputted {argsArray[2]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!content.DutyModes.HasFlag(dutyMode))
                                                                              {
                                                                                  Svc.Log
                                                                                     .Info($"{failPreMessage}Argument 2 value was not of type {dutyMode}, which you inputted in Argument 1, Argument 2 value was {argsArray[2]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!content.CanRun(mode: dutyMode))
                                                                              {
                                                                                  string failReason = !UIState.IsInstanceContentCompleted(content.Id) ?
                                                                                                          "You don't have it unlocked" :
                                                                                                          (!ContentPathsManager.DictionaryPaths.ContainsKey(content.TerritoryType) ?
                                                                                                               "There is no path file" :
                                                                                                               (PlayerHelper.GetCurrentLevelFromSheet() < content.ClassJobLevelRequired ?
                                                                                                                    $"Your Lvl({PlayerHelper.GetCurrentLevelFromSheet()}) is less than {content.ClassJobLevelRequired}" :
                                                                                                                    (InventoryHelper.CurrentItemLevel < content.ItemLevelRequired ?
                                                                                                                         $"Your iLvl({InventoryHelper.CurrentItemLevel}) is less than {content.ItemLevelRequired}" :
                                                                                                                         "Your trust party is not of correct levels")));
                                                                                  Svc.Log.Info($"Unable to run {content.Name}, {failReason} {content.CanTrustRun()}");
                                                                                  return;
                                                                              }

                                                                              Configuration.Meta.DutyModeEnum = dutyMode;

                                                                              this.Run(territoryType, loopTimes, bareMode: argsArray.Length > 4 && bool.TryParse(argsArray[4], out bool parsedBool) && parsedBool);
                                                                          }),
                (["dataid"], "Logs and copies your target's dataid to clipboard", argsArray =>
                                                                                  {
                                                                                      IGameObject? obj = null;
                                                                                      if (argsArray.Length == 2)
                                                                                          obj = Svc.Objects[int.TryParse(argsArray[1], out int index) ? index : -1] ?? null;
                                                                                      else
                                                                                          obj = ObjectHelper.GetObjectByName(Svc.Targets.Target?.Name.TextValue ?? "");

                                                                                      Svc.Log.Info($"{obj?.BaseId}");
                                                                                      ImGui.SetClipboardText($"{obj?.BaseId}");
                                                                                  }),
                (["leveling"], "Enables leveling mode (0 = disabled)", argsArray =>
                                                                       {
                                                                           if(argsArray.Length == 2)
                                                                               if(int.TryParse(argsArray[1], out int levelingMode))
                                                                               {
                                                                                   this.LevelingModeEnum = (LevelingMode) levelingMode;
                                                                                   return;
                                                                               }

                                                                           this.LevelingModeEnum = LevelingMode.None;
                                                                       }),
            ];
            this.commands = [.. this.commands.Concat(ActiveHelper.activeHelpers.Where(iah => iah.Commands != null).
                                                                  Select<IActiveHelper, (string[], string, Action<string[]>)>(iah => (iah.Commands!, iah.CommandDescription!, iah.OnCommand)))];

            Svc.Commands.AddHandler("/ad", new CommandInfo(this.OnCommand));
            Svc.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
                                                 {
                                                     HelpMessage = string.Join("\n", this.commands.Select(tuple => $"/autoduty or /ad {string.Join(" / ", tuple.Item1)} -> {tuple.Item2}"))
                                                 });


            PluginInterface.UiBuilder.Draw         += this.DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUI;
            PluginInterface.UiBuilder.OpenMainUi   += this.OpenMainUI;

            Svc.Framework.Update             += this.Framework_Update;
            Svc.Framework.Update             += SchedulerHelper.ScheduleInvoker;
            Svc.ClientState.TerritoryChanged += this.ClientState_TerritoryChanged;
            Svc.ClientState.Login            += this.ClientStateOnLogin;
            Svc.Condition.ConditionChange    += this.Condition_ConditionChange;
            Svc.DutyState.DutyStarted        += this.DutyState_DutyStarted;
            Svc.DutyState.DutyWiped          += this.DutyState_DutyWiped;
            Svc.DutyState.DutyRecommenced    += this.DutyState_DutyRecommenced;
            Svc.DutyState.DutyCompleted      += this.DutyState_DutyCompleted;
            Svc.Log.MinimumLogLevel          =  LogEventLevel.Debug;
            PluginInterface.UiBuilder.Draw   += this.UiBuilderOnDraw;
        }
        catch (Exception e)
        {
            Svc.Log.Info($"Failed loading plugin\n{e}");
        }
    }

    private unsafe void OnCommand(string command, string args)
    {
        Match        match   = RegexHelper.ArgumentParserRegex().Match(args.ToLower());
        List<string> matches = [];

        while (match.Success)
        {
            matches.Add(match.Groups[match.Groups[1].Length > 0 ? 1 : 0].Value);
            match = match.NextMatch();
        }

        string[] argsArray = matches.Count > 0 ? [..matches] : [string.Empty];
        string check = argsArray[0];

        Svc.Log.Debug("command with: " + args);

        foreach ((string[] keywords, _, Action<string[]> action) in this.commands)
            if (keywords.Any(key => check.StartsWith(key)))
            {
                Svc.Log.Debug("Activating command: " + string.Join(" / ", keywords));
                action(argsArray);
                return;
            }

        switch (argsArray[0])
        {
            case "moveto":
                string[] argss = args.Replace("moveto ", "").Split("|");
                string[] vs    = argss[1].Split(", ");
                Vector3      v3    = new(float.Parse(vs[0]), float.Parse(vs[1]), float.Parse(vs[2]));

                GotoHelper.Invoke(Convert.ToUInt32(argss[0]), [v3], argss.Length > 2 ? float.Parse(argss[2]) : 0.25f, argss.Length > 3 ? float.Parse(argss[3]) : 0.25f);
                break;
            case "spew":
                IGameObject? spewObj = null;
                spewObj = argsArray.Length == 2 ? 
                              ObjectHelper.GetObjectByDataId(uint.TryParse(argsArray[1], out uint dataId) ? dataId : 0) : 
                              ObjectHelper.GetObjectByName(Svc.Targets.Target?.Name.TextValue ?? "");

                if (spewObj == null) 
                    return;

                GameObject* gObj = spewObj.Struct();

                static void PrintInfo(Func<string> info)
                {
                    try
                    {
                        Svc.Log.Info(info());
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Info($": {ex}");
                    }
                }

                PrintInfo(() => $"Spewing Object Information for: {gObj->NameString}");
                PrintInfo(() => $"Spewing Object Information for: {gObj->GetName()}");
                //DrawObject: {gObj->DrawObject}\n
                //LayoutInstance: { gObj->LayoutInstance}\n
                //EventHandler: { gObj->EventHandler}\n
                //LuaActor: {gObj->LuaActor}\n
                PrintInfo(() => $"DefaultPosition: {gObj->DefaultPosition}");
                PrintInfo(() => $"DefaultRotation: {gObj->DefaultRotation}");
                PrintInfo(() => $"EventState: {gObj->EventState}");
                PrintInfo(() => $"EntityId {gObj->EntityId}");
                PrintInfo(() => $"LayoutId: {gObj->LayoutId}");
                PrintInfo(() => $"BaseId {gObj->BaseId}");
                PrintInfo(() => $"OwnerId: {gObj->OwnerId}");
                PrintInfo(() => $"ObjectIndex: {gObj->ObjectIndex}");
                PrintInfo(() => $"ObjectKind {gObj->ObjectKind}");               
                PrintInfo(() => $"SubKind: {gObj->SubKind}");
                PrintInfo(() => $"Sex: {gObj->Sex}");
                PrintInfo(() => $"CurrentDistance (YalmDistanceFromPlayerX): {gObj->CurrentDistance}");
                PrintInfo(() => $"TargetStatus: {gObj->NextTargetStatus}");
                PrintInfo(() => $"NextDistance (YalmDistanceFromPlayerZ): {gObj->NextDistance}");
                PrintInfo(() => $"TargetableStatus: {gObj->TargetableStatus}");
                PrintInfo(() => $"Position: {gObj->Position}");
                PrintInfo(() => $"Rotation: {gObj->Rotation}");
                PrintInfo(() => $"Scale: {gObj->Scale}");
                PrintInfo(() => $"Height: {gObj->Height}");
                PrintInfo(() => $"VfxScale: {gObj->VfxScale}");
                PrintInfo(() => $"HitboxRadius: {gObj->HitboxRadius}");
                PrintInfo(() => $"DrawOffset: {gObj->DrawOffset}");
                PrintInfo(() => $"EventId: {gObj->EventId.Id}");
                PrintInfo(() => $"FateId: {gObj->FateId}");
                PrintInfo(() => $"NamePlateIconId: {gObj->NamePlateIconId}");
                PrintInfo(() => $"RenderFlags: {gObj->RenderFlags}");
                PrintInfo(() => $"GetGameObjectId().ObjectId: {gObj->GetGameObjectId().ObjectId}");
                PrintInfo(() => $"GetGameObjectId().Type: {gObj->GetGameObjectId().Type}");
                PrintInfo(() => $"GetObjectKind: {gObj->GetObjectKind()}");
                PrintInfo(() => $"GetIsTargetable: {gObj->GetIsTargetable()}");
                PrintInfo(() => $"GetName: {gObj->GetName()}");
                PrintInfo(() => $"GetRadius: {gObj->GetRadius()}");
                PrintInfo(() => $"GetHeight: {gObj->GetHeight()}");
                PrintInfo(() => $"GetDrawObject: {*gObj->GetDrawObject()}");
                PrintInfo(() => $"GetNameId: {gObj->GetNameId()}");
                PrintInfo(() => $"IsDead: {gObj->IsDead()}");
                PrintInfo(() => $"IsNotMounted: {gObj->IsNotMounted()}");
                PrintInfo(() => $"IsCharacter: {gObj->IsCharacter()}");
                PrintInfo(() => $"IsReadyToDraw: {gObj->IsReadyToDraw()}");
                break;
            default:
                this.OpenMainUI();
                break;
        }
    }

    private void ClientStateOnLogin()
    {
        if (this.Stage != Stage.Stopped || AutoRetainerMultiModeHelper.State == ActionState.Running)
            return;

        ConfigurationMain.Instance.SetProfileToDefault();

        SchedulerHelper.ScheduleAction("LoginConfig", () =>
                                                      {
                                                          if(!ConfigurationMain.Instance.charByCID.ContainsKey(Player.CID))
                                                              ConfigurationMain.Instance.charByCID.Add(Player.CID, new ConfigurationMain.CharData
                                                                                                                   {
                                                                                                                       CID   = Player.CID,
                                                                                                                       Name  = Player.Name,
                                                                                                                       World = Player.HomeWorldName
                                                                                                                   });

                                                          if (Configuration.Overlay.Show &&
                                                              (!Configuration.Overlay.HideWhenStopped || this.States.HasFlag(PluginState.Looping) ||
                                                               this.States.HasFlag(PluginState.Navigating)))
                                                              SchedulerHelper.ScheduleAction("ShowOverlay", () => this.Overlay.IsOpen = true, () => PlayerHelper.IsReady);

                                                          if (Configuration.Meta.ShowMainWindowOnStartup)
                                                              SchedulerHelper.ScheduleAction("ShowMainWindowOnStartup", this.OpenMainUI, () => PlayerHelper.IsReady);
                                                      }, () => ConfigurationMain.Instance.Initialized);
                                
    }

    private void UiBuilderOnDraw()
    {
        if (PlayerHelper.IsValid)
        {
            using PctDrawList? drawList = PctService.Draw();

            if (drawList != null)
            {
                BuildTab.DrawHelper(drawList);

                if (Configuration.DutyConfig.PathDrawEnabled && this.CurrentTerritoryContent?.TerritoryType == Svc.ClientState.TerritoryType && this.Actions.Count != 0 && 
                    (this.indexer < 0 || this.indexer >= this.Actions.Count || !this.Actions[this.indexer].Name.Equals("Boss") || this.Stage != Stage.Action))
                {
                    Vector3 lastPos         = Player.Position;
                    float   stepCountFactor = (1f / Configuration.DutyConfig.PathDrawStepCount);

                    for (int index = Math.Clamp(this.indexer, 0, this.Actions.Count-1); index < this.Actions.Count; index++)
                    {
                        PathAction curAction = this.Actions[index];
                        if (curAction.Position.LengthSquared() > 1)
                        {
                            float alpha = MathF.Max(0f, 1f - (index - this.indexer) * stepCountFactor);

                            if (alpha > 0)
                            {
                                uint mainColor = ImGui.GetColorU32(new Vector4(1f, 0.2f, 0f, alpha));
                                drawList.AddCircle(curAction.Position, 3, mainColor, 0, 3);

                                if (index > 0)
                                    drawList.AddLine(lastPos, curAction.Position, 0f, ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, alpha)));
                                if (index == this.indexer)
                                    drawList.AddLine(Player.Position, curAction.Position, 0, 0x00FFFFFF);

                                drawList.AddText(curAction.Position, ImGui.GetColorU32(new Vector4(alpha + 0.25f)), index.ToString(), 2f);

                                if (curAction.Name.Equals("KillInRange") && int.TryParse(curAction.Arguments[0], out int radius) && radius > 0)
                                {
                                    uint colorU32 = ImGui.GetColorU32(new Vector4(0.4f, 0.2f, 0f, alpha*0.1f));
                                    drawList.AddCircleFilled(curAction.Position, radius, colorU32, mainColor, p: new PctDxParams()
                                                                                                                 {
                                                                                                                     ProjectionHeight = 5f,
                                                                                                                 });
                                }
                            }
                            lastPos = curAction.Position;
                        }
                    }
                }
            }
        }
    }

    private DateTime lastDutyStart = DateTime.MinValue;

    private void DutyState_DutyStarted(IDutyStateEventArgs args)
    {
        this.dutyState         = DutyState.DutyStarted;
        this.lastDutyStart     = DateTime.UtcNow;
        DeathHelper.deathCount = 0;

        if (ContentHelper.DictionaryContent.TryGetValue(Player.Territory.RowId, out Content? content) && content.DutyModes.HasFlag(DutyMode.Regular))
        {
            if(ConfigurationMain.Instance.dutyCountResetDate <= DateTime.UtcNow)
                ConfigurationMain.Instance.dutyCountSinceReset.Clear();

            if (ConfigurationMain.Instance.dutyCountSinceReset.TryAdd(Player.CID, 0))
            {
                ConfigurationMain.Instance.dutyCountResetDate = TimeHelper.GetNextDateTimeForHour(8);
                Svc.Log.Debug($"[DutyCount] Added {Player.CID} and set date to {TimeHelper.GetNextDateTimeForHour(8)}");
            }

            ConfigurationMain.Instance.dutyCountSinceReset[Player.CID]++;
        }
    }

    private void DutyState_DutyWiped(IDutyStateEventArgs       args) => this.dutyState = DutyState.DutyWiped;
    private void DutyState_DutyRecommenced(IDutyStateEventArgs args) => this.dutyState = DutyState.DutyRecommenced;
    private void DutyState_DutyCompleted(IDutyStateEventArgs args)
    {
        Svc.Log.Warning("Duty Done");
        this.dutyState = DutyState.DutyComplete;
        if(this.States is not (PluginState.None or PluginState.Paused))
        {
            TimeSpan timeSpan = DateTime.UtcNow.Subtract(this.lastDutyStart);

            ConfigurationMain.StatData stats = ConfigurationMain.Instance.stats;

            stats.dutyRecords.Add(new DutyDataRecord(DateTime.UtcNow, timeSpan, Player.Territory.RowId, Player.CID, InventoryHelper.CurrentItemLevel, Player.Job, DeathHelper.deathCount));
            stats.dungeonsRun++;
            ConfigurationProfileV2.Save();
            StatsTab.refilter = true;
        }

        this.CheckFinishing();
    }

    internal void ExitDuty() => this.actions.ExitDuty(new PathAction());

    internal void LoadPath()
    {
        try
        {
            if (this.CurrentTerritoryContent == null || (this.CurrentTerritoryContent != null && this.CurrentTerritoryContent.TerritoryType != Svc.ClientState.TerritoryType))
            {
                if (ContentHelper.DictionaryContent.TryGetValue(Svc.ClientState.TerritoryType, out Content? content))
                {
                    this.CurrentTerritoryContent = content;
                }
                else
                {
                    this.Actions.Clear();
                    this.pathFile = "";
                    return;
                }
            }

            if (!MultiboxUtility.Config.MultiBox || !MultiboxUtility.Config.SynchronizePath || MultiboxUtility.Config.Host)
            {
                this.Actions.Clear();
                if (!ContentPathsManager.DictionaryPaths.TryGetValue(Svc.ClientState.TerritoryType, out ContentPathsManager.ContentPathContainer? container))
                {
                    this.pathFile = $"{this.pathsDirectory.FullName}{Path.DirectorySeparatorChar}({Svc.ClientState.TerritoryType}) {this.CurrentTerritoryContent?.EnglishName?.Replace(":", "")}.json";
                    return;
                }

                if (this.States.HasFlag(PluginState.Looping) && Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist)
                {
                    string? s = this.PlaylistCurrentEntry?.Path ?? null;
                    if (s != null)
                        this.currentPath = container.Paths.IndexOf(dp => dp.FileName.Equals(s, StringComparison.InvariantCultureIgnoreCase));
                }

                ContentPathsManager.DutyPath? path = this.currentPath < 0 ?
                                                         container.SelectPath(out this.currentPath) :
                                                         container.Paths[this.currentPath > -1 ? this.currentPath : 0];

                this.pathFile = path?.FilePath ?? "";
                if (path?.Actions != null)
                    this.Actions = [..path.Actions];

                if(MultiboxUtility.Config.MultiBox && MultiboxUtility.Config.SynchronizePath && MultiboxUtility.Config.Host)
                    MultiboxUtility.Server.SendPath();
            }

            //Svc.Log.Info($"Loading Path: {CurrentPath} {ListBoxPOSText.Count}");
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.ToString());
            //throw;
        }
    }

    private unsafe bool StopLoop =>
        Configuration.Loop.Termination.Enabled &&
        (this.CurrentTerritoryContent == null                                                                     ||
         (Configuration.Loop.Termination.StopLevel      && Player.Level                             >= Configuration.Loop.Termination.StopLevelInt) ||
         (Configuration.Loop.Termination.StopNoRestedXP && AgentHUD.Instance()->ExpRestedExperience == 0)                          ||
         (Configuration.Loop.Termination.TerminationBLUSpellsEnabled && (Configuration.Loop.Termination.TerminationBLUSpellsAll ?
                                                            Configuration.Loop.Termination.TerminationBLUSpells.All(BLUHelper.SpellUnlocked) :
                                                            Configuration.Loop.Termination.TerminationBLUSpells.Any(BLUHelper.SpellUnlocked))) ||
         (Configuration.Loop.Termination.StopItemQty && (Configuration.Loop.Termination.StopItemAll ?
                                            Configuration.Loop.Termination.StopItemQtyItemDictionary.All(x => InventoryManager.Instance()->GetInventoryItemCount(x.Key) >= x.Value.Value) :
                                            Configuration.Loop.Termination.StopItemQtyItemDictionary.Any(x => InventoryManager.Instance()->GetInventoryItemCount(x.Key) >= x.Value.Value))) ||
         (Configuration.Loop.Termination.StopWhenDutyGathered && GlamourLog_IPCSubscriber.AllStoredFromDungeon(Plugin.CurrentTerritoryContent.TerritoryType)) ||
         (Configuration.Loop.Termination.TerminationInventoryFree && Configuration.Loop.Termination.TerminationInventoryFreeSlots >= InventoryHelper.SlotsFree) ||
         (Configuration.Loop.Termination.TerminationiLvl && InventoryHelper.CurrentItemLevel >= Configuration.Loop.Termination.TerminationiLvlInt));

    private void TrustLeveling()
    {
        if (this.TrustLevelingEnabled && TrustHelper.Members.Any(tm => tm.Value.Level < tm.Value.LevelCap))
        {
            this.taskManager.Enqueue(() => Svc.Log.Debug($"Trust Leveling Enabled"),                     "TrustLeveling-Debug");
            this.taskManager.Enqueue(() => TrustHelper.ClearCachedLevels(this.CurrentTerritoryContent!), "TrustLeveling-ClearCachedLevels");
            this.taskManager.Enqueue(() => TrustHelper.GetLevels(this.CurrentTerritoryContent),          "TrustLeveling-GetLevels");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => TrustHelper.State != ActionState.Running, "TrustLeveling-RecheckingTrustLevels");
        }
    }

    private void ClientState_TerritoryChanged(uint t)
    {
        if (MultiboxUtility.Config.MultiBox)
        {
            bool isDuty = ContentHelper.DictionaryContent.ContainsKey(t);
            if (!MultiboxUtility.Config.Host)
            {
                if (isDuty)
                {
                    this.Run(t, 1);
                }
            } else
            {
                if(!isDuty)
                    MultiboxUtility.Server.ExitDuty();
            }
        }

        if (this.Stage is Stage.Stopped or Stage.Paused)
            return;

        Svc.Log.Debug($"ClientState_TerritoryChanged: t={t}");

        this.currentTerritoryType = t;
        this.mainListClicked      = false;

        this.DutyData = null;

        if (t == 0)
            return;

        this.currentPath = -1;

        this.LoadPath();

        if (!this.States.HasFlag(PluginState.Looping) || GCTurninHelper.State == ActionState.Running || RepairHelper.State == ActionState.Running || GotoHelper.State == ActionState.Running || GotoInnHelper.State == ActionState.Running || GotoBarracksHelper.State == ActionState.Running || GotoHousingHelper.State == ActionState.Running || this.CurrentTerritoryContent == null)
        {
            Svc.Log.Debug("We Changed Territories but are doing after loop actions or not running at all or in a Territory not supported by AutoDuty");
            return;
        }

        if (Configuration is { Overlay: { Show: true, HideWhenStopped: true }} && !this.States.HasFlag(PluginState.Looping))
        {
            this.Overlay.IsOpen = false;
            this.MainWindow.IsOpen = true;
        }

        this.action = "";

        if (t != this.CurrentTerritoryContent.TerritoryType)
        {
            if (this.currentLoop < Configuration.Meta.LoopTimes || Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist)
            {
                this.taskManager.Abort();
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Loop {this.currentLoop} of {Configuration.Meta.LoopTimes}"), "Loop-Debug");
                this.taskManager.Enqueue(() => { this.Stage  =  Stage.Looping; },                                           "Loop-SetStage=99");
                this.taskManager.Enqueue(() => { this.States &= ~PluginState.Navigating; },                                 "Loop-RemoveNavigationState");
                this.taskManager.Enqueue(() => PlayerHelper.IsReady,                                                        "Loop-WaitPlayerReady", new TaskManagerConfiguration(int.MaxValue));

                this.TrustLeveling();

                this.taskManager.Enqueue(() =>
                                         {
                                             if (this.StopLoop)
                                             {
                                                 this.taskManager.Enqueue(() => Svc.Log.Info($"Loop Stop Condition Encountered, Stopping Loop"));
                                                 this.LoopTasks(false, Configuration is { Loop.Between: { Enabled: true, ExecuteLastLoop: true }});
                                             }
                                             else
                                             {
                                                 this.LoopTasks(between: Configuration.Loop.Between.Enabled);
                                             }
                                         }, "Loop-CheckStopLoop");

            }
            else
            {
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Loops Done"), "Loop-Debug");
                this.taskManager.Enqueue(() => { this.States &= ~PluginState.Navigating; }, "Loop-RemoveNavigationState");
                this.taskManager.Enqueue(() => PlayerHelper.IsReady, "Loop-WaitPlayerReady", new TaskManagerConfiguration(timeLimitMS: int.MaxValue));
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Loop {this.currentLoop} == {Configuration.Meta.LoopTimes} we are done Looping, Invoking Loop Actions"), "Loop-Debug");
                this.taskManager.Enqueue(() => this.LoopTasks(false, Configuration is { Loop.Between: { Enabled: true, ExecuteLastLoop: true }}), "Loop-LoopCompleteActions");
            }
        }
    }

    private unsafe void Condition_ConditionChange(ConditionFlag flag, bool value)
    {
        if (this.Stage == Stage.Stopped) 
            return;

        if (flag == ConditionFlag.Unconscious)
        {
            switch (value)
            {
                case true when this.Stage != Stage.Dead || DeathHelper.DeathState != PlayerLifeState.Dead:
                    Svc.Log.Debug($"We Died, Setting Stage to Dead");
                    DeathHelper.DeathState = PlayerLifeState.Dead;
                    this.Stage             = Stage.Dead;
                    break;
                case false when this.Stage != Stage.Revived || DeathHelper.DeathState != PlayerLifeState.Revived:
                    Svc.Log.Debug($"We Revived, Setting Stage to Revived");
                    DeathHelper.DeathState = PlayerLifeState.Revived;
                    this.Stage             = Stage.Revived;
                    break;
            }

            return;
        }
        //Svc.Log.Debug($"{flag} : {value}");
        if (this.Stage is not (Stage.Dead or Stage.Revived or Stage.Action) && !this.recentlyWatchedCutscene && !Conditions.Instance()->WatchingCutscene && 
            (flag is not (ConditionFlag.WatchingCutscene or ConditionFlag.WatchingCutscene78 or ConditionFlag.OccupiedInCutSceneEvent) and (ConditionFlag.BetweenAreas or ConditionFlag.BetweenAreas51 or ConditionFlag.Jumping61) ||
             (flag is ConditionFlag.SufferingStatusAffliction63 && PlayerHelper.HasStatus(1268))) && 
            value && this.States.HasFlag(PluginState.Navigating))
        {
            Svc.Log.Info($"Condition_ConditionChange: Indexer Increase and Change Stage to Condition");
            this.indexer++;
            VNavmesh_IPCSubscriber.Path_Stop();
            this.Stage = Stage.Condition;
        }
        if (Conditions.Instance()->WatchingCutscene || flag is ConditionFlag.WatchingCutscene or ConditionFlag.WatchingCutscene78 or ConditionFlag.OccupiedInCutSceneEvent)
        {
            this.recentlyWatchedCutscene = true;
            SchedulerHelper.ScheduleAction("RecentlyWatchedCutsceneTimer", () => this.recentlyWatchedCutscene = false, 5000);
        }
    }

    public void Run(uint territoryType = 0, int loops = 0, bool startFromZero = true, bool bareMode = false)
    {
        if(InDungeon)
            Configuration.Meta.AutoDutyModeEnum = AutoDutyMode.Looping;

        Svc.Log.Debug($"Run: territoryType={territoryType} loops={loops} bareMode={bareMode}");

        if (territoryType > 0 && !this.LevelingEnabled)
        {
            if (ContentHelper.DictionaryContent.TryGetValue(territoryType, out Content? content))
            {
                this.CurrentTerritoryContent = content;
            }
            else
            {
                Svc.Log.Error($"({territoryType}) is not in our Dictionary as a compatible Duty");
                return;
            }
        }

        if (this.CurrentTerritoryContent == null)
            return;

        if (loops > 0) 
            Configuration.Meta.LoopTimes = loops;

        if (bareMode)
        {
            this.bareModeSettingsActive |= SettingsActive.BareMode_Active;
            if (Configuration.Loop.Pre.Enabled)
                this.bareModeSettingsActive |= SettingsActive.PreLoop_Enabled;
            if (Configuration.Loop.Between.Enabled) 
                this.bareModeSettingsActive |= SettingsActive.BetweenLoop_Enabled;
            if (Configuration.Loop.Termination.Enabled) 
                this.bareModeSettingsActive |= SettingsActive.TerminationActions_Enabled;
            Configuration.Loop.Pre.Enabled     = false;
            Configuration.Loop.Between.Enabled = false;
            Configuration.Loop.Termination.Enabled = false;
        }

        Svc.Log.Info($"Running AutoDuty in {this.CurrentTerritoryContent.EnglishName}, Looping {Configuration.Meta.LoopTimes} times{(bareMode ? " in BareMode (No Pre, Between or Termination Loop Actions)" : "")}");

        //MainWindow.OpenTab("Mini");
        if (Configuration.Overlay.Show)
            //MainWindow.IsOpen = false;
            this.Overlay.IsOpen = true;

        this.Stage =  Stage.Looping;
        this.States   |= PluginState.Looping;
        this.SetGeneralSettings(false);
        VNavmesh_IPCSubscriber.SetMovementAllowed(true);
        this.taskManager.Abort();
        Svc.Log.Info($"Running {this.CurrentTerritoryContent.Name} {Configuration.Meta.LoopTimes} Times");
        if (!InDungeon)
        {
            this.currentLoop = 0;
            if (Configuration.Loop.Pre.Enabled)
            {
                if (Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist && Plugin.PlaylistCurrentEntry != null)
                    unsafe
                    {
                        if (Plugin.PlaylistCurrentEntry.gearset.HasValue && RaptureGearsetModule.Instance()->IsValidGearset(Plugin.PlaylistCurrentEntry.gearset.Value))
                        {
                            this.taskManager.Enqueue(() => RaptureGearsetModule.Instance()->EquipGearset(Plugin.PlaylistCurrentEntry.gearset.Value));
                            this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull);
                        }
                    }


                foreach (LoopActionConfig loopAction in Configuration.Loop.Pre.Actions)
                {
                    bool queue = false;
                    loopAction.Run(ref queue);
                }
            }

            this.taskManager.Enqueue(() => Svc.Log.Debug($"Queueing First Run"));
            this.Queue(this.CurrentTerritoryContent!);
        }

        this.taskManager.Enqueue(() => Svc.Log.Debug($"Done Queueing-WaitDutyStarted, NavIsReady"));
        this.taskManager.Enqueue(() => Svc.DutyState.IsDutyStarted,          "Run-WaitDutyStarted");
        this.taskManager.Enqueue(() => VNavmesh_IPCSubscriber.Nav_IsReady, "Run-WaitNavIsReady", new TaskManagerConfiguration(int.MaxValue));
        this.taskManager.Enqueue(() => Svc.Log.Debug($"Start Navigation"));
        this.taskManager.Enqueue(() => this.StartNavigation(startFromZero), "Run-StartNavigation");

        if (this.currentLoop == 0)
        {
            this.currentLoop = 1;
            if (Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist)
            {
                Configuration.Meta.LoopTimes = Plugin.PlaylistCurrentEntry?.count ?? Configuration.Meta.LoopTimes;
                Plugin.PlaylistCurrentEntry!.curCount = 0;
            }
        }
    }

    internal void LoopTasks(bool queue = true, bool between = true)
    {
        this.taskManager.Enqueue(() => this.CurrentTerritoryContent != null, "Loop-WaitTillTerritory");

        bool multiboxClient = MultiboxUtility.Config is { MultiBox: true, Host: false };
        if (multiboxClient)
            queue = true;

        if (between)
            foreach (LoopActionConfig loopAction in Configuration.Loop.Between.Actions)
                loopAction.Run(ref queue);

        if (multiboxClient)
            queue = false;

        if (MultiboxUtility.Config.MultiBox)
        {
            if (MultiboxUtility.Config.Host)
                MultiboxUtility.MultiboxBlockingNextStep = true;
            else
                this.taskManager.Enqueue(() => MultiboxUtility.MultiboxBlockingNextStep = true);
        }

        if (!queue)
        {
            this.LoopsCompleteActions();
            return;
        }
        
        SchedulerHelper.ScheduleAction("LoopContinueTask", () =>
                                                           {
                                                               if (Plugin.States is PluginState.None)
                                                                   return;

                                                               if (Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Looping && this.LevelingEnabled)
                                                               {
                                                                   Svc.Log.Info("Leveling Enabled");
                                                                   Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);
                                                                   if (duty != null)
                                                                   {
                                                                       if (this.LevelingModeEnum      == LevelingMode.Support && Configuration.Meta.PreferTrustOverSupportLeveling &&
                                                                           duty.ClassJobLevelRequired > 70)
                                                                       {
                                                                           this.levelingModeEnum           = LevelingMode.Trust_Solo;
                                                                           Configuration.Meta.dutyModeEnum = DutyMode.Trust;

                                                                           Content? dutyTrust = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);

                                                                           if (duty != dutyTrust)
                                                                           {
                                                                               this.levelingModeEnum           = LevelingMode.Support;
                                                                               Configuration.Meta.dutyModeEnum = DutyMode.Support;
                                                                           }
                                                                       }

                                                                       Svc.Log.Info("Next Leveling Duty: " + duty.Name);
                                                                       this.CurrentTerritoryContent = duty;
                                                                       ContentPathsManager.DictionaryPaths[duty.TerritoryType].SelectPath(out this.currentPath);
                                                                   }
                                                                   else
                                                                   {
                                                                       this.currentLoop = Configuration.Meta.LoopTimes;
                                                                       this.LoopsCompleteActions();
                                                                       return;
                                                                   }
                                                               }

                                                               this.taskManager.Enqueue(() => Svc.Log.Debug($"Registering New Loop"));

                                                               this.Queue(this.CurrentTerritoryContent!);
                                                               this.taskManager.Enqueue(() =>
                                                                                            Svc.Log
                                                                                               .Debug($"Incrementing LoopCount, Setting Action Var, Wait for CorrectTerritory, PlayerIsValid, DutyStarted, and NavIsReady"));
                                                               this.taskManager.Enqueue(() =>
                                                                                        {
                                                                                            if (Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist)
                                                                                            {
                                                                                                this.currentLoop             = this.PlaylistCurrentEntry?.curCount ?? this.currentLoop + 1;
                                                                                                Configuration.Meta.LoopTimes = this.PlaylistCurrentEntry?.count    ?? Configuration.Meta.LoopTimes;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                this.currentLoop ++;
                                                                                            }
                                                                                        }, "Loop-IncrementCurrentLoop");
                                                               this.taskManager.Enqueue(() => this.action = $"Looping: {this.CurrentTerritoryContent?.Name} {this.currentLoop} of {Configuration.Meta.LoopTimes}", "Loop-SetAction");
                                                               this.taskManager.Enqueue(() => Svc.ClientState.TerritoryType == this.CurrentTerritoryContent?.TerritoryType, "Loop-WaitCorrectTerritory",
                                                                                        new TaskManagerConfiguration(int.MaxValue));
                                                               this.taskManager.Enqueue(() => PlayerHelper.IsValid,                 "Loop-WaitPlayerValid", new TaskManagerConfiguration(int.MaxValue));
                                                               this.taskManager.Enqueue(() => Svc.DutyState.IsDutyStarted,          "Loop-WaitDutyStarted", new TaskManagerConfiguration(int.MaxValue));
                                                               this.taskManager.Enqueue(() => VNavmesh_IPCSubscriber.Nav_IsReady, "Loop-WaitNavReady", new TaskManagerConfiguration(int.MaxValue));
                                                               this.taskManager.Enqueue(() => Svc.Log.Debug($"StartNavigation"));
                                                               this.taskManager.Enqueue(() => this.StartNavigation(true), "Loop-StartNavigation");
                                                           }, () => !MultiboxUtility.MultiboxBlockingNextStep);
    }

    private void LoopsCompleteActions()
    {
        this.SetGeneralSettings(false);

        if (Configuration.Loop.Termination.Enabled)
        {
            this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull);

            foreach (LoopActionConfig loopAction in Configuration.Loop.Termination.Actions)
            {
                bool queue = false;
                loopAction.Run(ref queue);
            }

            switch (Configuration.Loop.Termination.TerminationMethodEnum)
            {
                case TerminationMode.Kill_PC:
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Killing PC"));
                    if (!Configuration.Loop.Termination.TerminationKeepActive)
                    {
                        Configuration.Loop.Termination.TerminationMethodEnum = TerminationMode.Do_Nothing;
                           ConfigurationProfileV2.Save();
                    }

                    this.taskManager.Enqueue(() =>
                                             {
                                                 if (OperatingSystem.IsWindows())
                                                 {
                                                     ProcessStartInfo startinfo = new("shutdown.exe", "-s -t 20");
                                                     Process.Start(startinfo);
                                                 }
                                                 else if (OperatingSystem.IsLinux())
                                                 {
                                                     //Educated guess
                                                     ProcessStartInfo startinfo = new("shutdown", "-t 20");
                                                     Process.Start(startinfo);
                                                 }
                                                 else if (OperatingSystem.IsMacOS())
                                                 {
                                                     //hell if I know
                                                 }
                                             }, "Enqueuing SystemShutdown");
                    this.taskManager.Enqueue(() => Chat.ExecuteCommand($"/xlkill"), "Killing the game");
                    break;
                }
                case TerminationMode.Kill_Client:
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Killing Client"));
                    if (!Configuration.Loop.Termination.TerminationKeepActive)
                    {
                        Configuration.Loop.Termination.TerminationMethodEnum = TerminationMode.Do_Nothing;
                           ConfigurationProfileV2.Save();
                    }

                    this.taskManager.Enqueue(() => Chat.ExecuteCommand($"/xlkill"), "Killing the game");
                    break;
                }
                case TerminationMode.Logout:
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Logging Out"));
                    if (!Configuration.Loop.Termination.TerminationKeepActive)
                    {
                        Configuration.Loop.Termination.TerminationMethodEnum = TerminationMode.Do_Nothing;
                           ConfigurationProfileV2.Save();
                    }

                    this.taskManager.Enqueue(() => PlayerHelper.IsReady);
                    this.taskManager.EnqueueDelay(2000);
                    this.taskManager.Enqueue(() => Chat.ExecuteCommand($"/logout"));
                    this.taskManager.Enqueue(() => AddonHelper.ClickSelectYesno());
                    break;
                }
                case TerminationMode.Start_AR_Multi_Mode:
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Starting AR Multi Mode"));
                    this.taskManager.Enqueue(() => Chat.ExecuteCommand($"/ays multi e"));
                    break;
                case TerminationMode.Start_AR_Night_Mode:
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Starting AR Night Mode"));
                    this.taskManager.Enqueue(() => Chat.ExecuteCommand($"/ays night e"));
                    break;
                case TerminationMode.Do_Nothing:
                default:
                    break;
            }
        }

        Svc.Log.Debug($"Removing Looping, Setting CurrentLoop to 0, and Setting Stage to Stopped");

        this.States   &= ~PluginState.Looping;
        this.currentLoop =  0;
        this.taskManager.Enqueue(() => SchedulerHelper.ScheduleAction("SetStageStopped", () => this.Stage = Stage.Stopped, 1));
    }

    private void Queue(Content content)
    {
        if (Configuration.Meta.DutyModeEnum == DutyMode.Variant)
        {
            this.variantManager.RegisterVariantDuty(content);
        }
        else if (Configuration.Meta.DutyModeEnum.EqualsAny(DutyMode.Regular, DutyMode.Trial, DutyMode.Raid, DutyMode.Support, DutyMode.Trust, DutyMode.NoviceHall))
        {
            this.taskManager.Enqueue(() => QueueHelper.Invoke(content, Configuration.Meta.DutyModeEnum), "Queue-Invoke");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => QueueHelper.State != ActionState.Running, "Queue-WaitQueueComplete", new TaskManagerConfiguration(int.MaxValue));
        }
        else if (Configuration.Meta.DutyModeEnum == DutyMode.Squadron)
        {
            this.taskManager.Enqueue(() => GotoBarracksHelper.Invoke(), "Queue-GotoBarracksInvoke");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running, "Queue-WaitGotoComplete", new TaskManagerConfiguration(int.MaxValue));
            this.squadronManager.RegisterSquadron(content);
        }
        else if (Configuration.Meta.DutyModeEnum == DutyMode.Crucible)
        {
            this.taskManager.Enqueue(CrucibleManager.GotoLauda, "Queue-GotoLaudaInvoke");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => GotoHelper.State != ActionState.Running, "Queue-WaitGotoComplete", new TaskManagerConfiguration(int.MaxValue));
            this.crucibleManager.RegisterCrucible(content);
        }

        this.taskManager.Enqueue(() => !PlayerHelper.IsValid, "Queue-WaitNotValid");
        this.taskManager.Enqueue(() => PlayerHelper.IsValid,  "Queue-WaitValid", new TaskManagerConfiguration(int.MaxValue));
    }

    private void StageReadingPath()
    {
        if (!PlayerHelper.IsValid || !EzThrottler.Check("PathFindFailure") || this.indexer == -1 || this.indexer >= this.Actions.Count)
            return;

        this.action = $"{(this.Actions.Count >= this.indexer ? Plugin.Actions[this.indexer].ToCustomString() : "")}";

        this.pathAction = this.Actions[this.indexer];

        if (MultiboxUtility.MultiboxBlockingNextStep)
        {
            if (PartyHelper.PartyInCombat() && (Plugin.DutyData?.StopForCombat ?? true))
            {
                if (Configuration is { DutyConfig: { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false } })
                    this.SetRotationPluginSettings(true);
                VNavmesh_IPCSubscriber.Path_Stop();

                if (this.pathAction.Name.Equals("Boss") && this.pathAction.Position != Vector3.Zero && ObjectHelper.BelowDistanceToPlayer(this.pathAction.Position, 50, 10))
                {
                    this.bossObject = ObjectHelper.GetBossObject(25);
                    if (this.bossObject != null)
                    {
                        if (MultiboxUtility.Config.Host)
                            MultiboxUtility.MultiboxBlockingNextStep = false;
                        this.Stage = Stage.Action;
                        return;
                    }
                }
                this.Stage = Stage.Waiting_For_Combat;
            }
            return;
        }

        Svc.Log.Debug($"Starting Action {this.pathAction.ToCustomString()}");

        bool unsync = QueueHelper.ShouldBeUnSynced();

        if (this.pathAction.Tag.HasFlag(ActionTag.Unsynced) && !unsync)
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.indexer]} because we are synced");
            this.indexer++;
            return;
        }

        if (this.pathAction.Tag.HasFlag(ActionTag.W2W) && !Configuration.DutyConfig.IsW2W(unsync: unsync))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.indexer]} because we are not W2W-ing");
            this.indexer++;
            return;
        }

        if (this.pathAction.Tag.HasFlag(ActionTag.Synced) && unsync)
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.indexer]} because we are unsynced");
            this.indexer++;
            return;
        }

        if (this.pathAction.Tag.HasFlag(ActionTag.Comment))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.indexer].Name} because it is a comment");
            this.indexer++;
            return;
        }

        if (this.pathAction.Tag.HasFlag(ActionTag.Revival))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.indexer].Name} because it is a Revival Tag");
            this.indexer++;
            return;
        }

        if ((this.skipTreasureCoffer || !Configuration.DutyConfig.LootTreasure || Configuration.DutyConfig.LootBossTreasureOnly) && this.pathAction.Tag.HasFlag(ActionTag.Treasure))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.indexer].Name} because we are either in revival mode, LootTreasure is off or BossOnly");
            this.indexer++;
            return;
        }

        if (this.pathAction.Conditions.Any(pac => !pac.IsFulfilled()))
        {
            Svc.Log.Debug($"Skipping path entry {this.pathAction.Name} because one of the conditions is not fulfilled");
            this.indexer++;
            return;
        }

        this.DutyData?.StayCloseToTank = !(this.pathAction.Name.Equals("Boss") || this.pathAction.Flags.HasFlag(PathActionFlags.NoPartyCoherency));

        if(MultiboxUtility.Config.Host)
            MultiboxUtility.MultiboxBlockingNextStep = false;

        if (this.pathAction.Position == Vector3.Zero)
        {
            this.Stage = Stage.Action;
            return;
        }

        if (!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress && !VNavmesh_IPCSubscriber.Path_IsRunning)
        {
            Chat.ExecuteCommand("/automove off");
            VNavmesh_IPCSubscriber.Path_SetTolerance(0.25f);
            if (this.pathAction is { Name: "MoveTo", Arguments.Count: > 0 } && bool.TryParse(this.pathAction.Arguments[0], out bool useMesh) && !useMesh)
                VNavmesh_IPCSubscriber.Path_MoveTo([this.pathAction.Position], false);
            else
                VNavmesh_IPCSubscriber.SimpleMove_PathfindAndMoveTo(this.pathAction.Position, false);

            this.Stage = Stage.Moving;
        }
    }

    private void StageMoving()
    {
        if (!PlayerHelper.IsValid || this.indexer == -1 || this.indexer >= this.Actions.Count)
            return;

        this.action = $"{Plugin.Actions[this.indexer].ToCustomString()}";

        if (EzThrottler.Throttle("BossChecker", 25) && this.pathAction.Name.Equals("Boss") && this.pathAction.Position != Vector3.Zero && ObjectHelper.BelowDistanceToPlayer(this.pathAction.Position, 50, 10))
        {
            this.bossObject = ObjectHelper.GetBossObject(25);
            if (this.bossObject != null)
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                this.Stage = Stage.Action;
                return;
            }
        }

        if (PartyHelper.PartyInCombat() && (Plugin.DutyData?.StopForCombat ?? true))
        {
            if (Configuration is { DutyConfig: { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false } })
                this.SetRotationPluginSettings(true);
            VNavmesh_IPCSubscriber.Path_Stop();
            this.Stage = Stage.Waiting_For_Combat;
            return;
        }

        unsafe
        {
            if (ActionManager.Instance()->CastActionId == 6)
                return;

            if (!PlayerHelper.IsCasting && StuckHelper.IsStuck(out byte stuckCount))
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                if (Configuration.DutyConfig.Stuck.StuckReturn && stuckCount >= Configuration.DutyConfig.Stuck.StuckReturnX)
                {
                    Svc.Log.Debug($"Using Stuck Return Action");
                    if (ActionManager.Instance()->GetActionStatus(ActionType.Action, 6) == 0)
                    {
                        BossMod_IPCSubscriber.SetMovement(false);
                        this.SetRotationPluginSettings(false, false, true);
                        ActionManager.Instance()->UseAction(ActionType.Action, 6); // Chat.ExecuteCommand("/return");
                        SchedulerHelper.ScheduleAction("StuckHelperReturnInsurance", () =>
                                                                                     {
                                                                                         VNavmesh_IPCSubscriber.Path_Stop();
                                                                                         ActionManager.Instance()->UseAction(ActionType.Action, 6); //Chat.ExecuteCommand("/return");
                                                                                     }, () => ActionManager.Instance()->CastActionId != 6 && 
                                                                                              ActionManager.Instance()->GetActionStatus(ActionType.Action, 6) == 0 && PlayerHelper.IsReady, false);

                        SchedulerHelper.ScheduleAction("StuckHelperReturn", () =>
                                                                            {
                                                                                VNavmesh_IPCSubscriber.Path_Stop();
                                                                                Plugin.Stage           = Stage.Revived;
                                                                                DeathHelper.DeathState = PlayerLifeState.Revived;

                                                                                SchedulerHelper.ScheduleAction("StuckHelperUnschedule", () => SchedulerHelper.DescheduleAction("StuckHelperReturnInsurance"),
                                                                                                               () => DeathHelper.DeathState == PlayerLifeState.Alive);
                                                                            }, () => ActionManager.Instance()->CastActionId != 6 && PlayerHelper.IsReady);
                        return;
                    }
                    else
                    {
                        Svc.Log.Debug("Return action not available");
                    }
                }
                else if (Configuration.DutyConfig.Stuck.RebuildNavmeshOnStuck && stuckCount >= Configuration.DutyConfig.Stuck.RebuildNavmeshAfterStuckXTimes)
                {
                    VNavmesh_IPCSubscriber.GetNav_Rebuild();
                }

                this.Stage = Stage.Idle;
                this.Stage = Stage.Reading_Path;
                return;
            }
        }

        if ((!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress && VNavmesh_IPCSubscriber.Path_NumWaypoints == 0) || (!this.pathAction.Name.IsNullOrEmpty() && this.pathAction.Position != Vector3.Zero && ObjectHelper.GetDistanceToPlayer(this.pathAction.Position) <= (this.pathAction.Name.EqualsIgnoreCase("Interactable") ? 2f : 0.25f)))
        {
            if (this.pathAction.Name.IsNullOrEmpty() || this.pathAction.Name.Equals("MoveTo") || this.pathAction.Name.Equals("TreasureCoffer") || this.pathAction.Name.Equals("Revival"))
            {
                this.Stage = Stage.Reading_Path;
                this.indexer++;
            }
            else
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                this.Stage = Stage.Action;
            }

            return;
        }
    }

    private void StageAction()
    {
        if (this.indexer == -1 || this.indexer >= this.Actions.Count)
            return;
        
        if (Configuration is { DutyConfig: { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false } } && !Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]) this.SetRotationPluginSettings(true);
        
        if (!this.taskManager.IsBusy)
        {
            this.Stage = Stage.Reading_Path;
            this.indexer++;
            return;
        }
    }

    private void StageWaitingForCombat()
    {
        if (!EzThrottler.Throttle("CombatCheck", 250) || !PlayerHelper.IsReady || this.indexer == -1 || this.indexer >= this.Actions.Count || this.pathAction == null)
            return;

        this.action = $"Waiting For Combat";

        
        if (ReflectionHelper.Avarice_Reflection.PositionalChanged(out Positional positional))
            BossMod_IPCSubscriber.SetPositional(positional);

        if (this.pathAction.Name.Equals("Boss") && this.pathAction.Position != Vector3.Zero && ObjectHelper.GetDistanceToPlayer(this.pathAction.Position) < 50)
        {
            this.bossObject = ObjectHelper.GetBossObject(25);
            if (this.bossObject != null)
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                this.Stage = Stage.Action;
                return;
            }
        }

        if (PartyHelper.PartyInCombat())
        {
            if (Plugin.pathAction?.Name != "Boss")
            {
                unsafe
                {
                    IBattleChara?[] inCombatEnemies = [.. EnemyListNumberArray.Instance()->Enemies.ToArray().Where(x => x.MaxHPPercent > 0).
                                                                                                   Select(x => Svc.Objects.FirstOrDefault(y => y.EntityId == x.EntityId) as IBattleChara)];

                    if (inCombatEnemies.Length > 0 && inCombatEnemies.All(x =>
                                                                              x != null               &&
                                                                              !ObjectHelper.IsBoss(x) &&
                                                                              (ObjectHelper.GetDistanceToPlayer(x) > 25 ||
                                                                               !x.IsTargetable)))
                    {
                        if (EzThrottler.Throttle("CombatRangeCheck", 500))
                            foreach (IBattleChara? enemy in inCombatEnemies)
                            {
                                Vector3? vector3 = VNavmesh_IPCSubscriber.Query_Mesh_PointOnFloor(enemy!.Position, false, 5f);
                                if (vector3.HasValue)
                                {
                                    VNavmesh_IPCSubscriber.SimpleMove_PathfindAndMoveTo(vector3.Value, false);
                                    return;
                                }
                            }
                        else
                            return;
                    }
                    else
                    {
                        VNavmesh_IPCSubscriber.Path_Stop();
                    }
                }
            }

            if (Svc.Targets.Target == null)
            {
                //find and target closest attackable npc, if we are not targeting
                IGameObject? gos = ObjectHelper.GetObjectsByObjectKind(ObjectKind.BattleNpc)?.FirstOrDefault(o => o.GetNameplateKind() is NameplateKind.HostileEngagedSelfUndamaged or NameplateKind.HostileEngagedSelfDamaged && ObjectHelper.GetBattleDistanceToPlayer(o) <= 75);

                if (gos != null)
                    Svc.Targets.Target = gos;
            }
            if (Configuration.DutyConfig.AutoManageBossModAISettings)
            {
                if (Svc.Targets.Target != null)
                {
                    int enemyCount = ObjectFunctions.GetAttackableEnemyCountAroundPoint(Svc.Targets.Target.Position, 15);

                    if (!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress && VNavmesh_IPCSubscriber.Path_IsRunning)
                        VNavmesh_IPCSubscriber.Path_Stop();

                    if (enemyCount > 2)
                    {
                        Svc.Log.Debug($"Changing MaxDistanceToTarget to {Configuration.DutyConfig.BossMod.MaxDistanceToTargetAoEFloat}, because enemy count = {enemyCount}");
                        BossMod_IPCSubscriber.SetRange(Configuration.DutyConfig.BossMod.MaxDistanceToTargetAoEFloat);
                    }
                    else
                    {
                        Svc.Log.Debug($"Changing MaxDistanceToTarget to {Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat}, because enemy count = {enemyCount}");
                        BossMod_IPCSubscriber.SetRange(Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat);
                    }
                }
            }
            else if (!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress && VNavmesh_IPCSubscriber.Path_IsRunning)
            {
                VNavmesh_IPCSubscriber.Path_Stop();
            }
        }
        else if (!PartyHelper.PartyInCombat() && !VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress)
        {
            BossMod_IPCSubscriber.SetRange(Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat);

            VNavmesh_IPCSubscriber.Path_Stop();
            this.Stage = Stage.Reading_Path;
        }
    }

    public void StartNavigation(bool startFromZero = true)
    {
        Svc.Log.Debug($"StartNavigation: startFromZero={startFromZero}");
        if (ContentHelper.DictionaryContent.TryGetValue(Svc.ClientState.TerritoryType, out Content? content))
        {
            this.CurrentTerritoryContent = content;
            this.pathFile                = $"{Plugin.pathsDirectory.FullName}/({Svc.ClientState.TerritoryType}) {content.EnglishName?.Replace(":", "")}.json";
            this.LoadPath();

            if (false && content.DutyModes != DutyMode.None)
            {
                Svc.Log.Info("Identifying duty mode");
                if (PartyHelper.PartyMember2?.ObjectKind == ObjectKind.BattleNpc)
                    unsafe
                    {
                        Span<FFXIVClientStructs.FFXIV.Client.Game.UI.Buddy.BuddyMember> span = new(UIState.Instance()->Buddy.DutyHelperInfo.DutyHelpers, 7);

                         if (span[0].Synced == 0)
                         {
                             Configuration.Meta.dutyModeEnum = content.DutyModes.HasFlag(DutyMode.Squadron) ?
                                                              DutyMode.Squadron : DutyMode.Trust;
                         }
                         else
                         {
                             Configuration.Meta.dutyModeEnum = DutyMode.Support;
                         }
                    }
                else
                    Configuration.Meta.dutyModeEnum = content.DutyModes.GetFlags().FirstOrDefault(dm => dm is not (DutyMode.None or DutyMode.Squadron or DutyMode.Support or DutyMode.Trust));

                int level = Player.Level;
                Configuration.Meta.Unsynced = level == PlayerHelper.GetCurrentLevelFromSheet() && level - content.ClassJobLevelRequired > 3;
                Svc.Log.Info("DUTYMODE: " + Configuration.Meta.dutyModeEnum + " out of " + content.DutyModes + " - " + string.Join("|",content.DutyModes.GetFlags().Select(dm => dm.ToString())));
            }
        }
        else
        {
            this.CurrentTerritoryContent = null;
            this.pathFile                = "";
            MainWindow.ShowPopup("Error", "Unable to load content for Territory");
            return;
        }
        //MainWindow.OpenTab("Mini");
        if (Configuration.Overlay.Show)
            //MainWindow.IsOpen = false;
            this.Overlay.IsOpen = true;

        this.mainListClicked =  false;
        this.Stage           =  Stage.Reading_Path;
        this.States          |= PluginState.Navigating;

        this.DutyData = new DutyDataTemporary();

        if (Configuration.DutyConfig.AutoManageVnavAlignCamera && !VNavmesh_IPCSubscriber.Path_GetAlignCamera)
            VNavmesh_IPCSubscriber.Path_SetAlignCamera(true);

        if (Configuration is { DutyConfig: { AutoManageBossModAISettings: true, BossMod.UpdatePresetsAutomatically: true }})
        {
            BossMod_IPCSubscriber.RefreshPreset("AutoDuty",         Resources.AutoDutyPreset);
            BossMod_IPCSubscriber.RefreshPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
        }

        if (Configuration.DutyConfig.AutoManageBossModAISettings) 
            SetBMSettings();
        if (Configuration is { DutyConfig: { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false } }) 
            this.SetRotationPluginSettings(true);

        if (Configuration.DutyConfig.LootTreasure)
        {
            if (PandorasBox_IPCSubscriber.IsEnabled)
                PandorasBox_IPCSubscriber.SetFeatureEnabled("Automatically Open Chests", Configuration.DutyConfig.LootMethodEnum is LootMethod.Pandora or LootMethod.All);
            this.lootTreasure = Configuration.DutyConfig.LootMethodEnum is LootMethod.AutoDuty or LootMethod.All;
        }
        else
        {
            if (PandorasBox_IPCSubscriber.IsEnabled)
                PandorasBox_IPCSubscriber.SetFeatureEnabled("Automatically Open Chests", false);
            this.lootTreasure = false;
        }
        Svc.Log.Info("Starting Navigation");
        if (startFromZero) 
            this.indexer = 0;
    }

    private void DoneNavigating()
    {
        this.States &= ~PluginState.Navigating;
        this.CheckFinishing();
    }

    private void CheckFinishing()
    {
        //we finished lets exit the duty or stop
        if ((Configuration.DutyConfig.AutoExitDuty || this.currentLoop < Configuration.Meta.LoopTimes))
        {
            if (!this.Stage.EqualsAny(Stage.Stopped, Stage.Paused) &&
                (!Configuration.DutyConfig.OnlyExitWhenDutyDone || this.dutyState == DutyState.DutyComplete) &&
                !this.States.HasFlag(PluginState.Navigating))
            {
                if (ExitDutyHelper.State != ActionState.Running)
                    this.ExitDuty();
                if (Configuration is { DutyConfig: { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false } })
                    this.SetRotationPluginSettings(false);
                if (Configuration.DutyConfig.AutoManageBossModAISettings)
                    BossMod_IPCSubscriber.DisablePresets();
            }
        }
        else
        {
            this.Stage = Stage.Stopped;
        }
    }

    private void GetGeneralSettings()
    {
        /*
        if (Configuration.AutoManageVnavAlignCamera && VNavmesh_IPCSubscriber.IsEnabled && VNavmesh_IPCSubscriber.Path_GetAlignCamera())
            _settingsActive |= SettingsActive.Vnav_Align_Camera_Off;
        */
        if (YesAlready_IPCSubscriber.IsEnabled && YesAlready_IPCSubscriber.IsPluginEnabled) 
            this.settingsActive |= SettingsActive.YesAlready;

        if (PandorasBox_IPCSubscriber.IsEnabled && (PandorasBox_IPCSubscriber.GetFeatureEnabled("Auto-interact with Objects in Instances") ?? false))
            this.settingsActive |= SettingsActive.Pandora_Interact_Objects;

        Svc.Log.Debug($"General Settings Active: {this.settingsActive}");
    }

    internal void SetGeneralSettings(bool on)
    {
        if (!on)
            this.GetGeneralSettings();

        if (Configuration.DutyConfig.AutoManageVnavAlignCamera && this.settingsActive.HasFlag(SettingsActive.Vnav_Align_Camera_Off))
        {
            Svc.Log.Debug($"Setting VnavAlignCamera: {on}");
            VNavmesh_IPCSubscriber.Path_SetAlignCamera(on);
        }
        if (PandorasBox_IPCSubscriber.IsEnabled && this.settingsActive.HasFlag(SettingsActive.Pandora_Interact_Objects))
        {
            Svc.Log.Debug($"Setting PandorasBos Auto-interact with Objects in Instances: {on}");
            PandorasBox_IPCSubscriber.SetFeatureEnabled("Auto-interact with Objects in Instances", on);
        }
        if (YesAlready_IPCSubscriber.IsEnabled && this.settingsActive.HasFlag(SettingsActive.YesAlready))
        {
            Svc.Log.Debug($"Setting YesAlready Enabled: {on}");
            YesAlready_IPCSubscriber.SetState(on);
        }
    }

    internal void SetRotationPluginSettings(bool on, bool ignoreConfig = false, bool ignoreTimer = false)
    {
        // Only try to set the rotation state every few seconds
        if (on && (DateTime.Now - this.lastRotationSetTime).TotalSeconds < 5 && !ignoreTimer)
            return;
        
        if(on) 
            this.lastRotationSetTime = DateTime.Now;

        if (!ignoreConfig && !Configuration.DutyConfig.AutoManageRotationPluginState)
            return;

        bool? EnableWrath(bool active)
        {
            if (Wrath_IPCSubscriber.IsEnabled)
            {
                bool wrathRotationReady = true;
                if (active)
                    wrathRotationReady = Wrath_IPCSubscriber.IsCurrentJobAutoRotationReady ||
                                         Configuration.DutyConfig.Wrath.AutoSetupJobs && Wrath_IPCSubscriber.SetJobAutoReady();

                if (!active || wrathRotationReady)
                {
                    Svc.Log.Debug("Wrath rotation:" + active);
                    Wrath_IPCSubscriber.SetAutoMode(active);

                    return true;
                }
                return false;
            }
            return null;
        }

        bool? EnableRSR(bool active)
        {
            if (RSR_IPCSubscriber.IsEnabled)
            {
                Svc.Log.Debug("RSR: " + active);
                if (active)
                    RSR_IPCSubscriber.RotationAuto();
                else
                    RSR_IPCSubscriber.RotationStop();
                return true;
            }
            return null;
        }

        bool? EnableBM(bool active, bool rotation)
        {
            if (BossMod_IPCSubscriber.IsEnabled)
            {
                if (active)
                {
                    BossMod_IPCSubscriber.SetRange(Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat);
                    if (rotation)
                        BossMod_IPCSubscriber.SetPreset("AutoDuty", Resources.AutoDutyPreset);
                    else if (Configuration.DutyConfig.AutoManageBossModAISettings)
                        BossMod_IPCSubscriber.SetPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
                    return true;
                }
                else if (!rotation || Configuration.DutyConfig.AutoManageBossModAISettings)
                {
                    BossMod_IPCSubscriber.DisablePresets();
                    return true;
                }
                return false;
            }
            return null;
        }

        bool act = on;

        bool  wrathEnabled = Configuration is { DutyConfig.RotationPlugin: RotationPlugin.WrathCombo or RotationPlugin.All, Meta.DutyModeEnum: not DutyMode.NoviceHall };
        bool? wrath        = EnableWrath(on && wrathEnabled);
        if (on && wrathEnabled && wrath.HasValue)
            act = !wrath.Value;
        
        bool  rsrEnabled = Configuration is { DutyConfig.RotationPlugin: RotationPlugin.RotationSolverReborn or RotationPlugin.All, Meta.DutyModeEnum: not DutyMode.NoviceHall };
        bool? rsr        = EnableRSR(act && on && rsrEnabled);
        if (on && rsrEnabled && rsr.HasValue) 
            act = !rsr.Value;

        EnableBM(on, act && (Configuration.DutyConfig.RotationPlugin is RotationPlugin.BossMod or RotationPlugin.All || Configuration.Meta.DutyModeEnum is DutyMode.NoviceHall));
    }

    internal static void SetBMSettings(bool defaults = false)
    {
        BMRoleChecks();

        if (defaults)
        {
            Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased = true;
            Configuration.DutyConfig.BossMod.PositionalRoleBased          = true;
        }

        BossMod_IPCSubscriber.SetMovement(true);
        BossMod_IPCSubscriber.SetRange(Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat);
    }

    internal static void BMRoleChecks()
    {
        //RoleBased Positional
        if (PlayerHelper.IsValid && Configuration.DutyConfig.BossMod.PositionalRoleBased && Configuration.DutyConfig.BossMod.PositionalEnum != (Player.ClassJob.Value.GetJobRole() == JobRole.Melee ? Positional.Rear : Positional.Any))
        { 
            Configuration.DutyConfig.BossMod.PositionalEnum = (Player.ClassJob.Value.GetJobRole() == JobRole.Melee ? Positional.Rear : Positional.Any); 
            ConfigurationProfileV2.Save();
        }

        ClassJob classJob = Player.ClassJob.Value;

        //RoleBased MaxDistanceToTarget
        float maxDistanceToTarget = (classJob.GetJobRole() is JobRole.Melee or JobRole.Tank ? 
                                         Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleMelee : Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleRanged);
        if (PlayerHelper.IsValid && Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased && Math.Abs(Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat - maxDistanceToTarget) > 0.01f)
        {
            Configuration.DutyConfig.BossMod.MaxDistanceToTargetFloat = maxDistanceToTarget;
            ConfigurationProfileV2.Save();
        }

        //RoleBased MaxDistanceToTargetAoE

        float maxDistanceToTargetAoE = (classJob.GetJobRole() is JobRole.Melee or JobRole.Tank or JobRole.Ranged_Physical || (classJob.GetJobRole() == JobRole.Healer && classJob.RowId != (uint) Job.AST) ?
                                            Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleMelee : Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleRanged);
        if (PlayerHelper.IsValid && Configuration.DutyConfig.BossMod.MaxDistanceToTargetRoleBased && Math.Abs(Configuration.DutyConfig.BossMod.MaxDistanceToTargetAoEFloat - maxDistanceToTargetAoE) > 0.01f)
        {
            Configuration.DutyConfig.BossMod.MaxDistanceToTargetAoEFloat = maxDistanceToTargetAoE;
            ConfigurationProfileV2.Save();
        }
    }

    private unsafe void ActionInvoke()
    {
        if (this.pathAction == null) 
            return;

        if (!this.taskManager.IsBusy && !this.pathAction.Name.IsNullOrEmpty())
        {
            this.actions.InvokeAction(this.pathAction);
            this.pathAction = new PathAction();
        }
    }

    private void GetJobAndLevelingCheck()
    {
        Job curJob = Player.Job;
        if (curJob != this.jobLastKnown)
            if (this.LevelingEnabled)
            {
                Svc.Log.Info($"{(Configuration.Meta.DutyModeEnum == DutyMode.Support || Configuration.Meta.DutyModeEnum == DutyMode.Trust) && (Configuration.Meta.DutyModeEnum == DutyMode.Support || this.SupportLevelingEnabled) && (Configuration.Meta.DutyModeEnum != DutyMode.Trust || this.TrustLevelingEnabled)} ({Configuration.Meta.DutyModeEnum == DutyMode.Support} || {Configuration.Meta.DutyModeEnum == DutyMode.Trust}) && ({Configuration.Meta.DutyModeEnum == DutyMode.Support} || {this.SupportLevelingEnabled}) && ({Configuration.Meta.DutyModeEnum != DutyMode.Trust} || {this.TrustLevelingEnabled})");
                Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);
                if (duty != null)
                {
                    Plugin.CurrentTerritoryContent = duty;
                    this.mainListClicked           = true;
                    ContentPathsManager.DictionaryPaths[Plugin.CurrentTerritoryContent.TerritoryType].SelectPath(out this.currentPath);
                }
                else
                {
                    Plugin.CurrentTerritoryContent = null;
                    this.currentPath               = -1;
                }
            }

        this.jobLastKnown = curJob;
    }

    private void CheckRetainerWindow()
    {
        if (AutoRetainerHelper.State == ActionState.Running || AutoRetainer_IPCSubscriber.IsBusy() || this.Stage == Stage.Paused)
            return;

        if (Svc.Condition[ConditionFlag.OccupiedSummoningBell])
            AutoRetainerHelper.Instance.CloseAddons();
    }

    private void InteractablesCheck()
    {
        if (this.Interactables.Count == 0) return;

        IEnumerable<IGameObject> list = Svc.Objects.Where(x => this.Interactables.Contains(x.BaseId));

        if (!list.Any()) return;

        int index = this.Actions.Select((value, index) => (Value: value, Index: index))
                        .First(x => this.Interactables.Contains(x.Value.Arguments.Any(y => y.Any(z => z == ' ')) ? uint.Parse(x.Value.Arguments[0].Split(" ")[0]) : uint.Parse(x.Value.Arguments[0]))).Index;

        if (index > this.indexer)
        {
            this.indexer = index;
            this.Stage      = Stage.Reading_Path;
        }
    }

    private void PreStageChecks()
    {
        if (this.Stage == Stage.Stopped)
            return;

        this.CheckRetainerWindow();

        this.InteractablesCheck();

        if (EzThrottler.Throttle("OverrideAFK") && this.States.HasFlag(PluginState.Navigating) && PlayerHelper.IsValid) this.overrideAfk.ResetTimers();

        if (!Player.Available) 
            return;

        if (!InDungeon && this.CurrentTerritoryContent != null) this.GetJobAndLevelingCheck();

        if (!PlayerHelper.IsValid || !BossMod_IPCSubscriber.IsEnabled || !VNavmesh_IPCSubscriber.IsEnabled) 
            return;

        if (!RSR_IPCSubscriber.IsEnabled && !BossMod_IPCSubscriber.IsEnabled && !Configuration.DutyConfig.UsingAlternativeRotationPlugin) 
            return;

        if (this.currentTerritoryType == 0 && Svc.ClientState.TerritoryType != 0 && InDungeon) this.ClientState_TerritoryChanged(Svc.ClientState.TerritoryType);

        if (this.States.HasFlag(PluginState.Navigating) && Configuration.DutyConfig.LootTreasure && (!Configuration.DutyConfig.LootBossTreasureOnly || (this.pathAction?.Name == "Boss" && this.Stage == Stage.Action)) &&
            (this.treasureCofferGameObject = ObjectHelper.GetObjectsByObjectKind(ObjectKind.Treasure)
                                                        ?.FirstOrDefault(x => ObjectHelper.GetDistanceToPlayer(x) < 2)) != null)
        {
            BossMod_IPCSubscriber.SetRange(30f);
            ObjectHelper.InteractWithObject(this.treasureCofferGameObject, false);
        }

        if (this.indexer >= this.Actions.Count && this.Actions.Count > 0 && this.States.HasFlag(PluginState.Navigating)) this.DoneNavigating();

        if (this.Stage > Stage.Condition && !this.States.HasFlag(PluginState.Other)) this.action = this.Stage.ToCustomString();
    }

    public void Framework_Update(IFramework framework)
    {
        this.PreStageChecks();

        this.DutyData?.FrameworkUpdate(framework);

        this.crucibleManager.Update();

        switch (this.Stage)
        {
            case Stage.Reading_Path:
                this.StageReadingPath();
                break;
            case Stage.Moving:
                this.StageMoving();
                break;
            case Stage.Action:
                this.StageAction();
                break;
            case Stage.Waiting_For_Combat:
                this.StageWaitingForCombat();
                break;
            case Stage.Stopped:
            case Stage.Looping:
            case Stage.Condition:
            case Stage.Paused:
            case Stage.Dead:
            case Stage.Revived:
            case Stage.Interactable:
            case Stage.Idle:
            default:
                break;
        }
    }

    public class DutyDataTemporary : IDisposable
    {
        public event IFramework.OnUpdateDelegate FrameworkUpdateInDuty = _ => { };

        public bool StayCloseToTank
        {
            get;
            set
            {
                BossMod_IPCSubscriber.StayCloseToTank(value);
                field = value;
            }
        } = true;

        public bool StopForCombat { get; set; } = true;

        public void FrameworkUpdate(IFramework framework) =>
            this.FrameworkUpdateInDuty(framework);

        public void Dispose()
        {
            this.FrameworkUpdateInDuty = _ => { };
            GC.SuppressFinalize(this);
        }
    }

    private void StopAndResetAll()
    {
        ConfigOverrideHelper.Pop();

        if (this.bareModeSettingsActive != SettingsActive.None)
        {
            Configuration.Loop.Pre.Enabled     = this.bareModeSettingsActive.HasFlag(SettingsActive.PreLoop_Enabled);
            Configuration.Loop.Between.Enabled = this.bareModeSettingsActive.HasFlag(SettingsActive.BetweenLoop_Enabled);
            Configuration.Loop.Termination.Enabled = this.bareModeSettingsActive.HasFlag(SettingsActive.TerminationActions_Enabled);
            this.bareModeSettingsActive                   = SettingsActive.None;
        }

        this.States = PluginState.None;

        if (this.taskManager != null)
        {
            this.taskManager.StepMode = false;
            this.taskManager.Abort();
        }

        this.mainListClicked = false;

        this.DutyData = null;

        if (!InDungeon) 
            this.currentLoop = 0;
        if (Configuration.DutyConfig.AutoManageBossModAISettings) 
            BossMod_IPCSubscriber.DisablePresets();

        this.actions?.Rotation(true, false);

        this.SetGeneralSettings(true);
        if (Configuration.DutyConfig is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) 
            this.SetRotationPluginSettings(false);
        if (this.indexer > 0 && !this.mainListClicked)
            this.indexer = -1;
        if (Configuration.Overlay is { Show: true, HideWhenStopped: true })
            this.Overlay.IsOpen = false;
        if (VNavmesh_IPCSubscriber.IsEnabled && VNavmesh_IPCSubscriber.Path_GetTolerance > 0.25F)
            VNavmesh_IPCSubscriber.Path_SetTolerance(0.25f);
        FollowHelper.SetFollow(null);

        if (VNavmesh_IPCSubscriber.IsEnabled && VNavmesh_IPCSubscriber.Path_IsRunning)
            VNavmesh_IPCSubscriber.Path_Stop();

        if (MapHelper.State == ActionState.Running)
            MapHelper.StopMoveToMapMarker();

        if (DeathHelper.DeathState == PlayerLifeState.Revived)
            DeathHelper.Stop();

        foreach (IActiveHelper helper in ActiveHelper.activeHelpers) 
            helper.StopIfRunning();

        Wrath_IPCSubscriber.Release();
        this.action = "";
    }

    public void Dispose()
    {
        GitHubHelper.Dispose();
        this.StopAndResetAll();
        MultiboxUtility.Config?.MultiBox =  false;
        Svc.Framework.Update             -= this.Framework_Update;
        Svc.Framework.Update             -= SchedulerHelper.ScheduleInvoker;
        FileHelper.FileSystemWatcher?.Dispose();
        FileHelper.fileWatcher?.Dispose();
        this.windowSystem?.RemoveAllWindows();
        ECommonsMain.Dispose();
        this.MainWindow?.Dispose();
        this.overrideCamera?.Dispose();
        Svc.ClientState.TerritoryChanged -= this.ClientState_TerritoryChanged;
        Svc.Condition.ConditionChange    -= this.Condition_ConditionChange;
        PctService.Dispose();
        PluginInterface.UiBuilder.Draw   -= this.UiBuilderOnDraw;
        Svc.Commands.RemoveHandler(CommandName);
    }

    private void DrawUI() => this.windowSystem.Draw();

    public void OpenConfigUI()
    {
        if (this.MainWindow != null)
        {
            this.MainWindow.IsOpen = true;
            MainWindow.OpenTab("Config");
        }
    }

    public void OpenMainUI() => 
        this.MainWindow?.IsOpen = true;
}
