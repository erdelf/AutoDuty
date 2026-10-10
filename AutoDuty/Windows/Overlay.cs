using AutoDuty.Helpers;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using ECommons.ImGuiMethods;

namespace AutoDuty.Windows;

using System;
using System.Collections.Generic;
using Configurations;

public class Overlay : Window
{
    public Overlay() : base("AutoDuty Overlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize) => 
        this.RespectCloseHotkey = false;

    private static string hideText = " ";
    private static string hideTextAction = " ";
    private static string loopsText = "";

    private Vector2 pos;
    private int     lineHeightPrev = 1;
    private int     lineHeight     = 1;

    public override void PreDraw()
    {
        base.PreDraw();
　　　　　if (AutoDuty.Configuration.Overlay.NoBG)
   　　　　 this.Flags |= ImGuiWindowFlags.NoBackground;
　　　　　else
   　　　　 this.Flags &= ~ImGuiWindowFlags.NoBackground;        
        int heightDiff = (this.lineHeight - this.lineHeightPrev);

        if (AutoDuty.Configuration.Overlay.AnchorBottom && heightDiff != 0)
        {
            this.Position ??= this.pos;
            this.Position -= new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 1.1f * heightDiff) * ImGuiHelpers.GlobalScale;
        }
        else
        {
            this.Position = null;
        }

        this.lineHeightPrev = this.lineHeight;
    }

    public override void Draw()
    {
        this.pos = ImGui.GetWindowPos();
        this.lineHeight = 0;

        if (!PlayerHelper.IsValid)
        {
            if (!SchedulerHelper.Schedules.ContainsKey("OpenOverlay"))
                SchedulerHelper.ScheduleAction("OpenOverlay", () => this.IsOpen = true, () => PlayerHelper.IsReady);
            this.IsOpen = false;
            return;
        }

        if(!AutoDuty.Configuration.Overlay.Show)
        {
            this.IsOpen = false;
            return;
        }

        List<Action> lineActions = [];

        if (!Plugin.States.HasAnyFlag(PluginState.Looping, PluginState.Navigating))
        {
            if (AutoDuty.Configuration.Overlay.HideWhenStopped)
            {
                this.IsOpen = false;
                return;
            }
            this.lineHeight++;

            lineActions.Add(() =>
                            {
                                MainWindow.GotoAndActions();
                                if (!InDungeon)
                                {
                                    ImGui.SameLine(0, 5);
                                    if (ImGuiEx.IconButton($"\uf013##Config", "OpenAutoDuty"))
                                        Plugin.MainWindow.IsOpen = !Plugin.MainWindow.IsOpen;
                                    ImGui.SameLine(0, 5);
                                    if (ImGuiEx.IconButton(Dalamud.Interface.FontAwesomeIcon.WindowClose, "CloseOverlay"))
                                    {
                                        this.IsOpen                         = false;
                                        AutoDuty.Configuration.Overlay.Show = false;
                                        Plugin.MainWindow.IsOpen            = true;
                                    }
                                }
                            });
        }

        if (InDungeon || Plugin.States.HasFlag(PluginState.Looping))
        {
            this.lineHeight++;
            lineActions.Add(() =>
                            {
                                using (ImRaii.Disabled(!InDungeon || !ContentPathsManager.DictionaryPaths.ContainsKey(Svc.ClientState.TerritoryType)))
                                {
                                    if (Plugin.Stage == 0)
                                    {
                                        if (!Plugin.States.HasFlag(PluginState.Navigating) && !Plugin.States.HasFlag(PluginState.Looping))
                                            if (ImGui.Button("Start"))
                                            {
                                                Plugin.LoadPath();
                                                Plugin.Run(Svc.ClientState.TerritoryType);
                                            }
                                    }
                                    else
                                    {
                                        MainWindow.StopResumePause();
                                    }

                                    ImGui.SameLine(0, 5);
                                }

                                ImGui.PushItemWidth(75 * ImGuiHelpers.GlobalScale);
                                MainWindow.LoopsConfig();
                                ImGui.PopItemWidth();
                                ImGui.SameLine();
                                if (ImGuiEx.IconButton($"\uf013##Config", "OpenAutoDuty"))
                                    Plugin.MainWindow.IsOpen = !Plugin.MainWindow.IsOpen;

                                ImGui.SameLine();
                                if (ImGuiEx.IconButton(Dalamud.Interface.FontAwesomeIcon.WindowClose, "CloseOverlay"))
                                {
                                    this.IsOpen                        = false;
                                    AutoDuty.Configuration.Overlay.Show = false;
                                    Plugin.MainWindow.IsOpen           = true;
                                }
                            });

            if (AutoDuty.Configuration.Overlay.ShowDutyLoopText)
            {
                this.lineHeight++;
                lineActions.Add(() =>
                                {
                                    if (ImGui.Button($"{hideText}##OverlayHideButton"))
                                    {
                                        AutoDuty.Configuration.Overlay.ShowDutyLoopText = false;
                                        ConfigurationProfileV2.Save();
                                    }

                                    hideText = ImGui.IsItemHovered() ? "Hide" : string.Empty;

                                    ImGui.SameLine(0, 5);

                                    if (Plugin.States.HasFlag(PluginState.Navigating) || Plugin.States.HasFlag(PluginState.Navigating))
                                        loopsText =
                                            $"{(Plugin.CurrentTerritoryContent?.Name!.Length > 20 ? Plugin.CurrentTerritoryContent?.Name![..17] + "..." : Plugin.CurrentTerritoryContent?.Name)}{(Plugin.States.HasFlag(PluginState.Navigating) ? $": {Plugin.currentLoop} of {AutoDuty.Configuration.Meta.LoopTimes} Loops" : "")}";
                                    else
                                        loopsText =
                                            $"{(Plugin.CurrentTerritoryContent?.Name!.Length > 40 ? Plugin.CurrentTerritoryContent?.Name![..37] + "..." : Plugin.CurrentTerritoryContent?.Name)}{(Plugin.States.HasFlag(PluginState.Navigating) ? $": {Plugin.currentLoop} of {AutoDuty.Configuration.Meta.LoopTimes} Loops" : "")}";

                                    ImGui.TextColored(new Vector4(93 / 255f, 226 / 255f, 231 / 255f, 1), loopsText);
                                });
            }
        }
        if (InDungeon || Plugin.States.HasFlag(PluginState.Navigating) || RepairHelper.State == ActionState.Running || GotoHelper.State == ActionState.Running || GotoInnHelper.State == ActionState.Running || GotoBarracksHelper.State == ActionState.Running || GCTurninHelper.State == ActionState.Running || ExtractHelper.State == ActionState.Running || DesynthHelper.State == ActionState.Running || QueueHelper.State == ActionState.Running)
            if (AutoDuty.Configuration.Overlay.ShowActionText)
            {
                this.lineHeight++;

                lineActions.Add(() =>
                                {
                                    if (ImGui.Button(hideTextAction + "##OverlayHideActionButton"))
                                    {
                                        AutoDuty.Configuration.Overlay.ShowActionText = false;
                                        ConfigurationProfileV2.Save();
                                    }

                                    hideTextAction = ImGui.IsItemHovered() ? "Hide" : "";

                                    ImGui.SameLine(0, 5);
                                    ImGui.TextColored(new Vector4(0, 255f, 0, 1), Plugin.action.Length > 40 ? Plugin.action[..37] + "..." : Plugin.action);
                                });
            }

        if(Plugin.isDev)
            lineActions.Add(() => ImGui.Text(Plugin.Stage.ToString()));

        if(AutoDuty.Configuration.Overlay.AnchorBottom)
            for (int i = lineActions.Count - 1; i >= 0; i--)
                lineActions[i]();
        else
            for (int i = 0; i < lineActions.Count; i++)
                lineActions[i]();
    }
}
