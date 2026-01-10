namespace AutoDuty.Windows;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using Helpers;
using Managers;
using NightmareUI.Censoring;

internal static class StatsTab
{

    public static void Draw()
    {
        if (MainWindow.CurrentTabName != "Stats")
            MainWindow.SetCurrentTabName("Stats");

        ConfigurationMain.StatData stats = ConfigurationMain.Instance.stats;

        using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
            {
                ConfigurationMain.Instance.stats = new ConfigurationMain.StatData();
                return;
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

            
        if (!ImGui.BeginTable("##ADDutiesStats", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti, new Vector2(ImGui.GetContentRegionAvail().X, 500f)))
            return;

        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.CompletedAt"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Duration"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Duty"));
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Char"));
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.ilvl"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("StatsTab.Columns.Job"), ImGuiTableColumnFlags.WidthFixed);

        IEnumerable<DutyDataRecord>         records        = stats.dutyRecords;
        IOrderedEnumerable<DutyDataRecord>? recordsOrdered = null;

        ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();

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
            }
        }

        ImGui.TableHeadersRow();

        records = recordsOrdered ?? records;

        foreach ((DateTime completionTime, TimeSpan duration, uint territoryId, ulong cid, int ilvl, Job job) in records)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(completionTime.ToString("yyyy-MM-dd HH:mm:ss"));
            ImGui.TableNextColumn();
            ImGui.Text(duration.ToString(@"mm\:ss\.FFFF"));
            ImGui.TableNextColumn();
            ImGui.Text(ContentHelper.DictionaryContent[territoryId].Name ?? Loc.Get("StatsTab.Unknown", territoryId));
            ImGui.TableNextColumn();
            ImGui.Text(ConfigurationMain.Instance.charByCID.TryGetValue(cid, out ConfigurationMain.CharData cd) ?
                           Censor.Character(cd.Name, cd.World) :
                           string.Empty);

            ImGui.TableNextColumn();
            ImGui.Text(ilvl.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(job.ToString());
        }

        ImGui.EndTable();
    }
}