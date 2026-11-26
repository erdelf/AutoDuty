using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using static AutoDuty.Logging.ADLogging;

namespace AutoDuty.Logging;

using System.Linq;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

internal static class LogTableInfo
{
    public class LogTable : Table<LogEntry>
    {
        private readonly TimeColumn     time     = new();
        private readonly CountColumn    count    = new();
        private readonly LevelColumn    level    = new();
        private readonly CategoryColumn category = new();
        private readonly MessageColumn  message  = new();

        public LogTable()
        {
            List<Column<LogEntry>> headers = [this.time, this.count, this.level, this.category, this.message];

            this.Id       =  "LogTable";
            this.Columns  =  headers;
            this.Sortable =  true;
            this.flags    |= ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        }

        public override void LoadRows()
        {
            this.Rows.Clear();
            this.Rows.AddRange(LogSystem.Logs);
        }

        public void Reload()
        {
            this.Rows.Clear();
            this.LoadRows();
            this.RowsLoaded    = true;
            this.IsFilterDirty = true;
            this.IsSortDirty   = true;
        }
    }

    public sealed class TimeColumn : Column<LogEntry>
    {
        public TimeColumn()
        {
            this.Label = "Time";
            this.Width = ImGui.CalcTextSize("00:00:00 - 00:00:00").X;
            this.Flags = ImGuiTableColumnFlags.WidthFixed;
        }

        public override int Compare(LogEntry lhs, LogEntry rhs)
        {
            DateTime lhsTime = lhs.Count > 1 ? lhs.LastOccurrence : lhs.Timestamp;
            DateTime rhsTime = rhs.Count > 1 ? rhs.LastOccurrence : rhs.Timestamp;
            return lhsTime.CompareTo(rhsTime);
        }

        public override void DrawColumn(LogEntry row)
        {
            if (row.Count > 1)
            {
                ImGui.Text($"{row.Timestamp:HH:mm:ss}");
                ImGui.SameLine();
                ImGui.TextDisabled("-");
                ImGui.SameLine();
                ImGui.Text($"{row.LastOccurrence:HH:mm:ss}");
            }
            else
            {
                ImGui.Text(row.Timestamp.ToString("HH:mm:ss"));
            }
        }
    }

    public sealed class CountColumn : ColumnNumber<LogEntry>
    {
        public CountColumn()
        {
            this.LabelKey = "Count";
            this.SetFixedWidth(ImGui.CalcTextSize("x000").X);
            this.Flags = ImGuiTableColumnFlags.WidthFixed;
        }

        public override int ToValue(LogEntry row) => row.Count;

        public override void DrawColumn(LogEntry row)
        {
            if (row.Count > 1)
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"x{row.Count}");
            else
                ImGui.TextDisabled("1");
        }
    }

    public sealed class LevelColumn : ColumnFlags<LogLevel, LogEntry>
    {
        private LogLevel filterValue;

        public LevelColumn()
        {
            this.Label = "Level";
            this.SetFixedWidth(ImGui.CalcTextSize("Warning").X);
            this.allFlags    = Enum.GetValues<LogLevel>().Aggregate((a, b) => a | b);
            this.filterValue = this.allFlags;
            this.Flags       = ImGuiTableColumnFlags.WidthFixed;
        }

        public override string   NameKeySpace => "ImGuiTable.ColumnLogLevel";
        public override LogLevel FilterValue  => this.filterValue;

        public override void SetValue(LogLevel value, bool enable)
        {
            if (enable)
                this.filterValue |= value;
            else
                this.filterValue &= ~value;
        }

        public override bool ShouldShow(LogEntry row) => this.FilterValue.HasFlag(row.Level);

        public override int Compare(LogEntry lhs, LogEntry rhs)
            => ((int)lhs.Level).CompareTo((int)rhs.Level);

        public override void DrawColumn(LogEntry row)
        {
            Vector4 color = row.Level switch
            {
                LogLevel.Error => new Vector4(1,   0,    0,    1),
                LogLevel.Warning => new Vector4(1, 1,    0,    1),
                LogLevel.Info => new Vector4(0,    1,    1,    1),
                _ => new Vector4(0.7f,             0.7f, 0.7f, 1)
            };
            ImGui.TextColored(color, row.Level.ToString());
        }
    }

    public sealed class CategoryColumn : ColumnString<LogEntry>
    {
        public CategoryColumn()
        {
            this.Label = "Category";
            this.Flags = ImGuiTableColumnFlags.WidthFixed;
        }

        public override string ToName(LogEntry row) => row.Category ?? "";

        public override void DrawColumn(LogEntry row)
        {
            ImGui.Text(row.Category ?? "");
        }
    }

    public sealed class MessageColumn : ColumnString<LogEntry>
    {
        public MessageColumn()
        {
            this.Label = "Message";
            this.Flags = ImGuiTableColumnFlags.WidthStretch;
        }

        public override string ToName(LogEntry row) => row.Message;

        public override void DrawColumn(LogEntry row)
        {
            ImGui.Text(row.Message);
        }
    }
}

// Originally from HaselCommon
// https://github.com/Haselnussbomber/HaselCommon/blob/2b03c1d603931846e4f653893c65154c23f7ce36/HaselCommon/Gui/ImGuiTable/ColumnFlags.cs

public class ColumnFlags<T, TItem> : Column<TItem> where T : struct, Enum
{
    public T allFlags = default;

    public virtual IReadOnlyList<T> Values
        => Enum.GetValues<T>();

    public virtual string NameKeySpace => "";

    public virtual string[] Names
        => Enum.GetNames<T>();

    public virtual T FilterValue
        => default;

    public virtual void SetValue(T value, bool enable)
        => throw new NotImplementedException();

    public override bool DrawFilter()
    {
        using ImRaii.IdDisposable    id    = ImRaii.PushId("##Filter");
        using ImRaii.StyleDisposable style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0);
        ImGui.SetNextItemWidth(-Table.ARROW_WIDTH * ImGuiHelpers.GlobalScale);
        bool                         all   = this.FilterValue.HasFlag(this.allFlags);
        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.FrameBg, 0x803030A0, !all);
        using ImRaii.ComboDisposable combo = ImRaii.Combo(string.Empty, this.Label, ImGuiComboFlags.NoArrowButton);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            this.SetValue(this.allFlags, true);
            return true;
        }

        if (!all && ImGui.IsItemHovered())
            ImGui.SetTooltip("Right-click to clear filters.");

        if (!combo)
            return false;

        color.Pop();

        bool ret = false;
        if (ImGui.Checkbox("Enable All", ref all))
        {
            this.SetValue(this.allFlags, all);
            ret = true;
        }

        using ImRaii.IndentDisposable indent = ImRaii.PushIndent(10f);
        for (int i = 0; i < this.Names.Length; ++i)
        {
            bool tmp = this.FilterValue.HasFlag(this.Values[i]);
            if (!ImGui.Checkbox($"{this.Names[i]}", ref tmp))
                continue;

            this.SetValue(this.Values[i], tmp);
            ret = true;
        }

        return ret;
    }
}