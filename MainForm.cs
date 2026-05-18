using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FileExplorer;

public partial class MainForm : Form
{
    // ── State ────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, long> _folderSizes         = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _filteredFolderSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sizeLock = new();
    private CancellationTokenSource _cts       = new();
    private CancellationTokenSource _filterCts = new();
    private System.Windows.Forms.Timer? _filterDebounce;
    private System.Windows.Forms.Timer? _watcherDebounce;
    private string _filterText = "";
    private string? _currentPath;
    private int     _sortCol = 0;
    private SortOrder _sortDir = SortOrder.Descending;

    // ── Scan-progress tracking ────────────────────────────────────────────────
    private readonly Dictionary<string, DriveProgress> _driveProgress =
        new(StringComparer.OrdinalIgnoreCase);
    private long _lastScanUiMs; // throttle: ms timestamp of last in-progress UI post

    // ── Column layout ─────────────────────────────────────────────────────────
    private int[]  _baseColWidths = Array.Empty<int>();
    private bool   _redistributing;
    private bool   _drivesView;

    // ── Navigation history ────────────────────────────────────────────────────
    private readonly List<string> _history = new();
    private int  _historyIndex    = -1;
    private bool _navigatingHistory;

    // ── Shell icon cache ──────────────────────────────────────────────────────
    private readonly Dictionary<string, int> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _iconLock = new();

    // ── FileSystemWatcher ────────────────────────────────────────────────────
    private FileSystemWatcher? _watcher;

    // ── Construction ─────────────────────────────────────────────────────────
    public MainForm()
    {
        InitializeComponent();
        KeyPreview = true;
        KeyDown   += Form_KeyDown;
        LoadDrives();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Drive / tree loading
    // ─────────────────────────────────────────────────────────────────────────

    private void LoadDrives()
    {
        CancelAndResetCts();
        lock (_sizeLock) { _folderSizes.Clear(); _filteredFolderSizes.Clear(); }
        _driveProgress.Clear();

        progressBar.Value    = 0;
        scanStatusLabel.Text = "Processing";
        statusSizeLabel.Text = "";
        Volatile.Write(ref _lastScanUiMs, 0);

        treeView.BeginUpdate();
        treeView.Nodes.Clear();

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name})";

            var node = new TreeNode($"{label}  ({FormatSize(drive.TotalSize)}/calculating…)")
            {
                Tag              = drive.RootDirectory.FullName,
                ImageIndex       = GetShellIconIndex(drive.RootDirectory.FullName, isFolder: true),
                SelectedImageIndex = GetShellIconIndex(drive.RootDirectory.FullName, isFolder: true)
            };
            node.Nodes.Add(Placeholder());
            treeView.Nodes.Add(node);

            var root = drive.RootDirectory.FullName;
            _driveProgress[root] = new DriveProgress
            {
                TotalBytes    = drive.TotalSize - drive.TotalFreeSpace,
                TotalCapacity = drive.TotalSize
            };

            var tok = _cts.Token;
            var dp  = _driveProgress[root];
            Task.Run(() => DriveWalkAsync(root, node, dp, tok), tok);
        }

        treeView.EndUpdate();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Async recursive size calculation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task DriveWalkAsync(string root, TreeNode driveNode, DriveProgress dp,
                                       CancellationToken ct)
    {
        long total = await Task.Run(() => CalcDir(root, dp, ct), ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return;
        Interlocked.Increment(ref dp.Processed);
        dp.Complete = true;
        SafeInvoke(() =>
        {
            UpdateNodeText(driveNode, root, total);
            RefreshExpandedTreeNodes(driveNode.Nodes);
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
                AttributesToSkip   = FileAttributes.None
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
                Interlocked.Increment(ref dp.Total);
                long subSize = CalcDir(sub.FullName, dp, ct);
                size += subSize;
                Interlocked.Increment(ref dp.Processed);
                lock (_sizeLock) _folderSizes[sub.FullName] = subSize;

                // Throttle mid-scan UI posts to ≤1 per 200 ms to keep the
                // message queue clear so the form can paint normally.
                long now  = Environment.TickCount64;
                long prev = Volatile.Read(ref _lastScanUiMs);
                if (now - prev >= 200)
                {
                    Volatile.Write(ref _lastScanUiMs, now);
                    SafeInvoke(() =>
                    {
                        RefreshExpandedTreeNodes(treeView.Nodes);
                        RefreshListSizes();
                        UpdateProgressBar();
                    });
                }
            }
        }
        catch { }
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

    private void TreeView_AfterExpand(object? sender, TreeViewEventArgs e)
    {
        if (string.IsNullOrEmpty(_filterText) || e.Node == null) return;
        var childPaths = new List<string>();
        foreach (TreeNode child in e.Node.Nodes)
            if (child.Tag is string cp) childPaths.Add(cp);
        if (childPaths.Count > 0)
        {
            var tok = _filterCts.Token; var pattern = _filterText;
            Task.Run(() => ComputeFilteredSizesAsync(childPaths, pattern, tok), tok);
        }
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
                bool sizeKnown;
                long size;
                lock (_sizeLock) sizeKnown = _folderSizes.TryGetValue(sub.FullName, out size);
                if (!sizeKnown) size = -1;   // -1 = not yet scanned
                bool accessible = CanRead(sub.FullName);
                int iconIdx     = GetShellIconIndex(sub.FullName, isFolder: true);
                var node        = new TreeNode(FormatDirLabel(sub.Name, size))
                {
                    Tag              = sub.FullName,
                    ImageIndex       = iconIdx,
                    SelectedImageIndex = iconIdx
                };
                if (!accessible) node.ForeColor = SystemColors.GrayText;
                if (accessible && HasSubDirs(sub.FullName)) node.Nodes.Add(Placeholder());
                parent.Nodes.Add(node);
            }
        }
        catch { parent.ForeColor = SystemColors.GrayText; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Navigation
    // ─────────────────────────────────────────────────────────────────────────

    private void NavigateTo(string path, bool fromTree = false)
    {
        _drivesView  = false;
        _currentPath = path;

        // History: truncate forward entries and push new entry
        if (!_navigatingHistory)
        {
            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(path);
            _historyIndex = _history.Count - 1;
        }

        UpdateNavButtons();
        addressBox.Text = path;
        PopulateListView(path);
        if (!fromTree) ExpandTreeTo(path);
        UpdateProgressBar();
        WatchCurrentPath(path);
    }

    private void NavigateBack()
    {
        if (_historyIndex <= 0) return;
        _historyIndex--;
        _navigatingHistory = true;
        try { NavigateTo(_history[_historyIndex], fromTree: false); }
        finally { _navigatingHistory = false; }
        ExpandTreeTo(_history[_historyIndex]);
    }

    private void NavigateForward()
    {
        if (_historyIndex >= _history.Count - 1) return;
        _historyIndex++;
        _navigatingHistory = true;
        try { NavigateTo(_history[_historyIndex], fromTree: false); }
        finally { _navigatingHistory = false; }
        ExpandTreeTo(_history[_historyIndex]);
    }

    private void NavigateUp()
    {
        var parent = _currentPath != null ? Directory.GetParent(_currentPath)?.FullName : null;
        if (parent != null) NavigateTo(parent);
        else ShowDrivesInListView();
    }

    private void UpdateNavButtons()
    {
        btnBack.Enabled    = _historyIndex > 0;
        btnForward.Enabled = _historyIndex < _history.Count - 1;
        btnUp.Enabled      = _currentPath != null;
    }

    private void AddressBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            var path = addressBox.Text.Trim();
            if (Directory.Exists(path))       NavigateTo(path);
            else if (File.Exists(path))       { NavigateTo(Path.GetDirectoryName(path)!); }
            else { addressBox.Text = _currentPath ?? ""; }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            addressBox.Text = _currentPath ?? "";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  List population
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateListView(string path)
    {
        listView.BeginUpdate();
        listView.Items.Clear();

        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            AttributesToSkip   = FileAttributes.None
        };

        try
        {
            var di = new DirectoryInfo(path);

            try
            {
                foreach (var sub in di.EnumerateDirectories("*", enumOpts))
                {
                    long size;
                    lock (_sizeLock) _folderSizes.TryGetValue(sub.FullName, out size);
                    int iconIdx     = GetShellIconIndex(sub.FullName, isFolder: true);
                    var item        = new ListViewItem(sub.Name)
                    {
                        Tag        = sub.FullName,
                        ImageIndex = iconIdx
                    };
                    item.SubItems.Add(sub.LastWriteTime.ToString("yyyy-MM-dd  HH:mm:ss"));
                    bool accessible = CanRead(sub.FullName);
                    item.SubItems.Add(size > 0 ? FormatSize(size)
                                    : accessible ? "calculating…" : "Not Accessible");
                    item.SubItems.Add("Folder");
                    if (!accessible) item.ForeColor = SystemColors.GrayText;
                    listView.Items.Add(item);
                }
            }
            catch (UnauthorizedAccessException) { }

            try
            {
                foreach (var fi in di.EnumerateFiles("*", enumOpts))
                {
                    int iconIdx = GetShellIconIndex(fi.FullName, isFolder: false);
                    var item    = new ListViewItem(fi.Name)
                    {
                        Tag        = fi.FullName,
                        ImageIndex = iconIdx
                    };
                    item.SubItems.Add(fi.LastWriteTime.ToString("yyyy-MM-dd  HH:mm:ss"));
                    item.SubItems.Add(FormatSize(fi.Length));
                    item.SubItems.Add(GetFileType(fi.Extension));
                    listView.Items.Add(item);
                }
            }
            catch (UnauthorizedAccessException) { }

            ApplySort();

            // Apply filter after population (ignored if folder is not accessible)
            if (!string.IsNullOrEmpty(_filterText) && CanRead(path))
            {
                var toRemove = listView.Items.Cast<ListViewItem>()
                    .Where(i => !MatchesFilter(i.Text, _filterText))
                    .ToList();
                foreach (var item in toRemove) listView.Items.Remove(item);
            }

            int folders = listView.Items.Cast<ListViewItem>().Count(i => i.SubItems[3].Text == "Folder");
            int files   = listView.Items.Count - folders;
            statusLabel.Text = path;
            string filterSuffix  = string.IsNullOrEmpty(_filterText) ? "" : $"  [filter: {_filterText}]";
            statusSizeLabel.Text =
                $"{folders} folder{(folders == 1 ? "" : "s")},  {files} file{(files == 1 ? "" : "s")}{filterSuffix}";
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
        else { _sortCol = e.Column; _sortDir = SortOrder.Descending; }
        ApplySort();
    }

    private void ListView_DoubleClick(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0) return;
        var item = listView.SelectedItems[0];
        if (item.Tag is string p)
        {
            if (Directory.Exists(p)) NavigateTo(p);
            else if (File.Exists(p)) OpenDefault(p);
        }
    }

    private void ListView_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Back:
                e.Handled = true;
                NavigateUp();
                break;

            case Keys.Enter:
                e.Handled = true;
                if (listView.SelectedItems.Count > 0 && listView.SelectedItems[0].Tag is string ep)
                {
                    if (Directory.Exists(ep)) NavigateTo(ep);
                    else if (File.Exists(ep)) OpenDefault(ep);
                }
                break;

            case Keys.Delete:
                e.Handled = true;
                DeleteSelected();
                break;

            case Keys.F2:
                e.Handled = true;
                if (listView.SelectedItems.Count > 0)
                    listView.SelectedItems[0].BeginEdit();
                break;

            case Keys.Left when e.Alt:
                e.Handled = true;
                NavigateBack();
                break;

            case Keys.Right when e.Alt:
                e.Handled = true;
                NavigateForward();
                break;
        }
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5) { DoRefresh(); e.Handled = true; }
    }

    private void ListView_AfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        // LabelEdit is enabled only for rename; disable immediately so normal clicks don't re-open it
        listView.LabelEdit = false;

        if (e.Label == null) return; // user cancelled

        var item    = listView.Items[e.Item];
        var oldPath = (string)item.Tag!;
        var newName = e.Label.Trim();

        if (string.IsNullOrEmpty(newName) || newName == item.Text)
        {
            e.CancelEdit = true;
            return;
        }

        var dir     = Path.GetDirectoryName(oldPath)!;
        var newPath = Path.Combine(dir, newName);
        try
        {
            if (Directory.Exists(oldPath)) Directory.Move(oldPath, newPath);
            else                           File.Move(oldPath, newPath);

            item.Tag = newPath;
            lock (_sizeLock) { _folderSizes.Remove(oldPath); _folderSizes.Remove(dir); }
        }
        catch (Exception ex)
        {
            e.CancelEdit = true;
            MessageBox.Show($"Cannot rename: {ex.Message}", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplySort()
    {
        foreach (ColumnHeader col in listView.Columns)
        {
            string arrow = col.Index == _sortCol
                ? (_sortDir == SortOrder.Ascending ? " ▲" : " ▼") : "";
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
    //  Context menu
    // ─────────────────────────────────────────────────────────────────────────

    private void ListViewMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        bool hasSelection = listView.SelectedItems.Count > 0;
        bool isDir        = hasSelection && listView.SelectedItems[0].Tag is string p && Directory.Exists(p);

        // "Open" label adapts to context
        listViewMenu.Items[0].Text    = isDir ? "Open Folder" : "Open";
        listViewMenu.Items[0].Enabled = hasSelection;
        listViewMenu.Items[1].Enabled = hasSelection;
        listViewMenu.Items[2].Enabled = hasSelection && !isDir;
        listViewMenu.Items[3].Enabled = hasSelection;
        // separator at index 4
        listViewMenu.Items[5].Enabled = hasSelection && !_drivesView;
        listViewMenu.Items[6].Enabled = hasSelection && !_drivesView;

        if (!hasSelection) e.Cancel = true;
    }

    private void ContextMenu_Open(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0) return;
        var path = (string)listView.SelectedItems[0].Tag!;
        if (Directory.Exists(path)) NavigateTo(path);
        else OpenDefault(path);
    }

    private void ContextMenu_OpenExplorer(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0) return;
        var path = (string)listView.SelectedItems[0].Tag!;
        var target = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else
            Process.Start("explorer.exe", $"\"{target}\"");
    }

    private void ContextMenu_OpenDefault(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0) return;
        OpenDefault((string)listView.SelectedItems[0].Tag!);
    }

    private void ContextMenu_CopyPath(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0) return;
        Clipboard.SetText((string)listView.SelectedItems[0].Tag!);
    }

    private void ContextMenu_Rename(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0 || _drivesView) return;
        listView.LabelEdit = true;
        listView.SelectedItems[0].BeginEdit();
    }

    private void ContextMenu_Delete(object? sender, EventArgs e) => DeleteSelected();

    private static void OpenDefault(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open: {ex.Message}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        if (listView.SelectedItems.Count == 0 || _drivesView) return;
        var items = listView.SelectedItems.Cast<ListViewItem>().ToList();
        var names = string.Join("\n", items.Take(5).Select(i => i.Text))
                  + (items.Count > 5 ? $"\n…and {items.Count - 5} more" : "");
        var confirm = MessageBox.Show(
            $"Send to Recycle Bin?\n\n{names}", "Delete",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        foreach (var item in items)
        {
            var path = (string)item.Tag!;
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    hwnd   = Handle,
                    wFunc  = FO_DELETE,
                    pFrom  = path + '\0',
                    fFlags = FOF_ALLOWUNDO
                };
                SHFileOperation(ref op);
                lock (_sizeLock)
                {
                    _folderSizes.Remove(path);
                    var parent = Path.GetDirectoryName(path);
                    if (parent != null) _folderSizes.Remove(parent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot delete {item.Text}:\n{ex.Message}", "Delete",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        if (_currentPath != null) PopulateListView(_currentPath);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Refresh
    // ─────────────────────────────────────────────────────────────────────────

    private void Btn_Refresh_Click(object? sender, EventArgs e) => DoRefresh();

    private void DoRefresh()
    {
        var saved = _currentPath;
        LoadDrives();
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
    //  Tree navigation helper
    // ─────────────────────────────────────────────────────────────────────────

    private void ExpandTreeTo(string targetPath)
    {
        var segments = new List<string>();
        var di = new DirectoryInfo(targetPath);
        while (di != null) { segments.Insert(0, di.FullName); di = di.Parent!; }

        TreeNode? current = null;
        TreeNodeCollection nodes = treeView.Nodes;

        foreach (var seg in segments)
        {
            TreeNode? found = null;
            foreach (TreeNode n in nodes)
                if (SamePath(n.Tag as string, seg)) { found = n; break; }
            if (found == null) break;

            if (IsPlaceholder(found))
            {
                found.Nodes.Clear();
                PopulateTreeChildren(found, (string)found.Tag!);
            }
            found.Expand();
            current = found;
            nodes   = found.Nodes;
        }

        if (current != null) { treeView.SelectedNode = current; current.EnsureVisible(); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Live size update helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateNodeText(TreeNode node, string path, long size)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path.TrimEnd(Path.DirectorySeparatorChar);

        if (_driveProgress.TryGetValue(path, out var dp) && dp.TotalCapacity > 0)
        {
            int pct = (int)Math.Min(100, size * 100L / dp.TotalCapacity);
            node.Text = $"{name}  ({FormatSize(dp.TotalCapacity)}/{FormatSize(size)}) {pct}%";
        }
        else
        {
            node.Text = FormatDirLabel(name, size, GetFilteredSize(path));
        }
    }

    // Refreshes every tree node that is currently visible (its parent is expanded).
    // Called after a drive scan completes so all children get their final sizes.
    private void RefreshExpandedTreeNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is string path && !_driveProgress.ContainsKey(path))
            {
                bool scanned;
                long size;
                lock (_sizeLock) scanned = _folderSizes.TryGetValue(path, out size);
                if (scanned)   // size may be 0 (empty/inaccessible) — still show it
                {
                    var name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) name = path;
                    var label = FormatDirLabel(name, size, GetFilteredSize(path));
                    if (n.Text != label) n.Text = label;
                }
            }
            if (n.IsExpanded)
                RefreshExpandedTreeNodes(n.Nodes);
        }
    }

    private long GetFilteredSize(string path)
    {
        if (string.IsNullOrEmpty(_filterText)) return -1;
        lock (_sizeLock)
            return _filteredFolderSizes.TryGetValue(path, out var fs) ? fs : -1;
    }

    private void RefreshListSizes()
    {
        if (_drivesView)
        {
            foreach (ListViewItem item in listView.Items)
            {
                if (item.Tag is not string root) continue;
                long s; lock (_sizeLock) _folderSizes.TryGetValue(root, out s);
                if (s > 0) SetSubItemText(item, 2, FormatSize(s));
            }
            return;
        }
        if (_currentPath == null) return;
        foreach (ListViewItem item in listView.Items)
        {
            if (item.Tag is string p && item.SubItems[3].Text == "Folder")
            {
                bool scanned;
                long s;
                lock (_sizeLock) scanned = _folderSizes.TryGetValue(p, out s);
                if (s > 0)
                    SetSubItemText(item, 2, FormatSize(s));
                else if (scanned && item.SubItems[2].Text == "calculating…")
                    SetSubItemText(item, 2, "0 B");
            }
        }
    }

    // Only assign when the text actually changes — avoids unnecessary invalidation.
    private static void SetSubItemText(ListViewItem item, int index, string text)
    {
        if (item.SubItems[index].Text != text)
            item.SubItems[index].Text = text;
    }

    private void ShowDrivesInListView()
    {
        _drivesView  = true;
        _currentPath = null;
        addressBox.Text = "This PC";
        UpdateNavButtons();
        WatchCurrentPath(null);

        listView.BeginUpdate();
        listView.Items.Clear();

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady))
        {
            var root  = drive.RootDirectory.FullName;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd(Path.DirectorySeparatorChar)
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})";

            long size = 0;
            lock (_sizeLock) _folderSizes.TryGetValue(root, out size);
            bool complete = _driveProgress.TryGetValue(root, out var dp) && dp.Complete;

            int iconIdx = GetShellIconIndex(root, isFolder: true);
            var item    = new ListViewItem(label) { Tag = root, ImageIndex = iconIdx };
            item.SubItems.Add("");
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
        string? driveRoot = _currentPath is not null ? Path.GetPathRoot(_currentPath) : null;
        DriveProgress? dp = null;
        if (driveRoot is not null) _driveProgress.TryGetValue(driveRoot, out dp);

        int pct; string label;
        if (dp is null)
        {
            long totalBytes = 0, scannedBytes = 0;
            bool allComplete = _driveProgress.Count > 0;
            foreach (var p in _driveProgress.Values)
            {
                totalBytes   += Volatile.Read(ref p.TotalBytes);
                scannedBytes += Volatile.Read(ref p.ScannedBytes);
                if (!p.Complete) allComplete = false;
            }
            pct   = allComplete ? 100 : totalBytes > 0 ? Math.Min(99, (int)(scannedBytes * 100 / totalBytes)) : 0;
            label = _driveProgress.Count == 0 ? "Processing" : pct >= 100 ? "Scan complete  ✓" : "Processing";
        }
        else
        {
            long totalBytes   = Volatile.Read(ref dp.TotalBytes);
            long scannedBytes = Volatile.Read(ref dp.ScannedBytes);
            pct   = dp.Complete ? 100 : totalBytes > 0 ? Math.Min(99, (int)(scannedBytes * 100 / totalBytes)) : 0;
            label = dp.Complete ? "Scan complete  ✓" : "Processing";
        }
        progressBar.Value    = pct;
        scanStatusLabel.Text = label;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Filter
    // ─────────────────────────────────────────────────────────────────────────

    private void FilterText_Changed(object? sender, EventArgs e)
    {
        if (_filterDebounce == null)
        {
            _filterDebounce = new System.Windows.Forms.Timer { Interval = 350 };
            _filterDebounce.Tick += (_, _) => { _filterDebounce!.Stop(); ApplyFilterNow(); };
        }
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private void ApplyFilterNow()
    {
        _filterText = filterTextBox.Text.Trim();
        _filterCts.Cancel(); _filterCts.Dispose(); _filterCts = new CancellationTokenSource();
        lock (_sizeLock) _filteredFolderSizes.Clear();

        if (_currentPath != null) PopulateListView(_currentPath);

        if (!string.IsNullOrEmpty(_filterText))
        {
            var visiblePaths = new List<string>();
            CollectTreePaths(treeView.Nodes, visiblePaths);
            var tok = _filterCts.Token; var pattern = _filterText;
            Task.Run(() => ComputeFilteredSizesAsync(visiblePaths, pattern, tok), tok);
        }
        else
        {
            RestoreTreeNodeLabels(treeView.Nodes);
        }
    }

    private async Task ComputeFilteredSizesAsync(IReadOnlyList<string> paths, string pattern,
                                                   CancellationToken ct)
    {
        if (paths.Count == 0) return;
        var direct = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (ct.IsCancellationRequested) return;
            long size = await Task.Run(() => CalcFilteredDirect(path, pattern, ct), ct).ConfigureAwait(false);
            direct[path] = size;
        }
        if (ct.IsCancellationRequested) return;

        var aggregated = new Dictionary<string, long>(direct, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.OrderByDescending(p => p.Length))
        {
            if (ct.IsCancellationRequested) return;
            var parent = Path.GetDirectoryName(path);
            if (parent != null && aggregated.ContainsKey(parent))
                aggregated[parent] += aggregated[path];
        }
        lock (_sizeLock) { foreach (var kvp in aggregated) _filteredFolderSizes[kvp.Key] = kvp.Value; }
        if (!ct.IsCancellationRequested) SafeInvoke(() => UpdateTreeNodesWithFilter(treeView.Nodes));
    }

    private static long CalcFilteredDirect(string path, string pattern, CancellationToken ct)
    {
        long sum = 0;
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.None };
            foreach (var fi in new DirectoryInfo(path).EnumerateFiles("*", opts))
            {
                if (ct.IsCancellationRequested) return sum;
                if (MatchesFilter(fi.Name, pattern)) sum += fi.Length;
            }
        }
        catch { }
        return sum;
    }

    // Supports multiple patterns separated by semicolons, e.g. "*.log;*.txt"
    private static bool MatchesFilter(string name, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        foreach (var part in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sb = new System.Text.StringBuilder("^");
            foreach (char c in part)
                sb.Append(c switch { '*' => ".*", '?' => ".", _ => Regex.Escape(c.ToString()) });
            sb.Append('$');
            if (Regex.IsMatch(name, sb.ToString(), RegexOptions.IgnoreCase)) return true;
        }
        return false;
    }

    private void CollectTreePaths(TreeNodeCollection nodes, List<string> paths)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is string p) paths.Add(p);
            if (n.IsExpanded) CollectTreePaths(n.Nodes, paths);
        }
    }

    private void UpdateTreeNodesWithFilter(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is string path && !_driveProgress.ContainsKey(path))
            {
                bool scanned;
                long size;
                lock (_sizeLock) scanned = _folderSizes.TryGetValue(path, out size);
                if (scanned)
                {
                    var name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) name = path;
                    n.Text = FormatDirLabel(name, size, GetFilteredSize(path));
                }
            }
            if (n.IsExpanded) UpdateTreeNodesWithFilter(n.Nodes);
        }
    }

    private void RestoreTreeNodeLabels(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is string path)
            {
                bool scanned;
                long size;
                lock (_sizeLock) scanned = _folderSizes.TryGetValue(path, out size);
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path.TrimEnd(Path.DirectorySeparatorChar);

                if (_driveProgress.TryGetValue(path, out var dp) && dp.TotalCapacity > 0 && size > 0)
                {
                    int pct = (int)Math.Min(100, size * 100L / dp.TotalCapacity);
                    n.Text = $"{name}  ({FormatSize(dp.TotalCapacity)}/{FormatSize(size)}) {pct}%";
                }
                else if (scanned) n.Text = FormatDirLabel(name, size);
            }
            if (n.IsExpanded) RestoreTreeNodeLabels(n.Nodes);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FileSystemWatcher
    // ─────────────────────────────────────────────────────────────────────────

    private void WatchCurrentPath(string? path)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (path == null || !Directory.Exists(path)) return;
        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter        = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnWatcherEvent;
            _watcher.Created += OnWatcherEvent;
            _watcher.Deleted += OnWatcherEvent;
            _watcher.Renamed += OnWatcherRenamed;
        }
        catch { }
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        var parent = Path.GetDirectoryName(e.FullPath);
        if (parent != null) lock (_sizeLock) { _folderSizes.Remove(parent); _folderSizes.Remove(e.FullPath); }
        ScheduleListRefresh();
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        lock (_sizeLock)
        {
            _folderSizes.Remove(e.OldFullPath);
            var parent = Path.GetDirectoryName(e.FullPath);
            if (parent != null) _folderSizes.Remove(parent);
        }
        ScheduleListRefresh();
    }

    private void ScheduleListRefresh()
    {
        SafeInvoke(() =>
        {
            if (_watcherDebounce == null)
            {
                _watcherDebounce = new System.Windows.Forms.Timer { Interval = 600 };
                _watcherDebounce.Tick += (_, _) =>
                {
                    _watcherDebounce!.Stop();
                    if (_currentPath != null) PopulateListView(_currentPath);
                };
            }
            _watcherDebounce.Stop();
            _watcherDebounce.Start();
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Column header owner-draw
    // ─────────────────────────────────────────────────────────────────────────

    private void ListView_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var backBrush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);
        var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, e.Font ?? listView.Font, textRect,
            ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var border = new Pen(SystemColors.ControlDark);
        e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        e.Graphics.DrawLine(border, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
    }

    private void BtnBack_Click(object? sender, EventArgs e)    => NavigateBack();
    private void BtnForward_Click(object? sender, EventArgs e) => NavigateForward();
    private void BtnUp_Click(object? sender, EventArgs e)      => NavigateUp();
    private void ListView_DrawItem(object? sender, DrawListViewItemEventArgs e)       => e.DrawDefault = true;
    private void ListView_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e) => e.DrawDefault = true;

    // ─────────────────────────────────────────────────────────────────────────
    //  Shell icons
    // ─────────────────────────────────────────────────────────────────────────

    private int GetShellIconIndex(string path, bool isFolder)
    {
        // Key by extension for files, special key for folders and drives
        var key = isFolder ? (Path.GetPathRoot(path) == path ? "\x00drive" : "\x00folder")
                           : (Path.GetExtension(path).ToLowerInvariant() is { Length: > 0 } ext ? ext : "\x00file");
        lock (_iconLock)
        {
            if (_iconCache.TryGetValue(key, out int cached)) return cached;

            var info  = new SHFILEINFO();
            uint attr = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            SHGetFileInfo(path, attr, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                          SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

            int idx = isFolder ? 1 : 2; // fallback
            if (info.hIcon != IntPtr.Zero)
            {
                try
                {
                    using var icon = Icon.FromHandle(info.hIcon);
                    imageList.Images.Add(icon.ToBitmap());
                    idx = imageList.Images.Count - 1;
                }
                catch { }
                finally { DestroyIcon(info.hIcon); }
            }

            _iconCache[key] = idx;
            return idx;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Column persistence
    // ─────────────────────────────────────────────────────────────────────────

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileExplorer", "settings.json");

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var s = new AppSettings(
                listView.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray(),
                _sortCol, _sortDir.ToString(),
                _currentPath,
                splitContainer.SplitterDistance);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    private AppSettings? LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
        }
        catch { }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Utility helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsPlaceholder(TreeNode n) =>
        n.Nodes.Count == 1 && n.Nodes[0].Tag is null;

    private static TreeNode Placeholder() => new("…") { Tag = null };

    private static bool CanRead(string path)
    {
        try { Directory.EnumerateFileSystemEntries(path).GetEnumerator().MoveNext(); return true; }
        catch { return false; }
    }

    private static bool HasSubDirs(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path, "*",
                new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.None }).Any();
        }
        catch { return false; }
    }

    private static bool SamePath(string? a, string? b) =>
        a != null && b != null &&
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar),
                      b.TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    // size < 0  → not yet scanned, show name only
    // size >= 0 → scanned (0 = empty/inaccessible), always show [filtered/total]
    private static string FormatDirLabel(string name, long size, long filteredSize = -1)
    {
        if (size < 0) return name;
        long fs = filteredSize >= 0 ? filteredSize : size;
        return $"{name}  [{FormatSize(fs)}/{FormatSize(size)}]";
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F2} MB",
        >= 1_024         => $"{bytes / 1_024.0:F2} KB",
        _                => $"{bytes} B"
    };

    private static string GetFileType(string ext) =>
        string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpper() + " File";

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void CancelAndResetCts()
    {
        _cts.Cancel(); _cts.Dispose(); _cts = new CancellationTokenSource();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        splitContainer.Panel1MinSize    = 150;
        splitContainer.Panel2MinSize    = 300;

        // Restore persisted settings
        var saved = LoadSettings();
        splitContainer.SplitterDistance = saved?.SplitterDistance ?? 300;

        int dateWidth = TextRenderer.MeasureText("2000-00-00  00:00:00", listView.Font).Width + 16;
        if (listView.Columns[1].Width < dateWidth) listView.Columns[1].Width = dateWidth;
        int sizeWidth = TextRenderer.MeasureText("Not Accessible", listView.Font).Width + 16;
        if (listView.Columns[2].Width < sizeWidth) listView.Columns[2].Width = sizeWidth;

        if (saved?.ColumnWidths is { Length: 4 } cw)
            for (int i = 0; i < 4; i++) listView.Columns[i].Width = cw[i];

        if (saved != null)
        {
            _sortCol = saved.SortCol;
            if (Enum.TryParse<SortOrder>(saved.SortDir, out var sd)) _sortDir = sd;
        }

        _baseColWidths = listView.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();

        int formChrome = Width - ClientSize.Width;
        int lvBorder   = listView.Width - listView.ClientSize.Width;
        MinimumSize = new Size(
            formChrome + splitContainer.Panel1MinSize + splitContainer.SplitterWidth
            + _baseColWidths.Sum() + SystemInformation.VerticalScrollBarWidth + lvBorder,
            MinimumSize.Height);

        RedistributeColumns();
        listView.ClientSizeChanged += (_, _) => RedistributeColumns();

        SetWindowTheme(progressBar.ProgressBar.Handle, "", "");

        // ListView doesn't expose DoubleBuffered publicly; set it via reflection.
        typeof(ListView)
            .GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(listView, true);

        // Style nav bar to match system
        navBar.BackColor   = SystemColors.Control;
        btnBack.BackColor  = SystemColors.Control;
        btnForward.BackColor = SystemColors.Control;
        btnUp.BackColor    = SystemColors.Control;
        UpdateNavButtons();

        // Navigate to last path or show drives
        if (saved?.LastPath != null && Directory.Exists(saved.LastPath))
            NavigateTo(saved.LastPath);
        else
            ShowDrivesInListView();

        scanStatusLabel.Text = "Processing";
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    private void RedistributeColumns()
    {
        if (_redistributing || _baseColWidths.Length == 0) return;
        _redistributing = true;
        try
        {
            int available = listView.ClientSize.Width;
            int baseTotal = _baseColWidths.Sum();
            int extra     = Math.Max(0, available - baseTotal);
            int perCol    = extra / _baseColWidths.Length;
            int remainder = extra % _baseColWidths.Length;
            for (int i = 0; i < _baseColWidths.Length; i++)
                listView.Columns[i].Width = _baseColWidths[i] + perCol + (i < remainder ? 1 : 0);
        }
        finally { _redistributing = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts.Cancel();
        _filterCts.Cancel();
        _watcher?.Dispose();
        _filterDebounce?.Dispose();
        _watcherDebounce?.Dispose();
        SaveSettings();
        base.OnFormClosing(e);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  P/Invoke
    // ─────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    private const uint SHGFI_ICON             = 0x000000100;
    private const uint SHGFI_SMALLICON        = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL   = 0x000000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x000000010;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint   wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string  pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        public bool   fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    private const uint   FO_DELETE   = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Settings record (column persistence)
// ─────────────────────────────────────────────────────────────────────────────
internal record AppSettings(
    int[]   ColumnWidths,
    int     SortCol,
    string  SortDir,
    string? LastPath,
    int     SplitterDistance);

// ─────────────────────────────────────────────────────────────────────────────
//  Per-drive scan progress tracker
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class DriveProgress
{
    public int  Total     = 1;
    public int  Processed;
    public volatile bool Complete;
    public long TotalCapacity;
    public long TotalBytes;
    public long ScannedBytes;

    public int Percent =>
        Complete ? 100 : TotalBytes > 0 ? Math.Min(99, (int)(ScannedBytes * 100 / TotalBytes)) : 0;
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
        if (aDir != bDir) return aDir ? -1 : 1;
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
        DateTime.TryParse(x, out var dx); DateTime.TryParse(y, out var dy);
        return dx.CompareTo(dy);
    }

    private static int CmpSize(string x, string y) => ToBytes(x).CompareTo(ToBytes(y));

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
