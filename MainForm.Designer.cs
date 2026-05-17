#nullable enable
using System.ComponentModel;

namespace FileExplorer;

partial class MainForm
{
    private IContainer? components;
    private SplitContainer splitContainer = null!;
    private TreeView treeView = null!;
    private ListView listView = null!;
    private ToolStrip toolStrip = null!;
    private ToolStripButton btnRefresh = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private ToolStripStatusLabel scanStatusLabel = null!;
    private ToolStripProgressBar progressBar = null!;
    private ToolStripStatusLabel statusSizeLabel = null!;
    private ImageList imageList = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();

        // ── Image list ───────────────────────────────────────────────────────
        imageList = new ImageList(components)
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize  = new Size(16, 16)
        };
        imageList.Images.Add(Icons.Drive());   // 0 = drive
        imageList.Images.Add(Icons.Folder());  // 1 = folder
        imageList.Images.Add(Icons.File());    // 2 = file

        // ── Tool strip ───────────────────────────────────────────────────────
        toolStrip  = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        btnRefresh = new ToolStripButton("⟳  Refresh  [F5]")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText  = "Reload drives and current folder (F5)"
        };
        btnRefresh.Click += Btn_Refresh_Click;
        toolStrip.Items.Add(btnRefresh);

        // ── Tree view ────────────────────────────────────────────────────────
        treeView = new TreeView
        {
            Dock          = DockStyle.Fill,
            HideSelection = false,
            ImageList     = imageList,
            ShowRootLines = true,
            ShowLines     = true,
            ShowPlusMinus = true,
            Font          = new Font("Segoe UI", 9f)
        };
        treeView.BeforeExpand += TreeView_BeforeExpand;
        treeView.AfterSelect  += TreeView_AfterSelect;

        // ── List view ────────────────────────────────────────────────────────
        listView = new ListView
        {
            Dock               = DockStyle.Fill,
            View               = View.Details,
            FullRowSelect      = true,
            GridLines          = true,
            SmallImageList     = imageList,
            AllowColumnReorder = true,
            Sorting            = SortOrder.None,
            Font               = new Font("Segoe UI", 9f)
        };
        listView.Columns.Add("Name",          300);
        listView.Columns.Add("Date Modified", 155);
        listView.Columns.Add("Size",          110);
        listView.Columns.Add("Type",          140);
        listView.ColumnClick += ListView_ColumnClick;
        listView.DoubleClick += ListView_DoubleClick;

        // ── Split container ─────────────────────────────────────────────────
        splitContainer = new SplitContainer
        {
            Dock          = DockStyle.Fill,
            SplitterWidth = 5
        };
        splitContainer.Panel1.Controls.Add(treeView);
        splitContainer.Panel2.Controls.Add(listView);

        // ── Status strip ─────────────────────────────────────────────────────
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel("Ready")
        {
            Spring    = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        scanStatusLabel = new ToolStripStatusLabel("Processing")
        {
            TextAlign   = ContentAlignment.MiddleRight,
            BorderSides = ToolStripStatusLabelBorderSides.Left,
            Width       = 160
        };
        progressBar = new ToolStripProgressBar
        {
            Minimum     = 0,
            Maximum     = 100,
            Value       = 0,
            Size        = new Size(160, 16),
            Style       = ProgressBarStyle.Continuous,
            ToolTipText = "Scan progress for the selected drive"
        };
        statusSizeLabel = new ToolStripStatusLabel("")
        {
            TextAlign   = ContentAlignment.MiddleRight,
            BorderSides = ToolStripStatusLabelBorderSides.Left
        };
        statusStrip.Items.Add(statusLabel);
        statusStrip.Items.Add(scanStatusLabel);
        statusStrip.Items.Add(progressBar);
        statusStrip.Items.Add(statusSizeLabel);

        // ── Form ─────────────────────────────────────────────────────────────
        SuspendLayout();
        Controls.Add(splitContainer);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);
        ClientSize  = new Size(1200, 800);
        MinimumSize = new Size(700, 450);
        Text        = "File Explorer";
        ResumeLayout(false);
        PerformLayout();
    }
}

// ── Inline icon factory ───────────────────────────────────────────────────────
internal static class Icons
{
    public static Bitmap Drive()
    {
        var b = new Bitmap(16, 16);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        g.FillRectangle(Brushes.Silver, 1, 7, 14, 7);
        g.DrawRectangle(Pens.DimGray,   1, 7, 14, 7);
        using var dark = new SolidBrush(Color.DimGray);
        g.FillEllipse(dark, 3, 9, 3, 3);
        g.FillEllipse(dark, 9, 9, 3, 3);
        using var blue = new SolidBrush(Color.FromArgb(0, 120, 215));
        g.FillRectangle(blue, 6, 8, 4, 2);
        return b;
    }

    public static Bitmap Folder()
    {
        var b = new Bitmap(16, 16);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        using var tab  = new SolidBrush(Color.FromArgb(200, 175, 80));
        using var body = new SolidBrush(Color.FromArgb(255, 215, 100));
        using var edge = new Pen(Color.FromArgb(180, 140, 50));
        g.FillRectangle(tab,  1, 5,  5, 2);
        g.FillRectangle(body, 1, 6, 14, 8);
        g.DrawRectangle(edge, 1, 6, 14, 8);
        return b;
    }

    public static Bitmap File()
    {
        var b = new Bitmap(16, 16);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        Point[] poly = [new(2,1), new(10,1), new(13,4), new(13,14), new(2,14)];
        g.FillPolygon(Brushes.White, poly);
        g.DrawPolygon(Pens.Gray,  poly);
        g.DrawLine(Pens.LightSteelBlue, 10, 1, 10, 4);
        g.DrawLine(Pens.LightSteelBlue, 10, 4, 13, 4);
        return b;
    }
}
