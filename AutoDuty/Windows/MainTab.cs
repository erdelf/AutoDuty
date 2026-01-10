using AutoDuty.Helpers;
using AutoDuty.IPC;
using AutoDuty.Managers;
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
    using Dalamud.Interface;
    using Data;
    using ECommons.PartyFunctions;
    using FFXIVClientStructs.FFXIV.Client.UI.Misc;
    using static Data.Classes;
    using Vector2 = System.Numerics.Vector2;
    using Vector4 = System.Numerics.Vector4;

    internal static class MainTab
    {
        internal static ContentPathsManager.ContentPathContainer? DutySelected;
        internal static readonly (string Normal, string GameFont) Digits = ("0123456789", "");

        private static int _currentStepIndex = -1;
        private static readonly string _pathsURL = "https://github.com/erdelf/AutoDuty/tree/master/AutoDuty/Paths";

        // New search text field for filtering duties
        private static string _searchText = string.Empty;

        internal static void Draw()
        {
            if (MainWindow.CurrentTabName != "Main")
                MainWindow.SetCurrentTabName("Main");
            
            DutyMode     dutyMode     = AutoDuty.Configuration.DutyModeEnum;
            LevelingMode levelingMode = Plugin.LevelingModeEnum;

            static void DrawSearchBar()
            {
                // Set the maximum search to 10 characters
                const int inputMaxLength = 10;
                
                // Calculate the X width of the maximum amount of search characters
                float inputMaxWidth = ImGui.CalcTextSize("W").X * inputMaxLength;
                
                // Set the width of the search box to the calculated width
                ImGui.SetNextItemWidth(inputMaxWidth);
                
                ImGui.InputTextWithHint("##search", Loc.Get("MainTab.SearchDuties"), ref _searchText, inputMaxLength);

                // Apply filtering based on the search text
                if (_searchText.Length > 0)
                    // Trim and convert to lowercase for case-insensitive search
                    _searchText = _searchText.Trim().ToLower();
            }

            static void DrawPathSelection()
            {
                if (Plugin.CurrentTerritoryContent == null || !PlayerHelper.IsReady)
                    return;

                using ImRaii.IEndObject? d = ImRaii.Disabled(InDungeon && Plugin is { Stage: > 0 });

                if (ContentPathsManager.DictionaryPaths.TryGetValue(Plugin.CurrentTerritoryContent.TerritoryType, out ContentPathsManager.ContentPathContainer? container))
                {
                    List<ContentPathsManager.DutyPath> curPaths = container.Paths;
                    if (curPaths.Count > 1)
                    {
                        int                              curPath       = Math.Clamp(Plugin.currentPath, 0, curPaths.Count - 1);

                        Dictionary<string, JobWithRole>? pathSelection    = null;
                        JobWithRole                      curJob = Player.Job.JobToJobWithRole();
                        using (ImRaii.Disabled(curPath <= 0                                                                                           ||
                                               !AutoDuty.Configuration.PathSelectionsByPath.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ||
                                               !(pathSelection = AutoDuty.Configuration.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType])!.Any(kvp => kvp.Value.HasJob(Player.Job))))
                        {
                            if (ImGui.Button(Loc.Get("MainTab.ClearSavedPath")))
                            {
                                foreach (KeyValuePair<string, JobWithRole> keyValuePair in pathSelection!)
                                    pathSelection[keyValuePair.Key] &= ~curJob;

                                PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);
                                Configuration.Save();
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
                                    Dictionary<string, JobWithRole> pathJobs = AutoDuty.Configuration.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType]!;
                                    pathJobs.TryAdd(path.Value.FileName, JobWithRole.None);
                                    
                                    foreach (string jobsKey in pathJobs.Keys) 
                                        pathJobs[jobsKey] &= ~curJob;

                                    pathJobs[path.Value.FileName] |= curJob;

                                    PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);

                                    Configuration.Save();
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
                if (AutoDuty.Configuration.EnableTerminationActions &&
                    (AutoDuty.Configuration.StopLevel      ||
                     AutoDuty.Configuration.StopNoRestedXP ||
                     AutoDuty.Configuration.StopItemQty    ||
                     AutoDuty.Configuration.TerminationBLUSpellsEnabled))
                    ImGui.TextColoredWrapped(EzColor.Cyan, Loc.Get("MainTab.TerminationNotice"));
            }

            if (InDungeon)
            {
                if (Plugin.CurrentTerritoryContent == null)
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

                    DrawPathSelection();
                    if (!Plugin.states.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
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
                                    _currentStepIndex = -1;
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

                        DrawTerminationNotice();

                        if (!ImGui.BeginListBox("##MainList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) return;

                        if ((VNavmesh_IPCSubscriber.IsEnabled || AutoDuty.Configuration.UsingAlternativeMovementPlugin) &&
                            (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.UsingAlternativeBossPlugin)     &&
                            (RSR_IPCSubscriber.IsEnabled      || BossMod_IPCSubscriber.IsEnabled || AutoDuty.Configuration.UsingAlternativeRotationPlugin))
                        {
                            foreach ((PathAction Value, int Index) item in Plugin.Actions.Select((Value, Index) => (Value, Index))) item.Value.DrawCustomText(item.Index, () => ItemClicked(item));
                            //var text = item.Value.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase) ? item.Value.Note : $"{item.Value.ToCustomString()}";
                            ////////////////////////////////////////////////////////////////
                            if (_currentStepIndex != Plugin.indexer && _currentStepIndex > -1 && Plugin.Stage > 0)
                            {
                                float lineHeight = ImGui.GetTextLineHeightWithSpacing();
                                _currentStepIndex = Plugin.indexer;
                                if (_currentStepIndex > 1)
                                    ImGui.SetScrollY((_currentStepIndex - 1) * lineHeight);
                            }
                            else if (_currentStepIndex == -1 && Plugin.Stage > 0)
                            {
                                _currentStepIndex = 0;
                                ImGui.SetScrollY(_currentStepIndex);
                            }

                            if (InDungeon && Plugin is { Actions.Count: < 1 } && !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType))
                                ImGui.TextColored(new Vector4(0, 255, 0, 1),
                                                  Loc.Get("MainTab.NoPathFound", TerritoryName.GetTerritoryName(Plugin.CurrentTerritoryContent.TerritoryType).Split('|')[1].Trim(), Plugin.CurrentTerritoryContent.TerritoryType.ToString(), Plugin.pathsDirectory.FullName.Replace('\\', '/'), _pathsURL));
                        }
                        else
                        {
                            if (!VNavmesh_IPCSubscriber.IsEnabled && !AutoDuty.Configuration.UsingAlternativeMovementPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresVNavmesh"));
                            if (!BossMod_IPCSubscriber.IsEnabled && !AutoDuty.Configuration.UsingAlternativeBossPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresBossMod"));
                            if (!Wrath_IPCSubscriber.IsEnabled && !RSR_IPCSubscriber.IsEnabled && !BossMod_IPCSubscriber.IsEnabled && !AutoDuty.Configuration.UsingAlternativeRotationPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), Loc.Get("MainTab.RequiresRotation"));
                        }
                        ImGui.EndListBox();
                    }
                }
            }
            else
            {
                if (!Plugin.states.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
                    MainWindow.GotoAndActions();
                

                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Looping)))
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectMode"));
                    ImGui.SameLine(0);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.BeginCombo("##AutoDutyModeEnum", Loc.Get($"MainTab.Modes.{AutoDuty.Configuration.AutoDutyModeEnum}")))
                    {
                        foreach (AutoDutyMode mode in Enum.GetValues(typeof(AutoDutyMode)))
                            if (ImGui.Selectable(Loc.Get($"MainTab.Modes.{mode}"), AutoDuty.Configuration.AutoDutyModeEnum == mode))
                            {
                                AutoDuty.Configuration.AutoDutyModeEnum = mode;
                                Configuration.Save();
                            }

                        ImGui.EndCombo();
                    }
                    ImGui.PopItemWidth();
                }

                using (ImRaii.Disabled(Plugin.CurrentTerritoryContent == null))
                {
                    if (!Plugin.states.HasFlag(PluginState.Looping))
                    {
                        if (ImGui.Button(Loc.Get("MainTab.Run")))
                        {
                            bool synced = !QueueHelper.ShouldBeUnSynced();
                            if (AutoDuty.Configuration.DutyModeEnum == DutyMode.None)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorSelectVersion"));
                            else if (Svc.Party.PartyId > 0 && AutoDuty.Configuration.DutyModeEnum is DutyMode.Support or DutyMode.Squadron or DutyMode.Trust)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorNotInParty"));
                            else if (AutoDuty.Configuration is { DutyModeEnum: DutyMode.Regular, OverridePartyValidation: false } && synced && UniversalParty.Length < 4)
                                MainWindow.ShowPopup(Loc.Get("MainTab.Error"), Loc.Get("MainTab.ErrorGroupOf4"));
                            else if (AutoDuty.Configuration is { DutyModeEnum: DutyMode.Regular, OverridePartyValidation: false } && synced && !ObjectHelper.PartyValidation())
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


                
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Looping)))
                {
                    switch (AutoDuty.Configuration.AutoDutyModeEnum)
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
                            ImGui.TextColored(AutoDuty.Configuration.DutyModeEnum == DutyMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectDutyMode"));
                            ImGui.SameLine(0);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGui.BeginCombo("##DutyModeEnum", Loc.Get($"MainTab.DutyModes.{AutoDuty.Configuration.DutyModeEnum}")))
                            {
                                foreach (DutyMode mode in Enum.GetValues(typeof(DutyMode)))
                                    if (ImGui.Selectable(Loc.Get($"MainTab.DutyModes.{mode}"), AutoDuty.Configuration.DutyModeEnum == mode))
                                    {
                                        AutoDuty.Configuration.DutyModeEnum = mode;
                                            Configuration.Save();
                                    }

                                ImGui.EndCombo();
                            }
                            ImGui.PopItemWidth();

                            if (AutoDuty.Configuration.DutyModeEnum != DutyMode.None)
                            {
                                if (AutoDuty.Configuration.DutyModeEnum is DutyMode.Support or DutyMode.Trust)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextColored(Plugin.LevelingModeEnum == LevelingMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, Loc.Get("MainTab.SelectLevelingMode"));
                                    ImGui.SameLine(0);

                                    ImGuiComponents.HelpMarker(Loc.Get("MainTab.LevelingModeHelp",
                                                               AutoDuty.Configuration.DutyModeEnum != DutyMode.Trust ?
                                                                    string.Empty :
                                                                    Loc.Get("MainTab.LevelingModeHelpTrust")));
                                    ImGui.SameLine(0);
                                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                                    if (ImGui.BeginCombo("##LevelingModeEnum", Plugin.LevelingModeEnum switch
                                        {
                                            LevelingMode.None => Loc.Get("MainTab.LevelingModes.None"),
                                            _ => Loc.Get("MainTab.LevelingModes.Auto")
                                        }))
                                    {
                                        if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes.None"), Plugin.LevelingModeEnum == LevelingMode.None))
                                        {
                                            Plugin.LevelingModeEnum = LevelingMode.None;
                                                Configuration.Save();
                                        }

                                        LevelingMode autoLevelMode = (AutoDuty.Configuration.DutyModeEnum == DutyMode.Support ? LevelingMode.Support : LevelingMode.Trust_Group);
                                        if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes.Auto"), Plugin.LevelingModeEnum == autoLevelMode))
                                        {
                                            Plugin.LevelingModeEnum = autoLevelMode;
                                                Configuration.Save();
                                            if (AutoDuty.Configuration.AutoEquipRecommendedGear)
                                                AutoEquipHelper.Invoke();
                                        }

                                        if (AutoDuty.Configuration.DutyModeEnum == DutyMode.Trust)
                                            if (ImGui.Selectable(Loc.Get("MainTab.LevelingModes.Auto"), Plugin.LevelingModeEnum == LevelingMode.Trust_Solo))
                                            {
                                                Plugin.LevelingModeEnum = LevelingMode.Trust_Solo;
                                                    Configuration.Save();
                                                if (AutoDuty.Configuration.AutoEquipRecommendedGear)
                                                    AutoEquipHelper.Invoke();
                                            }


                                        ImGui.EndCombo();
                                    }

                                    ImGui.PopItemWidth();
                                }

                                if (AutoDuty.Configuration.DutyModeEnum == DutyMode.Support && levelingMode == LevelingMode.Support)
                                    if (ImGui.Checkbox(Loc.Get("MainTab.PreferTrust"), ref AutoDuty.Configuration.PreferTrustOverSupportLeveling))
                                            Configuration.Save();

                                if (Plugin.LevelingEnabled)
                                {
                                    bool experimentalEntries = AutoDuty.Configuration.LevelingListExperimentalEntries;
                                    if (ImGui.Checkbox("Include Testing/Unstable Dungeons", ref experimentalEntries))
                                    {
                                        AutoDuty.Configuration.LevelingListExperimentalEntries = experimentalEntries;
                                        Configuration.Save();
                                    }

                                    ImGuiEx.HelpMarker($"Adds more dungeons into the leveling list\nThese dungeons are currently in testing for reliability\nPlease report if you have issues with them");
                                }

                                if (AutoDuty.Configuration.DutyModeEnum == DutyMode.Squadron)
                                    if (ImGui.Checkbox(Loc.Get("MainTab.UseLowestMembers"), ref AutoDuty.Configuration.SquadronAssignLowestMembers))
                                            Configuration.Save();

                                if (AutoDuty.Configuration.DutyModeEnum == DutyMode.Trust && Player.Available)
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
                                if (ImGui.Checkbox(Loc.Get("MainTab.HideUnavailable"), ref AutoDuty.Configuration.HideUnavailableDuties))
                                        Configuration.Save();
                                if (AutoDuty.Configuration.DutyModeEnum is DutyMode.Regular or DutyMode.Trial or DutyMode.Raid)
                                    if (ImGuiEx.CheckboxWrapped(Loc.Get("MainTab.Unsynced"), ref AutoDuty.Configuration.Unsynced))
                                            Configuration.Save();
                            }

                            break;
                        }
                        case AutoDutyMode.Playlist:
                            ImGui.Separator();
                            break;
                        default:
                            AutoDuty.Configuration.AutoDutyModeEnum = AutoDutyMode.Looping;
                            break;
                    }

                    DrawTerminationNotice();

                    ushort ilvl = InventoryHelper.CurrentItemLevel;
                    if (!ImGui.BeginListBox("##DutyList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) 
                        return;

                    if (Player.Job.GetCombatRole() == CombatRole.NonCombat)
                    {
                        ImGuiEx.TextWrapped(new Vector4(255, 1, 0, 1), Loc.Get("MainTab.SwitchCombatJob"));
                    }
                    else if (Player.Job == Job.BLU && AutoDuty.Configuration.DutyModeEnum is not (DutyMode.Regular or DutyMode.Trial or DutyMode.Raid))
                    {
                        ImGuiEx.TextWrapped(new Vector4(0, 1, 1, 1), Loc.Get("MainTab.BlueMageRestriction"));
                    }
                    else if (VNavmesh_IPCSubscriber.IsEnabled && BossMod_IPCSubscriber.IsEnabled)
                    {
                        if (PlayerHelper.IsReady)
                            switch (AutoDuty.Configuration.AutoDutyModeEnum)
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
                                                if (AutoDuty.Configuration.DutyModeEnum == DutyMode.Trust && !item.Value.DutyModes.HasFlag(DutyMode.Trust))
                                                    continue;
                                                bool disabled = !item.Value.CanRun();
                                                if (!AutoDuty.Configuration.HideUnavailableDuties || !disabled)
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
                                        Dictionary<uint, Content> dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.DutyModes.HasFlag(AutoDuty.Configuration.DutyModeEnum)).ToDictionary();

                                        if (dictionary.Count > 0 && PlayerHelper.IsReady)
                                        {
                                            short level = PlayerHelper.GetCurrentLevelFromSheet();
                                            foreach ((uint _, Content content) in dictionary)
                                            {
                                                // Apply search filter
                                                if (!string.IsNullOrWhiteSpace(_searchText) && !(content.Name?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ?? false))
                                                    continue; // Skip duties that do not match the search text

                                                bool canRun = content.CanRun(level);
                                                using (ImRaii.Disabled(!canRun))
                                                {
                                                    if (AutoDuty.Configuration.HideUnavailableDuties && !canRun)
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
                                        for (int i = 0; i < Plugin.playlistCurrent.Count; i++)
                                        {
                                            PlaylistEntry entry = Plugin.playlistCurrent[i];

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

                                                    if (ImGui.Selectable(gearset->NameString, entry.gearset == gearset->Id)) entry.gearset = gearset->Id;
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
                                            ImGui.PushItemWidth((entryContainer.Paths.Count > 1 ? (ImGui.GetContentRegionAvail().X - 107f.Scale()) / 2f : ImGui.GetContentRegionAvail().X - 200f.Scale()));
                                            if (ImGui.BeginCombo($"##Playlist{i}DutySelection", $"({entry.Id}) {entryContent.Name}"))
                                            {
                                                short level = PlayerHelper.GetCurrentLevelFromSheet();
                                                DrawSearchBar();

                                                foreach (uint key in ContentPathsManager.DictionaryPaths.Keys)
                                                {
                                                    Content content = ContentHelper.DictionaryContent[key];

                                                    if (!string.IsNullOrWhiteSpace(_searchText) && !(content.Name?.Contains(_searchText, StringComparison.InvariantCultureIgnoreCase) ?? false))
                                                        continue;

                                                    if (content.DutyModes.HasFlag(entry.DutyMode) && content.CanRun(level, entry.DutyMode))
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
                                                if (ImGui.BeginCombo($"##Playlist{i}PathSelection", entryContainer.Paths.First(dp => dp.FileName == entry.path).Name))
                                                {
                                                    foreach (ContentPathsManager.DutyPath path in entryContainer.Paths)
                                                        if(ImGui.Selectable(path.Name, path.FileName == entry.path)) 
                                                            entry.path = path.FileName;

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
                                                    Plugin.playlistCurrent.Remove(entry);
                                                    Plugin.playlistCurrent.Insert(i - 1, entry);
                                                }
                                            }

                                            ImGui.SameLine();

                                            using(ImRaii.Disabled(Plugin.playlistCurrent.Count <= i+1))
                                            {
                                                if (ImGuiComponents.IconButton($"Playlist{i}Down", FontAwesomeIcon.ArrowDown))
                                                {
                                                    Plugin.playlistCurrent.Remove(entry);
                                                    Plugin.playlistCurrent.Insert(i+1, entry);
                                                }
                                            }

                                            ImGui.SameLine();

                                            if (ImGuiComponents.IconButton($"Playlist{i}Trash", FontAwesomeIcon.TrashAlt))
                                                Plugin.playlistCurrent.RemoveAt(i);
                                        }


                                        if (ImGuiComponents.IconButton("PlaylistAdd", FontAwesomeIcon.Plus)) 
                                            Plugin.playlistCurrent.Add(new PlaylistEntry { DutyMode = Plugin.playlistCurrent.Count != 0 ? Plugin.playlistCurrent.Last().DutyMode : DutyMode.Support });

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

                        Configuration.Save();
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
