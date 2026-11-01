using Dalamud.Bindings.ImGui;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Globalization;
using System.Numerics;

namespace AutoDuty.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class Extensions
    {
        public static void DrawCustomText(this PathAction pathAction, int index, Action? clickedAction)
        {
            ImGui.NewLine();
            GetCustomText(pathAction, index).ForEach(x => TextClicked(x.color, x.text, clickedAction));
        }

        public static List<(Vector4 color, string text)> GetCustomText(this PathAction pathAction, int index)
        {
            List<(Vector4 color, string text)> results = [];

            Vector4 v4 = index == Plugin.indexer ? new Vector4(0, 255, 255, 1) : (pathAction.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase) ? new Vector4(0, 255, 0, 1) : new Vector4(255, 255, 255, 1));

            if (pathAction.Tag.HasFlag(ActionTag.Comment))
            {
                results.Add((new Vector4(0, 1, 0, 1), pathAction.Note));
                return results;
            }
            if (!pathAction.Tag.HasFlag(ActionTag.Revival) && pathAction.Tag != ActionTag.None)
            {
                results.Add((index == Plugin.indexer ? v4 : new Vector4(1, 165 / 255f, 0, 1), $"{pathAction.Tag}"));
                results.Add((v4, " | "));
            }
            results.Add((v4, $"{pathAction.Name}"));
            results.Add((v4, " | "));
            results.Add((v4, $"{pathAction.Position.ToCustomString()}"));
            if (!pathAction.Arguments.All(x => x.IsNullOrEmpty()))
            {
                results.Add((v4, " | "));
                results.Add((v4, $"{pathAction.Arguments.ToCustomString()}"));
            }
            if (!pathAction.Note.IsNullOrEmpty())
            {
                results.Add((v4, " | "));
                results.Add((index == Plugin.indexer ? v4 : new Vector4(0, 1, 0, 1), $"{pathAction.Note}"));
            }

            if (pathAction.Conditions.Count != 0)
                foreach (PathActionCondition condition in pathAction.Conditions)
                {
                    results.Add((v4, " | "));
                    results.AddRange(condition.DrawStepEntry());
                }

            if (pathAction.ConditionsWhile.Count != 0)
            {
                results.Add((v4, " | While:"));
                foreach (PathActionCondition condition in pathAction.ConditionsWhile)
                {
                    results.Add((v4, " | "));
                    results.AddRange(condition.DrawStepEntry());
                }
            }

            if (pathAction.ConditionsUntil.Count != 0)
            {
                results.Add((v4, " | Until:"));
                foreach (PathActionCondition condition in pathAction.ConditionsUntil)
                {
                    results.Add((v4, " | "));
                    results.AddRange(condition.DrawStepEntry());
                }
            }

            return results;
        }

        private static void TextClicked(Vector4 col, string text, Action? clicked)
        {
            ImGui.SameLine(0, 0);
            ImGui.TextColored(col, text);
            if (clicked != null && ImGui.IsItemClicked(ImGuiMouseButton.Left) && Plugin.Stage == 0)
                clicked();
        }

        public static string ToCustomString(this Enum T) => T.ToString().Replace("_", " ") ?? "";

        public static string ToLocalizedString<T>(this T @enum) where T : Enum => 
            string.Join(", ", @enum.ToString().Split(",").Select(flag => Managers.Loc.Get($"ConfigTab.Enums.{typeof(T).Name}.{flag.Trim()}")));

        public static bool StartsWithIgnoreCase(this string str, string strsw) => str.StartsWith(strsw, StringComparison.OrdinalIgnoreCase);

        public static string ToCustomString(this List<string> strings, string delimiter = ",")
        {
            string outString = string.Empty;

            foreach ((string Value, int Index) stringIter in strings.Select((Value, Index) => (Value, Index)))
                outString += (stringIter.Index + 1) < strings.Count ? $"{stringIter.Value}{delimiter}" : $"{stringIter.Value}";

            return outString;
        }

        public static string ToCustomString(this PathAction pathAction) =>
            $"{(pathAction.Tag.HasAnyFlag(ActionTag.None, ActionTag.Treasure, ActionTag.Revival) ? "" : $"{pathAction.Tag.ToCustomString()}|")}{pathAction.Name}|{pathAction.Position.ToCustomString()}{(pathAction.Arguments.All(x => x.IsNullOrEmpty()) ? "" : $"|{pathAction.Arguments.ToCustomString()}")}{(pathAction.Note.IsNullOrEmpty() ? "" : $"|{pathAction.Note}")}";

        public static string ToCustomString(this Vector3 vector3) => vector3.ToString("F2", CultureInfo.InvariantCulture).Trim('<', '>');

        extension(string origin)
        {
            public bool TryGetVector3(out Vector3 vector3)
            {
                vector3 = Vector3.Zero;
                CultureInfo            cul         = CultureInfo.InvariantCulture;
                const StringComparison strComp     = StringComparison.InvariantCulture;
                string[]               splitString = origin.Replace(" ", string.Empty, strComp).Replace("<", string.Empty, strComp).Replace(">", string.Empty, strComp).Split(",");

                if (splitString.Length < 3) 
                    return false;
            
                vector3 = new Vector3(float.Parse(splitString[0], cul), float.Parse(splitString[1], cul), float.Parse(splitString[2], cul));

                return true;
            }
        }


        public static string ToName(this Sounds value) =>
            value switch
            {
                Sounds.None => "None",
                Sounds.Sound01 => "Sound Effect 1",
                Sounds.Sound02 => "Sound Effect 2",
                Sounds.Sound03 => "Sound Effect 3",
                Sounds.Sound04 => "Sound Effect 4",
                Sounds.Sound05 => "Sound Effect 5",
                Sounds.Sound06 => "Sound Effect 6",
                Sounds.Sound07 => "Sound Effect 7",
                Sounds.Sound08 => "Sound Effect 8",
                Sounds.Sound09 => "Sound Effect 9",
                Sounds.Sound10 => "Sound Effect 10",
                Sounds.Sound11 => "Sound Effect 11",
                Sounds.Sound12 => "Sound Effect 12",
                Sounds.Sound13 => "Sound Effect 13",
                Sounds.Sound14 => "Sound Effect 14",
                Sounds.Sound15 => "Sound Effect 15",
                Sounds.Sound16 => "Sound Effect 16",
                _ => "Unknown",
            };

        public static bool IsTrustLeveling(this LevelingMode mode) =>
            mode is LevelingMode.Trust_Group or LevelingMode.Trust_Solo;


        extension(ExternalPlugin plugin)
        {
            public (string url, string name) GetExternalPluginData() =>
                plugin switch
                {
                    ExternalPlugin.vnav => (@"https://puni.sh/api/repository/veyn", "vnavmesh"),
                    ExternalPlugin.BossMod => (@"https://puni.sh/api/repository/veyn", "BossMod"),
                    ExternalPlugin.Avarice => (@"https://love.puni.sh/ment.json", "Avarice"),
                    ExternalPlugin.RotationSolverReborn => (@"https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json", "RotationSolver"),
                    ExternalPlugin.WrathCombo => (@"https://love.puni.sh/ment.json", "WrathCombo"),
                    ExternalPlugin.AutoRetainer => (@"https://love.puni.sh/ment.json", "AutoRetainer"),
                    ExternalPlugin.Gearsetter => (@"https://puni.sh/api/repository/vera", "Gearsetter"),
                    ExternalPlugin.Stylist => (@"https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json", "Stylist"),
                    ExternalPlugin.Lifestream => (@"https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json", "Lifestream"),
                    ExternalPlugin.AntiAFK => (@"https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json", "AntiAfkKick-Dalamud"),
                    ExternalPlugin.Pandora => (@"https://love.puni.sh/ment.json", "PandorasBox"),
                    ExternalPlugin.GlamourLog => (@"https://puni.sh/api/repository/croizat", "GlamourLog"),
                    ExternalPlugin.Knightshopper => (@"https://puni.sh/api/repository/knightmore", "Knightshopper"),
                    _ => throw new ArgumentOutOfRangeException(nameof(plugin), plugin, null)
                };

            public string GetExternalPluginName() =>
                plugin switch
                {
                    ExternalPlugin.vnav => "vnavmesh",
                    ExternalPlugin.BossMod => "Boss Mod",
                    ExternalPlugin.Avarice => "Avarice",
                    ExternalPlugin.RotationSolverReborn => "Rotation Solver Reborn",
                    ExternalPlugin.WrathCombo => "Wrath Combo",
                    ExternalPlugin.AutoRetainer => "AutoRetainer",
                    ExternalPlugin.Gearsetter => "Gearsetter",
                    ExternalPlugin.Stylist => "Stylist",
                    ExternalPlugin.Lifestream => "Lifestream",
                    ExternalPlugin.AntiAFK => "Anti-AfkKick",
                    ExternalPlugin.Pandora => "Pandora's Box",
                    ExternalPlugin.GlamourLog => "Glamour Log",
                    ExternalPlugin.Knightshopper => "Knightshopper",
                    _ => throw new ArgumentOutOfRangeException(nameof(plugin), plugin, null)
                };
        }

        public static bool IsFulfilled(this ConditionType conditionType, params string[] conditionArray)
        {
            switch (conditionType)
            {
                case ConditionType.ItemCount:
                    if (conditionArray.Length > 2)
                        if (uint.TryParse(conditionArray[0], out uint itemId))
                            if (uint.TryParse(conditionArray[2], out uint quantity))
                                if (PathActionCondition.operations.ContainsKey(conditionArray[1]))
                                    return new PathActionConditionItemCount
                                           {
                                               itemId        = itemId,
                                               quantity      = quantity,
                                               operatorValue = conditionArray[1]
                                           }.IsFulfilled();
                    break;
                case ConditionType.ObjectData:
                    if (conditionArray.Length > 3)
                        if(uint.TryParse(conditionArray[0], out uint baseId))
                            if (Enum.TryParse(conditionArray[1], out ObjectDataProperty odp))
                                return new PathActionConditionObjectData
                                       {
                                           baseId   = baseId,
                                           property = odp,
                                           value    = int.TryParse(conditionArray[2], out int value) ? value : bool.TryParse(conditionArray[2], out bool confirm) && confirm ? 1 : 0
                                       }.IsFulfilled();
                    break;
                case ConditionType.Job:
                    if (conditionArray.Length > 0)
                        if (Enum.TryParse(conditionArray[0], out JobWithRole jwr))
                            return new PathActionConditionJob
                                   {
                                       job = jwr
                                   }.IsFulfilled();
                    break;
                case ConditionType.ActionStatus:
                    if (conditionArray.Length > 2)
                        if (Enum.TryParse(conditionArray[0], out ActionType type))
                            if (uint.TryParse(conditionArray[1], out uint id))
                                if (uint.TryParse(conditionArray[2], out uint status))
                                    return new PathActionConditionActionStatus
                                           {
                                               type       = type,
                                               id         = id,
                                               statusCode = status
                                           }.IsFulfilled();
                    break;
                case ConditionType.None:
                case ConditionType.Distance:
                default:
                    break;
            }

            return false;
        }
    }
}
