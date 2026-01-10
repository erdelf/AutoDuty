using AutoDuty.Data;
using AutoDuty.Helpers;
using AutoDuty.Managers;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Pictomancy;
using System.Diagnostics;
using System.Numerics;
using static AutoDuty.Managers.ContentPathsManager;
using static AutoDuty.Windows.MainWindow;

namespace AutoDuty.Windows
{
    using System;
    using System.Collections.Generic;
    using Dalamud.Interface;
    using Dalamud.Utility.Numerics;
    using Newtonsoft.Json;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    internal static class BuildTab
    {
        internal static List<(string, string, string)>? ActionsList { get; set; }

        private static          bool                     _scrollBottom      = false;
        private static          string                   _changelog         = string.Empty;
        private static          PathAction?              _action            = null;
        private static          string                   _actionText        = string.Empty;
        private static          string                   _note              = string.Empty;
        private static          Vector3                  _position          = Vector3.Zero;
        //private static          string                   _positionText      = string.Empty;
        private static          List<string>              _arguments          = [];
        private static          string                    _argumentHint       = string.Empty;
        private static          bool                      _showAddActionUI    = false;
        private static          (string, string, string)  _dropdownSelected   = (string.Empty, string.Empty, string.Empty);
        private static          int                       _buildListSelected  = -1;
        private static          string                    _addActionButton    = "Add";
        private static          bool                      _dragDrop           = false;
        private static          bool                      _noArgument         = false;
        private static          bool                      _comment            = false;
        private static          Vector4                   _argumentTextColor  = new(1,1,1,1);
        private static          bool                      _deleteItem         = false;
        private static          int                       _deleteItemIndex    = -1;
        private static          bool                      _duplicateItem      = false;
        private static          int                       _duplicateItemIndex = -1;
        private static          List<PathActionCondition> _conditions         = [];
        private static          ActionTag                 _actionTag;
        private static readonly ActionTag[]               _actionTags           = [ActionTag.None, ActionTag.Synced, ActionTag.Unsynced, ActionTag.W2W, ActionTag.Treasure];

        internal static unsafe void Draw()
        {
            if (MainWindow.CurrentTabName != "Build")
                MainWindow.SetCurrentTabName("Build");

            using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Navigating) || Plugin.states.HasFlag(PluginState.Looping)))
            {
                if (InDungeon)
                {
                    DrawPathElements();
                    DrawSeperator();
                    DrawButtons();
                    DrawSeperator();
                }
                DrawBuildList();
            }
        }

        private static void DrawSeperator()
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        private static void DrawPathElements()
        {
            using ImRaii.IEndObject? d = ImRaii.Disabled(!InDungeon || Plugin.Stage > 0 || !Player.Available);
            ImGui.Text(Loc.Get("BuildTab.BuildPath", Svc.ClientState.TerritoryType, ContentHelper.DictionaryContent.TryGetValue(Svc.ClientState.TerritoryType, out Classes.Content? content) ? content.Name : TerritoryName.GetTerritoryName(Svc.ClientState.TerritoryType)));

            ImGui.AlignTextToFramePadding();
            string idText = $"({Svc.ClientState.TerritoryType}) ";
            ImGui.Text(idText);
            ImGui.SameLine();
            string path = Path.GetFileName(Plugin.pathFile).Replace(idText, string.Empty).Replace(".json", string.Empty);
            string pathOrg = path;

            Vector2 textL = ImGui.CalcTextSize(".json");
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - textL.Length());
            if (ImGui.InputText("##BuildPathFileName", ref path, 100) && !path.Equals(pathOrg))
                Plugin.pathFile = $"{Plugin.pathsDirectory.FullName}{Path.DirectorySeparatorChar}{idText}{path}.json";

            ImGui.SameLine(0);
            ImGui.Text(Loc.Get("BuildTab.JsonExtension"));

            ImGui.AlignTextToFramePadding();
            ImGui.Text(Loc.Get("BuildTab.Changelog"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##Changelog", ref _changelog, 200);
        }

        private static void DrawButtons()
        {
            if (ImGui.Button(Loc.Get("BuildTab.AddPos")))
            {
                _scrollBottom = true;
                Plugin.Actions.Add(new PathAction { Name = "MoveTo", Position = Player.Position });
            }
            ImGui.SameLine(0, 5);
            ImGuiComponents.HelpMarker(Loc.Get("BuildTab.AddPosTooltip"));
            if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.AddAction")))
            {
                if (_showAddActionUI)
                    ClearAll();
                ImGui.OpenPopup("AddActionPopup");
            }
            ImGuiComponents.HelpMarker(Loc.Get("BuildTab.AddActionTooltip"));
            if (ImGui.BeginPopup("AddActionPopup"))
            {
                if (ActionsList == null)
                    return;

                foreach ((string, string, string) item in ActionsList)
                {
                    if (ImGui.Selectable(item.Item1))
                    {
                        _dropdownSelected  = item;
                        _buildListSelected = -1;
                        _argumentHint      = item.Item2.Equals("false", StringComparison.InvariantCultureIgnoreCase) ? string.Empty : item.Item2;
                        _actionText        = item.Item1;
                        _noArgument        = item.Item2.Equals("false", StringComparison.InvariantCultureIgnoreCase);
                        _addActionButton   = Loc.Get("BuildTab.Add");
                        _comment           = item.Item1.Equals("<-- Comment -->", StringComparison.InvariantCultureIgnoreCase);
                        _position          = Player.Available ? Player.Position : Vector3.Zero;
                        _actionTag         = ActionTag.None;
                        _conditions        = [];

                        switch (item.Item1)
                        {
                            case "<-- Comment -->":
                                _actionTag = ActionTag.Comment;
                                _position = Vector3.Zero;
                                break;
                            case "Revival":
                                _actionTag = ActionTag.Revival;
                                _position = Vector3.Zero;
                                break;
                            case "TreasureCoffer":
                                _actionTag = ActionTag.Treasure;
                                break;
                            case "ExitDuty":
                                _position = Vector3.Zero;
                                break;
                            case "SelectYesno":
                                _arguments = ["Yes"];
                                break;
                            case "MoveToObject":
                            case "Interactable":
                            case "Target":
                                IGameObject? targetObject = Player.Object?.TargetObject;
                                IGameObject? gameObject = (targetObject ?? null) ?? ClosestObject;
                                _arguments = [gameObject != null ? $"{gameObject.BaseId}" : string.Empty];
                                _note = gameObject != null ? gameObject.Name.GetText() : string.Empty;
                                break;
                            case "Boss":
                                IGameObject? gameObject2   = Player.Object?.TargetObject;
                                _note = gameObject2 != null ? gameObject2.Name.GetText() : string.Empty;
                                break;
                            default:
                                break;
                        }
                        _action          = new PathAction { Name = _actionText, Position = _position, Arguments = _arguments, Note = _note, Tag = _actionTag };
                        _showAddActionUI = true;
                    }
                    ImGuiComponents.HelpMarker(item.Item3);
                }
                ImGui.EndPopup();
            }
            if (_showAddActionUI && !ImGui.IsPopupOpen($"Add Action: ({_action?.Name})###AddActionUI"))
            {
                ImGui.SetNextWindowSize(new Vector2(ImGui.CalcTextSize("X").X * 55,                                   ImGui.GetTextLineHeight() * 7), ImGuiCond.FirstUseEver);
                ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
                ImGui.OpenPopup($"Add Action: ({_action?.Name})###AddActionUI");
            }
            if (ImGui.BeginPopupModal($"Add Action: ({_action?.Name})###AddActionUI", ref _showAddActionUI))
            {
                DrawAddActionUIPopup();
                ImGui.EndPopup();
            }
            ImGui.SameLine(0, 5);
            if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.ClearPath")))
            {
                Plugin.Actions.Clear();
                ClearAll();
            }
            ImGuiComponents.HelpMarker(Loc.Get("BuildTab.ClearPathTooltip"));
            ImGui.SameLine(0, 5);
            if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.SavePath")))
                try
                {
                    if (Plugin.Actions.Count < 1)
                    {
                        Svc.Log.Error(Loc.Get("BuildTab.SavePathError"));
                        return;
                    }
                    Svc.Log.Info(Loc.Get("BuildTab.SavingPath", Plugin.pathFile));

                    PathFile? pathFile = null;

                    if (DictionaryPaths.TryGetValue(Svc.ClientState.TerritoryType, out ContentPathContainer? container))
                    {
                        DutyPath? dutyPath = container.Paths.FirstOrDefault(dp => dp.FilePath == Plugin.pathFile);
                        if (dutyPath != null)
                        {
                            pathFile = dutyPath.PathFile;
                            if (pathFile.Meta.LastUpdatedVersion < Plugin.Version || _changelog.Length > 0)
                            {
                                pathFile.Meta.Changelog.Add(new PathFileChangelogEntry
                                                            {
                                                                Version = Plugin.Version,
                                                                Change  = _changelog
                                                            });
                                _changelog = string.Empty;
                            }
                        }
                    }

                    pathFile ??= new PathFile();

                    pathFile.Actions = [.. Plugin.Actions];
                    string json = JsonConvert.SerializeObject(pathFile, ConfigurationMain.JsonSerializerSettings);
                    File.WriteAllText(Plugin.pathFile, json);
                    Plugin.currentPath = 0;
                }
                catch (Exception e)
                {
                    Svc.Log.Error(e.ToString());
                    //throw;
                }

            ImGuiComponents.HelpMarker(Loc.Get("BuildTab.SavePathTooltip"));
            ImGui.SameLine(0, 5);
            if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.LoadPath")))
            {
                Plugin.LoadPath();
                ClearAll();
            }
            ImGuiComponents.HelpMarker(Loc.Get("BuildTab.LoadPathTooltip"));
            ImGui.SameLine(0, 5);
            using(ImRaii.Enabled())
            {
                using (ImRaii.Disabled(Plugin.pathFile.IsNullOrEmpty()))
                {
                    if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.OpenFile")))
                        Process.Start("explorer",  Plugin.pathFile ?? string.Empty);
                }
            }
        }

        private static unsafe void DrawAddActionUIPopup()
        {
            if (_action == null)
            {
                _showAddActionUI = false;
                ImGui.CloseCurrentPopup();
                return;
            }

            using (ImRaii.Disabled(_arguments.Count == 0 && !_noArgument && !_comment))
            {
                if (ImGuiEx.ButtonWrapped(_addActionButton))
                {
                    if (_action.Name is "MoveToObject" or "Target" or "Interactable")
                    {
                        if (uint.TryParse(_arguments[0], out uint dataId))
                            AddAction();
                        else
                            ShowPopup(Loc.Get("BuildTab.ErrorTitle"), Loc.Get("BuildTab.DataIdError", _action.Name), true);
                    }
                    else
                    {
                        AddAction();
                    }
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(_buildListSelected < 0))
            {
                if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.Delete")))
                {
                    _deleteItem = true;
                    _deleteItemIndex = _buildListSelected;
                    ClearAll();
                }

                ImGui.SameLine();
                if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.CopyToClipboard")))
                    ImGui.SetClipboardText(_action?.ToCustomString());
                if (Plugin.isDev)
                {
                    ImGui.SameLine();
                    using (ImRaii.Disabled(!Player.Available || _action == null))
                    {
                        if (ImGuiEx.ButtonWrapped(Loc.Get("BuildTab.TeleportTo")))
                            Player.GameObject->SetPosition(_action!.Position.X, _action.Position.Y, _action.Position.Z);
                    }
                }
            }
            if (!(_noArgument || _comment))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(_argumentTextColor, Loc.Get("BuildTab.Arguments"));
                ImGui.SameLine();
                ImGui.TextColored(_argumentTextColor, _argumentHint);
                ImGui.SameLine();
                float addX = ImGui.GetCursorPosX();
                if (ImGui.Button(Loc.Get("BuildTab.Plus") + "##AddArgument")) _arguments.Add(string.Empty);

                for (int i = 0; i < _arguments.Count; i++)
                {
                    ImGui.PushID(i);

                    using (ImRaii.Disabled(i <= 0))
                    {
                        if (ImGui.Button(Loc.Get("BuildTab.Up") + "##MoveUp"))
                            (_arguments[i], _arguments[i - 1]) = (_arguments[i - 1], _arguments[i]);
                    }

                    ImGui.SameLine();
                    using (ImRaii.Disabled(i >= _arguments.Count - 1))
                    {
                        if (ImGui.Button(Loc.Get("BuildTab.Down") + "##MoveDown"))
                            (_arguments[i], _arguments[i + 1]) = (_arguments[i + 1], _arguments[i]);
                    }

                    ImGui.SameLine();
                    using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
                    {
                        if (ImGui.Button(Loc.Get("BuildTab.Remove") + "##RemoveArgument"))
                        {
                            _arguments.RemoveAt(i);
                            i--;
                        }
                    }

                    ImGui.SameLine();
                    ImGui.SetCursorPosX(addX);
                    if (ImGui.Button(Loc.Get("BuildTab.Plus") + "##AddArgument"))
                        _arguments.Insert(i + 1, string.Empty);
                    ImGui.SameLine();

                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    string tempArgument = _arguments[i];
                    if (ImGui.InputText($"##Argument{i}", ref tempArgument, 200)) 
                        _arguments[i] = tempArgument;
                    ImGui.PopID();
                }
                ImGui.Spacing();
            }


            if (!_comment)
            {
                if (ImGui.Button(Loc.Get("BuildTab.Position")))
                    _position = (_position - Player.Position).LengthSquared() <= 0.1f ? Vector3.Zero : Player.Position;

                ImGui.SameLine();
                ImGui.PushItemWidth(145 * ImGuiHelpers.GlobalScale);
                //ImGui.InputText("##Position", ref _positionText, 200);
                ImGui.InputFloat("X##PositionX", ref _position.X, 0.1f, 1f);
                ImGui.SameLine();
                ImGui.InputFloat("Y##PositionY", ref _position.Y, 0.1f, 1f);
                ImGui.SameLine();
                ImGui.InputFloat("Z##PositionZ", ref _position.Z, 0.1f, 1f);


            }
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Loc.Get("BuildTab.Note"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##Note", ref _note, 200);
            using (ImRaii.Disabled(_action == null || _action.Tag.HasAnyFlag(ActionTag.Comment, ActionTag.Revival, ActionTag.Treasure)))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.Text(Loc.Get("BuildTab.Tag"));
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.BeginCombo("##TagSelection", _actionTag.HasAnyFlag(ActionTag.None, ActionTag.Synced, ActionTag.Unsynced) ? _actionTag.ToCustomString() : ActionTag.None.ToCustomString()))
                {
                    foreach (ActionTag actionTag in _actionTags)
                    {
                        bool selected = _actionTag.HasFlag(actionTag);
                        if (ImGui.Selectable(actionTag.ToCustomString(), selected))
                            if (selected)
                                _actionTag &= ~actionTag;
                            else
                                _actionTag |= actionTag;
                    }
                    ImGui.EndCombo();
                }
            }

            if (!_comment)
            {
                ImGui.Separator();
                ImGui.Text(Loc.Get("BuildTab.Conditions"));

                int delIndex = -1;
                for (int index = 0; index < _conditions.Count; index++)
                {
                    PathActionCondition condition = _conditions[index];
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
                        delIndex = index;
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(Loc.Get("BuildTab.Condition"));
                    ImGui.SameLine();
                    condition.DrawConfig();
                    ImGui.Separator();
                }

                if(delIndex >= 0)
                    _conditions.RemoveAt(delIndex);


                ConditionType newConditionType = ConditionType.None;
                if (ImGuiEx.EnumCombo(Loc.Get("BuildTab.AddNewCondition"), ref newConditionType))
                {
                    if (newConditionType != ConditionType.None)
                    {
                        _conditions.Add(newConditionType switch
                        {
                            ConditionType.Distance => new PathActionConditionDistance(),
                            ConditionType.ItemCount => new PathActionConditionItemCount(),
                            ConditionType.ObjectData => new PathActionConditionObjectData(),
                            ConditionType.Job => new PathActionConditionJob(),
                            ConditionType.ActionStatus => new PathActionConditionActionStatus(),
                            _ => throw new ArgumentOutOfRangeException()
                        });
                    }
                }
            }
        }

        private static void DrawBuildList()
        {
            if (!ImGui.BeginListBox("##BuildList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) 
                return;

            try
            {
                if (InDungeon)
                {
                    int? dragIndex = null;
                    int? dragNext  = null;


                    foreach ((PathAction Value, int Index) item in Plugin.Actions.Select((value, index) => (Value: value, Index: index)))
                    {
                        Vector4 v4 = item.Value.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase) ? new Vector4(0, 255, 0, 1) : new Vector4(255, 255, 255, 1);

                        ImGui.PushStyleColor(ImGuiCol.Text, v4);

                        if (ImGui.Selectable($"{item.Index}: ###Text{item.Index}", item.Index == _buildListSelected))
                        {
                            if (_buildListSelected == item.Index)
                            {
                                ClearAll();
                            }
                            else
                            {
                                _comment = item.Value.Name.Equals($"<-- Comment -->", StringComparison.InvariantCultureIgnoreCase);
                                _noArgument = (ActionsList?.Any(x => x.Item1.Equals($"{item.Value.Name}", StringComparison.InvariantCultureIgnoreCase) &&
                                                                     x.Item2.Equals("false", StringComparison.InvariantCultureIgnoreCase)) ?? false); // || item.Value.Name.Equals("MoveTo", StringComparison.InvariantCultureIgnoreCase);
                                _actionText = item.Value.Name;
                                _note       = item.Value.Note;
                                _arguments  = [..item.Value.Arguments];
                                _position   = item.Value.Position;
                                //_positionText      = _position.ToCustomString();
                                _buildListSelected = item.Index;
                                _showAddActionUI   = true;
                                _dropdownSelected  = ("", "", "");
                                _addActionButton   = Loc.Get("BuildTab.Modify");
                                _action            = item.Value;
                                _actionTag         = item.Value.Tag;
                                _conditions        = [..item.Value.Conditions];
                            }
                        }

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            _deleteItem      = true;
                            _deleteItemIndex = item.Index;
                        }

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
                        {
                            _duplicateItem      = true;
                            _duplicateItemIndex = item.Index;
                        }

                        if (ImGui.IsItemActive() && !ImGui.IsItemHovered() && !_dragDrop)
                            _buildListSelected = item.Index;

                        if (_buildListSelected == item.Index && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !_showAddActionUI)
                        {
                            float mouseYDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y;

                            if (MathF.Abs(mouseYDelta) > ImGui.GetTextLineHeight())
                            {
                                _dragDrop = true;
                                dragIndex = item.Index;
                                dragNext  = item.Index + (mouseYDelta < 0f ? -1 : 1);
                            }
                        }

                        item.Value.GetCustomText(item.Index).ForEach(x =>
                                                                     {
                                                                         ImGui.SameLine(0, 0);
                                                                         ImGui.TextColored(x.color, x.text);
                                                                     });
                        ImGui.PopStyleColor();
                    }

                    if (dragIndex.HasValue && dragNext is >= 0 && dragNext < Plugin.Actions.Count)
                    {
                        (Plugin.Actions[dragNext.Value], Plugin.Actions[dragIndex.Value]) = (Plugin.Actions[dragIndex.Value], Plugin.Actions[dragNext.Value]);
                        _buildListSelected                                                = dragNext.Value;
                        ImGui.ResetMouseDragDelta();
                    }
                    else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !_showAddActionUI)
                    {
                        _dragDrop          = false;
                        _buildListSelected = -1;
                    }

                    if (_deleteItem)
                    {
                        Plugin.Actions.RemoveAt(_deleteItemIndex);
                        _deleteItemIndex = -1;
                        _deleteItem      = false;
                    }

                    if (_duplicateItem)
                    {
                        Plugin.Actions.Insert(_duplicateItemIndex, Plugin.Actions[_duplicateItemIndex].JSONClone());
                        _duplicateItem = false;
                    }
                }
                else
                {
                    ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), Loc.Get("BuildTab.NotInDungeonMessage"));
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex.ToString());
            }

            if (_scrollBottom)
            {
                ImGui.SetScrollHereY(1.0f);
                _scrollBottom = false;
            }
            ImGui.EndListBox();
        }

        private static void ClearAll()
        {
            _actionText        = string.Empty;
            _note              = string.Empty;
            _position          = Vector3.Zero;
            _arguments         = [];
            _argumentHint      = string.Empty;
            _dropdownSelected  = (string.Empty, string.Empty, string.Empty);
            _showAddActionUI   = false;
            _noArgument        = false;
            _addActionButton   = Loc.Get("BuildTab.Add");
            _buildListSelected = -1;
            _action            = null;
            _comment           = false;
            _actionTag         = ActionTag.None;
            _conditions        = [];
        }

        private static void AddAction()
        {
            if (_action == null) return;

            _action.Name = _actionText;
            _action.Arguments = [.. _arguments];
            _action.Tag = _actionTag;
            _action.Position = !_comment ? _position : Vector3.Zero;
            _action.Note = _comment && !_note.StartsWith("<--") && !_note.EndsWith("-->") ? $"<-- {_note} -->" : _note;
            _action.Conditions = [.. _conditions];
            if (_buildListSelected == -1)
            {
                Plugin.Actions.Add(_action);
                _scrollBottom = true;
            }
            else
            {
                Plugin.Actions[_buildListSelected] = _action;
            }

            ImGui.CloseCurrentPopup();
            ClearAll();
        }

        public static void DrawHelper(PctDrawList drawList)
        {
            if (_showAddActionUI && _position.LengthSquared() > 0.1f)
            {
                drawList.AddCircle(_position, 1.25f, 0xFFFFFFFF, thickness: 2);
                drawList.AddText(_position + Vector3.UnitZ * 1.1f, 0xFFFFFFFF, Loc.Get("BuildTab.PlusZ"), 5f);
                drawList.AddText(_position + Vector3.UnitZ * -1.1f, 0xFFFFFFFF, Loc.Get("BuildTab.MinusZ"), 5f);

                drawList.AddText(_position + Vector3.UnitX * 1.1f,  0xFFFFFFFF, Loc.Get("BuildTab.PlusX"), 5f);
                drawList.AddText(_position + Vector3.UnitX * -1.1f, 0xFFFFFFFF, Loc.Get("BuildTab.MinusX"), 5f);

                if(PlayerHelper.IsValid)
                {
                    float playerY   = Player.Position.Y;
                    float ydiff = (_position.Y - playerY);
                    if (MathF.Abs(ydiff) > 0.1f)
                    {
                        drawList.AddText(_position + Vector3.UnitY * MathF.Sign(ydiff), 0xFFFFFFFF, Loc.Get("BuildTab.YDiff") + ydiff.ToString("F3", CultureInfo.CurrentCulture), 5f);
                        
                        drawList.PathLineTo(_position);
                        drawList.PathLineTo(_position.WithY(playerY));
                        drawList.PathStroke(0xFFFFFFFF);
                    }
                }

                drawList.PathLineTo(_position - Vector3.UnitX);
                drawList.PathLineTo(_position + Vector3.UnitX);
                drawList.PathStroke(0xFFFFFFFF);
                drawList.PathLineTo(_position - Vector3.UnitZ);
                drawList.PathLineTo(_position + Vector3.UnitZ);
                drawList.PathStroke(0xFFFFFFFF);
            }
        }
    }
}
