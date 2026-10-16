namespace Goam.App;

/// <summary>
/// The song list: a native DataGridView in virtual mode, so only the rows on screen are ever created or
/// painted - 300 songs cost the same as 10. Double-buffered to avoid flicker.
/// </summary>
internal sealed class SongGrid : DataGridView
{
    public const int PreviewColumn = 0;
    public const int NumberColumn = 1;
    public const int NameColumn = 2;
    public const int SelectColumn = 3;
    public const int RenameColumn = 4;
    public const int DeleteColumn = 5;

    public SongGrid()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        VirtualMode = true;
        ReadOnly = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToResizeColumns = false;
        AllowUserToOrderColumns = false;
        MultiSelect = false;
        RowHeadersVisible = false;
        ColumnHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.None;
        EnableHeadersVisualStyles = false;
        BackgroundColor = Theme.Panel;
        GridColor = Theme.Panel;
        RowTemplate.Height = 38;
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        ScrollBars = ScrollBars.Vertical;
        StandardTab = true;

        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            SelectionBackColor = Theme.PanelAlt,
            SelectionForeColor = Theme.Text,
            Font = Theme.Ui(10f),
            Padding = new Padding(4, 0, 4, 0),
            WrapMode = DataGridViewTriState.False,
        };

        Columns.Add(ButtonColumn("preview", 52));
        Columns.Add(TextColumn("number", 56, DataGridViewContentAlignment.MiddleRight));
        var name = TextColumn("name", 100, DataGridViewContentAlignment.MiddleLeft);
        name.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        name.MinimumWidth = 120;
        Columns.Add(name);
        Columns.Add(ButtonColumn("select", 118));
        Columns.Add(ButtonColumn("rename", 104));
        Columns.Add(ButtonColumn("delete", 94));
    }

    private static DataGridViewButtonColumn ButtonColumn(string name, int width) => new()
    {
        Name = name,
        Width = width,
        FlatStyle = FlatStyle.Flat,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        Resizable = DataGridViewTriState.False,
        DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
    };

    private static DataGridViewTextBoxColumn TextColumn(string name, int width, DataGridViewContentAlignment align) => new()
    {
        Name = name,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        Resizable = DataGridViewTriState.False,
        DefaultCellStyle = { Alignment = align },
    };
}
