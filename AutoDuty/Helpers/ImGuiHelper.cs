using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using System.Globalization;
using System.Text.RegularExpressions;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using Dalamud.Interface;
    using Dalamud.Interface.Colors;
    using Dalamud.Interface.Utility;
    using Dalamud.Interface.Utility.Raii;
    using ECommons.DalamudServices;
    using ECommons.ImGuiMethods;
    using IPC;
    using System;
    using System.Linq;
    using System.Numerics;

    internal static class ImGuiHelper
    {
        public static readonly Vector4 ExperimentalColor  = new(1, 0, 1, 1);
        public static readonly Vector4 ExperimentalColor2 = new(1, 0.6f, 0, 1);
        public static readonly Vector4 VersionColor       = new(0, 1, 1, 1);
        public static readonly Vector4 LinkColor          = new(0, 200, 238, 1);

        public static readonly Vector4 White         = new(1, 1, 1, 1);
        public static readonly Vector4 MaxLevelColor = new(0.5f, 1, 0.5f, 1);

        public static readonly Vector4 RoleTankColor       = new(0, 0.8f, 1, 1);
        public static readonly Vector4 RoleHealerColor     = new(0, 1, 0, 1);
        public static readonly Vector4 RoleDPSColor        = new(1, 0, 0, 1);
        public static readonly Vector4 RoleAllRounderColor = new(1, 1, 0.5f, 1);

        public static readonly Vector4 StateGoodColor = new(0, 1, 0, 1);
        public static readonly Vector4 StateBadColor  = new(1, 0, 0, 1);

        public static Vector4 AccentRed { get; } = new(0.85f, 0.35f, 0.35f, 1f);


        public const string idColor               = "<0.5,0.5,1>";
        public const string dutyColor             = "<0,1,0>";
        public const string pathFileColor         = "<0.8,0.8,0.8>";
        public const string pathFileColorNoUpdate = "<0,1,1>";

        public static void ColoredText(string text)
        {
            Match regex = RegexHelper.ColoredTextRegex().Match(text);
            ColoredText(regex, text);
        }

        public static void ColoredText(Match regex, string backupText)
        {
            void SameLine() => ImGui.SameLine(0, 0);

            if (regex.Success)
            {
                bool first = true;

                do
                {
                    bool nonColoredSet = false;

                    //ADLogging.Debug(string.Join(" | ", regex.Groups.Values.Select(g=> g.Value)));

                    string nonColored = regex.Groups[1].Value;
                    if (!nonColored.IsNullOrEmpty())
                    {
                        if (!first)
                            SameLine();

                        first = false;
                        ImGui.Text(nonColored);
                        nonColoredSet = true;
                        //ADLogging.Debug("non colored: " + nonColored);
                    }

                    string colorText   = regex.Groups[2].Value;
                    string coloredText = regex.Groups[3].Value;
                    if (!colorText.IsNullOrEmpty() && !coloredText.IsNullOrEmpty())
                    {
                        string[] split = colorText.Split(',');
                        if (split.Length >= 3)
                            if (float.TryParse(split[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float r))
                                if (float.TryParse(split[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float g))
                                    if (float.TryParse(split[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float b))
                                    {
                                        float a = 1;
                                        if (split.Length == 4 && float.TryParse(split[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out a))
                                        {
                                        }

                                        if (nonColoredSet)
                                            SameLine();
                                        else if (!first)
                                            SameLine();

                                        first = false;

                                        Vector4 color = new(r, g, b, a);
                                        ImGui.TextColored(color, coloredText);
                                        //ADLogging.Debug("colored: " + coloredText + " in: " + color);
                                    }
                    }

                    regex = regex.NextMatch();
                } while (regex.Success);
            }
            else
            {
                ImGui.Text(backupText);
            }
        }

        internal static void CenterNextElement(float percentWidth, float xIndent = 0)
        {
            float buttonWidth = ImGui.GetContentRegionAvail().X * percentWidth;
            ImGui.SetCursorPosX(xIndent + (ImGui.GetContentRegionAvail().X - buttonWidth) / 2f);
        }

        internal static void CenterNextElement(float width)
        {
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - width) / 2f);
        }

        internal static bool CenteredButton(string label, float percentWidth, float xIndent = 0)
        {
            float buttonWidth = ImGui.GetContentRegionAvail().X * percentWidth;
            ImGui.SetCursorPosX(xIndent + (ImGui.GetContentRegionAvail().X - buttonWidth) / 2f);
            return ImGui.Button(label, new Vector2(buttonWidth, 35f));
        }

        internal static void DrawIcon(FontAwesomeIcon icon)
        {
            using ImRaii.FontDisposable font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.SetItemAllowOverlap();
            ImGui.Text(icon.ToIconString());
            ImGui.SameLine();
        }

        internal static void HelpMarker(string text, FontAwesomeIcon icon = FontAwesomeIcon.InfoCircle, Vector4? color = null)
        {
            using ImRaii.ColorDisposable? col = ImRaii.PushColor(ImGuiCol.TextDisabled, color);
            ImGui.SameLine();

            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextDisabled(icon.ToIconString());

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                using (ImRaii.Tooltip())
                {
                    using (ImRaii.TextWrapPos(ImGui.GetFontSize() * 35.0f))
                        ImGui.Text(text);
                }
        }

        public static Vector4 GetRandomColor(this string input)
        {
            uint hash = 5381;
            foreach (char c in input)
                hash = (hash << 5) + hash + c;
            float hue = hash % 360 / 360.0f;

            float r = 0, g = 0, b = 0;
            ImGui.ColorConvertHSVtoRGB(hue, 0.7f, 0.9f, ref r, ref g, ref b);
            return new Vector4(r, g, b, 1.0f);
        }

        internal static EndUnconditionally RequiresPlugin(ExternalPlugin plugins, string id, string? message = null, bool inline = false, bool write = true)
        {
            if (plugins == ExternalPlugin.None)
                return new EndUnconditionally(() => { }, true);

            ExternalPlugin[] pluginsArray = plugins.GetFlags();

            if (pluginsArray.All(plugin => IPCSubscriber_Common.IsReady(plugin.GetExternalPluginData().name) || (plugin == ExternalPlugin.BossMod && IPCSubscriber_Common.IsReady("BossModReborn"))))
            {
                return new EndUnconditionally(() =>
                                              {
                                                  if (!write)
                                                      return;

                                                  if (inline)
                                                      ImGui.SameLine();
                                                  ImGui.Text($"{(inline ? "| " : "\t")}powered by ");
                                                  ImGui.SameLine(0, 1);
                                                  ImGui.TextColored(LinkColor, string.Join(", ", pluginsArray.Select(plugin => plugin.GetExternalPluginName())));
                                              }, true);
            }
            else
            {
                ImRaii.DisabledDisposable disabled = ImRaii.Disabled();

                ImGui.AlignTextToFramePadding();
                return new EndUnconditionally(() =>
                                              {
                                                  disabled.Dispose();

                                                  if (!write)
                                                      return;

                                                  ImGui.AlignTextToFramePadding();
                                                  if (inline)
                                                      ImGui.SameLine();
                                                  ImGui.Text(message ?? $"{(inline ? "| " : "\t")} requires ");
                                                  ImGui.SameLine(0, 1);
                                                  ImGui.TextColored(LinkColor, string.Join(", ", pluginsArray.Select(plugin => plugin.GetExternalPluginName())));

                                                  ImGui.SameLine(0, 5);
                                                  if (ImGui.Button($"Install##InstallExternalPlugin_{plugins}_{id}"))
                                                      PluginInstaller.InstallPlugin(plugins);
                                              }, false);
            }
        }


        public struct EndUnconditionally(Action endAction, bool success) : IDisposable
        {
            public bool    Success   => success;
            public Action? EndAction => endAction;

            private bool Disposed { get; set; } = false;

            public void Dispose()
            {
                if (this.Disposed)
                    return;

                this.EndAction?.Invoke();
                this.Disposed = true;
            }
        }


        /// <summary>
        /// Compute the distance between two strings.
        /// </summary>
        public static int ComputeLevenshteinDistance(string s, string t)
        {
            int    n = s.Length;
            int    m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Step 1
            if (n == 0)
                return m;

            if (m == 0)
                return n;

            // Step 2
            for (int i = 0; i <= n; d[i, 0] = i++)
            {
            }

            for (int j = 0; j <= m; d[0, j] = j++)
            {
            }

            // Step 3
            for (int i = 1; i <= n; i++)
            {
                //Step 4
                for (int j = 1; j <= m; j++)
                {
                    // Step 5
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    // Step 6
                    d[i, j] = Math.Min(
                                       Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                       d[i - 1, j - 1] + cost);
                }
            }
            // Step 7
            return d[n, m];
        }
    }

    public static class DateWidget
    {
        private const int HeightInItems = 1 + 1 + 1 + 4 + 1;
        private static readonly DateTime Sample = DateTime.UnixEpoch;

        private static readonly Vector4 Transparent = new(1, 1, 1, 0);
        private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        private static readonly string[] MonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
        private static readonly int[] NumDaysPerMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

        private static float LongestMonthWidth;
        private static readonly float[] MonthWidths = new float[12];

        private static uint LastOpenComboID;

        public static bool Validate(DateTime minimal, ref DateTime currentMin, ref DateTime currentMax, DateTime maximum)
        {
            bool needsRefresh, needsRefreshEver = false;

            if(minimal > maximum)
                minimal = maximum - TimeSpan.FromSeconds(1);

            int i = 0;
            do
            {
                i++;


                needsRefresh = false;
                if (minimal > currentMin)
                {
                    currentMin   = minimal;
                    needsRefresh = true;
                }
                else if (currentMin > currentMax)
                {
                    currentMin   = currentMax.Subtract(TimeSpan.FromDays(1));
                    needsRefresh = true;
                } else if (maximum < currentMax)
                {
                    currentMax = maximum;
                    needsRefresh = true;
                }

                needsRefreshEver |= needsRefresh;

                if (i > 100)
                {
                    needsRefresh = false;
                    currentMin   = minimal;
                    currentMax   = maximum;
                    ADLogging.Warning("I give up..");
                }
            } while (needsRefresh);

            return needsRefreshEver;
        }

        public static string DateFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " " + CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;

        public static bool DatePickerWithInput(string label, int id, ref string dateString, ref DateTime date, out bool isOpen)
        {
            isOpen = false;
            var ret = false;
            var format = DateFormat;
            ImGui.SetNextItemWidth(ImGui.CalcTextSize(Sample.ToString(format)).X + ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (ImGui.InputTextWithHint($"##{label}Input", format.ToUpper(), ref dateString, 32, ImGuiInputTextFlags.CallbackCompletion))
            {
                if (DateTime.TryParseExact(dateString, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tmp))
                {
                    date = tmp;
                }
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ret = true;
            }

            ImGui.SameLine(0, 3.0f * ImGuiHelpers.GlobalScale);

            ImGuiEx.IconButton(FontAwesomeIcon.Calendar, id.ToString());
            if (DatePicker(label, ref date, false, isOpen: out isOpen))
            {
                ret = true;
                dateString = date.ToString(format);
            }

            return ret;
        }

        private static bool DatePicker(string label, ref DateTime dateOut, bool closeWhenMouseLeavesIt, out bool isOpen, string leftArrow = "", string rightArrow = "")
        {
            isOpen = false;
            using var mono = ImRaii.PushFont(UiBuilder.MonoFont);
            if (LongestMonthWidth == 0.0f)
            {
                for (var i = 0; i < 12; i++)
                {
                    var mw = ImGui.CalcTextSize(MonthNames[i]).X;

                    MonthWidths[i] = mw;
                    LongestMonthWidth = Math.Max(LongestMonthWidth, mw);
                }
            }

            var id = ImGui.GetID(label);
            var style = ImGui.GetStyle();

            var arrowLeft = leftArrow.Length > 0 ? leftArrow : "<";
            var arrowRight = rightArrow.Length > 0 ? rightArrow : ">";
            var arrowLeftWidth = ImGui.CalcTextSize(arrowLeft).X;
            var arrowRightWidth = ImGui.CalcTextSize(arrowRight).X;

            var labelSize = ImGui.CalcTextSize(label, true, 0);

            var widthRequiredByCalendar = (2.0f * arrowLeftWidth) + (2.0f * arrowRightWidth) + LongestMonthWidth + ImGui.CalcTextSize("9999").X + (120.0f * ImGuiHelpers.GlobalScale);
            var popupHeight = ((labelSize.Y + (2 * style.ItemSpacing.Y)) * HeightInItems) + (style.FramePadding.Y * 3);

            var valueChanged = false;
            ImGui.SetNextWindowSize(new Vector2(widthRequiredByCalendar, widthRequiredByCalendar));
            ImGui.SetNextWindowSizeConstraints(new Vector2(widthRequiredByCalendar, popupHeight + 40), new Vector2(widthRequiredByCalendar, popupHeight + 40));

            using var popupItem = ImRaii.ContextPopupItem(label, ImGuiPopupFlags.None);
            if (!popupItem.Success)
                return valueChanged;
            isOpen = true;

            if (ImGui.GetIO().MouseClicked[1])
            {
                // reset date when user right-clicks the date chooser header when the dialog is open
                dateOut = DateTime.Now;
            }
            else if (LastOpenComboID != id)
            {
                LastOpenComboID = id;
                if (dateOut.Year == 1)
                    dateOut = DateTime.Now;
            }

            using var windowPadding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, style.FramePadding);
            using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, Transparent);

            ImGui.Spacing();

            var yearString = $"{dateOut.Year}";
            var yearPartWidth = arrowLeftWidth + arrowRightWidth + ImGui.CalcTextSize(yearString).X;

            var oldWindowRounding = style.WindowRounding;
            style.WindowRounding = 0;

            using (ImRaii.PushId(1234))
            {
                if (ImGui.SmallButton(arrowLeft))
                {
                    valueChanged = true;
                    dateOut = dateOut.AddMonths(-1);
                }

                ImGui.SameLine();

                var color = ImGui.GetColorU32(style.Colors[(int)ImGuiCol.Text]);
                var monthWidth = MonthWidths[dateOut.Month - 1];
                var pos = ImGui.GetCursorScreenPos();
                pos = pos with { X = pos.X + ((LongestMonthWidth - monthWidth) * 0.5f) };

                ImGui.GetForegroundDrawList().AddText(pos, color, MonthNames[dateOut.Month - 1]);

                ImGui.SameLine(0, LongestMonthWidth + style.ItemSpacing.X * 2);

                if (ImGui.SmallButton(arrowRight))
                {
                    valueChanged = true;
                    dateOut = dateOut.AddMonths(1);
                }
            }

            ImGui.SameLine(ImGui.GetWindowWidth() - yearPartWidth - style.WindowPadding.X - style.ItemSpacing.X * 4.0f);

            using (ImRaii.PushId(1235))
            {
                if (ImGui.SmallButton(arrowLeft))
                {
                    valueChanged = true;
                    dateOut = dateOut.AddYears(-1);
                }

                ImGui.SameLine();
                ImGui.Text($"{dateOut.Year}");
                ImGui.SameLine();

                if (ImGui.SmallButton(arrowRight))
                {
                    valueChanged = true;
                    dateOut = dateOut.AddYears(1);
                }
            }

            ImGui.Spacing();

            // This could be calculated only when needed (but I guess it's fast in any case...)
            var maxDayOfCurMonth = NumDaysPerMonth[dateOut.Month - 1];
            if (maxDayOfCurMonth == 28)
            {
                var year = dateOut.Year;
                var bis = ((year % 4) == 0) && ((year % 100) != 0 || (year % 400) == 0);
                if (bis)
                    maxDayOfCurMonth = 29;
            }

            using var buttonHovered = ImRaii.PushColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudOrange);
            using var buttonActive = ImRaii.PushColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudYellow);

            ImGui.Separator();

            // Display items
            var dayClicked = false;
            var dayOfWeek = (int)new DateTime(dateOut.Year, dateOut.Month, 1).NormalDayOfWeek();
            for (var dw = 0; dw < 7; dw++)
            {
                using (ImRaii.Group())
                {
                    using var textColor = ImRaii.PushColor(ImGuiCol.Text, CalculateTextColor(), dw == 0);

                    ImGui.Text($"{(dw == 0 ? "" : " ")}{DayNames[dw]}");
                    if (dw == 0)
                        ImGui.Separator();
                    else
                        ImGui.Spacing();

                    // Use dayOfWeek for spacing
                    var curDay = dw - dayOfWeek;
                    for (var row = 0; row < 7; row++)
                    {
                        var cday = curDay + (7 * row);
                        if (cday >= 0 && cday < maxDayOfCurMonth)
                        {
                            using var rowId = ImRaii.PushId(row * 10 + dw);
                            if (ImGui.SmallButton(string.Format(cday < 9 ? " {0}" : "{0}", cday + 1)))
                            {
                                ImGui.SetItemDefaultFocus();

                                valueChanged = true;
                                dateOut = new DateTime(dateOut.Year, dateOut.Month, cday + 1, dateOut.Hour, dateOut.Minute, dateOut.Second);
                            }
                        }
                        else
                        {
                            ImGui.TextUnformatted(" ");
                        }
                    }

                    if (dw == 0)
                        ImGui.Separator();
                }

                if (dw != 6)
                    ImGui.SameLine(ImGui.GetWindowWidth() - (6 - dw) * (ImGui.GetWindowWidth() / 7.0f));
            }

            if (ImGui.GetCursorPos().X > ImGui.GetContentRegionMax().X / 2) ImGui.NewLine();

            var hours = dateOut.Hour;
            var minutes = dateOut.Minute;
            ImGui.SetNextItemWidth(ImGui.CalcTextSize("000").X + ImGui.GetFrameHeightWithSpacing() * 2);
            if (ImGui.InputInt("##dtHours", ref hours))
            {
                hours = Math.Clamp(hours, 0, 23);
                valueChanged = true;
                dateOut = new DateTime(dateOut.Year, dateOut.Month, dateOut.Day, hours, dateOut.Minute, dateOut.Second);
            }

            ImGui.SameLine(0, 1);
            ImGuiEx.TextV(":");
            ImGui.SameLine(0, 1);
            ImGui.SetNextItemWidth(ImGui.CalcTextSize("000").X + ImGui.GetFrameHeightWithSpacing() * 2);
            if (ImGui.InputInt("##dtMinutes", ref minutes))
            {
                hours = Math.Clamp(minutes, 0, 23);
                valueChanged = true;
                dateOut = new DateTime(dateOut.Year, Math.Clamp(dateOut.Month, 1, 12), dateOut.Day, 
                                       Math.Clamp(dateOut.Hour, 0, 24), Math.Clamp(minutes, 0, 59), Math.Clamp(dateOut.Second, 0, 59));
            }

            style.WindowRounding = oldWindowRounding;

            var mustCloseCombo = dayClicked;
            if (closeWhenMouseLeavesIt && !mustCloseCombo)
            {
                var distance = ImGui.GetFontSize() * 1.75f; //1.3334f; //24;
                var pos = ImGui.GetWindowPos();
                pos.X -= distance;
                pos.Y -= distance;
                var size = ImGui.GetWindowSize();
                size.X += 2.0f * distance;
                size.Y += 2.0f * distance;
                var mousePos = ImGui.GetIO().MousePos;
                if (mousePos.X < pos.X || mousePos.Y < pos.Y || mousePos.X > pos.X + size.X || mousePos.Y > pos.Y + size.Y)
                    mustCloseCombo = true;
            }

            // ImGui issue #273849, children keep popups from closing automatically
            if (mustCloseCombo)
                ImGui.CloseCurrentPopup();

            return valueChanged;
        }

        private static Vector4 CalculateTextColor()
        {
            var textColor = ImGuiColors.DalamudGrey;
            var l = (textColor.X + textColor.Y + textColor.Z) * 0.33334f;
            return new Vector4(l * 2.0f > 1 ? 1 : l * 2.0f, l * .5f, l * .5f, textColor.W);
        }

        public static NormalDayOfWeek NormalDayOfWeek(this DateTime date)
        {
            var ndow = (int)date.DayOfWeek - 1;
            if (ndow == -1) ndow = 6;
            return (NormalDayOfWeek)ndow;
        }
    }

    //
    // Summary:
    //     Specifies the day of the week.
    public enum NormalDayOfWeek
    {
        //
        // Summary:
        //     Indicates Monday.
        Monday = 0,

        //
        // Summary:
        //     Indicates Tuesday.
        Tuesday = 1,

        //
        // Summary:
        //     Indicates Wednesday.
        Wednesday = 2,

        //
        // Summary:
        //     Indicates Thursday.
        Thursday = 3,

        //
        // Summary:
        //     Indicates Friday.
        Friday = 4,

        //
        // Summary:
        //     Indicates Saturday.
        Saturday = 5,

        //
        // Summary:
        //     Indicates Sunday.
        Sunday = 6,
    }
}