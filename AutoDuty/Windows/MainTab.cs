using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;

namespace AutoDuty.Windows
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Configurations;
    using Dalamud.Interface;
    using Dalamud.Interface.Utility;
    using Data;
    using ECommons.PartyFunctions;
    using ECommons.Throttlers;
    using FFXIVClientStructs.FFXIV.Client.UI.Misc;
    using static Data.Classes;
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
            
            DutyMode     dutyMode     = AutoDuty.Configuration.Meta.DutyModeEnum;
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
                                               !AutoDuty.Configuration.Meta.PathSelectionsByPath.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ||
                                               !(pathSelection = AutoDuty.Configuration.Meta.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType])!.Any(kvp => kvp.Value.HasJob(Player.Job))))
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
                                    Dictionary<string, JobWithRole> pathJobs = AutoDuty.Configuration.Meta.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType]!;
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
                if (AutoDuty.Configuration.Loop.Termination.Enabled &&
                    (AutoDuty.Configuration.Loop.Termination.StopLevel      ||
                     AutoDuty.Configuration.Loop.Termination.StopNoRestedXP ||
                     AutoDuty.Configuration.Loop.Termination.StopItemQty    ||
                     AutoDuty.Configuration.Loop.Termination.TerminationBLUSpellsEnabled))
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
                        DrawCrucibleMenuToggles();
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
                            using ImRaii.DisabledDisposable __ = ImRaii.Disabled(AutoDuty.Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist);
                            ImGui.SameLine();
                            byte variantPath = Plugin.VariantPath;
                            ImGui.InputByte($"###Path", ref variantPath, 1);
                            Plugin.VariantPath = variantPath;
                        }

                        DrawTerminationNotice();

                        if (!ImGui.BeginListBox("##MainList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) return;

                        if ((VNavmesh_IPCSubscriber.IsEnabled || AutoDuty.Configuration.DutyConfig.UsingAlternativeMovementPlugin) &&
                            (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.DutyConfig.UsingAlternativeBossPlugin)     &&
                            (RSR_IPCSubscriber.IsEnabled      || BossMod_IPCSubscriber.IsEnabled || AutoDuty.Configuration.DutyConfig.UsingAlternativeRotationPlugin))
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
                            if (!VNavmesh_IPCSubscriber.IsEnabled && !AutoDuty.Configuration.DutyConfig.UsingAlternativeMovementPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresVNavmesh"));
                            if (!BossMod_IPCSubscriber.IsEnabled && !AutoDuty.Configuration.DutyConfig.UsingAlternativeBossPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresBossMod"));
                            if (!Wrath_IPCSubscriber.IsEnabled && !RSR_IPCSubscriber.IsEnabled && !BossMod_IPCSubscriber.IsEnabled && !AutoDuty.Configuration.DutyConfig.UsingAlternativeRotationPlugin)
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
                    if (ImGui.BeginCombo("##AutoDutyModeEnum", Loc.Get($"MainTab.Modes.{AutoDuty.Configuration.Meta.AutoDutyModeEnum}")))
                    {
                        foreach (AutoDutyMode mode in Enum.GetValues(typeof(AutoDutyMode)))
                            if (ImGui.Selectable(Loc.Get($"MainTab.Modes.{mode}"), AutoDuty.Configuration.Meta.AutoDutyModeEnum == mode))
                            {
                                AutoDuty.Configuration.Meta.AutoDutyModeEnum = mode;
                                ConfigurationProfileV2.Save();
                            }

                        if (ImGui.Selectable(Loc.Get("MainTab.Modes.NoviceHall")))
                        {
                            AutoDuty.Configuration.Meta.AutoDutyModeEnum = AutoDutyMode.Playlist;
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
                            if (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.None)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorSelectVersion"));
                            else if (Svc.Party.PartyId > 0 && AutoDuty.Configuration.Meta.DutyModeEnum is DutyMode.Support or DutyMode.Squadron or DutyMode.Trust)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorNotInParty"));
                            else if (AutoDuty.Configuration is { Meta.DutyModeEnum: DutyMode.Regular, DutyConfig.OverridePartyValidation: false } && synced && UniversalParty.Length < 4)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorGroupOf4"));
                            else if (AutoDuty.Configuration is { Meta.DutyModeEnum: DutyMode.Regular, DutyConfig.OverridePartyValidation: false } && synced && !ObjectHelper.PartyValidation())
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorPartyMakeup"));
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
                    switch (AutoDuty.Configuration.Meta.AutoDutyModeEnum)
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
                            ImGui.TextColored(AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectDutyMode"));
                            ImGui.SameLine(0);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGui.BeginCombo("##DutyModeEnum", Loc.Get($"MainTab.DutyModes.{AutoDuty.Configuration.Meta.DutyModeEnum}"), ImGuiComboFlags.HeightLargest))
                            {
                                foreach (DutyMode mode in Enum.GetValues(typeof(DutyMode)))
                                    //if(mode is not DutyMode.NoviceHall)
                                    if (ImGui.Selectable(Loc.Get($"MainTab.DutyModes.{mode}"), AutoDuty.Configuration.Meta.DutyModeEnum == mode))
                                    {
                                        AutoDuty.Configuration.Meta.DutyModeEnum = mode;
                                        ConfigurationProfileV2.Save();
                                    }

                                ImGui.EndCombo();
                            }
                            ImGui.PopItemWidth();

                            if (AutoDuty.Configuration.Meta.DutyModeEnum != DutyMode.None)
                            {
                                if (AutoDuty.Configuration.Meta.DutyModeEnum is DutyMode.Support or DutyMode.Trust)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextColored(Plugin.LevelingModeEnum == LevelingMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectLevelingMode"));
                                    ImGui.SameLine(0);

                                    ImGuiComponents.HelpMarker(Loc.Get("MainTab.LevelingModeHelp", AutoDuty.Configuration.Meta.DutyModeEnum != DutyMode.Trust ?
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

                                        LevelingMode autoLevelMode = (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.Support ? LevelingMode.Support : LevelingMode.Trust_Group);
                                        if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes."+autoLevelMode)+"##LevelingModeComboAuto", Plugin.LevelingModeEnum == autoLevelMode))
                                        {
                                            Plugin.LevelingModeEnum = autoLevelMode;
                                            ConfigurationProfileV2.Save();

                                            AutoDuty.Configuration.Loop.Pre.Actions.RunConfig<AutoEquipLoopActionConfig>();
                                        }

                                        if (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.Trust)
                                            if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes." + "Trust_Solo") + "##LevelingModeComboTrustGroup", Plugin.LevelingModeEnum == LevelingMode.Trust_Solo))
                                            {
                                                Plugin.LevelingModeEnum = LevelingMode.Trust_Solo;
                                                ConfigurationProfileV2.Save();

                                                AutoDuty.Configuration.Loop.Pre.Actions.RunConfig<AutoEquipLoopActionConfig>();
                                            }

                                        ImGui.EndCombo();
                                    }

                                    ImGui.PopItemWidth();
                                }

                                if (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.Support && levelingMode == LevelingMode.Support)
                                {
                                    bool preferTrust = AutoDuty.Configuration.Meta.PreferTrustOverSupportLeveling;
                                    if (ImGui.Checkbox(Loc.Get("MainTab.PreferTrust"), ref preferTrust))
                                    {
                                        AutoDuty.Configuration.Meta.PreferTrustOverSupportLeveling = preferTrust;
                                        ConfigurationProfileV2.Save();
                                    }
                                }

                                if (Plugin.LevelingEnabled)
                                {
                                    bool experimentalEntries = AutoDuty.Configuration.DutyConfig.LevelingListExperimentalEntries;
                                    if (ImGui.Checkbox("Include Testing/Unstable Dungeons", ref experimentalEntries))
                                    {
                                        AutoDuty.Configuration.DutyConfig.LevelingListExperimentalEntries = experimentalEntries;
                                       ConfigurationProfileV2.Save();
                                    }

                                    ImGuiEx.HelpMarker($"Adds more dungeons into the leveling list\nThese dungeons are currently in testing for reliability\nPlease report if you have issues with them");
                                }

                                if (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.Squadron)
                                {
                                    bool squadronLowest = AutoDuty.Configuration.Meta.SquadronAssignLowestMembers;
                                    if (ImGui.Checkbox(Loc.Get("MainTab.UseLowestMembers"), ref squadronLowest))
                                    {
                                        AutoDuty.Configuration.Meta.SquadronAssignLowestMembers = squadronLowest;
                                        ConfigurationProfileV2.Save();
                                    }
                                }

                                if (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.Crucible && Player.Available)
                                    DrawCrucible();

                                if (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.Trust && Player.Available)
                                {
                                    ImGui.Separator();
                                    if (DutySelected is { Content.TrustMembers.Count: > 0 })
                                    {
                                        ImGuiEx.LineCentered(() => ImGuiEx.TextUnderlined(Loc.Get("MainTab.SelectTrustParty")));

                                        TrustHelper.ResetTrustIfInvalid();
                                        for (int i = 0; i < AutoDuty.Configuration.SelectedTrustMembers.Length; i++)
                                        {
                                            TrustMemberName? member = AutoDuty.Configuration.SelectedTrustMembers[i];

                                            if (member is null)
                                                continue;

                                            if (DutySelected.Content.TrustMembers.All(x => x.MemberName != member))
                                            {
                                                Svc.Log.Debug($"Killing {member}");
                                                AutoDuty.Configuration.SelectedTrustMembers[i] = null;
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
                                bool hideUnavailableDuties = AutoDuty.Configuration.Meta.HideUnavailableDuties;
                                if (ImGui.Checkbox(Loc.Get("MainTab.HideUnavailable"), ref hideUnavailableDuties))
                                {
                                    AutoDuty.Configuration.Meta.HideUnavailableDuties = hideUnavailableDuties;
                                    ConfigurationProfileV2.Save();
                                }

                                if (AutoDuty.Configuration.Meta.DutyModeEnum is DutyMode.Regular or DutyMode.Trial or DutyMode.Raid)
                                {
                                    bool metaUnsynced = AutoDuty.Configuration.Meta.Unsynced;
                                    if (ImGuiEx.CheckboxWrapped(Loc.Get("MainTab.Unsynced"), ref metaUnsynced))
                                    {
                                        AutoDuty.Configuration.Meta.Unsynced = metaUnsynced;
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
                            AutoDuty.Configuration.Meta.AutoDutyModeEnum = AutoDutyMode.Looping;
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

                    if (AutoDuty.Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist)
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

                    if (Player.Job.GetCombatRole() == CombatRole.NonCombat)
                    {
                        ImGuiEx.TextWrapped(new Vector4(255, 1, 0, 1), Loc.Get("MainTab.SwitchCombatJob"));
                    }
                    else if (Player.Job == Job.BLU && AutoDuty.Configuration.Meta.DutyModeEnum is not (DutyMode.Regular or DutyMode.Trial or DutyMode.Raid))
                    {
                        ImGuiEx.TextWrapped(new Vector4(0, 1, 1, 1), Loc.Get("MainTab.BlueMageRestriction"));
                    }
                    else if (VNavmesh_IPCSubscriber.IsEnabled && BossMod_IPCSubscriber.IsEnabled)
                    {
                        if (PlayerHelper.IsReady)
                            switch (AutoDuty.Configuration.Meta.AutoDutyModeEnum)
                            {
                                case AutoDutyMode.Looping:
                                    if (Plugin.LevelingModeEnum != LevelingMode.None)
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
                                                if (AutoDuty.Configuration.Meta.DutyModeEnum == DutyMode.Trust && !item.Value.DutyModes.HasFlag(DutyMode.Trust))
                                                    continue;
                                                bool disabled = !item.Value.CanRun();
                                                if (!AutoDuty.Configuration.Meta.HideUnavailableDuties || !disabled)
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
                                        Dictionary<uint, Content> dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.DutyModes.HasFlag(AutoDuty.Configuration.Meta.DutyModeEnum)).ToDictionary();

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
                                                    if (AutoDuty.Configuration.Meta.HideUnavailableDuties && !canRun)
                                                        continue;
                                                    if (ImGui.Selectable($"L{content.ClassJobLevelRequired} ({content.TerritoryType}) {content.Name}", DutySelected?.ID == content.TerritoryType))
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

                                            if (entry.DutyMode == DutyMode.Variant)
                                            {
                                                ImGui.PushItemWidth(80f.Scale());
                                                ImGui.SameLine();
                                                ImGui.InputByte($"###Playlist{i}PathIndex", ref entry.variantPathIndex, 1);
                                                ImGui.PopItemWidth();
                                            }

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

        private static void DrawCrucible()
        {
            ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = AutoDuty.Configuration.Meta.Crucible;

            ImGui.Separator();
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

            if(crucible.TeamMode == CrucibleTeamMode.Leveling)
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

            List<uint>                                  team   = CrucibleTeam.For(crucible.TeamMode);
            IReadOnlyDictionary<uint, CrucibleFamiliar> cached = CrucibleTeam.Familiars;
            List<uint>                                  owned  = CrucibleTeam.Owned().ToList();

            if (ImGui.CollapsingHeader($"{Loc.Get("MainTab.Crucible.Familiars", team.Count, CrucibleTeam.TeamSize())}###CrucibleFamiliars"))
            {
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

            if (ImGui.CollapsingHeader($"{Loc.Get("MainTab.Crucible.Menus")}###CrucibleMenus"))
                DrawCrucibleMenuToggles();

            if(crucible.Shop)
                if (ImGui.CollapsingHeader($"{Loc.Get("MainTab.Crucible.ShopSettings")}###CrucibleShopSettings"))
                    DrawCrucibleShopSettings(crucible);
        }

        private static readonly Vector4 CruciblePickedRow = new(0.25f, 0.55f, 0.95f, 0.18f);
        private static readonly Vector4 CrucibleFaded     = new(1f, 1f, 1f, 0.45f);

        private static void DrawCrucibleFamiliarTable(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible, List<uint> team,
                                                      IReadOnlyDictionary<uint, CrucibleFamiliar> cached, List<uint> owned)
        {
            bool custom = crucible.TeamMode == CrucibleTeamMode.Custom;

            IEnumerable<uint> rows = crucible.TeamMode switch
            {
                CrucibleTeamMode.Leveling    => CrucibleTeam.Leveling(false),
                CrucibleTeamMode.Recommended => CrucibleTeam.Recommended(false),
                _ => owned
            };

            float scale     = ImGuiHelpers.GlobalScale;
            float rowHeight = ImGui.GetFrameHeightWithSpacing();
            float height    = Math.Min(owned.Count + 1, 12) * rowHeight + 4 * scale;

            const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX;
            if (!ImGui.BeginTable("##CrucibleTable", 6, flags, new Vector2(0, height)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("##pick",                                 ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableSetupColumn("No.",                                    ImGuiTableColumnFlags.WidthFixed, 28 * scale);
            ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.Familiar"),    ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.Rank"),        ImGuiTableColumnFlags.WidthFixed, 36 * scale);
            ImGui.TableSetupColumn("EXP",                                    ImGuiTableColumnFlags.WidthFixed, 90 * scale);
            ImGui.TableSetupColumn("HP",                                     ImGuiTableColumnFlags.WidthFixed, 44 * scale);
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
                if (familiar is { Rank: > 0 })
                    Centered(familiar.Rank.ToString(), null);
                else
                    Centered("—", CrucibleFaded);

                ImGui.TableNextColumn();
                float share = CrucibleUi.ExpShare(familiar?.Exp ?? "");
                if (familiar is { Exp.Length: > 0 })
                    ImGui.ProgressBar(share, new Vector2(-1, ImGui.GetFrameHeight()), familiar.Exp);
                else
                {
                    ImGui.AlignTextToFramePadding();
                    Centered("—", CrucibleFaded);
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (familiar is { Hp: > 0 })
                    RightAligned(familiar.Hp.ToString(), null);
                else
                    RightAligned("—", CrucibleFaded);
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

        private static ImGuiEx.RealtimeDragDrop<uint>? _crucibleGearDragDrop;
        private static (uint Row, int Position)? _crucibleGearTyped;

        private static void DrawCrucibleShopSettings(ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible)
        {
            Toggle("RespectGearRequirements", crucible.RespectGearRequirements, v => crucible.RespectGearRequirements = v);

            if (!crucible.ShopGearOrder.SequenceEqual(CrucibleItemData.ShopGearOrder))
                crucible.ShopGearOrder = CrucibleItemData.ShopGearOrder.ToList();

            if(!crucible.ShopGearOrder.SequenceEqual(CrucibleItemData.ShopGear))
                using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
                    if (ImGui.SmallButton(Loc.Get("MainTab.Crucible.ClearShopGearOrder")))
                    {
                        crucible.ShopGearOrder = CrucibleItemData.ShopGear.ToList();
                        ConfigurationProfileV2.Save();
                    }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Loc.Get("MainTab.Crucible.ClearShopGearOrderHelp"));

            ImGui.TextColored(CrucibleFaded, Loc.Get("MainTab.Crucible.ShopGearOrderHelp"));

            CrucibleShopGearTable(crucible.ShopGearOrder);
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

        private static void CrucibleShopGearTable(List<uint> order)
        {
            _crucibleGearDragDrop ??= new ImGuiEx.RealtimeDragDrop<uint>(
                "CrucibleShopGearDragDrop",
                (row) => row.ToString(),
                smallButton: false
            );

            uint[] before = order.ToArray();

            int moveFromIndex = -1;
            int moveToIndex   = -1;

            _crucibleGearDragDrop.Begin();

            using (ImRaii.TableDisposable gearTable = ImRaii.Table("Crucible Shop Gear Table", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            {
                if (gearTable)
                {
                    ImGui.TableSetupColumn("##Reorder");
                    ImGui.TableSetupColumn("#");
                    ImGui.TableSetupColumn(Loc.Get("MainTab.Crucible.GearItem"), ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableHeadersRow();

                    for (int i = 0; i < order.Count; i++)
                    {
                        ImGui.PushID(i);

                        uint row = order[i];

                        ImGui.TableNextRow();
                        _crucibleGearDragDrop.NextRow();
                        _crucibleGearDragDrop.SetRowColor(row);

                        ImGui.TableSetColumnIndex(0);
                        _crucibleGearDragDrop.DrawButtonDummy(row, order, i);

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(40 * ImGuiHelpers.GlobalScale);
                        int position = _crucibleGearTyped?.Row == row ? _crucibleGearTyped.Value.Position : i + 1;
                        if (ImGui.InputInt("##Position", ref position, 0, 0)) 
                            _crucibleGearTyped = (row, position);

                        if (ImGui.IsItemDeactivated())
                        {
                            if (_crucibleGearTyped?.Row == row)
                            {
                                int typedPosition = Math.Clamp(_crucibleGearTyped.Value.Position, 1, order.Count);

                                moveFromIndex = i;
                                moveToIndex   = typedPosition - 1;
                                _crucibleGearTyped = null;
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(CrucibleItemData.NameOf(row));

                        ImGui.PopID();
                    }
                }
            }

            _crucibleGearDragDrop.End();

            bool typedNewPosition = moveFromIndex >= 0 && moveFromIndex != moveToIndex;
            if (typedNewPosition)
            {
                uint movedItem = order[moveFromIndex];
                order.RemoveAt(moveFromIndex);
                order.Insert(moveToIndex, movedItem);
            }

            if (!order.SequenceEqual(before)) 
                ConfigurationProfileV2.Save();
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
                if (familiar.Classification.Length > 0)
                    ImGui.TextColored(CrucibleFaded, familiar.Classification);

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
            ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = AutoDuty.Configuration.Meta.Crucible;

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
                if (ImGui.Checkbox(Loc.Get($"MainTab.Crucible.Toggles.{key}"), ref value))
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
                bool       enabled        = AutoDuty.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName);
                CombatRole playerRole     = Player.Job.GetCombatRole();
                int        numberSelected = AutoDuty.Configuration.SelectedTrustMembers.Count(x => x != null);

                TrustMember?[] members = [..AutoDuty.Configuration.SelectedTrustMembers.Select(tmn => tmn != null ? TrustHelper.Members[(TrustMemberName)tmn] : null)];

                bool canSelect = members.CanSelectMember(member, playerRole) && member.Level >= content.ClassJobLevelRequired;

                using (ImRaii.Disabled(!enabled && (numberSelected == 3 || !canSelect)))
                {
                    if (ImGui.Checkbox($"###{member.Index}{content.Id}", ref enabled))
                    {
                        if (enabled)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                if (AutoDuty.Configuration.SelectedTrustMembers[i] is null)
                                {
                                    AutoDuty.Configuration.SelectedTrustMembers[i] = member.MemberName;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (AutoDuty.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName))
                            {
                                int idx = AutoDuty.Configuration.SelectedTrustMembers.IndexOf(x => x != null && x == member.MemberName);
                                AutoDuty.Configuration.SelectedTrustMembers[idx] = null;
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