using AutoDuty.Helpers;
using AutoDuty.IPC;
using AutoDuty.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.EzSharedDataManager;
using ECommons.Funding;
using ECommons.ImGuiMethods;
using ECommons.Schedulers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Numerics;

namespace AutoDuty.Windows;

using ECommons.DalamudServices;
using ECommons.Reflection;
using global::AutoDuty.Multibox;
using System;
using Configurations;

public sealed class MainWindow : Window, IDisposable
{
    internal static string CurrentTabName = "";

    private static bool _showPopup = false;
    private static bool _nestedPopup = false;
    private static string _popupText = "";
    private static string _popupTitle = "";
    private static string openTabName = "";

    public MainWindow() : base(
        $"AutoDuty v0.0.0.{Plugin.Version}###Autoduty")
    {
        this.SizeConstraints = new WindowSizeConstraints
                               {
                                   MinimumSize = new Vector2(10, 10),
                                   MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
                               };

        this.TitleBarButtons.Add(new TitleBarButton { Icon        = FontAwesomeIcon.Cog, IconOffset                         = new Vector2(1, 1), Click          = _ => OpenTab("Config") });
        this.TitleBarButtons.Add(new TitleBarButton { ShowTooltip = () => ImGui.SetTooltip("Support erdelf on Ko-fi"), Icon = FontAwesomeIcon.Heart, IconOffset = new Vector2(1, 1), Click = _ => GenericHelpers.ShellStart("https://ko-fi.com/erdelf") });
    }

    internal static void SetCurrentTabName(string tabName)
    {
        if (CurrentTabName != tabName)
            CurrentTabName = tabName;
    }

    internal static void OpenTab(string tabName)
    {
        openTabName = tabName;
        _ = new TickScheduler(delegate
        {
            openTabName = "";
        }, 25);
    }

    public void Dispose()
    {
    }

    internal static void Start() => 
        ImGui.SameLine(0, 5);

    internal static void LoopsConfig()
    {
        using ImRaii.DisabledDisposable _ = ImRaii.Disabled(MultiboxUtility.Config.MultiBox && !MultiboxUtility.Config.Host);

        int loopTimes = AutoDuty.Configuration.Meta.LoopTimes;
        if ((AutoDuty.Configuration.Meta.UseSliderInputs  && ImGui.SliderInt("Times", ref loopTimes, 1, 100)) ||
            (!AutoDuty.Configuration.Meta.UseSliderInputs && ImGui.InputInt("Times", ref loopTimes, 1)))
        {
            if (loopTimes <= 0)
                loopTimes = 1;

            if (AutoDuty.Configuration.Meta.AutoDutyModeEnum == AutoDutyMode.Playlist)
                Plugin.PlaylistCurrentEntry?.count = loopTimes;
            AutoDuty.Configuration.Meta.LoopTimes = loopTimes;
            ConfigurationProfileV2.Save();
        }
    }

    internal static void StopResumePause()
    {
        using (ImRaii.Disabled(!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.States.HasFlag(PluginState.Navigating) && !ActiveHelper.AnyHelperRunning()))
        {
            if (ImGui.Button($"Stop###Stop2"))
            {
                StopAndReset();
                return;
            }
            ImGui.SameLine(0, 5);
        }

        using (ImRaii.Disabled((!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.States.HasFlag(PluginState.Navigating) && !ActiveHelper.AnyHelperRunning()) || Plugin.CurrentTerritoryContent == null))
        {
            if (Plugin.Stage == Stage.Paused)
            {
                if (ImGui.Button("Resume"))
                {
                    Plugin.taskManager.StepMode = false;
                    Plugin.Stage = Plugin.previousStage;
                    Plugin.States &= ~PluginState.Paused;
                }
            }
            else
            {
                if (ImGui.Button("Pause")) Plugin.Stage = Stage.Paused;
            }
        }
    }

    private static void StopAndReset()
    {
        Plugin.playlistIndex = 0;
        Plugin.Stage = Stage.Stopped;
    }

    internal static void GotoAndActions()
    {
        ImGui.NewLine();
        if(Plugin.States.HasFlag(PluginState.Other))
            if(ImGui.Button("Stop###Stop1"))
                StopAndReset();

        using (ImRaii.Disabled(Plugin.States.HasAnyFlag(PluginState.Looping, PluginState.Navigating, PluginState.Other)))
        {
            if (AutoDuty.Configuration.Overlay.GoToActions)
            {
                ImGui.SameLine(0, 5);
                if (ImGui.Button(Loc.Get("Overlay.Button.Goto")))
                    ImGui.OpenPopup("GotoPopup");
            }

            if (ImGui.BeginPopup("GotoPopup"))
            {
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.Barracks"))) GotoBarracksHelper.Invoke();
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.Inn"))) GotoInnHelper.Invoke();
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.GCSupply"))) GotoHelper.Invoke(PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()), [GCTurninHelper.GCSupplyLocation], 0.25f, 3f);
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.FlagMarker"))) MapHelper.MoveToMapMarker();
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.SummoningBell"))) SummoningBellHelper.Invoke(AutoRetainerHelper.Instance.ActionConfig.PreferredSummoningBellEnum);
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.Apartment"))) GotoHousingHelper.Invoke(Housing.Apartment);
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.PersonalHome"))) GotoHousingHelper.Invoke(Housing.Personal_Home);
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.FCEstate"))) GotoHousingHelper.Invoke(Housing.FC_Estate);

                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.TripleTriadTrader"))) GotoHelper.Invoke(TripleTriadCardSellHelper.GoldSaucerTerritoryType, TripleTriadCardSellHelper.TripleTriadCardVendorLocation);
                if (ImGui.Selectable(Loc.Get("MainWindow.Goto.Crucible"))) GotoHelper.Invoke(CrucibleManager.LaudaTerritory, CrucibleManager.LaudaPosition, lastPointTollerance: 4f);
                ImGui.EndPopup();
            }

            foreach (LoopActionConfig action in AutoDuty.Configuration.Overlay.LoopActions)
                action.OnGUIButton();
        }

    }

    internal static void ToolTip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGuiEx.Text(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    internal static void ShowPopup(string popupTitle, string popupText, bool nested = false)
    {
        _popupTitle = popupTitle;
        _popupText = popupText;
        _showPopup = true;
        _nestedPopup = nested;
    }

    internal static void DrawPopup(bool nested = false)
    {
        if (!_showPopup || (_nestedPopup && !nested) || (!_nestedPopup && nested)) return;

        if (!ImGui.IsPopupOpen($"{_popupTitle}###Popup"))
            ImGui.OpenPopup($"{_popupTitle}###Popup");

        Vector2 textSize = ImGui.CalcTextSize(_popupText);
        ImGui.SetNextWindowSize(new Vector2(textSize.X + 25, textSize.Y + 100));
        if (ImGui.BeginPopupModal($"{_popupTitle}###Popup", ref _showPopup, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove))
        {
            ImGuiEx.TextCentered(_popupText);
            ImGui.Spacing();
            if (ImGuiHelper.CenteredButton("OK", .5f, 15))
            {
                _showPopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private static void KofiLink()
    {
        OpenTab(CurrentTabName);
        if (EzThrottler.Throttle("KofiLink", 15000))
            _ = new TickScheduler(delegate
                                  {
                                      GenericHelpers.ShellStart("https://ko-fi.com/erdelf");
                                  }, 500);
    }

    //ECommons
    static uint ColorNormal
    {
        get
        {
            Vector4 vector1 = ImGuiEx.Vector4FromRGB(0x022594);
            Vector4 vector2 = ImGuiEx.Vector4FromRGB(0x940238);

            uint    gen  = GradientColor.Get(vector1, vector2).ToUint();
            uint[]? data = EzSharedData.GetOrCreate<uint[]>("ECommonsPatreonBannerRandomColor", [gen]);
            if (!GradientColor.IsColorInRange(data[0].ToVector4(), vector1, vector2)) 
                data[0] = gen;
            return data[0];
        }
    }

    public static void EzTabBar(string id, string? KoFiTransparent, string openTab, ImGuiTabBarFlags flags, params (string name, Action function, Vector4? color, bool child)[] tabs)
    {
        ImGui.BeginTabBar(id, flags);


        bool valid = (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.DutyConfig.UsingAlternativeBossPlugin)     &&
                     (VNavmesh_IPCSubscriber.IsEnabled || AutoDuty.Configuration.DutyConfig.UsingAlternativeMovementPlugin) &&
                     (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.DutyConfig.UsingAlternativeRotationPlugin);

        if (!valid)
            openTab = "Info";

        foreach ((string name, Action function, Vector4? color, bool child) in tabs)
        {
            if (name.IsNullOrEmpty()) 
                continue;
            if (color != null) 
                ImGui.PushStyleColor(ImGuiCol.Tab, color.Value);

            if ((valid || name == "Info") && ImGui.BeginTabItem($"{Loc.Get($"MainWindow.Tabs.{name}")}###MainWindowTab{name}", openTab == name ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                if (color != null) 
                    ImGui.PopStyleColor();
                if (child) 
                    ImGui.BeginChild(name + "child");

                if(!valid)
                {
                    ImGui.NewLine();
                    ImGui.TextColored(EzColor.Red, "You need to do the basic setup below. Enjoy");
                }

                function();

                if (child) 
                    ImGui.EndChild();
                ImGui.EndTabItem();
            }
            else
            {
                if (color != null) 
                    ImGui.PopStyleColor();
            }
        }
        if (KoFiTransparent != null) 
            PatreonBanner.RightTransparentTab();
        
        ImGui.EndTabBar();
    }

    private static (string, Action, Vector4?, bool)[] TabList =>
    [
        ("Main", MainTab.Draw, null, false),
        ("Build", BuildTab.DrawBuildTab, null, false),
        ("Paths", PathsTab.Draw, null, false),
        ("Config", ConfigTab.Draw, null, false),
        ("Info", InfoTab.Draw, null, false),
        ("Logs", LogTab.Draw, null, false),
        ("Stats", StatsTab.Draw, null, false),
        ("Support", KofiLink, ImGui.ColorConvertU32ToFloat4(ColorNormal), false)
    ];

    public override void Draw()
    {
        DrawPopup();

        if(DalamudHelper.IsOnStaging())
        {
            ImGui.TextColored(GradientColor.Get(ImGuiHelper.ExperimentalColor, ImGuiHelper.ExperimentalColor2, 500), "NOT SUPPORTED ON STAGING.");
            ImGui.Text("Please type in \"/xlbranch\" and pick Release, then restart the game.");

            if (!ImGui.CollapsingHeader("Use despite staging. Support will not be given##stagingHeader"))
                return;
        }

        EzTabBar("MainTab", null, openTabName, ImGuiTabBarFlags.None, TabList);
    }
}
