using System;
using System.Collections.Generic;
using System.Text;

namespace AutoDuty.Logging;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility.Table;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

// Originally from Dalamud
// https://github.com/goatcorp/Dalamud/tree/master/Dalamud/Interface/Utility/Table

// And also from HaselCommon:
// https://github.com/Haselnussbomber/HaselCommon/blob/2b03c1d603931846e4f653893c65154c23f7ce36/HaselCommon/Gui/ImGuiTable/Table.cs

public static class Table
{
    public const float ARROW_WIDTH = 10;
}

public partial class Table<T> : IDisposable
{
    protected List<T>  rows = [];
    protected List<T>? filteredRows;
    protected bool     closePopupNextFrame;

    public string? Id { get; set; }

    public List<T> Rows
    {
        get => this.rows;
        set
        {
            this.rows        =  value;
            this.RowsLoaded  =  true;
            this.IsSortDirty |= true;
        }
    }

    public List<T>?        FilteredRows     => this.filteredRows;
    public bool            RowsLoaded       { get; set; }
    public List<Column<T>> Columns          { get; set; } = [];
    public bool            IsFilterDirty    { get; set; } = true;
    public bool            IsSortDirty      { get; set; } = true;
    public int             ScrollFreezeCols { get; set; } = 1;

    public bool Sortable
    {
        get => this.flags.HasFlag(ImGuiTableFlags.Sortable);
        set => this.flags = value ? this.flags | ImGuiTableFlags.Sortable : this.flags & ~ImGuiTableFlags.Sortable;
    }

    public            bool     ClosePopup       { get; set; }
    protected virtual Vector2? GetCellPadding() => null;

    public ImGuiTableFlags flags = ImGuiTableFlags.RowBg
                                 | ImGuiTableFlags.Sortable
                                 | ImGuiTableFlags.BordersOuter
                                 | ImGuiTableFlags.ScrollY
                                 | ImGuiTableFlags.BordersInnerV
                                 | ImGuiTableFlags.NoBordersInBodyUntilResize;

    private void Initialize()
    {
    }

    public virtual void Dispose()
    {
    }

    public virtual void OnLanguageChanged(string langCode)
    {
        if (this.RowsLoaded)
        {
            this.Rows.Clear();
            this.LoadRows();

            foreach (Column<T> column in this.Columns)
            {
                column.OnLanguageChanged(langCode);
                this.UpdateColumnLabel(column);
            }
        }

        this.IsSortDirty = true;
    }

    public void Draw()
    {
        using ImRaii.IdDisposable id = ImRaii.PushId(this.Id!, !string.IsNullOrEmpty(this.Id));
        this.DrawTableInternal();
    }

    public virtual float CalculateLineHeight() => ImGui.GetTextLineHeightWithSpacing();

    // Override to give a table a fixed outer size (e.g. inside an auto-sizing popup).
    protected virtual Vector2 GetOuterSize() =>
        ImGui.GetContentRegionAvail();

    public virtual void LoadRows()
    {
    }

    private void SortInternal()
    {
        if (!this.Sortable)
            return;

        ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();
        this.IsSortDirty |= sortSpecs.SpecsDirty;

        if (!this.IsSortDirty)
            return;

        if (sortSpecs.SpecsCount == 0)
        {
            if (this.flags.HasFlag(ImGuiTableFlags.SortTristate))
                this.SortTristate();
        }
        else if (this.Columns.Count > 0)
        {
            Column<T> column = this.Columns[this.Columns.Count      <= sortSpecs.Specs.ColumnIndex ? 0 : sortSpecs.Specs.ColumnIndex];
            this.rows.Sort((a, b) => (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending ? -1 : 1) * column.Compare(a, b));
        }

        this.filteredRows    = null;
        sortSpecs.SpecsDirty = this.IsSortDirty = false;
    }

    public virtual void SortTristate()
    {
        throw new NotImplementedException();
    }

    private void DrawTableInternal()
    {
        if (this.Columns.Count == 0)
            return;

        Vector2?                     cellPadding = this.GetCellPadding();
        using ImRaii.StyleDisposable padding     = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, cellPadding ?? Vector2.Zero, cellPadding.HasValue);

        using ImRaii.TableDisposable table = ImRaii.Table("Table", this.Columns.Count, this.flags, this.GetOuterSize());
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(this.ScrollFreezeCols, 1);

        foreach (Column<T> column in this.Columns)
            ImGui.TableSetupColumn(column.Label, column.Flags, column.Width * ImGuiHelpers.GlobalScale);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        if (this.closePopupNextFrame)
        {
            ImGuiP.ClosePopupsExceptModals();
            this.closePopupNextFrame = false;
        }

        if (this.ClosePopup && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            this.ClosePopup          = false;
            this.closePopupNextFrame = true;
        }

        foreach ((int columnIndex, Column<T> column) in this.Columns.Index())
        {
            if (!ImGui.TableSetColumnIndex(columnIndex))
                continue;

            using ImRaii.IdDisposable id = ImRaii.PushId($"ColumnHeader{columnIndex}");

            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
            {
                ImGui.TableHeader(string.Empty);
                ImGui.SameLine();
            }

            if (column.AutoLabel && string.IsNullOrEmpty(column.Label))
                this.UpdateColumnLabel(column);

            this.IsFilterDirty |= column.DrawFilter();
        }

        if (!this.RowsLoaded)
        {
            this.LoadRows();
            this.RowsLoaded = true;
        }

        this.SortInternal();

        if (this.filteredRows == null || this.IsFilterDirty)
        {
            this.filteredRows?.Clear();
            this.filteredRows ??= [];
            this.filteredRows.AddRange(this.rows.Where(row => this.Columns.All(column => column.ShouldShow(row))));

            this.IsFilterDirty = false;
        }

        float lineHeight = this.CalculateLineHeight();
        if (lineHeight == 0)
            foreach ((int index, T row) in this.filteredRows.Index())
                this.DrawRow(row, index);
        else
            ImGuiClip.ClippedDraw(this.filteredRows, this.DrawRow, lineHeight);
    }

    private void DrawRow(T row, int rowIndex)
    {
        int                       column = 0;
        using ImRaii.IdDisposable id     = ImRaii.PushId(rowIndex);
        foreach (Column<T> header in this.Columns)
        {
            id.Push(column++);
            if (ImGui.TableNextColumn())
                header.DrawColumn(row);
            id.Pop();
        }
    }

    private void UpdateColumnLabel<TColumn>(Column<TColumn> column)
    {
        if (column.AutoLabel)
            column.Label = column.LabelKey;
    }
}

// Originally from HaselCommon
// https://github.com/Haselnussbomber/HaselCommon/blob/2b03c1d603931846e4f653893c65154c23f7ce36/HaselCommon/Gui/ImGuiTable/Column.cs
public class Column<T>
{
    public string                Label     { get; set; } = string.Empty;
    public string                LabelKey  { get; set; } = string.Empty;
    public bool                  AutoLabel { get; set; } = true;
    public float                 Width     { get; set; } = -1;
    public ImGuiTableColumnFlags Flags     { get; set; } = ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.WidthStretch;

    public Column()
    {
        Type type = this.GetType();
        this.LabelKey = $"{type.Namespace}.{type.Name}.Label";
    }

    public void SetStretchWidth(float width = 1)
    {
        if (this.Flags.HasFlag(ImGuiTableColumnFlags.WidthFixed))
            this.Flags &= ~ImGuiTableColumnFlags.WidthFixed;

        this.Flags |= ImGuiTableColumnFlags.WidthStretch;
        this.Width =  width;
    }

    public void SetFixedWidth(float width)
    {
        if (this.Flags.HasFlag(ImGuiTableColumnFlags.WidthStretch))
            this.Flags &= ~ImGuiTableColumnFlags.WidthStretch;

        this.Flags |= ImGuiTableColumnFlags.WidthFixed;
        this.Width =  width;
    }

    public virtual bool DrawFilter()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(this.Label);
        return false;
    }

    public virtual bool ShouldShow(T row)
        => true;

    public virtual int Compare(T lhs, T rhs)
        => 0;

    public virtual void DrawColumn(T row)
    {
    }

    public virtual void OnLanguageChanged(string langCode)
    {
    }
}

// Originally from HaselCommon
// https://github.com/Haselnussbomber/HaselCommon/blob/2b03c1d603931846e4f653893c65154c23f7ce36/HaselCommon/Gui/ImGuiTable/ColumnString.cs

public class ColumnString<T> : Column<T>
{
    public ColumnString() => this.Flags &= ~ImGuiTableColumnFlags.NoResize;

    protected Regex? filterRegex;

    public string FilterValue { get; set; } = string.Empty;

    public virtual string ToName(T row)
        => row!.ToString() ?? string.Empty;

    public override int Compare(T lhs, T rhs)
        => string.Compare(this.ToName(lhs), this.ToName(rhs), StringComparison.InvariantCulture);

    public override bool DrawFilter()
    {
        using ImRaii.StyleDisposable style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0);

        ImGui.SetNextItemWidth(-Table.ARROW_WIDTH * ImGuiHelpers.GlobalScale);
        string tmp = this.FilterValue;
        if (!ImGui.InputTextWithHint("##Filter", this.Label, ref tmp, 256, ImGuiInputTextFlags.AutoSelectAll) || tmp == this.FilterValue)
            return false;

        this.FilterValue = tmp;
        try
        {
            this.filterRegex = new Regex(this.FilterValue, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            this.filterRegex = null;
        }

        return true;
    }

    public override bool ShouldShow(T row)
    {
        string name = this.ToName(row);
        if (this.FilterValue.Length == 0)
            return true;

        return this.filterRegex?.IsMatch(name) ?? name.Contains(this.FilterValue, StringComparison.OrdinalIgnoreCase);
    }

    public override void DrawColumn(T row)
    {
        ImGui.Text(this.ToName(row));
    }
}

// Originally from HaselCommon
// https://github.com/Haselnussbomber/HaselCommon/blob/2b03c1d603931846e4f653893c65154c23f7ce36/HaselCommon/Gui/ImGuiTable/ColumnNumber.cs

public class ColumnNumber<T> : ColumnString<T>
{
    public enum ComparisonMethod : byte
    {
        Equal        = 0,
        LessEqual    = 1,
        GreaterEqual = 2
    };

    protected ComparisonMethod comparison = ComparisonMethod.Equal;
    protected int?             filterNumber;

    public virtual int ToValue(T row)
        => row!.GetHashCode();

    public override int Compare(T lhs, T rhs)
        =>
            this.ToValue(lhs).CompareTo(this.ToValue(rhs));

    public override string ToName(T row)
        =>
            this.ToValue(row).ToString();

    public override bool DrawFilter()
    {
        if (!base.DrawFilter())
            return false;

        if (int.TryParse(this.FilterValue, out int number))
        {
            this.filterNumber = number;
            this.filterRegex  = null;
        }
        else
        {
            this.filterNumber = null;
        }

        return true;
    }

    public override bool ShouldShow(T row)
    {
        if (!this.filterNumber.HasValue)
            return base.ShouldShow(row);

        int value = this.ToValue(row);
        return this.comparison switch
        {
            ComparisonMethod.Equal => value        == this.filterNumber.Value,
            ComparisonMethod.LessEqual => value    <= this.filterNumber.Value,
            ComparisonMethod.GreaterEqual => value >= this.filterNumber.Value,
            _ => true
        };
    }
}