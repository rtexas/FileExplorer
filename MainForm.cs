using System.Collections;
using System.Runtime.InteropServices;

namespace FileExplorer;

public partial class MainForm : Form
{
    // ── State ────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, long> _folderSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sizeLock = new();
    private CancellationTokenSource _cts = new();
    private string? _currentPath;
    private int     _sortCol = 0;
    private SortOrder _sortDir = SortOrder.Descending;

    // ── Scan-progress tracking (one entry per drive root) ─────────────────────
    private readonly Dictionary<string, DriveProgress> _driveProgress =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Column layout ─────────────────────────────────────────────────────────
    private int[]  _baseColWidths  = Array.Empty<int>();
    private bool   _redistributing;          // re-entrancy guard for RedistributeColumns
    private bool   _drivesView;             // true while the list pane is showing the drives list

    // ── Construction ─────────────────────────────────────────────────────────
    public MainForm()
    {
        InitializeComponent();
        KeyPreview = true;
        KeyDown   += (_, e) => { if (e.KeyCode == Keys.F5) DoRefresh(); };
        LoadDrives();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Drive / tree loading
    // ─────────────────────────────────────────────────────────────────────────

    private void LoadDrives()
    {
        CancelAndResetCts();
        lock (_sizeLock) _folderSizes.Clear();
        _driveProgress.Clear();

        // Reset progress bar
        progressBar.Value       = 0;
        scanStatusLabel.Text    = "Processing";
        statusSizeLabel.Text    = "";

        treeView.BeginUpdate();
        treeView.Nodes.Clear();

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable
                              && d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name})";

            var node = new TreeNode($"{label}  [calculating…]")
            {
                Tag              = drive.RootDirectory.FullName,
                ImageIndex       = 0,
                SelectedImageIndex = 0
            };
            node.Nodes.Add(Placeholder());
            treeView.Nodes.Add(node);

            // Create progress tracker for this drive (Total starts at 1 for the root)
            var root = drive.RootDirectory.FullName;
            _driveProgress[root] = new DriveProgress
            {
                TotalBytes = drive.TotalSize - drive.TotalFreeSpace
            };

            // Start background size walk
            var tok = _cts.Token;
            var dp  = _driveProgress[root];
            Task.Run(() => DriveWalkAsync(root, node, dp, tok), tok);
        }

        treeView.EndUpdate();
        // ShowDrivesInListView() is intentionally NOT called here.
        // LoadDrives runs from the constructor before the window handle exists,
        // so the ListView has no usable client area yet.  OnLoad (and DoRefresh)
        // call ShowDrivesInListView() once layout is complete.
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Async recursive size calculation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task DriveWalkAsync(string root, TreeNode driveNode, DriveProgress dp,
                                       CancellationToken ct)
    {
        long total = await Task.Run(() => CalcDir(root, dp, ct), ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return;

        // Root directory itself is now done — mark complete
        Interlocked.Increment(ref dp.Processed);
        dp.Complete = true;

        SafeInvoke(() =>
        {
            UpdateNodeText(driveNode, root, total);
            RefreshListSizes();
            UpdateProgressBar();
        });
    }

    private long CalcDir(string path, DriveProgress dp, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return 0;

        long size = 0;
        try
        {
            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip   = FileAttributes.None    // include hidden + system
            };

            var di = new DirectoryInfo(path);

            foreach (var f in di.EnumerateFiles("*", enumOpts))
            {
                if (ct.IsCancellationRequested) return size;
                size += f.Length;
                Interlocked.Add(ref dp.ScannedBytes, f.Length);
            }

            foreach (var sub in di.EnumerateDirectories("*", enumOpts))
            {
                if (ct.IsCancellationRequested) return size;

                // Count this newly discovered subdirectory toward the total
                Interlocked.Increment(ref dp.Total);

                long subSize = CalcDir(sub.FullName, dp, ct);
                size += subSize;

                // This subdirectory is fully processed
                Interlocked.Increment(ref dp.Processed);

                lock (_sizeLock) _folderSizes[sub.FullName] = subSize;

                var capPath = sub.FullName;
                var capSize = subSize;
                SafeInvoke(() =>
                {
                    UpdateNodeTextByPath(capPath, capSize);
                    RefreshListSizes();
                    UpdateProgressBar();
                });
            }
        }
        catch { /* access denied or I/O error — skip */ }

        lock (_sizeLock) _folderSizes[path] = size;
        return size;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Tree view events
    // ─────────────────────────────────────────────────────────────────────────

    private void TreeView_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node!;
        if (IsPlaceholder(node))
        {
            node.Nodes.Clear();
            PopulateTreeChildren(node, (string)node.Tag!);
        }
    }

    private void TreeView_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is string path)
            NavigateTo(path, fromTree: true);
    }

    private void PopulateTreeChildren(TreeNode parent, string path)
    {
        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip   = FileAttributes.None
        };

        try
        {
            foreach (var sub in new DirectoryInfo(path)
                         .EnumerateDirectories("*", enumOpts)
                         .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                long size;
                lock (_sizeLock) _folderSizes.TryGetValue(sub.FullName, out size);

                bool accessible = CanRead(sub.FullName);
                var  node       = new TreeNode(FormatDirLabel(sub.Name, size))
                {
                    Tag              = sub.FullName,
                    ImageIndex       = 1,
                    SelectedImageIndex = 1
                };

                if (!accessible)
                    node.ForeColor = SystemColors.GrayText;

                if (accessible && HasSubDirs(sub.FullName))
                    node.Nodes.Add(Placeholder());

                parent.Nodes.Add(node);
            }
        }
        catch
        {
            parent.ForeColor = SystemColors.GrayText;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Navigation
    // ─────────────────────────────────────────────────────────────────────────

    private void NavigateTo(string path, bool fromTree = false)
    {
        _drivesView  = false;
        _currentPath = path;
        PopulateListView(path);
        if (!fromTree) ExpandTreeTo(path);
        UpdateProgressBar();   // reflect the correct drive's progress immediately
    }

    private void PopulateListView(string path)
    {
        listView.BeginUpdate();
        listView.Items.Clear();

        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = false,          // we want to SEE denied entries
            AttributesToSkip   = FileAttributes.None
        };

        try
        {
            var di = new DirectoryInfo(path);

            // ── Folders ──────────────────────────────────────────────────────
            try
            {
                foreach (var sub in di.EnumerateDirectories("*", enumOpts))
                {
                    long size;
                    lock (_sizeLock) _folderSizes.TryGetValue(sub.FullName, out size);

                    var item = new ListViewItem(sub.Name)
                    {
                        Tag        = sub.FullName,
                        ImageIndex = 1
                    };
                    item.SubItems.Add(sub.LastWriteTime.ToString("yyyy-MM-dd  HH:mm:ss"));
                    bool accessible = CanRead(sub.FullName);
                    item.SubItems.Add(size > 0 ? FormatSize(size)
                                    : accessible ? "calculating…"
                                    : "Not Accessible");
                    item.SubItems.Add("Folder");

                    if (!accessible)
                        item.ForeColor = SystemColors.GrayText;

                    listView.Items.Add(item);
                }
            }
            catch (UnauthorizedAccessException) { }

            // ── Files ────────────────────────────────────────────────────────
            try
            {
                foreach (var fi in di.EnumerateFiles("*", enumOpts))
                {
                    var item = new ListViewItem(fi.Name)
                    {
                        Tag        = fi.FullName,
                        ImageIndex = 2
                    };
                    item.SubItems.Add(fi.LastWriteTime.ToString("yyyy-MM-dd  HH:mm:ss"));
                    item.SubItems.Add(FormatSize(fi.Length));
                    item.SubItems.Add(GetFileType(fi.Extension));
                    listView.Items.Add(item);
                }
            }
            catch (UnauthorizedAccessException) { }

            ApplySort();

            int folders = listView.Items.Cast<ListViewItem>().Count(i => i.SubItems[3].Text == "Folder");
            int files   = listView.Items.Count - folders;
            statusLabel.Text     = path;
            statusSizeLabel.Text = $"{folders} folder{(folders == 1 ? "" : "s")},  {files} file{(files == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Error: {ex.Message}";
        }

        listView.EndUpdate();
        RedistributeColumns();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  List view events
    // ─────────────────────────────────────────────────────────────────────────

    private void ListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column == _sortCol)
            _sortDir = _sortDir == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        else
        {
            _sortCol = e.Column;
            _sortDir = SortOrder.Descending;
        }
        ApplySort();
    }

    private void ListView_DoubleClick(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0) return;
        var item = listView.SelectedItems[0];
        if (item.Tag is string p && Directory.Exists(p))
            NavigateTo(p);
    }

    private void ApplySort()
    {
        // Update column header markers
        foreach (ColumnHeader col in listView.Columns)
        {
            string arrow = col.Index == _sortCol
                ? (_sortDir == SortOrder.Ascending ? " ▲" : " ▼")
                : "";
            col.Text = col.Index switch
            {
                0 => "Name"          + arrow,
                1 => "Date Modified" + arrow,
                2 => "Size"          + arrow,
                3 => "Type"          + arrow,
                _ => col.Text
            };
        }

        listView.ListViewItemSorter = new LvSorter(_sortCol, _sortDir);
        listView.Sort();
        listView.ListViewItemSorter = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Refresh
    // ─────────────────────────────────────────────────────────────────────────

    private void Btn_Refresh_Click(object? sender, EventArgs e) => DoRefresh();

    private void DoRefresh()
    {
        var saved = _currentPath;
        LoadDrives();           // resets state and restarts background walks
        if (saved != null)
        {
            _drivesView  = false;
            _currentPath = saved;
            PopulateListView(saved);
            ExpandTreeTo(saved);
        }
        else
        {
            ShowDrivesInListView();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Tree navigation helper (expand + select path)
    // ─────────────────────────────────────────────────────────────────────────

    private void ExpandTreeTo(string targetPath)
    {
        // Build ordered list of ancestor paths from root → target
        var segments = new List<string>();
        var di = new DirectoryInfo(targetPath);
        while (di != null)
        {
            segments.Insert(0, di.FullName);
            di = di.Parent!;
        }

        TreeNode? current     = null;
        TreeNodeCollection nodes = treeView.Nodes;

        foreach (var seg in segments)
        {
            TreeNode? found = null;
            foreach (TreeNode n in nodes)
            {
                if (SamePath(n.Tag as string, seg))
                {
                    found = n;
                    break;
                }
            }
            if (found == null) break;

            // Populate lazy children before expanding
            if (IsPlaceholder(found))
            {
                found.Nodes.Clear();
                PopulateTreeChildren(found, (string)found.Tag!);
            }

            found.Expand();
            current = found;
            nodes   = found.Nodes;
        }

        if (current != null)
        {
            treeView.SelectedNode = current;
            current.EnsureVisible();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Live size update helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateNodeText(TreeNode node, string path, long size)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;            // drive root
        node.Text = FormatDirLabel(name, size);
    }

    private void UpdateNodeTextByPath(string path, long size)
    {
        static void Walk(TreeNodeCollection nodes, string path, long size)
        {
            foreach (TreeNode n in nodes)
            {
                if (SamePath(n.Tag as string, path))
                {
                    var name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) name = path;
                    n.Text = FormatDirLabel(name, size);
                }
                if (n.IsExpanded) Walk(n.Nodes, path, size);
            }
        }
        Walk(treeView.Nodes, path, size);
    }

    private void RefreshListSizes()
    {
        if (_drivesView)
        {
            // Update the size column for each drive row as scans complete.
            foreach (ListViewItem item in listView.Items)
            {
                if (item.Tag is not string root) continue;
                long s;
                lock (_sizeLock) _folderSizes.TryGetValue(root, out s);
                if (s > 0) item.SubItems[2].Text = FormatSize(s);
            }
            return;
        }

        if (_currentPath == null) return;
        foreach (ListViewItem item in listView.Items)
        {
            if (item.Tag is string p && item.SubItems[3].Text == "Folder")
            {
                long s;
                lock (_sizeLock) _folderSizes.TryGetValue(p, out s);
                if (s > 0) item.SubItems[2].Text = FormatSize(s);
            }
        }
    }

    private void ShowDrivesInListView()
    {
        _drivesView  = true;
        _currentPath = null;

        listView.BeginUpdate();
        listView.Items.Clear();

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable
                              && d.IsReady))
        {
            var root  = drive.RootDirectory.FullName;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd(Path.DirectorySeparatorChar)
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})";

            long size = 0;
            lock (_sizeLock) _folderSizes.TryGetValue(root, out size);
            bool complete = _driveProgress.TryGetValue(root, out var dp) && dp.Complete;

            var item = new ListViewItem(label)
            {
                Tag        = root,
                ImageIndex = 0
            };
            item.SubItems.Add("");   // Date Modified — not meaningful at drive level
            item.SubItems.Add(size > 0 ? FormatSize(size) : complete ? "0 B" : "calculating…");
            item.SubItems.Add(drive.DriveType == DriveType.Removable ? "Removable Disk" : "Local Disk");
            listView.Items.Add(item);
        }

        int n = listView.Items.Count;
        statusLabel.Text     = "This PC";
        statusSizeLabel.Text = $"{n} drive{(n == 1 ? "" : "s")}";

        listView.EndUpdate();
        RedistributeColumns();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Scan progress bar
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateProgressBar()
    {
        // Determine which drive the current path lives on
        string? driveRoot = _currentPath is not null ? Path.GetPathRoot(_currentPath) : null;

        DriveProgress? dp = null;
        if (driveRoot is not null)
            _driveProgress.TryGetValue(driveRoot, out dp);

        int pct;
        string label;

        if (dp is null)
        {
            // No selection yet — show aggregate across all drives
            long totalBytes   = 0;
            long scannedBytes = 0;
            bool allComplete  = _driveProgress.Count > 0;
            foreach (var p in _driveProgress.Values)
            {
                totalBytes   += Volatile.Read(ref p.TotalBytes);
                scannedBytes += Volatile.Read(ref p.ScannedBytes);
                if (!p.Complete) allComplete = false;
            }
            pct   = allComplete ? 100
                  : totalBytes > 0 ? Math.Min(99, (int)(scannedBytes * 100 / totalBytes)) : 0;
            label = _driveProgress.Count == 0  ? "Processing"
                  : pct >= 100                 ? "Scan complete  ✓"
                  :                              "Processing";
        }
        else
        {
            long totalBytes   = Volatile.Read(ref dp.TotalBytes);
            long scannedBytes = Volatile.Read(ref dp.ScannedBytes);
            pct   = dp.Complete ? 100
                  : totalBytes > 0 ? Math.Min(99, (int)(scannedBytes * 100 / totalBytes)) : 0;
            label = dp.Complete ? "Scan complete  ✓" : "Processing";
        }

        progressBar.Value    = pct;
        scanStatusLabel.Text = label;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Utility helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsPlaceholder(TreeNode n) =>
        n.Nodes.Count == 1 && n.Nodes[0].Tag is null;

    private static TreeNode Placeholder() =>
        new("…") { Tag = null };

    private static bool CanRead(string path)
    {
        try { Directory.EnumerateFileSystemEntries(path).GetEnumerator().MoveNext(); return true; }
        catch { return false; }
    }

    private static bool HasSubDirs(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip   = FileAttributes.None
            }).Any();
        }
        catch { return false; }
    }

    private static bool SamePath(string? a, string? b) =>
        a != null && b != null &&
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar),
                      b.TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    private static string FormatDirLabel(string name, long size) =>
        size > 0 ? $"{name}  [{FormatSize(size)}]" : name;

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F2} MB",
        >= 1_024         => $"{bytes / 1_024.0:F2} KB",
        _                => $"{bytes} B"
    };

    private static string GetFileType(string ext) =>
        string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpper() + " File";

    // Safely marshal an action to the UI thread from a background thread.
    // Swallows disposal races that can occur between the IsDisposed check and BeginInvoke.
    private void SafeInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void CancelAndResetCts()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // SplitContainer min/distance must be set after the form has real dimensions.
        splitContainer.Panel1MinSize    = 150;
        splitContainer.Panel2MinSize    = 300;
        splitContainer.SplitterDistance = 300;

        // Ensure the Date Modified column is wide enough for "yyyy-MM-dd  HH:mm:ss".
        int dateWidth = TextRenderer.MeasureText("2000-00-00  00:00:00", listView.Font).Width + 16;
        if (listView.Columns[1].Width < dateWidth)
            listView.Columns[1].Width = dateWidth;

        // Ensure the Size column is wide enough to display "Not Accessible".
        int sizeWidth = TextRenderer.MeasureText("Not Accessible", listView.Font).Width + 16;
        if (listView.Columns[2].Width < sizeWidth)
            listView.Columns[2].Width = sizeWidth;

        // Snapshot content-minimum widths for all four columns.
        _baseColWidths = listView.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();

        // Minimum form width: chrome + min-tree + splitter + columns + scrollbar + lv-border.
        // Scrollbar is included because the worst case (scrollbar visible) reduces ClientSize.Width
        // by VerticalScrollBarWidth, so the outer listView must be that much wider to fit the columns.
        int formChrome = Width - ClientSize.Width;
        int lvBorder   = listView.Width - listView.ClientSize.Width;
        MinimumSize = new Size(
            formChrome
            + splitContainer.Panel1MinSize
            + splitContainer.SplitterWidth
            + _baseColWidths.Sum()
            + SystemInformation.VerticalScrollBarWidth
            + lvBorder,
            MinimumSize.Height);

        // ClientSizeChanged fires on both outer resize and scrollbar appear/disappear,
        // ensuring columns always fill the visible area exactly.
        RedistributeColumns();
        listView.ClientSizeChanged += (_, _) => RedistributeColumns();

        // Strip visual styles from the progress bar for a flat border appearance.
        SetWindowTheme(progressBar.ProgressBar.Handle, "", "");

        // The window handle now exists and layout is complete — safe to populate the
        // list pane and paint status-bar text for the first time.
        ShowDrivesInListView();
        scanStatusLabel.Text = "Processing";
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    private void RedistributeColumns()
    {
        // Guard against re-entrancy: setting column widths can trigger ClientSizeChanged
        // synchronously (e.g. when a horizontal scrollbar appears/disappears), which would
        // call this method recursively and freeze the UI thread.
        if (_redistributing || _baseColWidths.Length == 0) return;
        _redistributing = true;
        try
        {
            // ClientSize.Width already excludes the vertical scrollbar when it is visible.
            int available = listView.ClientSize.Width;
            int baseTotal = _baseColWidths.Sum();
            int extra     = Math.Max(0, available - baseTotal);
            int perCol    = extra / _baseColWidths.Length;
            int remainder = extra % _baseColWidths.Length;

            // Do NOT call BeginUpdate/EndUpdate here — those suppress and then flush item
            // redraws, which is what causes ClientSizeChanged to fire during EndUpdate().
            for (int i = 0; i < _baseColWidths.Length; i++)
                listView.Columns[i].Width = _baseColWidths[i] + perCol + (i < remainder ? 1 : 0);
        }
        finally { _redistributing = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts.Cancel();
        base.OnFormClosing(e);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Per-drive scan progress tracker
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class DriveProgress
{
    /// <summary>Total directories discovered (root counts as 1 from the start).</summary>
    public int Total = 1;

    /// <summary>Directories whose CalcDir has fully returned.</summary>
    public int Processed;

    /// <summary>Set to true by DriveWalkAsync when the root CalcDir returns.</summary>
    public volatile bool Complete;

    /// <summary>Drive used bytes (TotalSize - TotalFreeSpace) — denominator for progress.</summary>
    public long TotalBytes;

    /// <summary>Bytes accumulated so far across all scanned files — numerator for progress.</summary>
    public long ScannedBytes;

    public int Percent =>
        Complete ? 100
        : TotalBytes > 0 ? Math.Min(99, (int)(ScannedBytes * 100 / TotalBytes)) : 0;
}

// ─────────────────────────────────────────────────────────────────────────────
//  ListView column sorter
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class LvSorter(int col, SortOrder dir) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not ListViewItem a || y is not ListViewItem b) return 0;

        bool aDir = a.SubItems[3].Text == "Folder";
        bool bDir = b.SubItems[3].Text == "Folder";
        if (aDir != bDir) return aDir ? -1 : 1;    // folders always first

        int r = col switch
        {
            1 => CmpDate(a.SubItems[1].Text, b.SubItems[1].Text),
            2 => CmpSize(a.SubItems[2].Text, b.SubItems[2].Text),
            _ => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase)
        };
        return dir == SortOrder.Descending ? -r : r;
    }

    private static int CmpDate(string x, string y)
    {
        DateTime.TryParse(x, out var dx);
        DateTime.TryParse(y, out var dy);
        return dx.CompareTo(dy);
    }

    private static int CmpSize(string x, string y) =>
        ToBytes(x).CompareTo(ToBytes(y));

    private static double ToBytes(string s)
    {
        if (s is "calculating…" or "" or "Not Accessible") return 0;
        var sp = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (!double.TryParse(sp[0], out double v)) return 0;
        return sp.Length > 1 ? sp[1].Trim() switch
        {
            "GB" => v * 1_073_741_824,
            "MB" => v * 1_048_576,
            "KB" => v * 1_024,
            _    => v
        } : v;
    }
}
