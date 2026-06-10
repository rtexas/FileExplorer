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
        imageList = new ImageList(components);
        toolStrip = new ToolStrip();
        btnRefresh = new ToolStripButton();
        filterLabel = new ToolStripLabel();
        filterTextBox = new ToolStripTextBox();
        navBar = new Panel();
        addressBox = new TextBox();
        btnUp = new Button();
        btnForward = new Button();
        btnBack = new Button();
        treeView = new TreeView();
        listView = new ListView();
        listViewMenu = new ContextMenuStrip(components);
        menuOpen = new ToolStripMenuItem();
        menuOpenExplorer = new ToolStripMenuItem();
        menuOpenDefault = new ToolStripMenuItem();
        menuCopyPath = new ToolStripMenuItem();
        menuSeparator = new ToolStripSeparator();
        menuRename = new ToolStripMenuItem();
        menuDelete = new ToolStripMenuItem();
        splitContainer = new SplitContainer();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();
        scanStatusLabel = new ToolStripStatusLabel();
        progressBar = new ToolStripProgressBar();
        statusSizeLabel = new ToolStripStatusLabel();
        toolStrip.SuspendLayout();
        navBar.SuspendLayout();
        listViewMenu.SuspendLayout();
        ((ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();
        // 
        // imageList
        // 
        imageList.ColorDepth = ColorDepth.Depth32Bit;
        imageList.ImageSize = new Size(16, 16);
        imageList.TransparentColor = Color.Transparent;
        // 
        // toolStrip
        // 
        toolStrip.Items.AddRange(new ToolStripItem[] { btnRefresh, filterLabel, filterTextBox });
        toolStrip.Location = new Point(0, 0);
        toolStrip.Name = "toolStrip";
        toolStrip.Size = new Size(1200, 25);
        toolStrip.TabIndex = 3;
        // 
        // btnRefresh
        // 
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Size = new Size(92, 22);
        btnRefresh.Text = "⟳  Refresh  [F5]";
        btnRefresh.Click += Btn_Refresh_Click;
        // 
        // filterLabel
        // 
        filterLabel.Name = "filterLabel";
        filterLabel.Size = new Size(36, 22);
        filterLabel.Text = "Filter:";
        // 
        // filterTextBox
        // 
        filterTextBox.BackColor = Color.White;
        filterTextBox.BorderStyle = BorderStyle.FixedSingle;
        filterTextBox.Name = "filterTextBox";
        filterTextBox.Size = new Size(100, 25);
        filterTextBox.TextChanged += FilterText_Changed;
        // 
        // navBar
        // 
        navBar.Controls.Add(addressBox);
        navBar.Controls.Add(btnUp);
        navBar.Controls.Add(btnForward);
        navBar.Controls.Add(btnBack);
        navBar.Dock = DockStyle.Top;
        navBar.Location = new Point(0, 0);
        navBar.Name = "navBar";
        navBar.Size = new Size(1200, 30);
        navBar.TabIndex = 2;
        //
        // btnBack
        //
        btnBack.Dock = DockStyle.Left;
        btnBack.FlatAppearance.BorderSize = 0;
        btnBack.FlatStyle = FlatStyle.Flat;
        btnBack.Font = new Font("Segoe UI", 10F);
        btnBack.Name = "btnBack";
        btnBack.Size = new Size(32, 30);
        btnBack.TabIndex = 3;
        btnBack.Text = "←";
        btnBack.Click += BtnBack_Click;
        //
        // btnForward
        //
        btnForward.Dock = DockStyle.Left;
        btnForward.FlatAppearance.BorderSize = 0;
        btnForward.FlatStyle = FlatStyle.Flat;
        btnForward.Font = new Font("Segoe UI", 10F);
        btnForward.Name = "btnForward";
        btnForward.Size = new Size(32, 30);
        btnForward.TabIndex = 2;
        btnForward.Text = "→";
        btnForward.Click += BtnForward_Click;
        //
        // btnUp
        //
        btnUp.Dock = DockStyle.Left;
        btnUp.FlatAppearance.BorderSize = 0;
        btnUp.FlatStyle = FlatStyle.Flat;
        btnUp.Font = new Font("Segoe UI", 10F);
        btnUp.Name = "btnUp";
        btnUp.Size = new Size(32, 30);
        btnUp.TabIndex = 1;
        btnUp.Text = "↑";
        btnUp.Click += BtnUp_Click;
        //
        // addressBox
        //
        addressBox.Dock = DockStyle.Fill;
        addressBox.Location = new Point(96, 4);
        addressBox.Name = "addressBox";
        addressBox.Size = new Size(100, 23);
        addressBox.TabIndex = 0;
        addressBox.KeyDown += AddressBox_KeyDown;
        // 
        // treeView
        //
        treeView.Dock = DockStyle.Fill;
        treeView.Location = new Point(0, 0);
        treeView.Name = "treeView";
        treeView.Size = new Size(121, 97);
        treeView.TabIndex = 0;
        treeView.BeforeExpand += TreeView_BeforeExpand;
        treeView.AfterExpand += TreeView_AfterExpand;
        treeView.AfterSelect += TreeView_AfterSelect;
        // 
        // listView
        //
        listView.Columns.Add("Name", 200);
        listView.Columns.Add("Date Modified", 150);
        listView.Columns.Add("Size", 100);
        listView.Columns.Add("Type", 120);
        listView.ContextMenuStrip = listViewMenu;
        listView.Dock = DockStyle.Fill;
        listView.FullRowSelect = true;
        listView.Location = new Point(0, 0);
        listView.Name = "listView";
        listView.OwnerDraw = true;
        listView.Size = new Size(121, 97);
        listView.SmallImageList = imageList;
        listView.TabIndex = 0;
        listView.UseCompatibleStateImageBehavior = false;
        listView.View = View.Details;
        listView.AfterLabelEdit += ListView_AfterLabelEdit;
        listView.ColumnClick += ListView_ColumnClick;
        listView.DrawColumnHeader += ListView_DrawColumnHeader;
        listView.DrawItem += ListView_DrawItem;
        listView.DrawSubItem += ListView_DrawSubItem;
        listView.DoubleClick += ListView_DoubleClick;
        listView.KeyDown += ListView_KeyDown;
        // 
        // listViewMenu
        // 
        listViewMenu.Items.AddRange(new ToolStripItem[] { menuOpen, menuOpenExplorer, menuOpenDefault, menuCopyPath, menuSeparator, menuRename, menuDelete });
        listViewMenu.Name = "listViewMenu";
        listViewMenu.Size = new Size(180, 32);
        listViewMenu.Opening += ListViewMenu_Opening;
        //
        // menuOpen
        //
        menuOpen.Name = "menuOpen";
        menuOpen.Size = new Size(179, 22);
        menuOpen.Text = "Open";
        menuOpen.Click += ContextMenu_Open;
        //
        // menuOpenExplorer
        //
        menuOpenExplorer.Name = "menuOpenExplorer";
        menuOpenExplorer.Size = new Size(179, 22);
        menuOpenExplorer.Text = "Show in Explorer";
        menuOpenExplorer.Click += ContextMenu_OpenExplorer;
        //
        // menuOpenDefault
        //
        menuOpenDefault.Name = "menuOpenDefault";
        menuOpenDefault.Size = new Size(179, 22);
        menuOpenDefault.Text = "Open with Default App";
        menuOpenDefault.Click += ContextMenu_OpenDefault;
        //
        // menuCopyPath
        //
        menuCopyPath.Name = "menuCopyPath";
        menuCopyPath.Size = new Size(179, 22);
        menuCopyPath.Text = "Copy Path";
        menuCopyPath.Click += ContextMenu_CopyPath;
        //
        // menuSeparator
        //
        menuSeparator.Name = "menuSeparator";
        menuSeparator.Size = new Size(176, 6);
        //
        // menuRename
        //
        menuRename.Name = "menuRename";
        menuRename.Size = new Size(179, 22);
        menuRename.Text = "Rename";
        menuRename.Click += ContextMenu_Rename;
        //
        // menuDelete
        //
        menuDelete.Name = "menuDelete";
        menuDelete.Size = new Size(179, 22);
        menuDelete.Text = "Delete";
        menuDelete.Click += ContextMenu_Delete;
        // 
        // splitContainer
        //
        splitContainer.Dock = DockStyle.Fill;
        splitContainer.Location = new Point(0, 0);
        splitContainer.Name = "splitContainer";
        // 
        // splitContainer.Panel1
        // 
        splitContainer.Panel1.Controls.Add(treeView);
        // 
        // splitContainer.Panel2
        // 
        splitContainer.Panel2.Controls.Add(listView);
        splitContainer.Size = new Size(150, 100);
        splitContainer.TabIndex = 1;
        // 
        // statusStrip
        // 
        statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel, scanStatusLabel, progressBar, statusSizeLabel });
        statusStrip.Location = new Point(0, 778);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(1200, 22);
        statusStrip.TabIndex = 4;
        // 
        // statusLabel
        // 
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(39, 17);
        statusLabel.Text = "Ready";
        // 
        // scanStatusLabel
        // 
        scanStatusLabel.Name = "scanStatusLabel";
        scanStatusLabel.Size = new Size(64, 17);
        scanStatusLabel.Text = "Processing";
        // 
        // progressBar
        // 
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(100, 16);
        // 
        // statusSizeLabel
        // 
        statusSizeLabel.Name = "statusSizeLabel";
        statusSizeLabel.Size = new Size(0, 17);
        // 
        // MainForm
        // 
        ClientSize = new Size(1200, 800);
        Controls.Add(splitContainer);
        Controls.Add(navBar);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);
        MinimumSize = new Size(700, 450);
        Name = "MainForm";
        Text = "File Explorer";
        toolStrip.ResumeLayout(false);
        toolStrip.PerformLayout();
        navBar.ResumeLayout(false);
        navBar.PerformLayout();
        listViewMenu.ResumeLayout(false);
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        ((ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
    private ToolStripMenuItem menuOpen;
    private ToolStripMenuItem menuOpenExplorer;
    private ToolStripMenuItem menuOpenDefault;
    private ToolStripMenuItem menuCopyPath;
    private ToolStripSeparator menuSeparator;
    private ToolStripMenuItem menuRename;
    private ToolStripMenuItem menuDelete;
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
