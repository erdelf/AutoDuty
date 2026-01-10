using System;
using AutoDuty.Managers;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.ImGuiMethods;
using ECommons.Throttlers;
using Dalamud.Bindings.ImGui;
using Serilog.Events;
using System.Numerics;
using static AutoDuty.Updater.GitHubHelper;

namespace AutoDuty.Windows
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using ECommons.DalamudServices;

    internal static class LogTab
    {
        internal static void Add(LogMessage message) => _logEntriesToAdd.Enqueue(message);

        private static Task<UserCode?>? _taskUserCode = null;
        private static Task<PollResponseClass?>? _taskPollResponse = null;
        private static Task<string?>? _taskSubmitIssue = null;
        private static readonly Queue<LogMessage> _logEntriesToAdd = [];
        private static string _titleInput = $"[Bug] ";
        private static string _whatHappenedInput = string.Empty;
        private static string _reproStepsInput = string.Empty;
        private static bool _popupOpen = false;
        private static UserCode? _userCode = null;
        private static PollResponseClass? _pollResponse = null;
        private static ImGuiWindowFlags _imGuiWindowFlags = ImGuiWindowFlags.None;
        private static bool _copied = false;
        private static bool _clearedDataAfterPopupClose = true;
        public static void Draw()
        {
            if (MainWindow.CurrentTabName != "Logs")
                MainWindow.SetCurrentTabName("Logs");

            if (!_popupOpen && !_clearedDataAfterPopupClose)
            {
                _clearedDataAfterPopupClose = false;
                _copied = false;
                _taskPollResponse = null;
                _taskUserCode = null;
                _userCode = null;
                _pollResponse = null;
                _reproStepsInput = string.Empty;
                _taskSubmitIssue = null;
                _titleInput = string.Empty;
                _whatHappenedInput = string.Empty;
            }
            ImGuiEx.Spacing();
            if (ImGui.Checkbox(Loc.Get("LogTab.AutoScroll"), ref AutoDuty.Configuration.AutoScroll))
                Configuration.Save();
            ImGui.SameLine();
            if (ImGuiEx.IconButton(Dalamud.Interface.FontAwesomeIcon.Trash))
                Plugin.dalamudLogEntries.Clear();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("LogTab.ClearLog"));
            ImGui.SameLine();
            if (ImGuiEx.IconButton(Dalamud.Interface.FontAwesomeIcon.Copy))
                ImGui.SetClipboardText(Plugin.dalamudLogEntries.SelectMulti(x => x.Message).ToList().ToCustomString("\n"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("LogTab.CopyLog"));
            ImGui.SameLine();
            using (ImRaii.Disabled(!_taskUserCode?.IsCompletedSuccessfully ?? false))
            {
                if (ImGui.Button(Loc.Get("LogTab.CreateIssue")))
                {
                    if (_pollResponse == null || _pollResponse.Access_Token.IsNullOrEmpty())
                    {
                        if (_userCode == null)
                            _taskUserCode = Task.Run(GetUserCode);
                        _imGuiWindowFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove;
                    }
                    else
                    {
                        ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
                        _imGuiWindowFlags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
                    }
                    _popupOpen = true;
                    ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.None, new Vector2(0.5f, 0.5f));
                    ImGui.OpenPopup(Loc.Get("LogTab.CreateIssue"));
                }
            }
            if (_pollResponse != null && !_pollResponse.Access_Token.IsNullOrEmpty())
            {
                ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
                ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.None, new Vector2(0.5f, 0.5f));
                _imGuiWindowFlags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
            }    
            if (ImGui.BeginPopupModal(Loc.Get("LogTab.CreateIssue"), ref _popupOpen, _imGuiWindowFlags))
            {
                _clearedDataAfterPopupClose = false;
                if (_pollResponse == null || _pollResponse.Access_Token.IsNullOrEmpty())
                    DrawUserCodePopup();
                else
                    DrawIssuePopup();
                ImGui.EndPopup();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("LogTab.CreateIssueTooltip"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            var currentLevel = AutoDuty.Configuration.LogEventLevel;
            if (ImGui.BeginCombo("##LogEventLevel", Loc.Get($"LogTab.LogLevels.{currentLevel}")))
            {
                foreach (var level in Enum.GetValues<LogEventLevel>())
                {
                    if (ImGui.Selectable(Loc.Get($"LogTab.LogLevels.{level}"), level == currentLevel))
                    {
                        AutoDuty.Configuration.LogEventLevel = level;
                        if (Svc.Log.MinimumLogLevel > AutoDuty.Configuration.LogEventLevel)
                            Svc.Log.MinimumLogLevel = AutoDuty.Configuration.LogEventLevel;
                        Configuration.Save();
                    }
                }
                ImGui.EndCombo();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Get("LogTab.FilterLogLevel"));
            ImGuiEx.Spacing();

            if (AutoDuty.Configuration.LogEventLevel < LogEventLevel.Information) ImGui.TextWrapped(Loc.Get("LogTab.DebugLevelWarning"));

            ImGuiEx.Spacing();
            ImGui.BeginChild("scrolling", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y), true, ImGuiWindowFlags.HorizontalScrollbar);

            Plugin.dalamudLogEntries.Each(e => { if (e.LogEventLevel >= AutoDuty.Configuration.LogEventLevel) ImGui.TextColored(GetLogEntryColor(e.LogEventLevel), e.Message); });

            if (EzThrottler.Throttle("AddLogEntries", 25))
                while (_logEntriesToAdd.Count != 0)
                {
                    LogMessage? logEntry = _logEntriesToAdd.Dequeue();
                    if (logEntry == null)
                        return;
                    Plugin.dalamudLogEntries.Add(logEntry);
                }

            if (AutoDuty.Configuration.AutoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                ImGui.SetScrollHereY(1.0f);

            ImGui.EndChild();
        }

        private static Vector4 GetLogEntryColor(LogEventLevel logEntryType) => logEntryType switch
        {
            LogEventLevel.Error => ImGuiColors.DalamudRed,
            LogEventLevel.Warning => ImGuiColors.DalamudOrange,
            _ => ImGuiColors.DalamudWhite,
        };

        private static void DrawUserCodePopup()
        { 
            if (_taskPollResponse != null && _userCode != null)
            {
                if (_taskPollResponse.IsCompletedSuccessfully)
                {
                    _pollResponse = _taskPollResponse.Result;
                    if ((_pollResponse == null || _pollResponse.Access_Token.IsNullOrEmpty()) && EzThrottler.Throttle("Polling", _pollResponse is { Interval: not -1 } ? _pollResponse.Interval * 1100 : _userCode!.Interval * 1100))
                        _taskPollResponse = Task.Run(() => PollResponse(_userCode));
                }
                ImGui.TextColored(ImGuiColors.HealerGreen, Loc.Get("LogTab.Auth.PollingGithub", (_pollResponse != null ? (_pollResponse.Access_Token.IsNullOrEmpty() ? $"{_pollResponse.Error}" : $"{_pollResponse.Access_Token}") : "")));
            }
            else if (_taskUserCode is { IsCompletedSuccessfully: false })
            {
                Vector4 vector4 = new(0, 1, 0, 1);
                ImGui.TextColored(in vector4, Loc.Get("LogTab.Auth.WaitingResponse"));
                return;
            }
            else if (_taskUserCode is { IsCompletedSuccessfully: true })
            {
                _userCode = _taskUserCode.Result;
                _taskUserCode = null;
            }
            else if (_userCode != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.ParsedBlue);
                if (ImGuiEx.Button(Loc.Get("LogTab.Auth.ClickHere")))
                {
                    ImGui.SetClipboardText(_userCode.User_Code);
                    _copied = true;
                }
                ImGui.SameLine();
                ImGui.Text(Loc.Get("LogTab.Auth.ToCopy"));
                ImGui.SameLine();
                Vector4 vector4 = new(0, 1, 0, 1);
                ImGui.TextColored(vector4, _userCode.User_Code);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.SetClipboardText(_userCode.User_Code);
                    _copied = true;
                }
                ImGui.SameLine();
                ImGui.Text(Loc.Get("LogTab.Auth.ToClipboard"));
                using (ImRaii.Disabled(!_copied))
                {
                    if (ImGui.Button(Loc.Get("LogTab.Auth.OpenGithub") + "###OpenUri"))
                    {
                        GenericHelpers.ShellStart($"https://github.com/login/device");
                        if (EzThrottler.Throttle("Polling", _userCode!.Interval * 1100))
                            _taskPollResponse = Task.Run(() => PollResponse(_userCode));
                    }
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    ImGui.Text(Loc.Get("LogTab.Auth.InBrowserPaste"));
                }
            }
        }

        private static void DrawIssuePopup()
        {
            if (_taskSubmitIssue is { IsCompletedSuccessfully: false })
            {
                ImGui.TextColored(ImGuiColors.HealerGreen, Loc.Get("LogTab.IssueForm.Submitting"));
                return;
            }
            else if (_taskSubmitIssue is { IsCompletedSuccessfully: true })
            {
                _popupOpen = false;
                ImGui.CloseCurrentPopup();
                return;
            }
            ImGui.Text(Loc.Get("LogTab.IssueForm.Title"));
            ImGui.Separator();
            ImGui.Text(Loc.Get("LogTab.IssueForm.AddTitle"));
            ImGui.SameLine(0, 5);
            ImGui.TextColored(ImGuiColors.DalamudRed, "*");
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##TitleInput", ref _titleInput, 500);
            ImGui.Separator();
            ImGui.NewLine();
            ImGui.TextWrapped(Loc.Get("LogTab.IssueForm.DuplicateWarning"));
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                GenericHelpers.ShellStart("https://github.com/erdelf/AutoDuty/issues");
            ImGui.NewLine();
            ImGui.TextWrapped(Loc.Get("LogTab.IssueForm.WhatHappened"));
            ImGui.SameLine(0, 5);
            ImGui.TextColored(ImGuiColors.DalamudRed, "*");
            ImGui.TextWrapped(Loc.Get("LogTab.IssueForm.WhatHappenedHelp"));
            ImGui.InputTextMultiline("##WhatHappenedInput", ref _whatHappenedInput, 500, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y / 2.5f));
            ImGui.NewLine();
            ImGui.TextWrapped(Loc.Get("LogTab.IssueForm.ReproSteps"));
            ImGui.SameLine(0, 5);
            ImGui.TextColored(ImGuiColors.DalamudRed, "*");
            ImGui.TextWrapped(Loc.Get("LogTab.IssueForm.ReproStepsHelp"));
            ImGui.InputTextMultiline("##ReproStepsInput", ref _reproStepsInput, 500, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - (ImGui.CalcTextSize("Submit Issue").Y * 3)));
            ImGui.NewLine();
            using (ImRaii.Disabled(_titleInput.Equals("[Bug] ") || _whatHappenedInput.IsNullOrEmpty() || _reproStepsInput.IsNullOrEmpty()))
            {
                if (ImGui.Button(Loc.Get("LogTab.IssueForm.Submit")))
                    if (_pollResponse != null)
                        _taskSubmitIssue = Task.Run(static async () => await FileIssue(_titleInput, _whatHappenedInput, _reproStepsInput, _pollResponse.Access_Token));
            }
        }
    }
}
