namespace AutoDuty.Windows;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Configurations;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Data;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using ECommons.Throttlers;
using Helpers;
using Managers;
using NightmareUI.Censoring;

internal static class StatsTab
{
    private static ConfigurationMain.StatData? filteredStats;
    private static List<DutyDataRecord>?      sortedRecords;

    private static readonly List<uint> territoryFilter       = [];

    private static string territoryFilterSearch             = string.Empty;
    private static bool   territoryFilterSearchSelectedOnly = false;


    private static readonly List<ulong> charFilter = [];
    
    private static          string     charFilterSearch             = string.Empty;
    private static          bool       charFilterSearchSelectedOnly = false;

    private static JobWithRole jobFilter = JobWithRole.All;

    private static int ilvlFilterMin = 0;
    private static int ilvlFilterMax = 999;

    private static readonly DateTime dateTimeMin = new(2025, 12, 25);

    private static DateTime dateTimeFilterMinDate  = dateTimeMin;
    private static string   dateTimeFilterMinInput = dateTimeFilterMinDate.ToString(DateWidget.DateFormat);


    private static readonly DateTime dateTimeMax = DateTime.UtcNow.AddDays(1);

    private static DateTime dateTimeFilterMaxDate  = dateTimeMax;
    private static string   dateTimeFilterMaxInput = dateTimeFilterMaxDate.ToString(DateWidget.DateFormat);

    private static TimeSpan durationFilterMin     = TimeSpan.Zero;
    private static string   durationFilterMinTemp = durationFilterMin.ToString(TimeSpanFormat);

    private static TimeSpan durationFilterMax     = TimeSpan.FromHours(2);
    private static string   durationFilterMaxTemp = durationFilterMax.ToString(TimeSpanFormat);

    private static int deathsFilterMin = 0;
    private static int deathsFilterMax = 99;

    private const string TimeSpanFormat = @"hh\:mm\:ss\.FFFF";

    public static bool refilter = false;

    public static void Draw()
    {
        ConfigurationMain.StatData stats = filteredStats ??= ConfigurationMain.Instance.stats;

        ImGuiIOPtr io = ImGui.GetIO();

        using (ImRaii.Disabled(!io.KeyCtrl))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
            {
                ConfigurationMain.Instance.stats = new ConfigurationMain.StatData();
                refilter                         = true;
            }
        }
        ImGui.SameLine();
        ImGui.Text(Loc.Get("StatsTab.ClearStatistics"));
        ImGuiComponents.HelpMarker(Loc.Get("StatsTab.ClearStatisticsHelp"));


        ImGui.Text(Loc.Get("StatsTab.DutiesRun", stats.dungeonsRun));
        ImGui.Text(Loc.Get("StatsTab.TimeSpent", stats.timeSpent + (Plugin.runStartTime.Equals(DateTime.UnixEpoch) ? TimeSpan.Zero : DateTime.UtcNow - Plugin.runStartTime)));

        ImGui.Separator();
        ImGui.Text(Loc.Get("StatsTab.DutiesRunHeader"));

        ImGui.Checkbox(Loc.Get("StatsTab.ScrambleNames"), ref Censor.Config.Enabled);

            
        if (!ImGui.BeginTable("##ADDutiesStats", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti, new Vector2(ImGui.GetContentRegionAvail().X, 500f)))
            return;

        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.CompletedAt"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Duration"),    ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Duty"));
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Char"));
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.ilvl"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Job"),  ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Deaths"),  ImGuiTableColumnFlags.WidthFixed);

        IEnumerable<DutyDataRecord> records = stats.dutyRecords;

        ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();

        // Re-sort only when table data, filters, or specs change.
        if (sortedRecords == null || refilter || sortSpecs.SpecsDirty)
        {
            IOrderedEnumerable<DutyDataRecord>? recordsOrdered = null;

            for (int i = 0; i < sortSpecs.SpecsCount; i++)
            {
                ImGuiTableColumnSortSpecs spec = sortSpecs.Specs[i];

                if(spec.SortDirection == ImGuiSortDirection.None)
                    continue;

                void Order(Func<DutyDataRecord, object> func)
                {
                    recordsOrdered = recordsOrdered != null ?
                                         (spec.SortDirection & ImGuiSortDirection.Ascending) != 0 ? recordsOrdered.ThenBy(func) : recordsOrdered.ThenByDescending(func) :
                                         (spec.SortDirection & ImGuiSortDirection.Ascending) != 0 ? records.OrderBy(func) : records.OrderByDescending(func);
                }

                switch (spec.ColumnIndex)
                {
                    case 0:
                        Order(ddr => ddr.CompletionTime);
                        break;
                    case 1:
                        Order(ddr => ddr.Duration);
                        break;
                    case 2:
                        Order(ddr => ddr.TerritoryId);
                        break;
                    case 3:
                        Order(ddr => ddr.CID);
                        break;
                    case 4:
                        Order(ddr => ddr.ilvl);
                        break;
                    case 5:
                        Order(ddr => ddr.Job);
                        break;
                    case 6:
                        Order(ddr => ddr.Deaths ?? -1);
                        break;
                }
            }

            sortedRecords        = (recordsOrdered ?? records).ToList();
            sortSpecs.SpecsDirty = false;
        }

        ImGui.TableHeadersRow();

        records = sortedRecords!;
    
    #region filters
        ConfigurationMain.StatData unfilteredRecords = ConfigurationMain.Instance.stats;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        if (DateWidget.DatePickerWithInput("##StatsTabFilterDateMin", 0, ref dateTimeFilterMinInput, ref dateTimeFilterMinDate, out bool _))
            refilter = true;
        if (DateWidget.DatePickerWithInput("##StatsTabFilterDateMax", 1, ref dateTimeFilterMaxInput, ref dateTimeFilterMaxDate, out bool _))
            refilter = true;

        if (DateWidget.Validate(dateTimeMin, ref dateTimeFilterMinDate, ref dateTimeFilterMaxDate, dateTimeMax))
        {
            dateTimeFilterMinInput = dateTimeFilterMinDate.ToString(DateWidget.DateFormat);
            dateTimeFilterMaxInput = dateTimeFilterMaxDate.ToString(DateWidget.DateFormat);

            refilter = true;
        }

        ImGui.TableNextColumn();
        TimeSpan durationFilterAdjust = new(0, 0, io.KeyShift ? 60 : io.KeyCtrl ? 10 : 1);

        if (ImGui.InputText("##DurationFilterMinText", ref durationFilterMinTemp))
        {
            if (TimeSpan.TryParseExact(durationFilterMinTemp, TimeSpanFormat, CultureInfo.CurrentCulture, out TimeSpan durationNew))
            {
                durationFilterMin = durationNew;
                refilter          = true;
            } else
            {
                durationFilterMinTemp = durationFilterMin.ToString(TimeSpanFormat);
            }
        }
        ImGui.SameLine();
        if (ImGuiEx.IconButton(FontAwesomeIcon.Minus, "DurationFilterMinMinus"))
        {
            TimeSpan durationNew = durationFilterMin.Subtract(durationFilterAdjust);
            durationFilterMin     = durationNew <= TimeSpan.Zero ? TimeSpan.Zero : durationNew;
            durationFilterMinTemp = durationFilterMin.ToString(TimeSpanFormat);
            refilter              = true;
        }
        ImGui.SameLine();
        if (ImGuiEx.IconButton(FontAwesomeIcon.Plus, "DurationFilterMinPlus"))
        {
            TimeSpan durationNew = durationFilterMin.Add(durationFilterAdjust);
            durationFilterMin     = durationNew.TotalHours > 2 ? new TimeSpan(2, 0, 0) : durationNew;
            durationFilterMinTemp = durationFilterMin.ToString(TimeSpanFormat);
            refilter              = true;
        }


        if (ImGui.InputText("##DurationFilterMaxText", ref durationFilterMaxTemp))
        {
            if (TimeSpan.TryParseExact(durationFilterMaxTemp, TimeSpanFormat, CultureInfo.CurrentCulture, out TimeSpan durationNew))
            {
                durationFilterMax = durationNew;
                refilter          = true;
            }
            else
            {
                durationFilterMaxTemp = durationFilterMax.ToString(TimeSpanFormat);
            }
        }
        ImGui.SameLine();
        if (ImGuiEx.IconButton(FontAwesomeIcon.Minus, "DurationFilterMaxMinus"))
        {
            TimeSpan durationNew = durationFilterMax.Subtract(durationFilterAdjust);
            durationFilterMax     = durationNew <= TimeSpan.Zero ? TimeSpan.Zero : durationNew;
            durationFilterMaxTemp = durationFilterMax.ToString(TimeSpanFormat);
            refilter              = true;
        }
        ImGui.SameLine();
        if (ImGuiEx.IconButton(FontAwesomeIcon.Plus, "DurationFilterMaxPlus"))
        {
            TimeSpan durationNew = durationFilterMax.Add(durationFilterAdjust);
            durationFilterMax     = durationNew.TotalHours > 2 ? new TimeSpan(2, 0, 0) : durationNew;
            durationFilterMaxTemp = durationFilterMax.ToString(TimeSpanFormat);
            refilter              = true;
        }


        ImGui.TableNextColumn();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        using (ImRaii.ComboDisposable endObject = ImRaii.Combo("##StatsTabFilterTerritory", territoryFilter.Count is >= 1 or 0 ? 
                                                                                                   Loc.Get("StatsTab.SelectedMultiCount", territoryFilter.Count) :
                                                                                                   TerritoryName(territoryFilter.First())))
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                territoryFilter.Clear();
                refilter = true;
            }

            if (endObject.Success)
            {
                ImGui.InputText("##StatsTabFilterTerritorySearch", ref territoryFilterSearch, 100);
                ImGui.SameLine();
                ImGui.Checkbox($"{Loc.Get("StatsTab.FilterOnlySelected")}##StatsTabFilterTerritorySearchSelectedOnly", ref territoryFilterSearchSelectedOnly);

                foreach (uint u in unfilteredRecords.dutyRecords.Select(ddr => ddr.TerritoryId).Distinct())
                {
                    string label    = $"({u}) {TerritoryName(u)}";
                    bool   filtered = territoryFilter.Contains(u);
                    if ((!territoryFilterSearchSelectedOnly || filtered)                                                        &&
                        (territoryFilterSearch.Length == 0  || label.Contains(territoryFilterSearch, StringComparison.InvariantCultureIgnoreCase)) &&
                        ImGui.Checkbox(label + $"##StatsTabFilterTerritoryLabel{u}", ref filtered))

                    {
                        if (!territoryFilter.Remove(u))
                            territoryFilter.Add(u);

                        refilter = true;
                    }
                }
            }
        }
        ImGui.TableNextColumn();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        using (ImRaii.ComboDisposable endObject = ImRaii.Combo("##StatsTabFilterChar", charFilter.Count is >= 1 or 0 ?
                                                                                           Loc.Get("StatsTab.SelectedMultiCount", charFilter.Count) :
                                                                                           CharName(charFilter.First())))
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                charFilter.Clear();
                refilter = true;
            }

            if (endObject.Success)
            {
                ImGui.InputText("##StatsTabFilterCharSearch", ref charFilterSearch, 100);
                ImGui.SameLine();
                ImGui.Checkbox($"{Loc.Get("StatsTab.FilterOnlySelected")}##StatsTabFilterCharSearchSelectedOnly", ref charFilterSearchSelectedOnly);

                foreach (ulong cid in unfilteredRecords.dutyRecords.Select(ddr => ddr.CID).Distinct())
                {
                    string label    = CharName(cid);
                    bool   filtered = charFilter.Contains(cid);
                    if ((!charFilterSearchSelectedOnly || filtered)                                                                      &&
                        (charFilterSearch.Length == 0  || label.Contains(charFilterSearch, StringComparison.InvariantCultureIgnoreCase)) &&
                        ImGui.Checkbox(label + $"##StatsTabFilterCharLabel{cid}", ref filtered))

                    {
                        if (!charFilter.Remove(cid))
                            charFilter.Add(cid);

                        refilter = true;
                    }
                }
            }
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        using (ImRaii.ComboDisposable endObject = ImRaii.Combo("##StatsTabFilterIlvl", $"{ilvlFilterMin} - {ilvlFilterMax}"))
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                ilvlFilterMin = 0;
                ilvlFilterMax = 999;
                   refilter = true;
            }
            if (endObject.Success)
                if (ImGui.DragIntRange2("##StatsTabFilterIlvl", ref ilvlFilterMin, ref ilvlFilterMax, 10, 0, 999))
                    refilter = true;
        }
        ImGui.TableNextColumn();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        using (ImRaii.ComboDisposable endObject = ImRaii.Combo("##StatsTabFilterJob", jobFilter.ToLocalizedString()))
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                jobFilter = JobWithRole.All;
                refilter = true;
            }

            if (endObject.Success)
            {
                if(JobWithRoleHelper.DrawCategory(JobWithRole.All, ref jobFilter))
                    refilter = true;
            }
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        using (ImRaii.ComboDisposable endObject = ImRaii.Combo("##StatsTabFilterDeaths", $"{deathsFilterMin} - {deathsFilterMax}"))
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                deathsFilterMin = 0;
                deathsFilterMax = 999;
                refilter        = true;
            }
            if (endObject.Success)
                if (ImGui.DragIntRange2("##StatsTabFilterDeaths", ref deathsFilterMin, ref deathsFilterMax, 1, 0, 999))
                    refilter = true;
        }

        if(EzThrottler.Throttle("StatsTabAutomaticRefilter", 60_000))
            refilter = true;

        if (refilter)
        {
            filteredStats = ConfigurationMain.Instance.stats.Filter(ddr =>
                                                                        (territoryFilter.Count == 0 || territoryFilter.Contains(ddr.TerritoryId)) &&
                                                                        (charFilter.Count      == 0 || charFilter.Contains(ddr.CID))              &&
                                                                        jobFilter.HasJob(ddr.Job)                                                 &&

                                                                        ddr.ilvl           >= ilvlFilterMin         && ddr.ilvl           <= ilvlFilterMax         &&
                                                                        ddr.CompletionTime >= dateTimeFilterMinDate && ddr.CompletionTime <= dateTimeFilterMaxDate &&
                                                                        ddr.Duration       >= durationFilterMin     && ddr.Duration       <= durationFilterMax     &&
                                                                        (ddr.Deaths == null || ddr.Deaths >= deathsFilterMin && ddr.Deaths <= deathsFilterMax));
            sortedRecords = null;
            refilter = false;
        }

#endregion

        foreach ((DateTime completionTime, TimeSpan duration, uint territoryId, ulong cid, int ilvl, Job job, int? deaths) in records)
            // ReSharper restore PossibleMultipleEnumeration
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text((Censor.Config.Enabled ? completionTime : completionTime.ToLocalTime()).ToString("yyyy-MM-dd HH:mm:ss"));
            ImGui.TableNextColumn();
            ImGui.Text(duration.ToString(TimeSpanFormat));
            ImGui.TableNextColumn();
            ImGui.Text(TerritoryName(territoryId));
            ImGui.TableNextColumn();
            ImGui.Text(CharName(cid));

            ImGui.TableNextColumn();
            ImGui.Text(ilvl.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(job.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(deaths?.ToString() ?? "??");
        }

        ImGui.EndTable();
    }

    private static string TerritoryName(uint tt)
    {
        string? text = null;
        if (ContentHelper.DictionaryContent.TryGetValue(tt, out Classes.Content? content))
            text = content.Name;

        return text ?? Loc.Get("StatsTab.Unknown", tt);
    }

    private static string CharName(ulong cid) =>
        ConfigurationMain.Instance.charByCID.TryGetValue(cid, out ConfigurationMain.CharData cd) ?
            Censor.Character(cd.Name, cd.World) :
            string.Empty;
}
