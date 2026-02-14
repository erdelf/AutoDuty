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

using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Data;
using ECommons.Automation.NeoTaskManager;
using ECommons.Configuration;
using ECommons.EzIpcManager;
using ECommons.IPC.Subscribers;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Pictomancy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Multibox;
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
        get => Configuration.AutoDutyModeEnum switch
        {
            AutoDutyMode.Playlist when this.states.HasFlag(PluginState.Looping) || !InDungeon => (this.playlistCurrent.Count >= 0 && this.playlistIndex < this.playlistCurrent.Count && this.playlistIndex >= 0) ?
                                                                                                              this.playlistCurrent[this.playlistIndex].Content : null,
            AutoDutyMode.Looping or _ => field
        };
        set
        {
            this.currentPlayerItemLevelAndClassJob = PlayerHelper.IsValid ? new KeyValuePair<ushort, Job?>(InventoryHelper.CurrentItemLevel, Player.Job) : new KeyValuePair<ushort, Job?>(0, null);
            field           = value;
        }
    } = null;

    internal uint currentTerritoryType = 0;
    internal int  currentPath          = -1;

    internal readonly List<PlaylistEntry> playlistCurrent = [];
    internal          int                 playlistIndex   = 0;

    internal PlaylistEntry? PlaylistCurrentEntry => this.playlistIndex >= 0 && this.playlistIndex < this.playlistCurrent.Count ?
                                                        this.playlistCurrent[this.playlistIndex] : null;

    internal bool SupportLevelingEnabled => this.LevelingModeEnum == LevelingMode.Support;
    internal bool TrustLevelingEnabled   => this.LevelingModeEnum.IsTrustLeveling();
    internal bool LevelingEnabled        => this.LevelingModeEnum != LevelingMode.None;

    internal static   string         Name   => "AutoDuty";
    internal static   AutoDuty       Plugin { get; private set; } = null!;
    internal          bool           stopForCombat    = true;
    internal readonly DirectoryInfo  pathsDirectory   = null!;
    internal readonly FileInfo       assemblyFileInfo = null!;
    internal readonly FileInfo       configFile       = null!;
    internal readonly DirectoryInfo? dalamudDirectory;
    internal          DirectoryInfo? assemblyDirectoryInfo;

    internal static   Configuration Configuration => ConfigurationMain.Instance.GetCurrentConfig;
    internal readonly WindowSystem  windowSystem = new("AutoDuty");

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
                    this.states               |= PluginState.Paused;
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
                    BossMod_IPCSubscriber.SetRange(Configuration.MaxDistanceToTargetFloat);
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

            if (value is Stage.Stopped or Stage.Paused && !this.runStartTime.Equals(DateTime.UnixEpoch))
            {
                ConfigurationMain.Instance.stats.timeSpent += DateTime.UtcNow.Subtract(this.runStartTime);
                this.runStartTime                          =  DateTime.UnixEpoch;
                Configuration.Save();
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
    internal          PluginState                   states          = PluginState.None;
    internal          int                           indexer         = -1;
    internal          bool                          mainListClicked = false;
    internal          IBattleChara?                 bossObject;

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
    private readonly  ActionsManager           actions           = null!;
    private readonly  SquadronManager          squadronManager   = null!;
    private readonly  VariantManager           variantManager    = null!;
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

    public AutoDuty()
    {
        try
        {
            Plugin = this;

            IPCBase.DefaultWrapper = SafeWrapper.IPCException;
            ECommonsMain.Init(PluginInterface, Plugin, Module.DalamudReflector, Module.ObjectFunctions);
            PictoService.Initialize(PluginInterface);

            this.isDev = PluginInterface.IsDev;

            //EzConfig.Init<ConfigurationMain>();
            EzConfig.DefaultSerializationFactory = new AutoDutySerializationFactory();
            (ConfigurationMain.Instance = EzConfig.Init<ConfigurationMain>()).Init();

            // Initialize localization system
            LocalizationManager.Initialize();



            //Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            
            ConfigTab.BuildManuals();
            this.configDirectory      = PluginInterface.ConfigDirectory;
            this.configFile            = PluginInterface.ConfigFile;
            this.dalamudDirectory      = this.configFile.Directory?.Parent;
            this.pathsDirectory        = new DirectoryInfo(this.configDirectory.FullName + "/paths");
            this.assemblyFileInfo      = PluginInterface.AssemblyLocation;
            this.assemblyDirectoryInfo = this.assemblyFileInfo.Directory;

            this.Version = 
                ((PluginInterface.IsDev     ? new Version(0,0,0, 291) :
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
            Patcher.Patch(startup: true);

            this.overrideAfk     = new OverrideAFK();
            this.ipcProvider     = new IPCProvider();
            this.squadronManager = new SquadronManager(this.taskManager);
            this.variantManager  = new VariantManager(this.taskManager);
            this.actions         = new ActionsManager(Plugin, this.taskManager);
            BuildTab.ActionsList  = this.actions.actionsList;
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
                                                     Plugin.states               &= ~PluginState.Paused;
                                                 }
                                             }),
                (["queue"], "queues duty", argsArray =>
                                           {
                                               QueueHelper.Invoke(ContentHelper.DictionaryContent
                                                                               .FirstOrDefault(x => x.Value.Name!.Equals(string.Join(" ", argsArray).Replace("queue ", string.Empty), StringComparison.InvariantCultureIgnoreCase)).Value ??
                                                                  null,
                                                                  Configuration.DutyModeEnum);
                                           }),
                (["overlay"], "opens overlay", argsArray =>
                                               {
                                                   if (argsArray.Length == 1)
                                                   {
                                                       Configuration.ShowOverlay = true;
                                                       this.Overlay.IsOpen       = true;

                                                       if (!Plugin.states.HasAnyFlag(PluginState.Looping, PluginState.Navigating))
                                                           Configuration.HideOverlayWhenStopped = false;
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
                                                             if (this.states.HasFlag(PluginState.Navigating))
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

                                                                              Configuration.DutyModeEnum = dutyMode;

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
                PrintInfo(() => $"YalmDistanceFromPlayerX: {gObj->YalmDistanceFromPlayerX}");
                PrintInfo(() => $"TargetStatus: {gObj->TargetStatus}");
                PrintInfo(() => $"YalmDistanceFromPlayerZ: {gObj->YalmDistanceFromPlayerZ}");
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
        ConfigurationMain.Instance.SetProfileToDefault();

        SchedulerHelper.ScheduleAction("LoginConfig", () =>
                                                      {
                                                          if(!ConfigurationMain.Instance.charByCID.ContainsKey(Player.CID))
                                                              ConfigurationMain.Instance.charByCID.Add(Player.CID, new ConfigurationMain.CharData
                                                                                                                   {
                                                                                                                       CID   = Player.CID,
                                                                                                                       Name  = Player.Name,
                                                                                                                       World = Player.CurrentWorldName
                                                                                                                   });

                                                          if (Configuration.ShowOverlay &&
                                                              (!Configuration.HideOverlayWhenStopped || this.states.HasFlag(PluginState.Looping) ||
                                                               this.states.HasFlag(PluginState.Navigating)))
                                                              SchedulerHelper.ScheduleAction("ShowOverlay", () => this.Overlay.IsOpen = true, () => PlayerHelper.IsReady);

                                                          if (Configuration.ShowMainWindowOnStartup)
                                                              SchedulerHelper.ScheduleAction("ShowMainWindowOnStartup", this.OpenMainUI, () => PlayerHelper.IsReady);
                                                      }, () => ConfigurationMain.Instance.Initialized);
                                
    }

    private void UiBuilderOnDraw()
    {
        if (PlayerHelper.IsValid)
        {
            using PctDrawList? drawList = PictoService.Draw();

            if (drawList != null)
            {
                BuildTab.DrawHelper(drawList);

                if (Configuration.PathDrawEnabled && this.CurrentTerritoryContent?.TerritoryType == Svc.ClientState.TerritoryType && this.Actions.Count != 0 && 
                    (this.indexer < 0 || this.indexer >= this.Actions.Count || !this.Actions[this.indexer].Name.Equals("Boss") || this.Stage != Stage.Action))
                {
                    Vector3 lastPos         = Player.Position;
                    float   stepCountFactor = (1f / Configuration.PathDrawStepCount);

                    for (int index = Math.Clamp(this.indexer, 0, this.Actions.Count-1); index < this.Actions.Count; index++)
                    {
                        PathAction action = this.Actions[index];
                        if (action.Position.LengthSquared() > 1)
                        {
                            float alpha = MathF.Max(0f, 1f - (index - this.indexer) * stepCountFactor);

                            if (alpha > 0)
                            {
                                drawList.AddCircle(action.Position, 3, ImGui.GetColorU32(new Vector4(1f, 0.2f, 0f, alpha)), 0, 3);

                                if (index > 0)
                                    drawList.AddLine(lastPos, action.Position, 0f, ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, alpha)));
                                if (index == this.indexer)
                                    drawList.AddLine(Player.Position, action.Position, 0, 0x00FFFFFF);

                                drawList.AddText(action.Position, ImGui.GetColorU32(new Vector4(alpha + 0.25f)), index.ToString(), 2f);
                            }

                            lastPos = action.Position;
                        }
                    }
                }
            }
        }
    }

    private DateTime lastDutyStart = DateTime.MinValue;

    private void DutyState_DutyStarted(object?     sender, ushort e)
    {
        this.dutyState         = DutyState.DutyStarted;
        this.lastDutyStart     = DateTime.UtcNow;
        DeathHelper.deathCount = 0;
    }

    private void DutyState_DutyWiped(object?       sender, ushort e) => this.dutyState = DutyState.DutyWiped;
    private void DutyState_DutyRecommenced(object? sender, ushort e) => this.dutyState = DutyState.DutyRecommenced;
    private void DutyState_DutyCompleted(object? sender, ushort e)
    {
        this.dutyState = DutyState.DutyComplete;
        if(this.states is not (PluginState.None or PluginState.Paused))
        {
            TimeSpan timeSpan = DateTime.UtcNow.Subtract(this.lastDutyStart);

            ConfigurationMain.StatData stats = ConfigurationMain.Instance.stats;

            stats.dutyRecords.Add(new DutyDataRecord(DateTime.UtcNow, timeSpan, Player.Territory.RowId, Player.CID, InventoryHelper.CurrentItemLevel, Player.Job, DeathHelper.deathCount));
            stats.dungeonsRun++;
            Configuration.Save();
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

                if (this.states.HasFlag(PluginState.Looping) && Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
                {
                    string? s = this.PlaylistCurrentEntry?.path ?? null;
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
        Configuration.EnableTerminationActions &&
        (this.CurrentTerritoryContent == null                                                                               ||
         (Configuration.StopLevel      && Player.Level >= Configuration.StopLevelInt) ||
         (Configuration.StopNoRestedXP && AgentHUD.Instance()->ExpRestedExperience == 0)                               ||
         (Configuration.TerminationBLUSpellsEnabled && (Configuration.TerminationBLUSpellsEnabled ?
                                                            Configuration.TerminationBLUSpells.All(BLUHelper.SpellUnlocked) :
                                                            Configuration.TerminationBLUSpells.Any(BLUHelper.SpellUnlocked))) ||
         (Configuration.StopItemQty && (Configuration.StopItemAll ?
                                            Configuration.StopItemQtyItemDictionary.All(x => InventoryManager.Instance()->GetInventoryItemCount(x.Key) >= x.Value.Value) :
                                            Configuration.StopItemQtyItemDictionary.Any(x => InventoryManager.Instance()->GetInventoryItemCount(x.Key) >= x.Value.Value))));

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

    private void ClientState_TerritoryChanged(ushort t)
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

        if (this.Stage == Stage.Stopped)
            return;

        Svc.Log.Debug($"ClientState_TerritoryChanged: t={t}");

        this.currentTerritoryType  = t;
        this.mainListClicked       = false;
        this.FrameworkUpdateInDuty = _ => { };

        if (t == 0)
            return;

        this.currentPath = -1;

        this.LoadPath();

        if (!this.states.HasFlag(PluginState.Looping) || GCTurninHelper.State == ActionState.Running || RepairHelper.State == ActionState.Running || GotoHelper.State == ActionState.Running || GotoInnHelper.State == ActionState.Running || GotoBarracksHelper.State == ActionState.Running || GotoHousingHelper.State == ActionState.Running || this.CurrentTerritoryContent == null)
        {
            Svc.Log.Debug("We Changed Territories but are doing after loop actions or not running at all or in a Territory not supported by AutoDuty");
            return;
        }

        if (Configuration is { ShowOverlay: true, HideOverlayWhenStopped: true } && !this.states.HasFlag(PluginState.Looping))
        {
            this.Overlay.IsOpen = false;
            this.MainWindow.IsOpen = true;
        }

        this.action = "";

        if (t != this.CurrentTerritoryContent.TerritoryType)
        {
            if (this.currentLoop < Configuration.LoopTimes || Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
            {
                this.taskManager.Abort();
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Loop {this.currentLoop} of {Configuration.LoopTimes}"), "Loop-Debug");
                this.taskManager.Enqueue(() => { this.Stage  =  Stage.Looping; },                                           "Loop-SetStage=99");
                this.taskManager.Enqueue(() => { this.states &= ~PluginState.Navigating; },                                 "Loop-RemoveNavigationState");
                this.taskManager.Enqueue(() => PlayerHelper.IsReady,                                                        "Loop-WaitPlayerReady", new TaskManagerConfiguration(int.MaxValue));
                if (Configuration.EnableBetweenLoopActions)
                {
                    this.taskManager.Enqueue(() => { this.action = $"Waiting {Configuration.WaitTimeBeforeAfterLoopActions}s"; },                                    "Loop-WaitTimeBeforeAfterLoopActionsActionSet");
                    this.taskManager.Enqueue(() => EzThrottler.Throttle("Loop-WaitTimeBeforeAfterLoopActions", Configuration.WaitTimeBeforeAfterLoopActions * 1000), "Loop-WaitTimeBeforeAfterLoopActionsThrottle");
                    this.taskManager.Enqueue(() => EzThrottler.Check("Loop-WaitTimeBeforeAfterLoopActions"), "Loop-WaitTimeBeforeAfterLoopActionsCheck",
                                             new TaskManagerConfiguration(Configuration.WaitTimeBeforeAfterLoopActions * 1000));
                    this.taskManager.Enqueue(() => { this.action = $"After Loop Actions"; }, "Loop-AfterLoopActionsSetAction");
                }

                this.TrustLeveling();

                this.taskManager.Enqueue(() =>
                                         {
                                             if (this.StopLoop)
                                             {
                                                 this.taskManager.Enqueue(() => Svc.Log.Info($"Loop Stop Condition Encountered, Stopping Loop"));
                                                 this.LoopTasks(false, Configuration is { EnableBetweenLoopActions: true, ExecuteBetweenLoopActionLastLoop: true });
                                             }
                                             else
                                             {
                                                 this.LoopTasks(between: Configuration.EnableBetweenLoopActions);
                                             }
                                         }, "Loop-CheckStopLoop");

            }
            else
            {
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Loops Done"), "Loop-Debug");
                this.taskManager.Enqueue(() => { this.states &= ~PluginState.Navigating; }, "Loop-RemoveNavigationState");
                this.taskManager.Enqueue(() => PlayerHelper.IsReady, "Loop-WaitPlayerReady", new TaskManagerConfiguration(timeLimitMS: int.MaxValue));
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Loop {this.currentLoop} == {Configuration.LoopTimes} we are done Looping, Invoking Loop Actions"), "Loop-Debug");
                this.taskManager.Enqueue(() => this.LoopTasks(false, Configuration is { EnableBetweenLoopActions: true, ExecuteBetweenLoopActionLastLoop: true }), "Loop-LoopCompleteActions");
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
        if (this.Stage is not Stage.Dead and not Stage.Revived and not Stage.Action && !this.recentlyWatchedCutscene && !Conditions.Instance()->WatchingCutscene && 
            flag is not ConditionFlag.WatchingCutscene and not ConditionFlag.WatchingCutscene78 and not ConditionFlag.OccupiedInCutSceneEvent and (ConditionFlag.BetweenAreas or ConditionFlag.BetweenAreas51 or ConditionFlag.Jumping61) && 
            value && this.states.HasFlag(PluginState.Navigating))
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
            Configuration.AutoDutyModeEnum = AutoDutyMode.Looping;

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
            Configuration.LoopTimes = loops;

        if (bareMode)
        {
            this.bareModeSettingsActive |= SettingsActive.BareMode_Active;
            if (Configuration.EnablePreLoopActions)
                this.bareModeSettingsActive |= SettingsActive.PreLoop_Enabled;
            if (Configuration.EnableBetweenLoopActions) 
                this.bareModeSettingsActive |= SettingsActive.BetweenLoop_Enabled;
            if (Configuration.EnableTerminationActions) 
                this.bareModeSettingsActive |= SettingsActive.TerminationActions_Enabled;
            Configuration.EnablePreLoopActions     = false;
            Configuration.EnableBetweenLoopActions = false;
            Configuration.EnableTerminationActions = false;
        }

        Svc.Log.Info($"Running AutoDuty in {this.CurrentTerritoryContent.EnglishName}, Looping {Configuration.LoopTimes} times{(bareMode ? " in BareMode (No Pre, Between or Termination Loop Actions)" : "")}");

        //MainWindow.OpenTab("Mini");
        if (Configuration.ShowOverlay)
            //MainWindow.IsOpen = false;
            this.Overlay.IsOpen = true;

        this.Stage =  Stage.Looping;
        this.states   |= PluginState.Looping;
        this.SetGeneralSettings(false);
        VNavmesh_IPCSubscriber.SetMovementAllowed(true);
        this.taskManager.Abort();
        Svc.Log.Info($"Running {this.CurrentTerritoryContent.Name} {Configuration.LoopTimes} Times");
        if (!InDungeon)
        {
            this.currentLoop = 0;
            if (Configuration.EnablePreLoopActions)
            {
                if (Configuration.ExecuteCommandsPreLoop)
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"ExecutingCommandsPreLoop, executing {Configuration.CustomCommandsTermination.Count} commands"));
                    Configuration.CustomCommandsPreLoop.Each(x => this.taskManager.Enqueue(() => Chat.ExecuteCommand(x), "Run-ExecuteCommandsPreLoop"));
                }

                if (Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist && Plugin.PlaylistCurrentEntry != null)
                    unsafe
                    {
                        if (Plugin.PlaylistCurrentEntry.gearset.HasValue && RaptureGearsetModule.Instance()->IsValidGearset(Plugin.PlaylistCurrentEntry.gearset.Value))
                        {
                            this.taskManager.Enqueue(() => RaptureGearsetModule.Instance()->EquipGearset(Plugin.PlaylistCurrentEntry.gearset.Value));
                            this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull);
                        }
                    }

                this.AutoConsume();

                if (this.LevelingModeEnum == LevelingMode.None) 
                    this.AutoEquipRecommendedGear();

                if (Configuration.AutoRepair && InventoryHelper.CanRepair())
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"AutoRepair PreLoop Action"));
                    this.taskManager.Enqueue(RepairHelper.Invoke, "Run-AutoRepair");
                    this.taskManager.EnqueueDelay(50);
                    this.taskManager.Enqueue(() => RepairHelper.State != ActionState.Running, "Run-WaitAutoRepairComplete", new TaskManagerConfiguration(int.MaxValue));
                    this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull,                  "Run-WaitAutoRepairIsReadyFull");
                }

                if (Configuration.DutyModeEnum != DutyMode.Squadron && Configuration.RetireMode)
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Retire PreLoop Action"));
                    switch (Configuration.RetireLocationEnum)
                    {
                        case RetireLocation.GC_Barracks:
                            this.taskManager.Enqueue(GotoBarracksHelper.Invoke, "Run-GotoBarracksInvoke");
                            break;
                        case RetireLocation.Inn:
                            this.taskManager.Enqueue(() => GotoInnHelper.Invoke(), "Run-GotoInnInvoke");
                            break;
                        case RetireLocation.Apartment:
                        case RetireLocation.Personal_Home:
                        case RetireLocation.FC_Estate:
                        default:
                            this.taskManager.Enqueue(() => GotoHousingHelper.Invoke((Housing)Configuration.RetireLocationEnum), "Run-GotoHousingInvoke");
                            break;
                    }
                    this.taskManager.EnqueueDelay(50);
                    this.taskManager.Enqueue(() => GotoHousingHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running, "Run-WaitGotoComplete",
                                             new TaskManagerConfiguration(int.MaxValue));
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
            if (Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist) 
            {
                Configuration.LoopTimes = Plugin.PlaylistCurrentEntry?.count ?? Configuration.LoopTimes;
                Plugin.PlaylistCurrentEntry!.curCount = 0;
        }
    }
    }

    internal unsafe void LoopTasks(bool queue = true, bool between = true)
    {
        this.taskManager.Enqueue(() => this.CurrentTerritoryContent != null, "Loop-WaitTillTerritory");

        if (between)
        {
            if (Configuration.ExecuteCommandsBetweenLoop)
            {
                this.taskManager.Enqueue(() => Svc.Log.Debug($"ExecutingCommandsBetweenLoops, executing {Configuration.CustomCommandsBetweenLoop.Count} commands"));
                Configuration.CustomCommandsBetweenLoop.Each(x => Chat.ExecuteCommand(x));
                this.taskManager.EnqueueDelay(1000);
            }

            if (Configuration.AutoOpenCoffers)
                EnqueueActiveHelper<CofferHelper>();

            if (AutoRetainer_IPCSubscriber.RetainersAvailable())
            {
                this.taskManager.Enqueue(() => Svc.Log.Debug($"AutoRetainer BetweenLoop Actions"));
                if (Configuration.EnableAutoRetainer)
                {
                    this.taskManager.Enqueue(() => AutoRetainerHelper.Invoke(), "Loop-AutoRetainer");
                    this.taskManager.EnqueueDelay(50);
                    this.taskManager.Enqueue(() => AutoRetainerHelper.State != ActionState.Running, "Loop-WaitAutoRetainerComplete", new TaskManagerConfiguration(int.MaxValue));
                }
                else
                {
                    this.taskManager.Enqueue(() => AutoRetainer_IPCSubscriber.IsBusy(),  "Loop-AutoRetainerIntegrationDisabledWait15sRetainerSense", new TaskManagerConfiguration(15000));
                    this.taskManager.Enqueue(() => !AutoRetainer_IPCSubscriber.IsBusy(), "Loop-AutoRetainerIntegrationDisabledWaitARNotBusy", new TaskManagerConfiguration(int.MaxValue));
                    this.taskManager.Enqueue(() => AutoRetainerHelper.ForceStop(),       "Loop-AutoRetainerStop");
                }
            }
        }


        if (queue && Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
        {
            PlaylistEntry? currentEntry = Plugin.PlaylistCurrentEntry;
            if (currentEntry != null && ++currentEntry.curCount < currentEntry.count)
            {
                Svc.Log.Debug($"repeating the duty once more: {currentEntry.curCount + 1} of {currentEntry.count}");
            }
            else
            {
                Svc.Log.Debug("next playlist entry");
                Plugin.playlistIndex++;
                if (Plugin.playlistIndex >= Plugin.playlistCurrent.Count)
                {
                    Svc.Log.Debug("playlist done");
                    queue                = false;
                    Plugin.playlistIndex = 0;
                }
                else
                {
                    Plugin.PlaylistCurrentEntry!.curCount = 0;

                    if (Plugin.PlaylistCurrentEntry.gearset.HasValue && RaptureGearsetModule.Instance()->IsValidGearset(Plugin.PlaylistCurrentEntry.gearset.Value))
                    {
                        this.taskManager.Enqueue(() => RaptureGearsetModule.Instance()->EquipGearset(Plugin.PlaylistCurrentEntry.gearset.Value));
                        this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull);
                    }
                }
            }
        }

        if (between)
        {
            this.AutoEquipRecommendedGear();

            if (Configuration.AutoRepair && InventoryHelper.CanRepair()) 
                EnqueueActiveHelper<RepairHelper>();

            if (Configuration.AutoExtract && QuestManager.IsQuestComplete(66174)) 
                EnqueueActiveHelper<ExtractHelper>();

            if (Configuration.AutoDesynth) 
                EnqueueActiveHelper<DesynthHelper>();

            if (Configuration.AutoGCTurnin && (!Configuration.AutoGCTurninSlotsLeftBool || InventoryManager.Instance()->GetEmptySlotsInBag() <= Configuration.AutoGCTurninSlotsLeft) && PlayerHelper.GetGrandCompanyRank() > 5)
                EnqueueActiveHelper<GCTurninHelper>();

            
            if (Configuration.TripleTriadRegister) 
                EnqueueActiveHelper<TripleTriadCardUseHelper>();
            if (Configuration.TripleTriadSell) 
                EnqueueActiveHelper<TripleTriadCardSellHelper>();
        

            if (Configuration.DiscardItems) 
                EnqueueActiveHelper<DiscardHelper>();

            if (Configuration.DutyModeEnum != DutyMode.Squadron && Configuration.RetireMode)
            {
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Retire Between Loop Action"));

                switch (Configuration.RetireLocationEnum)
                {
                    case RetireLocation.GC_Barracks:
                        this.taskManager.Enqueue(() => GotoBarracksHelper.Invoke(), "Loop-GotoBarracksInvoke");
                        break;
                    case RetireLocation.Inn:
                        this.taskManager.Enqueue(() => GotoInnHelper.Invoke(), "Loop-GotoInnInvoke");
                        break;
                    case RetireLocation.Apartment:
                    case RetireLocation.Personal_Home:
                    case RetireLocation.FC_Estate:
                    default:
                        Svc.Log.Info($"{(Housing)Configuration.RetireLocationEnum} {Configuration.RetireLocationEnum}");
                        this.taskManager.Enqueue(() => GotoHousingHelper.Invoke((Housing)Configuration.RetireLocationEnum), "Loop-GotoHousingInvoke");
                        break;
                }

                this.taskManager.EnqueueDelay(50);
                this.taskManager.Enqueue(() => GotoHousingHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running, "Loop-WaitGotoComplete",
                                         new TaskManagerConfiguration(int.MaxValue));
            }
        }

        void EnqueueActiveHelper<T>() where T : ActiveHelperBase<T>, new()
        {
            this.taskManager.Enqueue(() => Svc.Log.Debug($"Enqueueing {typeof(T).Name}"), "Loop-ActiveHelper");
            this.taskManager.Enqueue(() => ActiveHelperBase<T>.Invoke(), $"Loop-{typeof(T).Name}");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => ActiveHelperBase<T>.State != ActionState.Running, $"Loop-Wait-{typeof(T).Name}-Complete", new TaskManagerConfiguration(int.MaxValue));
            this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull,                         "Loop-WaitIsReadyFull");
        }

        if (queue || MultiboxUtility.Config is { MultiBox: true, Host: false }) 
            this.AutoConsume();

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
                                                               if (Plugin.states is PluginState.None)
                                                                   return;

                                                               if (Configuration.AutoDutyModeEnum == AutoDutyMode.Looping && this.LevelingEnabled)
                                                               {
                                                                   Svc.Log.Info("Leveling Enabled");
                                                                   Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);
                                                                   if (duty != null)
                                                                   {
                                                                       if (this.LevelingModeEnum      == LevelingMode.Support && Configuration.PreferTrustOverSupportLeveling &&
                                                                           duty.ClassJobLevelRequired > 70)
                                                                       {
                                                                           this.levelingModeEnum        = LevelingMode.Trust_Solo;
                                                                           Configuration.dutyModeEnum = DutyMode.Trust;

                                                                           Content? dutyTrust = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);

                                                                           if (duty != dutyTrust)
                                                                           {
                                                                               this.levelingModeEnum           = LevelingMode.Support;
                                                                               Configuration.dutyModeEnum = DutyMode.Support;
                                                                           }
                                                                       }

                                                                       Svc.Log.Info("Next Leveling Duty: " + duty.Name);
                                                                       this.CurrentTerritoryContent = duty;
                                                                       ContentPathsManager.DictionaryPaths[duty.TerritoryType].SelectPath(out this.currentPath);
                                                                   }
                                                                   else
                                                                   {
                                                                       this.currentLoop = Configuration.LoopTimes;
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
                                                                                            if (Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
                                                                                            {
                                                                                                this.currentLoop               = this.PlaylistCurrentEntry?.curCount ?? this.currentLoop + 1;
                                                                                                Configuration.LoopTimes = this.PlaylistCurrentEntry?.count ?? Configuration.LoopTimes;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                this.currentLoop ++;
                                                                                            }
                                                                                        }, "Loop-IncrementCurrentLoop");
                                                               this.taskManager.Enqueue(() => this.action = $"Looping: {this.CurrentTerritoryContent?.Name} {this.currentLoop} of {Configuration.LoopTimes}", "Loop-SetAction");
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

        if (Configuration.EnableTerminationActions)
        {
            this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull);
            this.taskManager.Enqueue(() => Svc.Log.Debug($"TerminationActions are Enabled"));
            if (Configuration.ExecuteCommandsTermination)
            {
                this.taskManager.Enqueue(() => Svc.Log.Debug($"ExecutingCommandsTermination, executing {Configuration.CustomCommandsTermination.Count} commands"));
                Configuration.CustomCommandsTermination.Each(x => Chat.ExecuteCommand(x));
            }

            if (Configuration.PlayEndSound)
            {
                this.taskManager.Enqueue(() => Svc.Log.Debug($"Playing End Sound"));
                SoundHelper.StartSound(Configuration.PlayEndSound, Configuration.CustomSound, Configuration.SoundEnum);
            }

            switch (Configuration.TerminationMethodEnum)
            {
                case TerminationMode.Kill_PC:
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Killing PC"));
                    if (!Configuration.TerminationKeepActive)
                    {
                        Configuration.TerminationMethodEnum = TerminationMode.Do_Nothing;
                            Configuration.Save();
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
                    if (!Configuration.TerminationKeepActive)
                    {
                        Configuration.TerminationMethodEnum = TerminationMode.Do_Nothing;
                            Configuration.Save();
                    }

                    this.taskManager.Enqueue(() => Chat.ExecuteCommand($"/xlkill"), "Killing the game");
                    break;
                }
                case TerminationMode.Logout:
                {
                    this.taskManager.Enqueue(() => Svc.Log.Debug($"Logging Out"));
                    if (!Configuration.TerminationKeepActive)
                    {
                        Configuration.TerminationMethodEnum = TerminationMode.Do_Nothing;
                            Configuration.Save();
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

        this.states   &= ~PluginState.Looping;
        this.currentLoop =  0;
        this.taskManager.Enqueue(() => SchedulerHelper.ScheduleAction("SetStageStopped", () => this.Stage = Stage.Stopped, 1));
    }

    private void AutoEquipRecommendedGear()
    {
        if (Configuration.AutoEquipRecommendedGear)
        {
            this.taskManager.Enqueue(() => Svc.Log.Debug($"AutoEquipRecommendedGear Between Loop Action"));
            this.taskManager.Enqueue(() => AutoEquipHelper.Invoke(), "AutoEquipRecommendedGear-Invoke");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => AutoEquipHelper.State != ActionState.Running, "AutoEquipRecommendedGear-WaitAutoEquipComplete", new TaskManagerConfiguration(int.MaxValue));
            this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull, "AutoEquipRecommendedGear-WaitANotIsOccupied");
        }
    }

    private void AutoConsume()
    {
        if (Configuration.AutoConsume)
        {
            this.taskManager.Enqueue(() => Svc.Log.Debug($"AutoConsume PreLoop Action"));
            Configuration.AutoConsumeItemsList.Each(x =>
                                                    {
                                                        bool isAvailable = InventoryHelper.IsItemAvailable(x.Value.ItemId, x.Value.CanBeHq);
                                                        if (isAvailable)
                                                        {
                                                            if (Configuration.AutoConsumeIgnoreStatus)
                                                                this.taskManager.Enqueue(() => InventoryHelper.UseItemUntilAnimationLock(x.Value.ItemId, x.Value.CanBeHq), $"AutoConsume - {x.Value.Name} is available: {isAvailable}");
                                                            else
                                                                this.taskManager.Enqueue(() => InventoryHelper.UseItemUntilStatus(x.Value.ItemId, x.Key, Configuration.AutoConsumeTime * 60, x.Value.CanBeHq), $"AutoConsume - {x.Value.Name} is available: {isAvailable}");
                                                        }

                                                        this.taskManager.EnqueueDelay(50);
                                                        this.taskManager.Enqueue(() => PlayerHelper.IsReadyFull, "AutoConsume-WaitPlayerIsReadyFull");
                                                        this.taskManager.EnqueueDelay(250);
                                                    });
        }
    }

    private void Queue(Content content)
    {
        if (Configuration.DutyModeEnum == DutyMode.Variant)
        {
            this.variantManager.RegisterVariantDuty(content);
        }
        else if (Configuration.DutyModeEnum.EqualsAny(DutyMode.Regular, DutyMode.Trial, DutyMode.Raid, DutyMode.Support, DutyMode.Trust))
        {
            this.taskManager.Enqueue(() => QueueHelper.Invoke(content, Configuration.DutyModeEnum), "Queue-Invoke");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => QueueHelper.State != ActionState.Running, "Queue-WaitQueueComplete", new TaskManagerConfiguration(int.MaxValue));
        }
        else if (Configuration.DutyModeEnum == DutyMode.Squadron)
        {
            this.taskManager.Enqueue(() => GotoBarracksHelper.Invoke(), "Queue-GotoBarracksInvoke");
            this.taskManager.EnqueueDelay(50);
            this.taskManager.Enqueue(() => GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running, "Queue-WaitGotoComplete", new TaskManagerConfiguration(int.MaxValue));
            this.squadronManager.RegisterSquadron(content);
        }

        this.taskManager.Enqueue(() => !PlayerHelper.IsValid, "Queue-WaitNotValid");
        this.taskManager.Enqueue(() => PlayerHelper.IsValid, "Queue-WaitValid", new TaskManagerConfiguration(int.MaxValue));
    }

    private void StageReadingPath()
    {
        if (!PlayerHelper.IsValid || !EzThrottler.Check("PathFindFailure") || this.indexer == -1 || this.indexer >= this.Actions.Count)
            return;

        this.action = $"{(this.Actions.Count >= this.indexer ? Plugin.Actions[this.indexer].ToCustomString() : "")}";

        this.pathAction = this.Actions[this.indexer];

        if (MultiboxUtility.MultiboxBlockingNextStep)
        {
            if (PartyHelper.PartyInCombat() && Plugin.stopForCombat)
            {
                if (Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false })
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

        bool unsync = !Svc.PlayerState.IsLevelSynced;

        if (this.pathAction.Tag.HasFlag(ActionTag.Unsynced) && !unsync)
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.indexer]} because we are synced");
            this.indexer++;
            return;
        }

        if (this.pathAction.Tag.HasFlag(ActionTag.W2W) && !Configuration.IsW2W(unsync: unsync))
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

        if ((this.skipTreasureCoffer || !Configuration.LootTreasure || Configuration.LootBossTreasureOnly) && this.pathAction.Tag.HasFlag(ActionTag.Treasure))
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

        BossMod_IPCSubscriber.InBoss(this.pathAction.Name.Equals("Boss"));

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
        if (!PlayerHelper.IsReady || this.indexer == -1 || this.indexer >= this.Actions.Count)
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

        if (PartyHelper.PartyInCombat() && Plugin.stopForCombat)
        {
            if (Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) 
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
                if (Configuration.StuckReturn && stuckCount >= Configuration.StuckReturnX)
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
                else if (Configuration.RebuildNavmeshOnStuck && stuckCount >= Configuration.RebuildNavmeshAfterStuckXTimes)
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
        
        if (Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false } && !Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]) this.SetRotationPluginSettings(true);
        
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
            if (Svc.Targets.Target == null)
            {
                //find and target closest attackable npc, if we are not targeting
                IGameObject? gos = ObjectHelper.GetObjectsByObjectKind(ObjectKind.BattleNpc)?.FirstOrDefault(o => o.GetNameplateKind() is NameplateKind.HostileEngagedSelfUndamaged or NameplateKind.HostileEngagedSelfDamaged && ObjectHelper.GetBattleDistanceToPlayer(o) <= 75);

                if (gos != null)
                    Svc.Targets.Target = gos;
            }
            if (Configuration.AutoManageBossModAISettings)
            {
                if (Svc.Targets.Target != null)
                {
                    int enemyCount = ObjectFunctions.GetAttackableEnemyCountAroundPoint(Svc.Targets.Target.Position, 15);

                    if (!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress && VNavmesh_IPCSubscriber.Path_IsRunning)
                        VNavmesh_IPCSubscriber.Path_Stop();

                    if (enemyCount > 2)
                    {
                        Svc.Log.Debug($"Changing MaxDistanceToTarget to {Configuration.MaxDistanceToTargetAoEFloat}, because enemy count = {enemyCount}");
                        BossMod_IPCSubscriber.SetRange(Configuration.MaxDistanceToTargetAoEFloat);
                    }
                    else
                    {
                        Svc.Log.Debug($"Changing MaxDistanceToTarget to {Configuration.MaxDistanceToTargetFloat}, because enemy count = {enemyCount}");
                        BossMod_IPCSubscriber.SetRange(Configuration.MaxDistanceToTargetFloat);
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
            BossMod_IPCSubscriber.SetRange(Configuration.MaxDistanceToTargetFloat);

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
                             Configuration.dutyModeEnum = content.DutyModes.HasFlag(DutyMode.Squadron) ?
                                                              DutyMode.Squadron : DutyMode.Trust;
                         }
                         else
                         {
                             Configuration.dutyModeEnum = DutyMode.Support;
                         }
                    }
                else
                    Configuration.dutyModeEnum = content.DutyModes.GetFlags().FirstOrDefault(dm => dm is not (DutyMode.None or DutyMode.Squadron or DutyMode.Support or DutyMode.Trust));

                Svc.Log.Info("DUTYMODE: " + Configuration.DutyModeEnum + " out of " + content.DutyModes + " - " + string.Join("|",content.DutyModes.GetFlags().Select(dm => dm.ToString())));
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
        if (Configuration.ShowOverlay)
            //MainWindow.IsOpen = false;
            this.Overlay.IsOpen = true;

        this.mainListClicked =  false;
        this.Stage           =  Stage.Reading_Path;
        this.states          |= PluginState.Navigating;
        this.stopForCombat   =  true;
        if (Configuration.AutoManageVnavAlignCamera && !VNavmesh_IPCSubscriber.Path_GetAlignCamera)
            VNavmesh_IPCSubscriber.Path_SetAlignCamera(true);

        if (Configuration is { AutoManageBossModAISettings: true, BM_UpdatePresetsAutomatically: true })
        {
            BossMod_IPCSubscriber.RefreshPreset("AutoDuty",         Resources.AutoDutyPreset);
            BossMod_IPCSubscriber.RefreshPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
        }

        if (Configuration.AutoManageBossModAISettings) 
            SetBMSettings();
        if (Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) 
            this.SetRotationPluginSettings(true);

        if (Configuration.LootTreasure)
        {
            if (PandorasBox_IPCSubscriber.IsEnabled)
                PandorasBox_IPCSubscriber.SetFeatureEnabled("Automatically Open Chests", Configuration.LootMethodEnum is LootMethod.Pandora or LootMethod.All);
            this.lootTreasure = Configuration.LootMethodEnum is LootMethod.AutoDuty or LootMethod.All;
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
        this.states &= ~PluginState.Navigating;
        this.CheckFinishing();
    }

    private void CheckFinishing()
    {
        //we finished lets exit the duty or stop
        if ((Configuration.AutoExitDuty || this.currentLoop < Configuration.LoopTimes))
        {
            if (!this.Stage.EqualsAny(Stage.Stopped, Stage.Paused) &&
                (!Configuration.OnlyExitWhenDutyDone || this.dutyState == DutyState.DutyComplete) &&
                !this.states.HasFlag(PluginState.Navigating))
            {
                if (ExitDutyHelper.State != ActionState.Running)
                    this.ExitDuty();
                if (Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false })
                    this.SetRotationPluginSettings(false);
                if (Configuration.AutoManageBossModAISettings)
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
        if (!on) this.GetGeneralSettings();

        if (Configuration.AutoManageVnavAlignCamera && this.settingsActive.HasFlag(SettingsActive.Vnav_Align_Camera_Off))
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

        if (!ignoreConfig && !Configuration.AutoManageRotationPluginState)
            return;

        bool? EnableWrath(bool active)
        {
            if (Wrath_IPCSubscriber.IsEnabled)
            {
                bool wrathRotationReady = true;
                if (active)
                    wrathRotationReady = Wrath_IPCSubscriber.IsCurrentJobAutoRotationReady ||
                                         ConfigurationMain.Instance.GetCurrentConfig.Wrath_AutoSetupJobs && Wrath_IPCSubscriber.SetJobAutoReady();

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
                    BossMod_IPCSubscriber.SetRange(Configuration.MaxDistanceToTargetFloat);
                    if (rotation)
                        BossMod_IPCSubscriber.SetPreset("AutoDuty", Resources.AutoDutyPreset);
                    else if (ConfigurationMain.Instance.GetCurrentConfig.AutoManageBossModAISettings)
                        BossMod_IPCSubscriber.SetPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
                    return true;
                }
                else if (!rotation || ConfigurationMain.Instance.GetCurrentConfig.AutoManageBossModAISettings)
                {
                    BossMod_IPCSubscriber.DisablePresets();
                    return true;
                }
                return false;
            }
            return null;
        }

        bool act = on;

        bool wrathEnabled = Configuration.rotationPlugin is RotationPlugin.WrathCombo or RotationPlugin.All;
        bool? wrath        = EnableWrath(on && wrathEnabled);
        if (on && wrathEnabled && wrath.HasValue)
            act = !wrath.Value;
        
        bool rsrEnabled = Configuration.rotationPlugin is RotationPlugin.RotationSolverReborn or RotationPlugin.All;
        bool? rsr        = EnableRSR(act && on && rsrEnabled);
        if (on && rsrEnabled && rsr.HasValue) 
            act = !rsr.Value;

        EnableBM(on, act && Configuration.rotationPlugin is RotationPlugin.BossMod or RotationPlugin.All);
    }

    internal static void SetBMSettings(bool defaults = false)
    {
        BMRoleChecks();

        if (defaults)
        {
            Configuration.MaxDistanceToTargetRoleBased = true;
            Configuration.PositionalRoleBased             = true;
        }

        BossMod_IPCSubscriber.SetMovement(true);
        BossMod_IPCSubscriber.SetRange(Configuration.MaxDistanceToTargetFloat);
    }

    internal static void BMRoleChecks()
    {
        //RoleBased Positional
        if (PlayerHelper.IsValid && Configuration.PositionalRoleBased && Configuration.PositionalEnum != (Player.ClassJob.Value.GetJobRole() == JobRole.Melee ? Positional.Rear : Positional.Any))
        {
            Configuration.PositionalEnum = (Player.ClassJob.Value.GetJobRole() == JobRole.Melee ? Positional.Rear : Positional.Any);
            Configuration.Save();
        }

        ClassJob classJob = Player.ClassJob.Value;

        //RoleBased MaxDistanceToTarget
        float maxDistanceToTarget = (classJob.GetJobRole() is JobRole.Melee or JobRole.Tank ? 
                                         Configuration.MaxDistanceToTargetRoleMelee : Configuration.MaxDistanceToTargetRoleRanged);
        if (PlayerHelper.IsValid && Configuration.MaxDistanceToTargetRoleBased && Math.Abs(Configuration.MaxDistanceToTargetFloat - maxDistanceToTarget) > 0.01f)
        {
            Configuration.MaxDistanceToTargetFloat = maxDistanceToTarget;
            Configuration.Save();
        }

        //RoleBased MaxDistanceToTargetAoE

        float maxDistanceToTargetAoE = (classJob.GetJobRole() is JobRole.Melee or JobRole.Tank or JobRole.Ranged_Physical || (classJob.GetJobRole() == JobRole.Healer && classJob.RowId != (uint) ClassJobType.Astrologian) ?
                                            Configuration.MaxDistanceToTargetRoleMelee : Configuration.MaxDistanceToTargetRoleRanged);
        if (PlayerHelper.IsValid && Configuration.MaxDistanceToTargetRoleBased && Math.Abs(Configuration.MaxDistanceToTargetAoEFloat - maxDistanceToTargetAoE) > 0.01f)
        {
            Configuration.MaxDistanceToTargetAoEFloat = maxDistanceToTargetAoE;
            Configuration.Save();
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
                Svc.Log.Info($"{(Configuration.DutyModeEnum == DutyMode.Support || Configuration.DutyModeEnum == DutyMode.Trust) && (Configuration.DutyModeEnum == DutyMode.Support || this.SupportLevelingEnabled) && (Configuration.DutyModeEnum != DutyMode.Trust || this.TrustLevelingEnabled)} ({Configuration.DutyModeEnum == DutyMode.Support} || {Configuration.DutyModeEnum == DutyMode.Trust}) && ({Configuration.DutyModeEnum == DutyMode.Support} || {this.SupportLevelingEnabled}) && ({Configuration.DutyModeEnum != DutyMode.Trust} || {this.TrustLevelingEnabled})");
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

        if (EzThrottler.Throttle("OverrideAFK") && this.states.HasFlag(PluginState.Navigating) && PlayerHelper.IsValid) this.overrideAfk.ResetTimers();

        if (!Player.Available) 
            return;

        if (!InDungeon && this.CurrentTerritoryContent != null) this.GetJobAndLevelingCheck();

        if (!PlayerHelper.IsValid || !BossMod_IPCSubscriber.IsEnabled || !VNavmesh_IPCSubscriber.IsEnabled) 
            return;

        if (!RSR_IPCSubscriber.IsEnabled && !BossMod_IPCSubscriber.IsEnabled && !Configuration.UsingAlternativeRotationPlugin) 
            return;

        if (this.currentTerritoryType == 0 && Svc.ClientState.TerritoryType != 0 && InDungeon) this.ClientState_TerritoryChanged(Svc.ClientState.TerritoryType);

        if (this.states.HasFlag(PluginState.Navigating) && Configuration.LootTreasure && (!Configuration.LootBossTreasureOnly || (this.pathAction?.Name == "Boss" && this.Stage == Stage.Action)) &&
            (this.treasureCofferGameObject = ObjectHelper.GetObjectsByObjectKind(ObjectKind.Treasure)
                                                        ?.FirstOrDefault(x => ObjectHelper.GetDistanceToPlayer(x) < 2)) != null)
        {
            BossMod_IPCSubscriber.SetRange(30f);
            ObjectHelper.InteractWithObject(this.treasureCofferGameObject, false);
        }

        if (this.indexer >= this.Actions.Count && this.Actions.Count > 0 && this.states.HasFlag(PluginState.Navigating)) this.DoneNavigating();

        if (this.Stage > Stage.Condition && !this.states.HasFlag(PluginState.Other)) this.action = this.Stage.ToCustomString();
    }

    public void Framework_Update(IFramework framework)
    {
        this.PreStageChecks();

        this.FrameworkUpdateInDuty(framework);

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

    public event IFramework.OnUpdateDelegate FrameworkUpdateInDuty = _ => {};

    private void StopAndResetAll()
    {
        if (this.bareModeSettingsActive != SettingsActive.None)
        {
            Configuration.EnablePreLoopActions     = this.bareModeSettingsActive.HasFlag(SettingsActive.PreLoop_Enabled);
            Configuration.EnableBetweenLoopActions = this.bareModeSettingsActive.HasFlag(SettingsActive.BetweenLoop_Enabled);
            Configuration.EnableTerminationActions = this.bareModeSettingsActive.HasFlag(SettingsActive.TerminationActions_Enabled);
            this.bareModeSettingsActive                   = SettingsActive.None;
        }

        this.states = PluginState.None;

        if (this.taskManager != null)
        {
            this.taskManager.StepMode = false;
            this.taskManager.Abort();
        }

        this.mainListClicked              = false;
        this.FrameworkUpdateInDuty = _ => {};
        if (!InDungeon) 
            this.currentLoop = 0;
        if (Configuration.AutoManageBossModAISettings) 
            BossMod_IPCSubscriber.DisablePresets();

        this.stopForCombat = true;
        this.actions.Rotation(true, false);

        this.SetGeneralSettings(true);
        if (Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) 
            this.SetRotationPluginSettings(false);
        if (this.indexer > 0 && !this.mainListClicked)
            this.indexer = -1;
        if (Configuration is { ShowOverlay: true, HideOverlayWhenStopped: true })
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
        PictoService.Dispose();
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
