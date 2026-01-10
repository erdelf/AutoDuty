using AutoDuty.Helpers;
using AutoDuty.Managers;
using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using System.Diagnostics;

namespace AutoDuty.Windows
{
    using IPC;

    internal static class InfoTab
    {
        private const string InfoUrl          = "https://docs.google.com/spreadsheets/d/151RlpqRcCpiD_VbQn6Duf-u-S71EP7d0mx3j1PDNoNA";
        private const string GitIssueUrl      = "https://github.com/erdelf/AutoDuty/issues";
        private const string PunishDiscordUrl = "https://discord.com/channels/1001823907193552978/1236757595738476725";

        public static void Draw()
        {
            if (MainWindow.CurrentTabName != "Info")
                MainWindow.SetCurrentTabName("Info");

            ImGui.NewLine();
            ImGuiEx.TextWrapped(Loc.Get("InfoTab.SetupGuideIntro"));
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("InfoTab.InformationAndSetup")).X) / 2);
            if (ImGui.Button(Loc.Get("InfoTab.InformationAndSetup")))
                Process.Start("explorer.exe", InfoUrl);
            ImGui.NewLine();
            ImGuiEx.TextWrapped(Loc.Get("InfoTab.PathStatusInfo"));
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("InfoTab.GitHubIssues")).X) / 2);
            if (ImGui.Button(Loc.Get("InfoTab.GitHubIssues")))
                Process.Start("explorer.exe", GitIssueUrl);
            ImGui.NewLine();
            ImGuiEx.TextCentered(Loc.Get("InfoTab.DiscordInvite"));
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("InfoTab.PunishDiscord")).X) / 2);
            if (ImGui.Button(Loc.Get("InfoTab.PunishDiscord")))
                Process.Start("explorer.exe", PunishDiscordUrl);

            ImGui.NewLine();

            int id = 0;

            void PluginInstallLine(ExternalPlugin plugin, string message)
            {
                bool isReady = plugin == ExternalPlugin.BossMod ?
                                   BossMod_IPCSubscriber.IsEnabled :
                                   IPCSubscriber_Common.IsReady(plugin.GetExternalPluginData().name);

                if(!isReady)
                    if (ImGui.Button($"{Loc.Get("InfoTab.Install")}##InstallExternalPlugin_{plugin}_{id++}"))
                        PluginInstaller.InstallPlugin(plugin);

                ImGui.NextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(isReady ? EzColor.Green : EzColor.Red, plugin.GetExternalPluginName());

                ImGui.NextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(message);
                ImGui.NextColumn();
            }

            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("InfoTab.RequiredPlugins")).X) / 2);
            ImGui.Text(Loc.Get("InfoTab.RequiredPlugins"));

            ImGui.Columns(3, "PluginInstallerRequired", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.BossMod, Loc.Get("InfoTab.PluginDesc.BossModFights"));
            PluginInstallLine(ExternalPlugin.vnav, Loc.Get("InfoTab.PluginDesc.VNavMovement"));

            ImGui.Columns(1);
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("InfoTab.CombatPlugins")).X) / 2);
            ImGui.Text(Loc.Get("InfoTab.CombatPlugins"));

            ImGui.Indent(65f);
            ImGui.TextColored(EzColor.Cyan, Loc.Get("InfoTab.CombatPluginsNote"));
            ImGui.Unindent(65f);

            ImGui.Columns(3, "PluginInstallerCombat", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.BossMod,              Loc.Get("InfoTab.PluginDesc.BossModRotations"));
            PluginInstallLine(ExternalPlugin.WrathCombo,           Loc.Get("InfoTab.PluginDesc.WrathCombo"));
            PluginInstallLine(ExternalPlugin.RotationSolverReborn, Loc.Get("InfoTab.PluginDesc.RotationSolverReborn"));

            ImGui.Columns(1);
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(Loc.Get("InfoTab.RecommendedPlugins")).X) / 2);
            ImGui.Text(Loc.Get("InfoTab.RecommendedPlugins"));
            ImGui.NewLine();
            ImGui.Columns(3, "PluginInstallerRecommended", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.AntiAFK,      Loc.Get("InfoTab.PluginDesc.AntiAFK"));
            PluginInstallLine(ExternalPlugin.AutoRetainer, Loc.Get("InfoTab.PluginDesc.AutoRetainer"));
            PluginInstallLine(ExternalPlugin.Avarice,      Loc.Get("InfoTab.PluginDesc.Avarice"));
            PluginInstallLine(ExternalPlugin.Lifestream,   Loc.Get("InfoTab.PluginDesc.Lifestream"));
            PluginInstallLine(ExternalPlugin.Pandora,      Loc.Get("InfoTab.PluginDesc.Pandora"));
            PluginInstallLine(ExternalPlugin.Gearsetter,   Loc.Get("InfoTab.PluginDesc.Gearsetter"));
            PluginInstallLine(ExternalPlugin.Stylist,      Loc.Get("InfoTab.PluginDesc.Stylist"));


            ImGui.Columns(1);
        }
    }
}
