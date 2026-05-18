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
    private ToolStripLabel filterLabel = null!;
    private ToolStripTextBox filterTextBox = null!;
    private Panel navBar = null!;
    private Button btnBack = null!;
    private Button btnForward = null!;
    private Button btnUp = null!;
    private TextBox addressBox = null!;
    private ContextMenuStrip listViewMenu = null!;
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
        imageList.Images.Add(Icons.Drive());   // 0 = drive (fallback)
        imageList.Images.Add(Icons.Folder());  // 1 = folder (fallback)
        imageList.Images.Add(Icons.File());    // 2 = file (fallback)
        // Indices 3+ are shell icons populated at runtime by GetShellIconIndex()

        // ── Tool strip ───────────────────────────────────────────────────────
        toolStrip  = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        btnRefresh = new ToolStripButton("⟳  Refresh  [F5]")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText  = "Reload drives and current folder (F5)"
        };
        btnRefresh.Click += Btn_Refresh_Click;
        toolStrip.Items.Add(btnRefresh);
        toolStrip.Items.Add(new ToolStripSeparator());
        filterLabel = new ToolStripLabel("Filter:")
        {
            Margin = new Padding(6, 0, 2, 0)
        };
        filterTextBox = new ToolStripTextBox
        {
            Size        = new Size(220, 22),
            ToolTipText = "Filter by name pattern(s), e.g. *.log  or  *.log;*.txt"
        };
        filterTextBox.TextChanged += FilterText_Changed;
        toolStrip.Items.Add(filterLabel);
        toolStrip.Items.Add(filterTextBox);

        // ── Navigation bar (Back / Forward / Up / address box) ────────────────
        navBar = new Panel
        {
            Dock    = DockStyle.Top,
            Height  = 28,
            Padding = new Padding(2, 2, 2, 2)
        };

        btnBack = new Button
        {
            Text      = "←",
            Width     = 28,
            Dock      = DockStyle.Left,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 8f),
            TabStop   = false
        };
        btnBack.FlatAppearance.BorderSize = 0;
        btnBack.Click += BtnBack_Click;

        btnForward = new Button
        {
            Text      = "→",
            Width     = 28,
            Dock      = DockStyle.Left,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 8f),
            TabStop   = false
        };
        btnForward.FlatAppearance.BorderSize = 0;
        btnForward.Click += BtnForward_Click;

        btnUp = new Button
        {
            Text      = "↑",
            Width     = 28,
            Dock      = DockStyle.Left,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 9f),
            TabStop   = false
        };
        btnUp.FlatAppearance.BorderSize = 0;
        btnUp.Click += BtnUp_Click;

        addressBox = new TextBox
        {
            Dock      = DockStyle.Fill,
            Font      = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.FixedSingle
        };
        addressBox.KeyDown += AddressBox_KeyDown;

        // Add right-to-left so Fill textbox claims remaining space after left-docked buttons
        navBar.Controls.Add(addressBox);
        navBar.Controls.Add(btnUp);
        navBar.Controls.Add(btnForward);
        navBar.Controls.Add(btnBack);

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
        treeView.AfterExpand  += TreeView_AfterExpand;

        // ── List view ────────────────────────────────────────────────────────
        listView = new ListView
        {
            Dock               = DockStyle.Fill,
            View               = View.Details,
            FullRowSelect      = true,
            GridLines          = true,
            SmallImageList     = imageList,
            AllowColumnReorder = true,
            LabelEdit          = true,
            Sorting            = SortOrder.None,
            Font               = new Font("Segoe UI", 9f)
        };
        listView.Columns.Add("Name",          300);
        listView.Columns.Add("Date Modified", 155);
        listView.Columns.Add("Size",          110);
        listView.Columns.Add("Type",          140);
        listView.ColumnClick       += ListView_ColumnClick;
        listView.DoubleClick       += ListView_DoubleClick;
        listView.KeyDown           += ListView_KeyDown;
        listView.AfterLabelEdit    += ListView_AfterLabelEdit;
        listView.OwnerDraw          = true;
        listView.DrawColumnHeader  += ListView_DrawColumnHeader;
        listView.DrawItem          += ListView_DrawItem;
        listView.DrawSubItem       += ListView_DrawSubItem;

        // ── Context menu ─────────────────────────────────────────────────────
        listViewMenu = new ContextMenuStrip(components);
        listViewMenu.Items.Add("Open",                         null, ContextMenu_Open);
        listViewMenu.Items.Add("Open in Windows Explorer",     null, ContextMenu_OpenExplorer);
        listViewMenu.Items.Add("Open with Default App",        null, ContextMenu_OpenDefault);
        listViewMenu.Items.Add("Copy Path",                    null, ContextMenu_CopyPath);
        listViewMenu.Items.Add(new ToolStripSeparator());
        listViewMenu.Items.Add("Rename\tF2",                   null, ContextMenu_Rename);
        listViewMenu.Items.Add("Delete\tDel",                  null, ContextMenu_Delete);
        listViewMenu.Opening += ListViewMenu_Opening;
        listView.ContextMenuStrip = listViewMenu;

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
        // Add order matters for DockStyle stacking (higher index = laid out first).
        // Desired visual order top→bottom: toolStrip, navBar, splitContainer, statusStrip.
        SuspendLayout();
        Controls.Add(splitContainer);   // index 0 – Fill, laid out last
        Controls.Add(navBar);           // index 1 – Top, laid out third  → below toolStrip
        Controls.Add(toolStrip);        // index 2 – Top, laid out second → very top
        Controls.Add(statusStrip);      // index 3 – Bottom, laid out first → very bottom
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
