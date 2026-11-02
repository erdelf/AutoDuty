using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;

namespace AutoDuty.Windows
{
    using Configurations;
    using Dalamud.Interface;
    using Dalamud.Interface.Textures.TextureWraps;
    using Dalamud.Interface.Utility;
    using Data;
    using ECommons.PartyFunctions;
    using ECommons.Throttlers;
    using FFXIVClientStructs.FFXIV.Client.UI.Misc;
    using FFXIVClientStructs.FFXIV.Common.Lua;
    using global::AutoDuty.Managers;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using static Data.Classes;
    using static System.Net.Mime.MediaTypeNames;
    using Vector2 = System.Numerics.Vector2;
    using Vector4 = System.Numerics.Vector4;

    internal static class MainTab
    {
        internal static ContentPathsManager.ContentPathContainer? DutySelected;
        internal static readonly (string Normal, string GameFont) Digits = ("0123456789", "");

        private static          int    currentStepIndex = -1;
        private static readonly string pathsURL         = "https://github.com/erdelf/AutoDuty/tree/master/AutoDuty/Paths";

        // New search text field for filtering duties
        private static string searchText    = string.Empty;
        public static string playlistName = string.Empty;

        internal static void Draw()
        {
            MainWindow.CurrentTabName = "Main";
            
            DutyMode     dutyMode     = Configuration.Meta.DutyModeEnum;
            LevelingMode levelingMode = Plugin.LevelingModeEnum;

            static void DrawSearchBar()
            {
                // Set the maximum search to 10 characters
                const int inputMaxLength = 10;
                
                // Calculate the X width of the maximum amount of search characters
                float inputMaxWidth = ImGui.CalcTextSize("W").X * inputMaxLength;
                
                // Set the width of the search box to the calculated width
                ImGui.SetNextItemWidth(inputMaxWidth);
                
                ImGui.InputTextWithHint("##search", Loc.Get("MainTab.SearchDuties"), ref searchText, inputMaxLength);

                // Apply filtering based on the search text
                if (searchText.Length > 0)
                    // Trim and convert to lowercase for case-insensitive search
                    searchText = searchText.Trim().ToLower();
            }

            static void DrawPathSelection()
            {
                if (Plugin.CurrentTerritoryContent == null || !PlayerHelper.IsReady)
                    return;

                using ImRaii.DisabledDisposable? d = ImRaii.Disabled(InDungeon && Plugin is { Stage: > 0 });

                if (ContentPathsManager.DictionaryPaths.TryGetValue(Plugin.CurrentTerritoryContent.TerritoryType, out ContentPathsManager.ContentPathContainer? container))
                {
                    List<ContentPathsManager.DutyPath> curPaths = container.Paths;
                    if (curPaths.Count > 1)
                    {
                        int                              curPath       = Math.Clamp(Plugin.currentPath, 0, curPaths.Count - 1);

                        Dictionary<string, JobWithRole>? pathSelection    = null;
                        JobWithRole                      curJob = Player.Job.JobToJobWithRole();
                        using (ImRaii.Disabled(curPath <= 0                                                                                           ||
                                               !Configuration.Meta.PathSelectionsByPath.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ||
                                               !(pathSelection = Configuration.Meta.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType])!.Any(kvp => kvp.Value.HasJob(Player.Job))))
                        {
                            if (ImGui.Button(Loc.Get("MainTab.ClearSavedPath")))
                            {
                                foreach (KeyValuePair<string, JobWithRole> keyValuePair in pathSelection!)
                                    pathSelection[keyValuePair.Key] &= ~curJob;

                                PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);
                                ConfigurationProfileV2.Save();
                                if (!InDungeon)
                                    container.SelectPath(out Plugin.currentPath);
                            }
                        }

                        ImGui.SameLine();
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.BeginCombo("##SelectedPath", curPaths[curPath].Name))
                        {
                            foreach ((ContentPathsManager.DutyPath Value, int Index) path in curPaths.Select((value, index) => (Value: value, Index: index)))
                            {
                                if (ImGui.Selectable(path.Value.Name))
                                {
                                    curPath = path.Index;
                                    PathSelectionHelper.AddPathSelectionEntry(Plugin.CurrentTerritoryContent!.TerritoryType);
                                    Dictionary<string, JobWithRole> pathJobs = Configuration.Meta.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType]!;
                                    pathJobs.TryAdd(path.Value.FileName, JobWithRole.None);
                                    
                                    foreach (string jobsKey in pathJobs.Keys) 
                                        pathJobs[jobsKey] &= ~curJob;

                                    pathJobs[path.Value.FileName] |= curJob;

                                    PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);

                                    ConfigurationProfileV2.Save();
                                    Plugin.currentPath = curPath;
                                    Plugin.LoadPath();
                                }
                                if (ImGui.IsItemHovered() && !path.Value.PathFile.Meta.Notes.All(x => x.IsNullOrEmpty()))
                                    ImGui.SetTooltip(string.Join("\n", path.Value.PathFile.Meta.Notes));
                            }
                            ImGui.EndCombo();
                        }
                        ImGui.PopItemWidth();
                        
                        if (ImGui.IsItemHovered() && !curPaths[curPath].PathFile.Meta.Notes.All(x => x.IsNullOrEmpty()))
                            ImGui.SetTooltip(string.Join("\n", curPaths[curPath].PathFile.Meta.Notes));
                        
                    }
                }
            }

            static void DrawTerminationNotice()
            {
                if (Configuration.Loop.Termination.Enabled &&
                    (Configuration.Loop.Termination.StopLevel      ||
                     Configuration.Loop.Termination.StopNoRestedXP ||
                     Configuration.Loop.Termination.StopItemQty    ||
                     Configuration.Loop.Termination.TerminationBLUSpellsEnabled))
                    ImGui.TextColoredWrapped(EzColor.Cyan, Loc.Get("MainTab.TerminationNotice"));
            }

            static void LoadPlaylist(Playlist playlist)
            {
                playlist.AdjustToCurrentChar();
                Plugin.PlaylistCurrent = playlist;
                playlistName           = playlist.Name;
            }

            if (InDungeon)
            {
                if (Plugin.CurrentTerritoryContent == null || Plugin.CurrentTerritoryContent.TerritoryType != Svc.ClientState.TerritoryType)
                {
                    Plugin.LoadPath();
                }
                else
                {
                    ImGui.AlignTextToFramePadding();
                    float progress = VNavmesh_IPCSubscriber.IsEnabled ? VNavmesh_IPCSubscriber.Nav_BuildProgress : 0;
                    if (progress >= 0)
                    {
                        ImGui.Text(Loc.Get("MainTab.MeshLoading", Plugin.CurrentTerritoryContent.Name));
                        ImGui.SameLine();
                        ImGui.ProgressBar(progress, new Vector2(200, 0));
                    }
                    else
                    {
                        ImGui.Text(Loc.Get("MainTab.MeshLoadedPath", Plugin.CurrentTerritoryContent.Name, ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ? Loc.Get("MainTab.Loaded") : Loc.Get("MainTab.None")));
                    }

                    ImGui.Separator();
                    ImGui.Spacing();

                    if (dutyMode == DutyMode.Trust && Plugin.CurrentTerritoryContent != null)
                    {
                        ImGui.Columns(3);
                        using (ImRaii.Disabled()) 
                            DrawTrustMembers(Plugin.CurrentTerritoryContent);
                        ImGui.Columns(1);
                        ImGui.Spacing();
                    }

                    if (Plugin.CurrentTerritoryContent?.DutyModes.HasFlag(DutyMode.Crucible) == true)
                    {
                        DrawCrucible(true);
                        ImGui.Separator();
                        ImGui.Spacing();
                    }

                    DrawPathSelection();
                    if (!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
                        MainWindow.GotoAndActions();
                    using (ImRaii.Disabled(!VNavmesh_IPCSubscriber.IsEnabled || !InDungeon || !VNavmesh_IPCSubscriber.Nav_IsReady || !BossMod_IPCSubscriber.IsEnabled))
                    {
                        using (ImRaii.Disabled(!InDungeon || !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType)))
                        {
                            if (Plugin.Stage == 0)
                            {
                                if (ImGui.Button(Loc.Get("MainTab.Start")))
                                {
                                    Plugin.LoadPath();
                                    currentStepIndex = -1;
                                    if (Plugin.mainListClicked)
                                        Plugin.Run(Svc.ClientState.TerritoryType, 0, !Plugin.mainListClicked);
                                    else
                                        Plugin.Run(Svc.ClientState.TerritoryType);
                                }
                            }
                            else
                            {
                                MainWindow.StopResumePause();
                            }

                            ImGui.SameLine(0, 15);
                        }
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("MainTab.Times")).X * 1.1f.Scale());
                        MainWindow.LoopsConfig();
                        ImGui.PopItemWidth();

                        if(dutyMode == DutyMode.Variant)
                        {
                            using ImRaii.ItemWidthDisposable _ = ImRaii.ItemWidth(150 * ImGuiHelpers.GlobalScale);
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(Loc.Get("MainTab.CurrentVariantPath"));
                            using ImRaii.DisabledDisposable __ = ImRaii.Disabled(Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist);
                            ImGui.SameLine();
                            byte variantPath = Plugin.VariantPath;
                            ImGui.InputByte($"###Path", ref variantPath, 1);
                            Plugin.VariantPath = variantPath;
                        }

                        DrawTerminationNotice();

                        if (!ImGui.BeginListBox("##MainList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) return;

                        if ((VNavmesh_IPCSubscriber.IsEnabled || Configuration.DutyConfig.UsingAlternativeMovementPlugin) &&
                            (BossMod_IPCSubscriber.IsEnabled  || Configuration.DutyConfig.UsingAlternativeBossPlugin)     &&
                            (RSR_IPCSubscriber.IsEnabled      || BossMod_IPCSubscriber.IsEnabled || Configuration.DutyConfig.UsingAlternativeRotationPlugin))
                        {
                            foreach ((PathAction Value, int Index) item in Plugin.Actions.Select((Value, Index) => (Value, Index))) item.Value.DrawCustomText(item.Index, () => ItemClicked(item));
                            //var text = item.Value.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase) ? item.Value.Note : $"{item.Value.ToCustomString()}";
                            ////////////////////////////////////////////////////////////////
                            if (currentStepIndex != Plugin.indexer && currentStepIndex > -1 && Plugin.Stage > 0)
                            {
                                float lineHeight = ImGui.GetTextLineHeightWithSpacing();
                                currentStepIndex = Plugin.indexer;
                                if (currentStepIndex > 1)
                                    ImGui.SetScrollY((currentStepIndex - 1) * lineHeight);
                            }
                            else if (currentStepIndex == -1 && Plugin.Stage > 0)
                            {
                                currentStepIndex = 0;
                                ImGui.SetScrollY(currentStepIndex);
                            }

                            if (InDungeon && Plugin is { Actions.Count: < 1 } && !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType))
                                ImGui.TextColored(new Vector4(0, 255, 0, 1),
                                                  Loc.Get("MainTab.NoPathFound", TerritoryName.GetTerritoryName(Plugin.CurrentTerritoryContent.TerritoryType).Split('|')[1].Trim(), Plugin.CurrentTerritoryContent.TerritoryType.ToString(), Plugin.pathsDirectory.FullName.Replace('\\', '/'), pathsURL));
                        }
                        else
                        {
                            if (!VNavmesh_IPCSubscriber.IsEnabled && !Configuration.DutyConfig.UsingAlternativeMovementPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresVNavmesh"));
                            if (!BossMod_IPCSubscriber.IsEnabled && !Configuration.DutyConfig.UsingAlternativeBossPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresBossMod"));
                            if (!Wrath_IPCSubscriber.IsEnabled && !RSR_IPCSubscriber.IsEnabled && !BossMod_IPCSubscriber.IsEnabled && !Configuration.DutyConfig.UsingAlternativeRotationPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresRotation"));
                        }
                        ImGui.EndListBox();
                    }
                }
            }
            else
            {
                if (!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
                    MainWindow.GotoAndActions();
                

                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Looping)))
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectMode"));
                    ImGui.SameLine(0);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.BeginCombo("##AutoDutyModeEnum", Loc.Get($"MainTab.Modes.{Configuration.Meta.AutoDutyModeEnum}")))
                    {
                        foreach (AutoDutyMode mode in Enum.GetValues(typeof(AutoDutyMode)))
                            if (ImGui.Selectable(Loc.Get($"MainTab.Modes.{mode}"), Configuration.Meta.AutoDutyModeEnum == mode))
                            {
                                Configuration.Meta.AutoDutyModeEnum = mode;
                                ConfigurationProfileV2.Save();
                            }

                        if (ImGui.Selectable(Loc.Get("MainTab.Modes.NoviceHall")))
                        {
                            Configuration.Meta.AutoDutyModeEnum = AutoDutyMode.Playlist;
                            LoadPlaylist(NoviceHelper.CreatePlaylist());
                        }
                        ImGui.EndCombo();
                    }
                    ImGui.PopItemWidth();
                }

                using (ImRaii.Disabled(Plugin.CurrentTerritoryContent == null))
                {
                    if (!Plugin.States.HasFlag(PluginState.Looping))
                    {
                        if (ImGui.Button(Loc.Get("MainTab.Run")))
                        {
                            bool synced = !QueueHelper.ShouldBeUnSynced();
                            if (Configuration.Meta.DutyModeEnum == DutyMode.None)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorSelectVersion"));
                            else if (Svc.Party.PartyId > 0 && Configuration.Meta.DutyModeEnum is DutyMode.Support or DutyMode.Squadron or DutyMode.Trust)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorNotInParty"));
                            else if (Configuration is { Meta.DutyModeEnum: DutyMode.Regular, DutyConfig.OverridePartyValidation: false } && synced && UniversalParty.Length < 4)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorGroupOf4"));
                            else if (Configuration is { Meta.DutyModeEnum: DutyMode.Regular, DutyConfig.OverridePartyValidation: false } && synced && !ObjectHelper.PartyValidation())
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorPartyMakeup"));
                            else if (Configuration.Meta is { DutyModeEnum: DutyMode.Crucible } && CrucibleTeam.For(Configuration.Meta.Crucible.TeamMode).Count < 3)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.Crucible.MinimumTeamCount"));
                            else if (ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent?.TerritoryType ?? 0))
                                Plugin.Run();
                            else
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorNoPath", Plugin.CurrentTerritoryContent?.TerritoryType.ToString() ?? "", Plugin.CurrentTerritoryContent?.Name ?? ""));
                        }
                    }
                    else
                    {
                        MainWindow.StopResumePause();
                    }
                }

                
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Looping)))
                {
                    switch (Configuration.Meta.AutoDutyModeEnum)
                    {
                        case AutoDutyMode.Looping:
                        {
                            using (ImRaii.Disabled(Plugin.CurrentTerritoryContent == null))
                            {
                                ImGui.SameLine(0, 15);
                                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("MainTab.Times")).X * 1.1f.Scale());
                                MainWindow.LoopsConfig();
                                ImGui.PopItemWidth();
                            }

                            ImGui.AlignTextToFramePadding();
                            ImGui.TextColored(Configuration.Meta.DutyModeEnum == DutyMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectDutyMode"));
                            ImGui.SameLine(0);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGui.BeginCombo("##DutyModeEnum", Loc.Get($"MainTab.DutyModes.{Configuration.Meta.DutyModeEnum}"), ImGuiComboFlags.HeightLargest))
                            {
                                foreach (DutyMode mode in Enum.GetValues(typeof(DutyMode)))
                                    //if(mode is not DutyMode.NoviceHall)
                                    if (ImGui.Selectable(Loc.Get($"MainTab.DutyModes.{mode}"), Configuration.Meta.DutyModeEnum == mode))
                                    {
                                        Configuration.Meta.DutyModeEnum = mode;
                                        ConfigurationProfileV2.Save();
                                    }

                                ImGui.EndCombo();
                            }
                            ImGui.PopItemWidth();

                            if (Configuration.Meta.DutyModeEnum != DutyMode.None)
                            {
                                if (Configuration.Meta.DutyModeEnum is DutyMode.Support or DutyMode.Trust)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextColored(Plugin.LevelingModeEnum == LevelingMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectLevelingMode"));
                                    ImGui.SameLine(0);

                                    ImGuiComponents.HelpMarker(Loc.Get("MainTab.LevelingModeHelp", Configuration.Meta.DutyModeEnum != DutyMode.Trust ?
                                                                                                       string.Empty : Loc.Get("MainTab.LevelingModeHelpTrust")));
                                    ImGui.SameLine(0);
                                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                                    if (ImGui.BeginCombo("##LevelingModeEnum", Plugin.LevelingModeEnum switch
                                        {
                                            LevelingMode.None => Loc.Get("MainTab.LevelingModes.None"),
                                            _ => Loc.Get("MainTab.LevelingModes."+Plugin.LevelingModeEnum)
                                        }))
                                    {
                                        if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes.None"), Plugin.LevelingModeEnum == LevelingMode.None))
                                        {
                                            Plugin.LevelingModeEnum = LevelingMode.None;
                                               ConfigurationProfileV2.Save();
                                        }

                                        LevelingMode autoLevelMode = (Configuration.Meta.DutyModeEnum == DutyMode.Support ? LevelingMode.Support : LevelingMode.Trust_Group);
                                        if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes."+autoLevelMode)+"##LevelingModeComboAuto", Plugin.LevelingModeEnum == autoLevelMode))
                                        {
                                            Plugin.LevelingModeEnum = autoLevelMode;
                                            ConfigurationProfileV2.Save();

                                            Configuration.Loop.Pre.Actions.RunConfig<AutoEquipLoopActionConfig>();
                                        }

                                        if (Configuration.Meta.DutyModeEnum == DutyMode.Trust)
                                            if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes." + "Trust_Solo") + "##LevelingModeComboTrustGroup", Plugin.LevelingModeEnum == LevelingMode.Trust_Solo))
                                            {
                                                Plugin.LevelingModeEnum = LevelingMode.Trust_Solo;
                                                ConfigurationProfileV2.Save();

                                                Configuration.Loop.Pre.Actions.RunConfig<AutoEquipLoopActionConfig>();
                                            }

                                        ImGui.EndCombo();
                                    }

                                    ImGui.PopItemWidth();
                                }

                                if (Configuration.Meta.DutyModeEnum == DutyMode.Support && levelingMode == LevelingMode.Support)
                                {
                                    bool preferTrust = Configuration.Meta.PreferTrustOverSupportLeveling;
                                    if (ImGui.Checkbox(Loc.Get("MainTab.PreferTrust"), ref preferTrust))
                                    {
                                        Configuration.Meta.PreferTrustOverSupportLeveling = preferTrust;
                                        ConfigurationProfileV2.Save();
                                    }
                                }

                                if (Plugin.LevelingEnabled)
                                {
                                    bool experimentalEntries = Configuration.DutyConfig.LevelingListExperimentalEntries;
                                    if (ImGui.Checkbox("Include Testing/Unstable Dungeons", ref experimentalEntries))
                                    {
                                        Configuration.DutyConfig.LevelingListExperimentalEntries = experimentalEntries;
                                       ConfigurationProfileV2.Save();
                                    }

                                    ImGuiEx.HelpMarker($"Adds more dungeons into the leveling list\nThese dungeons are currently in testing for reliability\nPlease report if you have issues with them");
                                }

                                if (Configuration.Meta.DutyModeEnum == DutyMode.Squadron)
                                {
                                    bool squadronLowest = Configuration.Meta.SquadronAssignLowestMembers;
                                    if (ImGui.Checkbox(Loc.Get("MainTab.UseLowestMembers"), ref squadronLowest))
                                    {
                                        Configuration.Meta.SquadronAssignLowestMembers = squadronLowest;
                                        ConfigurationProfileV2.Save();
                                    }
                                }

                                if (Configuration.Meta.DutyModeEnum == DutyMode.Crucible && Player.Available)
                                    DrawCrucible(false);

                                if (Configuration.Meta.DutyModeEnum == DutyMode.Trust && Player.Available)
                                {
                                    ImGui.Separator();
                                    if (DutySelected is { Content.TrustMembers.Count: > 0 })
                                    {
                                        ImGuiEx.LineCentered(() => ImGuiEx.TextUnderlined(Loc.Get("MainTab.SelectTrustParty")));

                                        TrustHelper.ResetTrustIfInvalid();
                                        for (int i = 0; i < Configuration.SelectedTrustMembers.Length; i++)
                                        {
                                            TrustMemberName? member = Configuration.SelectedTrustMembers[i];

                                            if (member is null)
                                                continue;

                                            if (DutySelected.Content.TrustMembers.All(x => x.MemberName != member))
                                            {
                                                Svc.Log.Debug($"Killing {member}");
                                                Configuration.SelectedTrustMembers[i] = null;
                                            }
                                        }

                                        ImGui.Columns(3);
                                        using (ImRaii.Disabled(Plugin.TrustLevelingEnabled && TrustHelper.Members.Any(tm => tm.Value.Level < tm.Value.LevelCap))) DrawTrustMembers(DutySelected.Content);

                                        //ImGui.Columns(3, null, false);
                                        if (DutySelected.Content.TrustMembers.Count == 7)
                                            ImGui.NextColumn();

                                        if (ImGui.Button(Loc.Get("MainTab.Refresh"), new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                                        {
                                            if (InventoryHelper.CurrentItemLevel < 370)
                                                Plugin.LevelingModeEnum = LevelingMode.None;
                                            TrustHelper.ClearCachedLevels();

                                            SchedulerHelper.ScheduleAction("Refresh Levels - ShB", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[837u]),  () => TrustHelper.State == ActionState.None);
                                            SchedulerHelper.ScheduleAction("Refresh Levels - EW",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[952u]),  () => TrustHelper.State == ActionState.None);
                                            SchedulerHelper.ScheduleAction("Refresh Levels - DT",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[1167u]), () => TrustHelper.State == ActionState.None);
                                        }

                                        ImGui.NextColumn();
                                        ImGui.Columns(1);
                                    }
                                    else if (ImGui.Button(Loc.Get("MainTab.RefreshTrustLevels")))
                                    {
                                        if (InventoryHelper.CurrentItemLevel < 370)
                                            Plugin.LevelingModeEnum = LevelingMode.None;
                                        TrustHelper.ClearCachedLevels();

                                        SchedulerHelper.ScheduleAction("Refresh Levels - ShB", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[837u]),  () => TrustHelper.State == ActionState.None);
                                        SchedulerHelper.ScheduleAction("Refresh Levels - EW",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[952u]),  () => TrustHelper.State == ActionState.None);
                                        SchedulerHelper.ScheduleAction("Refresh Levels - DT",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[1167u]), () => TrustHelper.State == ActionState.None);
                                    }
                                }

                                DrawPathSelection();
                                ImGui.Separator();

                                DrawSearchBar();
                                ImGui.SameLine();
                                bool hideUnavailableDuties = Configuration.Meta.HideUnavailableDuties;
                                if (ImGui.Checkbox(Loc.Get("MainTab.HideUnavailable"), ref hideUnavailableDuties))
                                {
                                    Configuration.Meta.HideUnavailableDuties = hideUnavailableDuties;
                                    ConfigurationProfileV2.Save();
                                }

                                if (Configuration.Meta.DutyModeEnum is DutyMode.Regular or DutyMode.Trial or DutyMode.Raid)
                                {
                                    bool metaUnsynced = Configuration.Meta.Unsynced;
                                    if (ImGuiEx.CheckboxWrapped(Loc.Get("MainTab.Unsynced"), ref metaUnsynced))
                                    {
                                        Configuration.Meta.Unsynced = metaUnsynced;
                                        ConfigurationProfileV2.Save();
                                    }
                                }
                            }

                            break;
                        }
                        case AutoDutyMode.Playlist:
                            ImGui.Separator();
                            break;
                        default:
                            Configuration.Meta.AutoDutyModeEnum = AutoDutyMode.Looping;
                            break;
                    }
                    if (Player.Available)
                    {
                        if (EzThrottler.Throttle("MainTabRemainingDungeonThrottle", 2000))
                        {
                            if (ConfigurationMain.Instance.dutyCountResetDate <= DateTime.UtcNow)
                                ConfigurationMain.Instance.dutyCountSinceReset.Clear();
                        }

                        ImGui.SameLine();
                        ImGui.Text($"|{Loc.Get("MainTab.DungeonsRemaining", Math.Max(0, 100 - ConfigurationMain.Instance.dutyCountSinceReset.GetValueOrDefault(Player.CID, 0)))}");
                        ImGuiComponents.HelpMarker(Loc.Get("MainTab.DungeonsRemainingExplanation"));
                    }

                    DrawTerminationNotice();

                    if (Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist)
                    {
                        using ImRaii.DisabledDisposable _ = ImRaii.Disabled(Plugin.States != PluginState.None);

                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 180 * ImGuiHelpers.GlobalScale);
                        ImGui.SetItemAllowOverlap();
                        using (ImRaii.ComboDisposable configCombo = ImRaii.Combo("##PlaylistCombo", Plugin.PlaylistCurrent.Name))
                        {
                            if (configCombo)
                                for (int index = 0; index < ConfigurationMain.Instance.Playlists.Count; index++)
                                {
                                    Playlist playlist = ConfigurationMain.Instance.Playlists[index];

                                    float textX = ImGui.GetCursorPosX();
                                    ImGui.SetItemAllowOverlap();
                                    if (ImGui.Selectable($"###{playlist.Name}_{index}PlaylistSelectable", playlist.Name == Plugin.PlaylistCurrent.Name))
                                        LoadPlaylist(playlist);

                                    ImGui.SameLine(textX);
                                    ImGui.Text(playlist.Name);
                                }
                        }

                        ImGui.PopItemWidth();

                        ImGui.SameLine(0, 15f);

                        using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl || Plugin.PlaylistCurrent.Name == ConfigurationMain.PLAYLISTNAME_EPHEMERAL))
                        {
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
                            {
                                ConfigurationMain.Instance.Playlists.RemoveAll(p => p.Name == Plugin.PlaylistCurrent.Name);
                                
                                LoadPlaylist(ConfigurationMain.Instance.Playlists[0]);

                               ConfigurationProfileV2.Save();
                            }
                        }

                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                            ImGui.SetTooltip(Loc.Get("MainTab.Playlist.DeleteHelp"));

                        ImGui.InputText("##PlaylistNameInput", ref playlistName);
                        ImGui.SameLine();
                        using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl || playlistName == ConfigurationMain.PLAYLISTNAME_EPHEMERAL))
                        {
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.Save))
                            {
                                Playlist clone = Plugin.PlaylistCurrent.JSONClone(ConfigurationMain.JsonSerializerSettings);

                                int index = ConfigurationMain.Instance.Playlists.IndexOf(playlist => playlist.Name == playlistName);
                                if (index == -1)
                                {
                                    clone.Name = playlistName;
                                    ConfigurationMain.Instance.Playlists.Add(clone);
                                    LoadPlaylist(clone);
                                }
                                else
                                {
                                    ConfigurationMain.Instance.Playlists[index] = clone;
                                }

                                ConfigurationProfileV2.Save();
                            }
                        }

                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                            ImGui.SetTooltip(Loc.Get("MainTab.Playlist.SaveHelp"));
                    }

                    ushort ilvl = InventoryHelper.CurrentItemLevel;
                    if (!ImGui.BeginListBox("##DutyList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) 
                        return;

                    if (VNavmesh_IPCSubscriber.IsEnabled && BossMod_IPCSubscriber.IsEnabled)
                    {
                        if (PlayerHelper.IsReady)
                            switch (Configuration.Meta.AutoDutyModeEnum)
                            {
                                case AutoDutyMode.Looping:
                                    if (Player.Job.GetCombatRole() == CombatRole.NonCombat)
                                    {
                                        ImGuiEx.TextWrapped(new Vector4(255, 1, 0, 1), Loc.Get("MainTab.SwitchCombatJob"));
                                    }
                                    else if (Player.Job is not Job.BST && Configuration.Meta.DutyModeEnum is DutyMode.Crucible)
                                    {
                                        ImGuiEx.TextWrapped(new Vector4(0, 1, 1, 1), Loc.Get("MainTab.CrucibleRestriction"));
                                    }
                                    else if (Player.Job is Job.BLU or Job.BST && Configuration.Meta.DutyModeEnum is not (DutyMode.Regular or DutyMode.Trial or DutyMode.Raid or DutyMode.Crucible))
                                    {
                                        ImGuiEx.TextWrapped(new Vector4(0, 1, 1, 1), Loc.Get("MainTab.LimitedJobRestriction"));
                                    } 
                                    else if (Plugin.LevelingModeEnum != LevelingMode.None)
                                    {
                                        if (Player.Job.GetCombatRole() == CombatRole.NonCombat ||
                                            (Plugin.LevelingModeEnum.IsTrustLeveling() &&
                                             (ilvl < 370 || Plugin.currentPlayerItemLevelAndClassJob.Value != null && Plugin.currentPlayerItemLevelAndClassJob.Value != Player.Job)))
                                        {
                                            Svc.Log.Debug($"You are on a non-compatible job: {Player.Job.GetCombatRole()}, or your doing trust and your iLvl({ilvl}) is below 370, or your iLvl has changed, Disabling Leveling Mode");
                                            Plugin.LevelingModeEnum = LevelingMode.None;
                                        }
                                        else if (ilvl > 0 && ilvl != Plugin.currentPlayerItemLevelAndClassJob.Key)
                                        {
                                            Svc.Log.Debug($"Your iLvl has changed, Selecting new Duty.");
                                            Plugin.CurrentTerritoryContent = LevelingHelper.SelectHighestLevelingRelevantDuty(Plugin.LevelingModeEnum);
                                        }
                                        else
                                        {
                                            ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), Loc.Get("MainTab.LevelingModeStatus", Player.Level.ToString(), ilvl.ToString()));
                                            foreach ((Content Value, int Index) item in LevelingHelper.LevelingDuties.Select((value, index) => (Value: value, Index: index)))
                                            {
                                                if (Configuration.Meta.DutyModeEnum == DutyMode.Trust && !item.Value.DutyModes.HasFlag(DutyMode.Trust))
                                                    continue;
                                                bool disabled = !item.Value.CanRun();
                                                if (!Configuration.Meta.HideUnavailableDuties || !disabled)
                                                    using (ImRaii.Disabled(disabled))
                                                    {
                                                        ImGuiEx.TextWrapped(item.Value == Plugin.CurrentTerritoryContent ? new Vector4(0, 1, 1, 1) : new Vector4(1, 1, 1, 1),
                                                                            $"L{item.Value.ClassJobLevelRequired} (i{item.Value.ItemLevelRequired}): {item.Value.EnglishName}");
                                                        using (ImRaii.Enabled())
                                                        {
                                                            if (LevelingHelper.levelingListExperimental.Contains(item.Value.TerritoryType))
                                                                ImGuiEx.HelpMarker("This dungeon is currently in testing for reliability.\nDo report any issues with it", symbolOverride: FontAwesomeIcon.ExclamationTriangle.ToIconString(), color: EzColor.Yellow);
                                                            if (item.Value.TerritoryType == 1048u)
                                                                ImGuiEx.HelpMarker("CutsceneSkip detected. Please keep it actually on.", symbolOverride: FontAwesomeIcon.ExclamationTriangle.ToIconString(), color: EzColor.Blue);
                                                        }
                                                    }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Dictionary<uint, Content> dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.DutyModes.HasFlag(Configuration.Meta.DutyModeEnum)).ToDictionary();

                                        if (dictionary.Count > 0 && PlayerHelper.IsReady)
                                        {
                                            short level = PlayerHelper.GetCurrentLevelFromSheet();
                                            foreach ((uint _, Content content) in dictionary)
                                            {
                                                // Apply search filter
                                                if (!string.IsNullOrWhiteSpace(searchText) && !(content.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
                                                    continue; // Skip duties that do not match the search text

                                                bool canRun = content.CanRun(level);
                                                using (ImRaii.Disabled(!canRun))
                                                {
                                                    if (Configuration.Meta.HideUnavailableDuties && !canRun)
                                                        continue;
                                                    if (ImGui.Selectable($"L{content.ClassJobLevelRequired} (i{content.ItemLevelRequired}) ({content.TerritoryType}) {content.Name}", DutySelected?.ID == content.TerritoryType))
                                                    {
                                                        DutySelected                   = ContentPathsManager.DictionaryPaths[content.TerritoryType];
                                                        Plugin.CurrentTerritoryContent = content;
                                                        DutySelected.SelectPath(out Plugin.currentPath);
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (PlayerHelper.IsReady)
                                                ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), Loc.Get("MainTab.SelectSupportTrust"));
                                        }
                                    }

                                    break;
                                case AutoDutyMode.Playlist:
                                    unsafe
                                    {
                                        RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
                                        for (int i = 0; i < Plugin.PlaylistCurrent.Entries.Count; i++)
                                        {
                                            PlaylistEntry entry = Plugin.PlaylistCurrent.Entries[i];

                                            ImGui.AlignTextToFramePadding();
                                            ImGui.SetItemAllowOverlap();
                                            if (ImGui.Selectable($"{i}:##Playlist{i+1}Entry", Plugin.playlistIndex == i, ImGuiSelectableFlags.AllowItemOverlap)) 
                                                Plugin.playlistIndex = i;
                                            ImGui.SameLine(0, 10);

                                            //ImGui.AlignTextToFramePadding();
                                            //ImGui.Text($"{i}:"); // {entry.dutyMode} {entry.id}");
                                            //ImGui.SameLine(0, 0);
                                            ContentPathsManager.ContentPathContainer entryContainer = ContentPathsManager.DictionaryPaths[entry.Id];
                                            Content                                  entryContent   = ContentHelper.DictionaryContent[entry.Id];

                                            ImGui.PushItemWidth(80f.Scale());
                                            if (ImGui.InputInt($"##Playlist{i}Count", ref entry.count, step: 1, stepFast: 2, @"%dx")) 
                                                entry.count = Math.Max(1, entry.count);

                                            ImGui.PopItemWidth();
                                            ImGui.SameLine();
                                            ImGui.PushItemWidth(115f.Scale());
                                            if (ImGui.BeginCombo($"##Playlist{i}GearsetSelection", entry.gearset != null ? gearsetModule->GetGearset(entry.gearset.Value)->NameString : Loc.Get("MainTab.CurrentGearset")))
                                            {
                                                if (ImGui.Selectable(Loc.Get("MainTab.CurrentGearset"), entry.gearset == null)) 
                                                    entry.gearset = null;

                                                for (int g = 0; g < gearsetModule->NumGearsets; g++)
                                                {
                                                    RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(g);
                                                    if (((Job)gearset->ClassJob).GetCombatRole() == CombatRole.NonCombat)
                                                        continue;

                                                    if (ImGui.Selectable(gearset->NameString, entry.gearset == gearset->Id)) 
                                                        entry.gearset = gearset->Id;
                                                }

                                                ImGui.EndCombo();
                                            }
                                            ImGui.PopItemWidth();
                                            ImGui.SameLine();

                                            ImGui.PushItemWidth(80f.Scale());
                                            if (ImGui.BeginCombo($"##Playlist{i}DutyModeEnum", Loc.Get($"MainTab.DutyModes.{entry.DutyMode}")))
                                            {
                                                foreach (DutyMode mode in Enum.GetValues(typeof(DutyMode)))
                                                {
                                                    if (mode == DutyMode.None)
                                                        continue;

                                                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiHelper.StateGoodColor, entryContent.DutyModes.HasFlag(mode)))
                                                    {
                                                        if (ImGui.Selectable(Loc.Get($"MainTab.DutyModes.{mode}"), entry.DutyMode == mode)) 
                                                            entry.DutyMode = mode;
                                                    }
                                                }

                                                ImGui.EndCombo();
                                            }
                                        
                                            ImGui.PopItemWidth();
                                            ImGui.SameLine();
                                            ImGui.PushItemWidth(150f);
                                            if (ImGui.BeginCombo($"##Playlist{i}DutySelection", $"({entry.Id}) {entryContent.Name}"))
                                            {
                                                Job?    entryJob  = null;
                                                ushort? entryIlvl = null;
                                                if(entry.gearset.HasValue)
                                                {
                                                    RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset((int)entry.gearset);
                                                    entryJob = (Job)gearset->ClassJob;
                                                    entryIlvl = (ushort) gearset->ItemLevel;
                                                }

                                                short level = PlayerHelper.GetCurrentLevelFromSheet(entryJob);
                                                entryIlvl ??= InventoryHelper.CurrentItemLevel;
                                                DrawSearchBar();

                                                foreach (uint key in ContentPathsManager.DictionaryPaths.Keys)
                                                {
                                                    Content content = ContentHelper.DictionaryContent[key];

                                                    if (!string.IsNullOrWhiteSpace(searchText) && !(content.Name?.Contains(searchText, StringComparison.InvariantCultureIgnoreCase) ?? false))
                                                        continue;

                                                    if (content.DutyModes.HasFlag(entry.DutyMode) && content.CanRun(level, entry.DutyMode, ilvl: entryIlvl))
                                                        if (ImGui.Selectable($"({key}) {content.Name}", entry.Id == key))
                                                            entry.Id = key;
                                                }

                                                ImGui.EndCombo();
                                            }

                                            if(entry.Id != entryContent.TerritoryType)
                                                continue;

                                            if (entryContainer.Paths.Count > 1)
                                            {
                                                ImGui.SameLine();
                                                if (ImGui.BeginCombo($"##Playlist{i}PathSelection", entryContainer.Paths.First(dp => dp.FileName == entry.Path).Name))
                                                {
                                                    foreach (ContentPathsManager.DutyPath path in entryContainer.Paths)
                                                        if(ImGui.Selectable(path.Name, path.FileName == entry.Path)) 
                                                            entry.Path = path.FileName;

                                                    ImGui.EndCombo();
                                                }
                                            }

                                            ImGui.PopItemWidth();
                                            ImGui.SameLine();

                                            if (entry.DutyMode is DutyMode.Regular or DutyMode.Trial or DutyMode.Raid)
                                                ImGuiEx.CheckboxWrapped($"Unsynced###Unsync{i}", ref entry.unsynced);
                                            ImGui.SameLine();
                                            using (ImRaii.Disabled(i <= 0))
                                            {
                                                if (ImGuiComponents.IconButton($"Playlist{i}Up", FontAwesomeIcon.ArrowUp))
                                                {
                                                    Plugin.PlaylistCurrent.Entries.Remove(entry);
                                                    Plugin.PlaylistCurrent.Entries.Insert(i - 1, entry);
                                                }
                                            }

                                            if (entry is { DutyMode: DutyMode.Variant, Id: 1315 })
                                            {
                                                ImGui.PushItemWidth(80f.Scale());
                                                ImGui.SameLine();
                                                ImGui.InputByte($"###Playlist{i}PathIndex", ref entry.variantPathIndex, 1);
                                                ImGui.PopItemWidth();
                                            }

                                            ImGui.SameLine();

                                            if (ImGuiEx.IconButtonWithText(entry.terminationConfigOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight, $"T##Playlist{i}Termination"))
                                                entry.terminationConfigOpen = !entry.terminationConfigOpen;

                                            ImGui.SameLine();

                                            using(ImRaii.Disabled(Plugin.PlaylistCurrent.Entries.Count <= i+1))
                                            {
                                                if (ImGuiComponents.IconButton($"Playlist{i}Down", FontAwesomeIcon.ArrowDown))
                                                {
                                                    Plugin.PlaylistCurrent.Entries.Remove(entry);
                                                    Plugin.PlaylistCurrent.Entries.Insert(i+1, entry);
                                                }
                                            }

                                            ImGui.SameLine();

                                            if (ImGuiComponents.IconButton($"Playlist{i}Trash", FontAwesomeIcon.TrashAlt))
                                                Plugin.PlaylistCurrent.Entries.RemoveAt(i);


                                            if (entry.terminationConfigOpen)
                                            {
                                                ImGui.PushID($"Playlist{i}TerminationEntry_");
                                                ImGui.Indent();
                                                entry.TerminationConfig.Draw();
                                                ImGui.Unindent();
                                                ImGui.PopID();
                                            }
                                        }

                                        if (ImGuiComponents.IconButton("PlaylistAdd", FontAwesomeIcon.Plus)) 
                                            Plugin.PlaylistCurrent.Entries.Add(new PlaylistEntry { DutyMode = Plugin.PlaylistCurrent.Entries.Count != 0 ? Plugin.PlaylistCurrent.Entries.Last().DutyMode : DutyMode.Support });
                                        break;
                                    }
                            }
                        else
                            ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), Loc.Get("MainTab.Busy"));
                    }
                    else
                    {
                        if (!VNavmesh_IPCSubscriber.IsEnabled)
                            ImGuiEx.TextWrapped(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresVNavmeshAlt"));
                        if (!BossMod_IPCSubscriber.IsEnabled)
                            ImGuiEx.TextWrapped(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresBossModAlt"));
                    }
                    ImGui.EndListBox();
                }
            }
        }

        private static void DrawCrucible(bool inDungeon)
        {
            ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = Configuration.Meta.Crucible;

            ImGui.Separator();

            if (!inDungeon)
            {
                ImGuiEx.LineCentered(() => ImGuiEx.TextUnderlined(Loc.Get("MainTab.Crucible.Team")));

                foreach (CrucibleTeamMode mode in Enum.GetValues<CrucibleTeamMode>())
                {
                    if (mode != CrucibleTeamMode.Recommended)
                        ImGui.SameLine();

                    if (ImGui.RadioButton(Loc.Get($"MainTab.Crucible.Modes.{mode}"), crucible.TeamMode == mode) && crucible.TeamMode != mode)
                    {
                        crucible.TeamMode = mode;
                        ConfigurationProfileV2.Save();
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(Loc.Get($"MainTab.Crucible.ModesHelp.{mode}"));
                }

                ImGui.SameLine();
                CrucibleDegree degree = crucible.Degree;
                ImGui.PushItemWidth(150f.Scale());
                if (ImGuiEx.EnumCombo("##CrucibleDegrees", ref degree, Enum.GetValues<CrucibleDegree>().ToDictionary(cd => cd, cd => Loc.Get("MainTab.Crucible.Degrees." + cd))))
                {
                    crucible.Degree = degree;
                    ConfigurationProfileV2.Save();
                }
                ImGui.PopItemWidth();


                switch (crucible.TeamMode)
                {

                    case CrucibleTeamMode.Recommended:
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text(Loc.Get("MainTab.Crucible.RecommendedTeamCap"));
                        ImGui.SameLine();
                        ImGui.PushItemWidth(100f.Scale());
                        int teamCap = crucible.RecommendedTeamCap;
                        if (ImGui.InputInt("##RecommendedTeamCap", ref teamCap, 1))
                        {
                            crucible.RecommendedTeamCap = teamCap;
                            ConfigurationProfileV2.Save();
                        }
                        ImGui.PopItemWidth();

                        if (crucible.RecommendedTeamCap != 15)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.RecommendedTeamCapClear")))
                            {
                                crucible.RecommendedTeamCap = 15;
                                ConfigurationProfileV2.Save();
                            }
                        }

                        break;
                    case CrucibleTeamMode.Leveling:
                    {
                        foreach (CrucibleLevelingMode mode in Enum.GetValues<CrucibleLevelingMode>())
                        {
                            if (mode != CrucibleLevelingMode.Full)
                                ImGui.SameLine();

                            if (ImGui.RadioButton(Loc.Get($"MainTab.Crucible.LevelingModes.{mode}"), crucible.LevelingMode == mode) && crucible.LevelingMode != mode)
                            {
                                crucible.LevelingMode = mode;
                                ConfigurationProfileV2.Save();
                            }

                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(Loc.Get($"MainTab.Crucible.LevelingModesHelp.{mode}"));
                        }

                        break;
                    }
                    case CrucibleTeamMode.Custom:
                    default:
                        break;
                }

                List<uint>                                  team   = CrucibleTeam.For(crucible.TeamMode);
                IReadOnlyDictionary<uint, CrucibleFamiliar> cached = CrucibleTeam.Familiars;
                List<uint>                                  owned  = CrucibleTeam.Owned().ToList();

                if (ImGui.CollapsingHeader($"{Loc.Get("MainTab.Crucible.Familiars", team.Count, CrucibleTeam.TeamSize())}###CrucibleFamiliars"))
                {
                    string classificationValueName = crucible.ClassificationLock > 0 ? CrucibleTeam.ClassificationSheetNames[crucible.ClassificationLock] : Loc.Get("MainTab.Crucible.NoClassificationLock");
                    ImGui.PushItemWidth(80f.Scale());
                    
                    if (ImGui.BeginCombo("##CrucibleFamiliarClassificationSelection", classificationValueName, ImGuiComboFlags.HeightLargest))
                    {
                        ImGui.SetItemAllowOverlap();
                        if (ImGui.Selectable("##CrucibleClassificationNoLock", crucible.ClassificationLock == 0))
                        {
                            crucible.ClassificationLock = 0;
                            ConfigurationProfileV2.Save();
                        }

                        ImGui.SameLine();

                        string classificationNoLock = Loc.Get("MainTab.Crucible.NoClassificationLock");
                        ImGui.TextColored(classificationNoLock.GetRandomColor(), classificationNoLock);

                        for (int i = 1; i < CrucibleTeam.ClassificationSheetNames.Length; i++)
                        {
                            ImGui.SetItemAllowOverlap();
                            if (ImGui.Selectable("##CrucibleClassification" + i, i == crucible.ClassificationLock) && i != crucible.ClassificationLock)
                            {
                                crucible.ClassificationLock = i;
                                ConfigurationProfileV2.Save();
                            }
                            ImGui.SameLine();

                            string classificationName = CrucibleTeam.ClassificationSheetNames[i];
                            ImGui.TextColored(classificationName.GetRandomColor(), classificationName);
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.SameLine();
                    ImGui.PopItemWidth();
                    ImGui.SameLine();

                    using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl || cached.Count == 0))
                        if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ClearRanks")))
                            CrucibleTeam.ClearSaved();

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ClearRanksHelp"));

                    if (crucible is { TeamMode: CrucibleTeamMode.Custom, CustomTeam.Count: > 0 })
                    {
                        ImGui.SameLine();
                        using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
                            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ClearCustoms")))
                            {
                                crucible.CustomTeam.Clear();
                                ConfigurationProfileV2.Save();
                            }
                    }

                    if (owned.Count == 0)
                        ImGuiEx.TextWrapped(Loc.Get("MainTab.Crucible.NoFamiliars"));
                    else
                        DrawCrucibleFamiliarTable(crucible, team, cached, owned);
                }
                if (ImGui.CollapsingHeader(Loc.Get("MainTab.Crucible.TierList") + "###TierList"))
                    DrawCrucibleFamiliarTierTable(crucible);
            }

            if (ImGui.CollapsingHeader($"{Loc.Get("MainTab.Crucible.Menus")}###CrucibleMenus"))
                DrawCrucibleMenuToggles();

            if(crucible.Shop)
                if (ImGui.CollapsingHeader($"{Loc.Get("MainTab.Crucible.ShopSettings")}###CrucibleShopSettings"))
                    DrawCrucibleShopSettings(crucible);
        }

        private static readonly Vector4 CruciblePickedRow         = new(0.25f, 0.55f, 0.95f, 0.18f);
        private static readonly Vector4 CrucibleBlockedRow        = new(0.95f, 0.25f, 0.25f, 0.18f);
        private static readonly Vector4 CrucibleBlockHighlightRow = new(0.855f, 0.439f, 0.839f, 0.18f);
        private static readonly Vector4 CrucibleFaded             = new(1f, 1f, 1f, 0.45f);

        private static void DrawCrucibleFamiliarTable(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible, List<uint> team, IReadOnlyDictionary<uint, CrucibleFamiliar> cached, List<uint> owned)
        {
            bool custom = crucible.TeamMode == CrucibleTeamMode.Custom;

            IEnumerable<uint> rows = crucible.TeamMode switch
            {
                CrucibleTeamMode.Leveling    => CrucibleTeam.Leveling(false),
                CrucibleTeamMode.Recommended => CrucibleTeam.Recommended(false),
                _ => owned.Where(x => crucible.ClassificationLock == 0 || CrucibleTeam.FamiliarSheetDatas[x].Classification == crucible.ClassificationLock)
            };

            float scale     = ImGuiHelpers.GlobalScale;
            float rowHeight = ImGui.GetFrameHeightWithSpacing();
            float height    = Math.Min(owned.Count + 1, 12) * rowHeight + 4 * scale;

            const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable | ImGuiTableFlags.PadOuterX;
            if (!ImGui.BeginTable("##CrucibleFamiliarTable", 7, flags, new Vector2(0, height)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("##pick",                                   ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableSetupColumn("No.",                                      ImGuiTableColumnFlags.WidthFixed, 28 * scale);
            ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.Familiar"),       ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.Classification"), ImGuiTableColumnFlags.WidthFixed, 100 * scale);
            ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.Rank"),           ImGuiTableColumnFlags.WidthFixed, 36 * scale);
            ImGui.TableSetupColumn("EXP",                                      ImGuiTableColumnFlags.WidthFixed, 90 * scale);
            ImGui.TableSetupColumn("HP",                                       ImGuiTableColumnFlags.WidthFixed, 44 * scale);
            ImGui.TableHeadersRow();

            foreach (uint number in rows)
            {
                cached.TryGetValue(number, out CrucibleFamiliar? familiar);
                bool picked = team.Contains(number);

                ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());
                if (picked && !custom)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(CruciblePickedRow));

                ImGui.TableNextColumn();
                bool full = !picked && team.Count >= CrucibleTeam.TeamSize();
                using (ImRaii.Disabled(!custom || full))
                    if (ImGui.Checkbox($"##CruciblePick{number}", ref picked))
                        CrucibleTeam.SetCustomPick(number, picked);
                if (custom && full && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(Loc.Get("MainTab.Crucible.CustomFull", CrucibleTeam.TeamSize()));

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                RightAligned(number.ToString(), CrucibleFaded);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(CrucibleTeam.NameOf(number));
                if (ImGui.IsItemHovered())
                    DrawCrucibleFamiliarTooltip(number, familiar);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (!(familiar?.SheetData.ClassificationName.IsNullOrEmpty() ?? true))
                    Centered(familiar.SheetData.ClassificationName, familiar.SheetData.ClassificationName.GetRandomColor());
                else
                    Centered("—", CrucibleFaded);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (familiar is { Rank: > 0 })
                    Centered(familiar.Rank.ToString(), null);
                else
                    Centered("—", CrucibleFaded);

                ImGui.TableNextColumn();
                float share = CrucibleUi.ExpShare(familiar?.Exp ?? "");
                if (familiar is { Exp.Length: > 0 })
                {
                    ImGui.ProgressBar(share, new Vector2(-1, ImGui.GetFrameHeight()), familiar.Exp);
                }
                else
                {
                    ImGui.AlignTextToFramePadding();
                    Centered("—", CrucibleFaded);
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (familiar is { Hp: > 0 })
                    Centered(familiar.Hp.ToString(), null);
                else
                    Centered("—", CrucibleFaded);
            }

            ImGui.EndTable();

            ImGui.TextColored(CrucibleFaded, Loc.Get(custom ? "MainTab.Crucible.TableLegendCustom" : "MainTab.Crucible.TableLegend"));

            static void RightAligned(string text, Vector4? color)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X));
                Text(text, color);
            }

            static void Centered(string text, Vector4? color)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) / 2));
                Text(text, color);
            }

            static void Text(string text, Vector4? color)
            {
                if (color is { } c)
                    ImGui.TextColored(c, text);
                else
                    ImGui.TextUnformatted(text);
            }
        }

        private static void DrawCrucibleFamiliarTierTable(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible)
        {
            ImGui.Separator();
            BeastTierList active = BeastTierList.ActiveTierList;
            if (!active.IsCompleted)
            {
                active.Complete();
                ConfigurationProfileV2.Save();
            }

            ImGui.SetNextItemWidth(160f.Scale());
            if (ImGui.BeginCombo("##CrucibleBeastTierList", active.Name))
            {
                for (int i = 0; i < crucible.BeastTierLists.Count; i++)
                {
                    ImGui.PushID(i);
                    if (ImGui.Selectable(crucible.BeastTierLists[i].Name, i == crucible.BeastTierListIndex) && i != crucible.BeastTierListIndex)
                    {
                        crucible.BeastTierListIndex = i;
                        ConfigurationProfileV2.Save();
                    }
                    ImGui.PopID();
                }
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.BeastTierListHelp"));

            ImGui.SameLine();
            ImGui.SetNextItemWidth(140f.Scale());
            string name = active.Name;
            if (ImGui.InputText("##CrucibleBeastTierListName", ref name, 64))
                active.Name = name;
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                active.Name = UniqueListName(crucible.BeastTierLists, active.Name.Trim().Length > 0 ? active.Name.Trim() : "Tier List", active);
                ConfigurationProfileV2.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.RenameBeastTierListHelp"));

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.NewBeastTierList")))
                AddTierList(crucible, "Tier List", BeastTierList.beastTierListDefault);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.NewBeastTierListHelp"));

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.CopyBeastTierList")))
                AddTierList(crucible, active.Name, active);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.CopyBeastTierListHelp"));

            ImGui.SameLine();
            using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl || crucible.BeastTierLists.Count <= 1))
                if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.DeleteBeastTierList")))
                {
                    crucible.BeastTierLists.RemoveAt(crucible.BeastTierListIndex);
                    crucible.BeastTierListIndex = Math.Max(0, crucible.BeastTierListIndex - 1);
                    ConfigurationProfileV2.Save();
                    return;
                }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.DeleteBeastTierListHelp"));

            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ExportBeastTierList")))
            {
                ImGui.SetClipboardText(JsonConvert.SerializeObject(active));
                Notify.Success(Loc.Get("MainTab.Crucible.ExportBeastTierListDone"));
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ExportBeastTierListHelp"));

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ImportBeastTierList")))
                ImportBeastTierList(crucible);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ImportBeastTierListHelp"));

            if (active.OrderChanged)
            {
                ImGui.SameLine();
                using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
                    if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ClearBeastTierOrder")))
                    {
                        active.Clear();
                        active.Complete();
                        ConfigurationProfileV2.Save();
                    }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ClearBeastTierOrderHelp"));
            }

            ImGui.TextColored(CrucibleFaded, Loc.Get("MainTab.Crucible.BeastTierOrderHelp"));

            CrucibleBeastTierTable(active);
        }

        private static ImGuiEx.RealtimeDragDrop<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry>? _crucibleShopDragDrop;
        private static ImGuiEx.RealtimeDragDrop<BeastTierList.Entry>?                                      _crucibleBeastTierDragDrop;
        private static (ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry Row, int Position)?       _crucibleShopTyped;
        private static (BeastTierList.Entry Row, int Position)?                                            _crucibleBeastTierTyped;

        private static void DrawCrucibleShopSettings(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible)
        {
            Toggle("RespectGearRequirements", crucible.RespectGearRequirements, v => crucible.RespectGearRequirements = v);
            ImGui.Separator();
            ConfigurationProfileV2.MetaConfig.CrucibleShopList active = CrucibleItemData.ActiveShopList;
            if (!active.Order.SequenceEqual(CrucibleItemData.ShopOrder))
                active.Order = CrucibleItemData.ShopOrder.ToList();

            ImGui.SetNextItemWidth(160f.Scale());
            if (ImGui.BeginCombo("##CrucibleShopList", active.Name))
            {
                for (int i = 0; i < crucible.ShopLists.Count; i++)
                {
                    ImGui.PushID(i);
                    if (ImGui.Selectable(crucible.ShopLists[i].Name, i == crucible.ShopListIndex) && i != crucible.ShopListIndex)
                    {
                        crucible.ShopListIndex = i;
                        ConfigurationProfileV2.Save();
                    }
                    ImGui.PopID();
                }
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ShopListHelp"));

            ImGui.SameLine();
            ImGui.SetNextItemWidth(140f.Scale());
            string name = active.Name;
            if (ImGui.InputText("##CrucibleShopListName", ref name, 64))
                active.Name = name;
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                active.Name = UniqueListName(crucible.ShopLists, active.Name.Trim().Length > 0 ? active.Name.Trim() : "Shop List", active);
                ConfigurationProfileV2.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.RenameShopListHelp"));

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.NewShopList")))
                AddShopList(crucible, "Shop List", CrucibleItemData.ShopItems);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.NewShopListHelp"));

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.CopyShopList")))
                AddShopList(crucible, active.Name, active.Order);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.CopyShopListHelp"));

            ImGui.SameLine();
            using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl || crucible.ShopLists.Count <= 1))
                if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.DeleteShopList")))
                {
                    crucible.ShopLists.RemoveAt(crucible.ShopListIndex);
                    crucible.ShopListIndex = Math.Max(0, crucible.ShopListIndex - 1);
                    ConfigurationProfileV2.Save();
                    return;
                }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.DeleteShopListHelp"));

            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ExportShopOrder")))
            {
                ImGui.SetClipboardText(JsonConvert.SerializeObject(active));
                Notify.Success(Loc.Get("MainTab.Crucible.ExportShopOrderDone"));
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ExportShopOrderHelp"));

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ImportShopOrder")))
                ImportShopOrder(crucible);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ImportShopOrderHelp"));

            if (active.OrderChanged)
            {
                ImGui.SameLine();
                using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
                    if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ClearShopOrder")))
                    {
                        active.Order = [];
                        active.CompleteList();
                        ConfigurationProfileV2.Save();
                    }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ClearShopOrderHelp"));
            }

            ImGui.TextColored(CrucibleFaded, Loc.Get("MainTab.Crucible.ShopOrderHelp"));

            CrucibleShopOrderTable(active.Order);
            return;

            static void Toggle(string key, bool value, Action<bool> set)
            {
                if (ImGui.Checkbox(Loc.Get($"MainTab.Crucible.Toggles.{key}"), ref value))
                {
                    set(value);
                    ConfigurationProfileV2.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Loc.Get($"MainTab.Crucible.TogglesHelp.{key}"));
            }
        }

        private static void AddShopList(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible, string name, IEnumerable<uint> order) =>
            AddShopList(crucible, name, order.Select(id => (ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry)id));

        private static void AddShopList(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible, string name, IEnumerable<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry> order)
        {
            crucible.ShopLists.Add(new ConfigurationProfileV2.MetaConfig.CrucibleShopList
                                   {
                                       Name  = UniqueListName(crucible.ShopLists, name, null),
                                       Order = order.ToList()
                                   }.CompleteList());
            crucible.ShopListIndex = crucible.ShopLists.Count - 1;
            ConfigurationProfileV2.Save();
        }

        private static void AddTierList(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible, string name, IEnumerable<uint> order) =>
            AddTierList(crucible, name, order.Select(id => (BeastTierList.Entry) id));

        private static void AddTierList(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible, string name, IEnumerable<BeastTierList.Entry> order)
        {
            BeastTierList beastTierList = new() { Name  = UniqueListName(crucible.BeastTierLists, name, null), };
            beastTierList.AddRange(order);
            beastTierList.Complete();

            crucible.BeastTierLists.Add(beastTierList);
            crucible.BeastTierListIndex = crucible.BeastTierLists.Count - 1;
            ConfigurationProfileV2.Save();
        }

        private static void CrucibleBeastTierTable(BeastTierList order)
        {
            ImGuiEx.RealtimeDragDrop<BeastTierList.Entry> dragDrop = _crucibleBeastTierDragDrop ??= new ImGuiEx.RealtimeDragDrop<BeastTierList.Entry>("CrucibleBeastTierDragDrop", row => row.ID.ToString(), smallButton: false);

            using ImRaii.ChildDisposable child = ImRaii.Child("##Sidebar", new Vector2(0, ImGui.GetContentRegionAvail().Y - 100f.Scale()), true);
            if (!child.Success)
                return;

            BeastTierList.Entry[] before = order.ToArray();

            int moveFromIndex = -1;
            int moveToIndex = -1;

            dragDrop.Begin();

            using (ImRaii.TableDisposable orderTable = ImRaii.Table("Crucible Beast Tier Table", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            {
                if (orderTable)
                {
                    ImGui.TableSetupScrollFreeze(8, 1);
                    ImGui.TableSetupColumn("##Reorder");
                    ImGui.TableSetupColumn("#");
                    ImGui.TableSetupColumn("No.", ImGuiTableColumnFlags.WidthFixed, 28f.Scale());
                    string blockString = Loc.Get("MainTab.Crucible.BeastTierPickBlocked");
                    ImGui.TableSetupColumn(blockString, ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(blockString).X + 20f.Scale());
                    blockString = Loc.Get("MainTab.Crucible.BeastTierFightBlocked");
                    ImGui.TableSetupColumn(blockString, ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(blockString).X + 20f.Scale());
                    ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.Familiar"));
                    ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.Classification"));
                    ImGui.TableSetupColumn("##Spacer", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

                    int columnCount = ImGui.TableGetColumnCount();
                    for (int column = 0; column < columnCount; column++)
                    {
                        if (!ImGui.TableSetColumnIndex(column))
                            continue;

                        string name = ImGui.TableGetColumnName(column);

                        ImGui.PushID(column);

                        if (column is 3 or 4)
                        {
                            ImGui.TextUnformatted(name);
                            ImGui.SameLine(0, 0);
                            ImGuiComponents.HelpMarker(Loc.Get(column switch
                            {
                                3 => "MainTab.Crucible.BeastTierPickBlockedHelp", 
                                4 => "MainTab.Crucible.BeastTierFightBlockedHelp"
                            }));
                        }
                        else
                        {
                            ImGui.TableHeader(name);
                        }

                        ImGui.PopID();
                    }

                    (bool pick, byte classification, bool value)? blockValue = null;

                    for (int i = 0; i < order.Count; i++)
                    {
                        ImGui.PushID(i);

                        BeastTierList.Entry       row      = order[i];
                        CrucibleFamiliarSheetData rowSheet = CrucibleTeam.FamiliarSheetDatas[row.ID];


                        ImGui.TableNextRow();
                        dragDrop.NextRow();

                        if (row.PickBlock)
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(CrucibleBlockedRow));
                        else if(row.BattleBlock)
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(CrucibleBlockHighlightRow));

                        dragDrop.SetRowColor(row.ID);

                        ImGui.TableSetColumnIndex(0);
                        dragDrop.DrawButtonDummy(row, order, i);

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(40 * ImGuiHelpers.GlobalScale);
                        int position = _crucibleBeastTierTyped?.Row == row ? _crucibleBeastTierTyped.Value.Position : i + 1;
                        if (ImGui.InputInt("##Position", ref position, 0, 0))
                            _crucibleBeastTierTyped = (row, position);

                        if (ImGui.IsItemDeactivated())
                        {
                            if (_crucibleBeastTierTyped?.Row == row)
                            {
                                int typedPosition = Math.Clamp(_crucibleBeastTierTyped.Value.Position, 1, order.Count);

                                moveFromIndex = i;
                                moveToIndex = typedPosition - 1;
                                _crucibleBeastTierTyped = null;
                            }
                        }


                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        Centered(row.ID.ToString(), CrucibleFaded);

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();

                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - 25f.Scale()) / 2));
                        bool blocked = row.PickBlock;
                        if (ImGui.Checkbox("###CrucibleTierPickBlock_" + row.ID, ref blocked))
                        {
                            if (ImGui.GetIO().KeyCtrl)
                            {
                                blockValue = (true, rowSheet.Classification, row.PickBlock);
                            }
                            else
                            {
                                row.PickBlock = blocked;
                                ConfigurationProfileV2.Save();
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();

                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - 25f.Scale()) / 2));

                        blocked = row.BattleBlock;
                        if (ImGui.Checkbox("###CrucibleTierBattleBlock_" + row.ID, ref blocked))
                        {
                            if (ImGui.GetIO().KeyCtrl)
                            {
                                blockValue = (false, rowSheet.Classification, row.BattleBlock);
                            }
                            else
                            {
                                row.BattleBlock = blocked;
                                ConfigurationProfileV2.Save();
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(rowSheet.Name);

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();

                        ImGui.TextColored(rowSheet.ClassificationName.GetRandomColor(), rowSheet.ClassificationName);
                        ImGui.PopID();
                    }

                    if (blockValue != null)
                        foreach (BeastTierList.Entry entry in order)
                            if (CrucibleTeam.FamiliarSheetDatas[entry.ID].Classification == blockValue.Value.classification && (blockValue.Value.pick ? entry.PickBlock : entry.BattleBlock) == blockValue.Value.value)
                                if(blockValue.Value.pick)
                                    entry.PickBlock = !blockValue.Value.value;
                                else
                                    entry.BattleBlock = !blockValue.Value.value;
                }
            }

            dragDrop.End();

            bool typedNewPosition = moveFromIndex >= 0 && moveFromIndex != moveToIndex;
            if (typedNewPosition)
            {
                BeastTierList.Entry movedItem = order[moveFromIndex];
                order.RemoveAt(moveFromIndex);
                order.Insert(moveToIndex, movedItem);
            }

            if (!order.SequenceEqual(before))
                ConfigurationProfileV2.Save();
            return;

            static void Centered(string text, Vector4? color)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) / 2));
                Text(text, color);
            }

            static void Text(string text, Vector4? color)
            {
                if (color is { } c)
                    ImGui.TextColored(c, text);
                else
                    ImGui.TextUnformatted(text);
            }
        }


        private static string UniqueListName<T>(List<T> entries, string name, object? self) where T : class, ConfigurationProfileV2.MetaConfig.ICrucibleList
        {
            bool Taken(string candidate) => 
                entries.Any(l => l != self && l.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));

            if (!Taken(name))
                return name;

            int n = 2;
            while (Taken($"{name} ({n})"))
                n++;
            return $"{name} ({n})";
        }

        private static void ImportShopOrder(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible)
        {
            try
            {
                string text = (ImGui.GetClipboardText() ?? string.Empty).Trim();
                string name = "Imported";

                List<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry> order;

                if (text.StartsWith('{') && JsonConvert.DeserializeObject<ConfigurationProfileV2.MetaConfig.CrucibleShopList>(text) is { } list)
                {
                    if (list.Name.Trim().Length > 0)
                        name = list.Name.Trim();
                    order = list.Order;
                }
                else
                {
                    order = RegexHelper.NumberParserRegex().Matches(text).Select(m => uint.TryParse(m.Value, out uint row) ? row : 0).Where(x => x != 0).Select(id => (ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry) id).ToList();
                }

                List<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry> valid = order.Where(x => CrucibleItemData.ShopItems.Contains(x.ID)).Distinct().ToList();
                if (valid.Count == 0)
                {
                    Notify.Error(Loc.Get("MainTab.Crucible.ImportShopOrderFailed"));
                    return;
                }

                AddShopList(crucible, name, valid);
                Notify.Success(Loc.Get("MainTab.Crucible.ImportShopOrderDone", crucible.ShopLists[crucible.ShopListIndex].Name));
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "[Crucible] Couldn't import the shop order from the clipboard");
                Notify.Error(Loc.Get("MainTab.Crucible.ImportShopOrderFailed"));
            }
        }

        private static void ImportBeastTierList(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible)
        {
            try
            {
                string text = (ImGui.GetClipboardText() ?? string.Empty).Trim();
                string name = "Imported";

                BeastTierList order;

                if (text.StartsWith('{') && JsonConvert.DeserializeObject<BeastTierList>(text) is { } list)
                {
                    if (list.Name.Trim().Length > 0)
                        name = UniqueListName(crucible.BeastTierLists, list.Name.Trim(), null);
                    order = list;
                }
                else
                {
                    order = new BeastTierList { Name = UniqueListName(crucible.BeastTierLists, name, null) };
                    order.AddRange(RegexHelper.NumberParserRegex().Matches(text).Select(m => uint.TryParse(m.Value, out uint row) ? row : 0).Where(x => x != 0).Select(id => (BeastTierList.Entry)id));
                }

                List<BeastTierList.Entry> valid = order.Where(x => BeastTierList.beastTierListDefault.Contains(x.ID)).Distinct().ToList();
                if (valid.Count == 0)
                {
                    Notify.Error(Loc.Get("MainTab.Crucible.ImportBeastTierListFailed"));
                    return;
                }

                AddTierList(crucible, name, valid);
                Notify.Success(Loc.Get("MainTab.Crucible.ImportBeastTierListDone", crucible.BeastTierLists[crucible.BeastTierListIndex].Name));
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "[Crucible] Couldn't import the beast tier list from the clipboard");
                Notify.Error(Loc.Get("MainTab.Crucible.ImportBeastTierListFailed"));
            }
        }

        private static readonly Vector4 CrucibleShopGear        = ImGuiHelper.RoleTankColor;
        private static readonly Vector4 CrucibleShopItemHealing = ImGuiHelper.RoleHealerColor;
        private static readonly Vector4 CrucibleShopItemDamage  = ImGuiHelper.RoleDPSColor;
        private static readonly Vector4 CrucibleShopFeed        = ImGuiHelper.RoleAllRounderColor;


        private static void CrucibleShopOrderTable(List<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry> order)
        {
            ImGuiEx.RealtimeDragDrop<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry> dragDrop = _crucibleShopDragDrop ??= new ImGuiEx.RealtimeDragDrop<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry>(
                "CrucibleShopDragDrop",
                row => row.ID.ToString(),
                smallButton: false
            );

            using ImRaii.ChildDisposable child = ImRaii.Child("##CrucibleShopSidebar", new Vector2(0, ImGui.GetContentRegionAvail().Y-100f.Scale()), true);
            if (!child.Success)
                return;

            ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry[] before = order.ToArray();

            int moveFromIndex = -1;
            int moveToIndex   = -1;

            dragDrop.Begin();

            using (ImRaii.TableDisposable orderTable = ImRaii.Table("Crucible Shop Order Table", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            {
                if (orderTable)
                {
                    ImGui.TableSetupScrollFreeze(6, 1);
                    ImGui.TableSetupColumn("##Reorder");
                    ImGui.TableSetupColumn("#");
                    string blockString = Loc.Get("MainTab.Crucible.ShopItemBlocked");
                    ImGui.TableSetupColumn(blockString, ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(blockString).X + 20f.Scale());
                    ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.ShopItem"));
                    ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.ShopItemType"));
                    ImGui.TableSetupColumn("##Spacer", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

                    int columnCount = ImGui.TableGetColumnCount();
                    for (int column = 0; column < columnCount; column++)
                    {
                        if (!ImGui.TableSetColumnIndex(column))
                            continue;

                        string name = ImGui.TableGetColumnName(column);

                        ImGui.PushID(column);

                        if (column == 2)
                        {
                            ImGui.TextUnformatted(name);
                            ImGui.SameLine(0, 0);
                            ImGuiComponents.HelpMarker(Loc.Get("MainTab.Crucible.ShopItemBlockedHelp"));
                        }
                        else
                        {
                            ImGui.TableHeader(name);
                        }

                        ImGui.PopID();
                    }

                    (CrucibleItemData.CrucibleItemCategory category, bool value)? blockValue = null;

                    for (int i = 0; i < order.Count; i++)
                    {
                        ImGui.PushID(i);

                        ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry row = order[i];

                        ImGui.TableNextRow();
                        dragDrop.NextRow();

                        if(row.Blocked)
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(CrucibleBlockedRow));

                        dragDrop.SetRowColor(row.ID);

                        ImGui.TableSetColumnIndex(0);
                        dragDrop.DrawButtonDummy(row, order, i);

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(40 * ImGuiHelpers.GlobalScale);
                        int position = _crucibleShopTyped?.Row == row ? _crucibleShopTyped.Value.Position : i + 1;
                        if (ImGui.InputInt("##Position", ref position, 0, 0))
                            _crucibleShopTyped = (row, position);

                        if (ImGui.IsItemDeactivated())
                        {
                            if (_crucibleShopTyped?.Row == row)
                            {
                                int typedPosition = Math.Clamp(_crucibleShopTyped.Value.Position, 1, order.Count);

                                moveFromIndex = i;
                                moveToIndex   = typedPosition - 1;
                                _crucibleShopTyped = null;
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();

                        CrucibleItemData.CrucibleItemCategory itemCategory = row.GetCategory;

                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - 25f.Scale()) / 2));
                        bool blocked = row.Blocked;
                        if (ImGui.Checkbox("###CrucibleBlock_" + row.ID, ref blocked))
                        {
                            if (ImGui.GetIO().KeyCtrl)
                            {
                                blockValue = (row.GetCategory, row.Blocked);
                            }
                            else
                            {
                                row.Blocked = blocked;
                                ConfigurationProfileV2.Save();
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(CrucibleItemData.NameOf(row.ID));
                        if (ImGui.IsItemHovered())
                            DrawCrucibleItemTooltip(row.ID);

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();

                        CrucibleItemData.CrucibleItemCategory category = itemCategory;

                        switch (category)
                        {
                            case CrucibleItemData.CrucibleItemCategory.Gear:
                                ImGui.TextColored(CrucibleShopGear, Loc.Get("MainTab.Crucible.GearItem"));
                                break;
                            case CrucibleItemData.CrucibleItemCategory.HealItem:
                                ImGui.TextColored(CrucibleShopItemHealing, Loc.Get("MainTab.Crucible.HealingItem"));
                                break;
                            case CrucibleItemData.CrucibleItemCategory.DamageItem:
                                ImGui.TextColored(CrucibleShopItemDamage, Loc.Get("MainTab.Crucible.DamageItem"));
                                break;
                            case CrucibleItemData.CrucibleItemCategory.Feed:
                                ImGui.TextColored(CrucibleShopFeed, Loc.Get("MainTab.Crucible.FeedItem"));
                                break;
                            case CrucibleItemData.CrucibleItemCategory.None:
                            case CrucibleItemData.CrucibleItemCategory.OtherItem:
                            case CrucibleItemData.CrucibleItemCategory.Item:
                            default:
                                ImGui.TextColored(CrucibleFaded, Loc.Get("MainTab.Crucible.ItemItem"));
                                break;
                        }
                        ImGui.PopID();
                    }

                    if (blockValue != null)
                        foreach (ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry entry in order)
                            if (entry.GetCategory == blockValue.Value.category && entry.Blocked == blockValue.Value.value)
                                entry.Blocked = !blockValue.Value.value;
                }
            }

            dragDrop.End();

            bool typedNewPosition = moveFromIndex >= 0 && moveFromIndex != moveToIndex;
            if (typedNewPosition)
            {
                ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry movedItem = order[moveFromIndex];
                order.RemoveAt(moveFromIndex);
                order.Insert(moveToIndex, movedItem);
            }

            if (!order.SequenceEqual(before))
                ConfigurationProfileV2.Save();
        }

        private static void DrawCrucibleItemTooltip(uint row)
        {
            if (CrucibleItemData.InfoOf(row) is not { } info)
                return;

            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);

            float iconSize = ImGui.GetFrameHeight() * 1.5f;
            if (info.Icon > 0 && ThreadLoadImageHandler.TryGetIconTextureWrap(info.Icon, false, out IDalamudTextureWrap icon))
            {
                ImGui.Image(icon.Handle, new Vector2(iconSize));
                ImGui.SameLine();
            }

            ImGui.BeginGroup();
            ImGui.TextUnformatted(info.Name);
            if (info.Type.Length > 0)
                ImGui.TextColored(CrucibleFaded, $"{info.Type} ({row})");
            ImGui.EndGroup();

            if (info.Summary.Length > 0)
                ImGui.TextUnformatted(info.Summary);

            if (info.Description.Length > 0)
            {
                ImGui.Separator();
                ImGui.TextUnformatted(info.Description);
            }

            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        private static void DrawCrucibleFamiliarTooltip(uint number, CrucibleFamiliar? familiar)
        {
            ImGui.BeginTooltip();

            ImGui.TextUnformatted($"No. {number}  {CrucibleTeam.NameOf(number)}");

            if (familiar is not { Rank: > 0 })
            {
                ImGui.TextColored(CrucibleFaded, Loc.Get("MainTab.Crucible.NotSeen"));
            }
            else
            {
                if (familiar.SheetData.ClassificationName.Length > 0)
                    ImGui.TextColored(CrucibleFaded, familiar.SheetData.ClassificationName);

                ImGui.Separator();
                if (ImGui.BeginTable("##CrucibleStats", 2, ImGuiTableFlags.SizingFixedFit))
                {
                    Stat("Rank",             familiar.Rank.ToString());
                    Stat("EXP",              familiar.Exp.Length > 0 ? familiar.Exp : "—");
                    Stat("HP",               familiar.Hp.ToString());
                    Stat("Strength",         familiar.Strength.ToString());
                    Stat("Intelligence",     familiar.Intelligence.ToString());
                    Stat("Constitution",     familiar.Constitution.ToString());
                    Stat("Phys. Resistance", familiar.PhysicalResistance.ToString());
                    Stat("Mag. Resistance",  familiar.MagicResistance.ToString());
                    ImGui.EndTable();
                }
            }

            ImGui.EndTooltip();
            return;

            static void Stat(string label, string value)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(CrucibleFaded, label);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(value);
            }
        }

        private static void DrawCrucibleMenuToggles()
        {
            ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = Configuration.Meta.Crucible;

            Toggle("FightPicks", crucible.FightPicks, v => crucible.FightPicks = v);
            ImGui.SameLine();
            Toggle("Loot", crucible.Loot, v => crucible.Loot = v);
            ImGui.SameLine();
            Toggle("Treasure", crucible.Treasure, v => crucible.Treasure = v);
            Toggle("Shop", crucible.Shop, v => crucible.Shop = v);
            ImGui.SameLine();
            Toggle("Rest", crucible.Rest, v => crucible.Rest = v);
            ImGui.SameLine();
            Toggle("Items", crucible.Items, v => crucible.Items = v);
            return;

            static void Toggle(string key, bool value, Action<bool> set)
            {
                if (ImGui.Checkbox($"{Loc.Get($"MainTab.Crucible.Toggles.{key}")}###CrucibleToggle{key}", ref value))
                {
                    set(value);
                    ConfigurationProfileV2.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Loc.Get($"MainTab.Crucible.TogglesHelp.{key}"));
            }
        }

        private static void DrawTrustMembers(Content content)
        {
            foreach (TrustMember member in content.TrustMembers)
            {
                bool       enabled        = Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName);
                CombatRole playerRole     = Player.Job.GetCombatRole();
                int        numberSelected = Configuration.SelectedTrustMembers.Count(x => x != null);

                TrustMember?[] members = [..Configuration.SelectedTrustMembers.Select(tmn => tmn != null ? TrustHelper.Members[(TrustMemberName)tmn] : null)];

                bool canSelect = members.CanSelectMember(member, playerRole) && member.Level >= content.ClassJobLevelRequired;

                using (ImRaii.Disabled(!enabled && (numberSelected == 3 || !canSelect)))
                {
                    if (ImGui.Checkbox($"###{member.Index}{content.Id}", ref enabled))
                    {
                        if (enabled)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                if (Configuration.SelectedTrustMembers[i] is null)
                                {
                                    Configuration.SelectedTrustMembers[i] = member.MemberName;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName))
                            {
                                int idx = Configuration.SelectedTrustMembers.IndexOf(x => x != null && x == member.MemberName);
                                Configuration.SelectedTrustMembers[idx] = null;
                            }
                        }

                        ConfigurationProfileV2.Save();
                    }
                }

                ImGui.SameLine(0, 2);
                ImGui.SetItemAllowOverlap();
                ImGui.TextColored(member.Role switch
                {
                    TrustRole.DPS => ImGuiHelper.RoleDPSColor,
                    TrustRole.Healer => ImGuiHelper.RoleHealerColor,
                    TrustRole.Tank => ImGuiHelper.RoleTankColor,
                    TrustRole.AllRounder => ImGuiHelper.RoleAllRounderColor,
                    _ => Vector4.One
                }, member.Name);
                if (member.Level > 0)
                {
                    ImGui.SameLine(0, 2);
                    ImGuiEx.TextV(member.Level < member.LevelCap ? ImGuiHelper.White : ImGuiHelper.MaxLevelColor, $"{member.Level.ToString().ReplaceByChar(Digits.Normal, Digits.GameFont)}");
                }

                ImGui.NextColumn();
            }
        }

        private static void ItemClicked((PathAction, int) item)
        {
            if (item.Item2 == Plugin.indexer || item.Item1.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase))
            {
                Plugin.indexer = -1;
                Plugin.mainListClicked = false;
            }
            else
            {
                Plugin.indexer = item.Item2;
                Plugin.mainListClicked = true;
            }
        }

        internal static void PathsUpdated() => 
            DutySelected = null;
    }
}   