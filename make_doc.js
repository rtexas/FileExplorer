const fs   = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  HeadingLevel, AlignmentType, BorderStyle, WidthType, ShadingType,
  LevelFormat, PageNumber, Header, Footer, TabStopType, TabStopPosition
} = require("docx");

// ── Palette ────────────────────────────────────────────────────────────────
const BLUE    = "1F4E79";
const LBLUE   = "2E75B6";
const GREEN   = "375623";
const SILVER  = "D6DCE4";
const CODE_BG = "F2F2F2";
const WHITE   = "FFFFFF";

// ── Helpers ────────────────────────────────────────────────────────────────
const thinBorder = (color = "AAAAAA") => ({ style: BorderStyle.SINGLE, size: 1, color });
const allBorders = (color = "AAAAAA") => ({
  top: thinBorder(color), bottom: thinBorder(color),
  left: thinBorder(color), right: thinBorder(color)
});

function h1(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_1,
    spacing: { before: 320, after: 160 },
    children: [new TextRun({ text, bold: true, color: WHITE, size: 28 })],
    shading: { fill: BLUE, type: ShadingType.CLEAR },
    indent: { left: 120, right: 120 }
  });
}
function h2(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_2,
    spacing: { before: 240, after: 100 },
    border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: LBLUE, space: 4 } },
    children: [new TextRun({ text, bold: true, color: LBLUE, size: 24 })]
  });
}
function h3(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_3,
    spacing: { before: 180, after: 60 },
    children: [new TextRun({ text, bold: true, color: "404040", size: 22 })]
  });
}
function body(text, opts = {}) {
  return new Paragraph({
    spacing: { before: 60, after: 60 },
    children: [new TextRun({ text, size: 20, ...opts })]
  });
}
function bullet(text, level = 0) {
  return new Paragraph({
    numbering: { reference: "bullets", level },
    spacing: { before: 40, after: 40 },
    children: [new TextRun({ text, size: 20 })]
  });
}
function gap(lines = 1) {
  return Array.from({ length: lines }, () =>
    new Paragraph({ spacing: { before: 0, after: 0 }, children: [new TextRun("")] })
  );
}
function codeBlock(source) {
  const lines = source.split("\n");
  return lines.map((line, i) =>
    new Paragraph({
      spacing: { before: 0, after: 0 },
      shading: { fill: CODE_BG, type: ShadingType.CLEAR },
      border: i === 0
        ? { top: thinBorder("CCCCCC"), left: thinBorder("CCCCCC"), right: thinBorder("CCCCCC") }
        : i === lines.length - 1
          ? { bottom: thinBorder("CCCCCC"), left: thinBorder("CCCCCC"), right: thinBorder("CCCCCC") }
          : { left: thinBorder("CCCCCC"), right: thinBorder("CCCCCC") },
      indent: { left: 180, right: 180 },
      children: [new TextRun({ text: line || " ", font: "Courier New", size: 16 })]
    })
  );
}
function kvTable(rows, col1W = 3000, col2W = 6360) {
  return new Table({
    width: { size: col1W + col2W, type: WidthType.DXA },
    columnWidths: [col1W, col2W],
    rows: rows.map(([k, v]) => new TableRow({
      children: [
        new TableCell({
          borders: allBorders(), width: { size: col1W, type: WidthType.DXA },
          shading: { fill: SILVER, type: ShadingType.CLEAR },
          margins: { top: 80, bottom: 80, left: 120, right: 120 },
          children: [new Paragraph({ children: [new TextRun({ text: k, bold: true, size: 18 })] })]
        }),
        new TableCell({
          borders: allBorders(), width: { size: col2W, type: WidthType.DXA },
          margins: { top: 80, bottom: 80, left: 120, right: 120 },
          children: [new Paragraph({ children: [new TextRun({ text: v, size: 18 })] })]
        })
      ]
    }))
  });
}
function dataTable(headers, rows, colWidths, headerColor = LBLUE) {
  const total = colWidths.reduce((a, b) => a + b, 0);
  const makeRow = (cells, isHeader) => new TableRow({
    tableHeader: isHeader,
    children: cells.map((cell, ci) => new TableCell({
      borders: allBorders(),
      width: { size: colWidths[ci], type: WidthType.DXA },
      shading: isHeader ? { fill: headerColor, type: ShadingType.CLEAR } : undefined,
      margins: { top: 80, bottom: 80, left: 120, right: 120 },
      children: [new Paragraph({
        children: [new TextRun({ text: cell, bold: isHeader,
          color: isHeader ? WHITE : "000000", size: 18 })]
      })]
    }))
  });
  return new Table({
    width: { size: total, type: WidthType.DXA },
    columnWidths: colWidths,
    rows: [makeRow(headers, true), ...rows.map(r => makeRow(r, false))]
  });
}

// ── Read source files ──────────────────────────────────────────────────────
const base = "C:\\Users\\RandyTaylor\\Claude\\FileExplorer";
const src = {
  csproj:   fs.readFileSync(path.join(base, "FileExplorer.csproj"),    "utf8"),
  program:  fs.readFileSync(path.join(base, "Program.cs"),             "utf8"),
  mainForm: fs.readFileSync(path.join(base, "MainForm.cs"),            "utf8"),
  designer: fs.readFileSync(path.join(base, "MainForm.Designer.cs"),   "utf8"),
  manifest: fs.readFileSync(path.join(base, "app.manifest"),           "utf8"),
};

// ── Document body ─────────────────────────────────────────────────────────
const children = [

  // ── Title ──────────────────────────────────────────────────────────────
  new Paragraph({
    spacing: { before: 2880, after: 120 }, alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: "FileExplorer", bold: true, size: 64, color: BLUE })]
  }),
  new Paragraph({
    spacing: { before: 0, after: 120 }, alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: "C# WinForms Application", size: 36, color: LBLUE })]
  }),
  new Paragraph({
    spacing: { before: 0, after: 120 }, alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: "Technical Reference Document", size: 28, color: "606060" })]
  }),
  new Paragraph({
    spacing: { before: 240, after: 0 }, alignment: AlignmentType.CENTER,
    children: [new TextRun({
      text: `Generated: ${new Date().toLocaleDateString("en-US",{year:"numeric",month:"long",day:"numeric"})}`,
      size: 20, color: "808080"
    })]
  }),
  ...gap(2),
  new Paragraph({ pageBreakBefore: true, children: [new TextRun("")] }),

  // ══════════════════════════════════════════════════════════════════════
  // 1  OVERVIEW
  // ══════════════════════════════════════════════════════════════════════
  h1("1  Project Overview"),
  ...gap(),
  body("FileExplorer is a read-only, two-pane Windows desktop application built with C# and Windows Forms (.NET 9). It provides a File Explorer-style interface for browsing local drives, folders, and files, with recursive folder-size calculation running in the background and a live scan-progress bar in the status strip."),
  ...gap(),
  kvTable([
    ["Solution name",    "FileExplorer"],
    ["Language",         "C# 13  (.NET 9)"],
    ["UI framework",     "Windows Forms  (WinForms)"],
    ["Target framework", "net9.0-windows"],
    ["IDE",              "Visual Studio 2026"],
    ["Build config",     "Release  |  Debug"],
    ["Output type",      "WinExe  (no console window)"],
    ["DPI mode",         "PerMonitorV2  (set in .csproj)"],
    ["Output path",      "bin\\Release\\net9.0-windows\\FileExplorer.exe"],
  ]),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 2  REQUIREMENTS
  // ══════════════════════════════════════════════════════════════════════
  h1("2  Requirements & Design Decisions"),
  ...gap(),
  h2("2.1  Functional Requirements"),
  ...gap(),
  dataTable(
    ["Feature", "Specification"],
    [
      ["UI layout",            "Two-pane SplitContainer: TreeView (left) + ListView (right)"],
      ["Drive scope",          "Fixed and Removable drives only — network drives excluded"],
      ["Left pane",            "TreeView showing drives expanding into folders; each node shows [size]"],
      ["Right pane",           "ListView in Details mode: Name, Date Modified, Size, Type columns"],
      ["Folder sizes",         "Recursive totals calculated upfront in background Tasks; tree and list update live"],
      ["File date column",     "Last Modified date (LastWriteTime), formatted yyyy-MM-dd  HH:mm:ss"],
      ["Hidden/system files",  "Shown — AttributesToSkip = FileAttributes.None"],
      ["Access-denied items",  "Displayed in grey (SystemColors.GrayText); Size column shows 'Not Accessible' instead of 'calculating…'"],
      ["Default sort",         "Descending; folders always pinned above files"],
      ["Sort interaction",     "Click column header → descending first; click again → ascending; ▲/▼ arrow shown"],
      ["Double-click folder",  "Navigates into folder in right pane and expands/selects node in tree"],
      ["Drives view",          "At startup and after Refresh, the right pane shows all drives: name, size (calculating… live-updated), type (Local / Removable Disk). Double-click navigates into the drive."],
      ["Refresh",              "Toolbar ⟳ Refresh [F5] button and F5 keyboard shortcut"],
      ["File operations",      "Read-only — no copy, move, delete, or rename"],
      ["Size display",         "Auto-scaled: B / KB / MB / GB (2 decimal places)"],
      ["Scan progress bar",    "ToolStripProgressBar, byte-based progress: ScannedBytes / TotalBytes (drive used space). Label shows 'Processing' during scan, 'Scan complete ✓' when done. Flat (non-themed) visual style. Resets on Refresh."],
      ["Column auto-sizing",   "Date Modified and Size column widths measured via TextRenderer in OnLoad. All four columns grow equally to fill the list pane width; redistributed on every resize and ClientSizeChanged event."],
      ["Minimum form width",   "Computed in OnLoad: chrome + min-tree-pane + splitter + column totals + scrollbar + list-border so all columns are always fully visible."],
    ],
    [3600, 5760]
  ),
  ...gap(),
  h2("2.2  Non-Functional Requirements"),
  bullet("UI must remain responsive while sizes are being calculated (async Tasks + BeginInvoke)."),
  bullet("Background walks are cancellable — CancellationTokenSource reset on every Refresh."),
  bullet("Thread-safe size dictionary access via lock(_sizeLock)."),
  bullet("Thread-safe progress counters via Interlocked.Add (ScannedBytes) and Interlocked.Increment (Total/Processed); read with Volatile.Read."),
  bullet("Background-to-UI marshalling via SafeInvoke() — checks IsDisposed/IsHandleCreated and catches ObjectDisposedException / InvalidOperationException to handle disposal races when the user closes the form mid-scan."),
  bullet("RedistributeColumns() uses a _redistributing bool guard to prevent infinite recursion via ClientSizeChanged (see Bug 3 in Section 7)."),
  bullet("DPI-aware at PerMonitorV2 level — configured via ApplicationHighDpiMode in .csproj."),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 3  PROJECT STRUCTURE
  // ══════════════════════════════════════════════════════════════════════
  h1("3  Project Structure"),
  ...gap(),
  dataTable(
    ["File", "Role"],
    [
      ["FileExplorer.csproj",  "MSBuild project — target framework, WinForms, DPI mode, manifest reference"],
      ["app.manifest",         "Windows application manifest — execution level (asInvoker), OS compatibility"],
      ["Program.cs",           "Entry point — ApplicationConfiguration.Initialize() + Application.Run(MainForm)"],
      ["MainForm.cs",          "All application logic: drive loading, async size calc, progress tracking, navigation, sorting, refresh"],
      ["MainForm.Designer.cs", "UI wiring: control declarations, InitializeComponent(), inline Icons factory class"],
    ],
    [2800, 6560]
  ),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 4  ARCHITECTURE
  // ══════════════════════════════════════════════════════════════════════
  h1("4  Architecture & Key Design Patterns"),
  ...gap(),
  h2("4.1  Two-Pane Layout"),
  body("A SplitContainer (Dock = Fill) divides the form. Panel1 holds the TreeView; Panel2 holds the ListView. The splitter is 5 px wide and is set to 300 px distance in OnLoad (not in InitializeComponent) to avoid a WinForms validation exception — see Section 7."),
  ...gap(),
  h2("4.2  Lazy Tree Expansion with Eager Size Calculation"),
  body("Tree nodes use a single placeholder child node (Tag = null, text \"…\") to show the expand arrow without loading the full subtree. When the user expands a node, TreeView_BeforeExpand fires, detects the placeholder via IsPlaceholder(), clears it, and calls PopulateTreeChildren()."),
  ...gap(),
  body("In parallel, on app start and on every Refresh, one background Task per drive is spawned (DriveWalkAsync → CalcDir). CalcDir walks the entire drive tree recursively, stores each subdirectory size in _folderSizes, and calls BeginInvoke after each subfolder is computed to update tree labels, list sizes, and the progress bar in real time."),
  ...gap(),
  h2("4.3  Thread Safety"),
  bullet("_folderSizes is protected by lock(_sizeLock) for all reads and writes."),
  bullet("DriveProgress.Total and .Processed use Interlocked.Increment for atomic writes from background threads."),
  bullet("DriveProgress.ScannedBytes is accumulated via Interlocked.Add per file; TotalBytes is set once in LoadDrives."),
  bullet("DriveProgress fields are read with Volatile.Read in UpdateProgressBar() on the UI thread."),
  bullet("All UI mutations from background threads go through SafeInvoke(), which checks IsDisposed/IsHandleCreated and wraps BeginInvoke in try/catch for ObjectDisposedException and InvalidOperationException."),
  bullet("CancelAndResetCts() cancels, disposes, and replaces the CancellationTokenSource on every Refresh."),
  ...gap(),
  h2("4.4  Navigation Flow"),
  body("NavigateTo(path, fromTree) is the single entry point for all folder navigation:"),
  bullet("Sets _drivesView = false (leaving drives view)."),
  bullet("Sets _currentPath."),
  bullet("Calls PopulateListView(path) to fill the right pane."),
  bullet("If navigation originated from the right pane (double-click), calls ExpandTreeTo(path) to sync the tree."),
  bullet("Calls UpdateProgressBar() to immediately reflect the correct drive's progress percentage."),
  ...gap(),
  h2("4.5  Sorting"),
  body("ApplySort() sets listView.ListViewItemSorter to an LvSorter instance, calls Sort(), then nulls the sorter so WinForms does not re-sort on every item add. Folders are always ranked above files. Column headers display ▲/▼ arrows. Clicking the active column toggles direction; clicking a new column resets to Descending."),
  ...gap(),
  h2("4.6  Inline Icon Factory (Icons class)"),
  body("The Icons static class in MainForm.Designer.cs generates three 16×16 Bitmap icons at runtime using System.Drawing primitives — no embedded resource files needed. Images are stored in an ImageList shared by both the TreeView and the ListView:"),
  bullet("Index 0 — Drive: silver rectangle + two grey wheel ellipses + blue activity bar."),
  bullet("Index 1 — Folder: gold tab + gold body rectangle."),
  bullet("Index 2 — File: white document polygon + folded corner lines."),
  ...gap(),
  h2("4.7  Scan Progress Tracking"),
  body("Each drive gets a DriveProgress instance (stored in _driveProgress, keyed by drive root path). Progress tracking is byte-based: TotalBytes holds the drive's used space (DriveInfo.TotalSize − TotalFreeSpace) and ScannedBytes accumulates via Interlocked.Add for every file processed in CalcDir(). This produces a bar that starts near 0% and grows proportionally to real disk work done, avoiding the artificial spikes of a directory-count approach."),
  ...gap(),
  dataTable(
    ["Field", "Type", "Description"],
    [
      ["Total",       "int",              "Directories discovered. Starts at 1 (root). Incremented via Interlocked.Increment before each recursive CalcDir call."],
      ["Processed",   "int",              "Directories fully computed. Incremented after CalcDir returns for each subdir; root incremented by DriveWalkAsync."],
      ["Complete",    "volatile bool",    "Set to true by DriveWalkAsync when the root-level CalcDir returns."],
      ["TotalBytes",  "long",             "Drive used bytes (TotalSize − TotalFreeSpace). Denominator for progress. Set once in LoadDrives() from DriveInfo."],
      ["ScannedBytes","long",             "Running byte total across all scanned files. Incremented atomically via Interlocked.Add per file in CalcDir(). Read with Volatile.Read in UpdateProgressBar()."],
    ],
    [1440, 1440, 6480]
  ),
  ...gap(),
  body("Progress % = Complete ? 100 : Min(99, ScannedBytes × 100 / TotalBytes). Capped at 99% until Complete is set, preventing a false 100% before the root walk finishes. The scanStatusLabel shows 'Processing' while scanning and 'Scan complete ✓' when dp.Complete is true."),
  ...gap(),
  body("UpdateProgressBar() is called from three places:"),
  bullet("Inside every SafeInvoke callback in CalcDir — bar updates after each subdirectory completes."),
  bullet("In DriveWalkAsync's final SafeInvoke — stamps 100% and 'Scan complete ✓' when the drive finishes."),
  bullet("In NavigateTo() — switching drives immediately shows that drive's current percentage."),
  ...gap(),
  body("When _currentPath is null (drives view), UpdateProgressBar() shows aggregate progress across all drives."),
  ...gap(),
  h2("4.8  Column Auto-Sizing & Distribution"),
  body("In OnLoad(), after the form has real dimensions and the window handle exists:"),
  bullet("Date Modified: TextRenderer.MeasureText('2000-00-00  00:00:00', font) + 16 px. All-digit sample gives a conservative, never-too-narrow width."),
  bullet("Size: TextRenderer.MeasureText('Not Accessible', font) + 16 px."),
  bullet("_baseColWidths[] snapshots all four measured widths as the content-minimum floor."),
  bullet("MinimumSize is derived: form chrome + Panel1MinSize + splitter + sum(_baseColWidths) + VerticalScrollBarWidth + ListView border."),
  bullet("RedistributeColumns() divides any extra ClientSize.Width evenly among all four columns (integer remainder distributed left-to-right one pixel at a time)."),
  bullet("Called initially in OnLoad, then wired to listView.ClientSizeChanged so resize and splitter drag both trigger it. A _redistributing bool guards against re-entrant calls."),
  body("Note: ClientSize.Width already excludes the vertical scrollbar when visible, so no manual VerticalScrollBarWidth subtraction is needed inside RedistributeColumns."),
  ...gap(),
  h2("4.9  Drives View (This PC)"),
  body("ShowDrivesInListView() populates the right pane with one row per fixed/removable drive (drive icon, label, empty date, 'calculating…' size updated live, 'Local Disk' or 'Removable Disk' type). It must be called from OnLoad() (not from the constructor via LoadDrives) because the ListView's ClientSize is zero before the window handle is created. DoRefresh() calls it directly when no folder path was previously selected."),
  body("RefreshListSizes() checks _drivesView first: when true it updates drive row sizes from _folderSizes; otherwise it updates folder rows in the current directory as usual."),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 5  CLASS & METHOD REFERENCE
  // ══════════════════════════════════════════════════════════════════════
  h1("5  Class & Method Reference"),
  ...gap(),
  h2("5.1  MainForm  (partial — MainForm.cs)"),
  ...gap(),
  dataTable(
    ["Member", "Type", "Description"],
    [
      ["_folderSizes",           "Dictionary<string,long>",          "Maps absolute path → recursive byte total. OrdinalIgnoreCase comparer."],
      ["_sizeLock",              "object",                           "Monitor lock for _folderSizes access from background threads."],
      ["_cts",                   "CancellationTokenSource",          "Controls in-flight background size-walk tasks."],
      ["_currentPath",           "string?",                          "Path currently displayed in the right pane; null when in drives view."],
      ["_sortCol",               "int",                              "Index of the active sort column (0=Name,1=Date,2=Size,3=Type)."],
      ["_sortDir",               "SortOrder",                        "Current sort direction; defaults to Descending."],
      ["_driveProgress",         "Dictionary<string,DriveProgress>", "Per-drive scan progress trackers; keyed by drive root path. OrdinalIgnoreCase."],
      ["_baseColWidths",         "int[]",                            "Content-minimum widths for all four columns, snapshotted in OnLoad() after TextRenderer measurement. Floor for RedistributeColumns()."],
      ["_redistributing",        "bool",                             "Re-entrancy guard for RedistributeColumns(). Prevents infinite recursion via ClientSizeChanged."],
      ["_drivesView",            "bool",                             "True when the right pane is showing the drives list (This PC). Set by ShowDrivesInListView(); cleared by NavigateTo()."],
      ["LoadDrives()",           "void",                             "Clears tree and progress, initialises a DriveProgress (with TotalBytes) per drive, spawns background Tasks. Does NOT call ShowDrivesInListView — that is deferred to OnLoad/DoRefresh."],
      ["DriveWalkAsync()",       "async Task",                       "Awaits CalcDir for a drive root, marks DriveProgress.Complete, updates drive node and progress bar via SafeInvoke."],
      ["CalcDir()",              "long",                             "Recursive sync method: sums file lengths, adds each file's bytes to DriveProgress.ScannedBytes via Interlocked.Add, increments Total/Processed per subdir, stores in _folderSizes, marshals UI updates via SafeInvoke."],
      ["SafeInvoke()",           "void",                             "Checks IsDisposed/IsHandleCreated then calls BeginInvoke inside a try/catch for ObjectDisposedException and InvalidOperationException. Used by all background threads instead of raw BeginInvoke."],
      ["UpdateProgressBar()",    "void",                             "Reads Volatile DriveProgress ScannedBytes/TotalBytes for the current drive (or aggregate). Sets progressBar.Value and scanStatusLabel.Text ('Processing' or 'Scan complete ✓')."],
      ["ShowDrivesInListView()", "void",                             "Populates ListView with one row per fixed/removable drive. Sets _drivesView=true, _currentPath=null. Must be called from OnLoad or DoRefresh (not constructor)."],
      ["RedistributeColumns()",  "void",                             "Distributes extra listView.ClientSize.Width equally among columns. Uses _redistributing guard to prevent re-entrancy. Called initially in OnLoad and on ClientSizeChanged."],
      ["SetWindowTheme()",       "extern int",                       "P/Invoke into uxtheme.dll. Called in OnLoad with ('', '') to strip visual styles from the progress bar for a flat appearance."],
      ["TreeView_BeforeExpand",  "event handler",                    "Replaces placeholder child with real subdirectory nodes via PopulateTreeChildren()."],
      ["TreeView_AfterSelect",   "event handler",                    "Calls NavigateTo() for the selected node path."],
      ["PopulateTreeChildren()", "void",                             "Reads subdirs, creates TreeNode per dir (greyed if inaccessible, placeholder if has subdirs)."],
      ["NavigateTo()",           "void",                             "Sets _drivesView=false, sets _currentPath, calls PopulateListView, optionally ExpandTreeTo, then UpdateProgressBar."],
      ["PopulateListView()",     "void",                             "Fills ListView with subdirs (Not Accessible for unreadable folders) then files; calls ApplySort(); updates status bar; calls RedistributeColumns() after EndUpdate."],
      ["ListView_ColumnClick",   "event handler",                    "Toggles or changes sort column/direction, calls ApplySort()."],
      ["ListView_DoubleClick",   "event handler",                    "If selected item is a directory (folder or drive), calls NavigateTo()."],
      ["ApplySort()",            "void",                             "Updates column header arrows, assigns LvSorter, calls Sort(), clears sorter."],
      ["DoRefresh()",            "void",                             "Saves _currentPath, calls LoadDrives() (resets progress bar). If saved path exists, re-navigates to it; otherwise calls ShowDrivesInListView()."],
      ["ExpandTreeTo()",         "void",                             "Walks ancestor chain, force-populates placeholder nodes, expands and selects."],
      ["UpdateNodeText()",       "void",                             "Updates a specific TreeNode text with name + [size]."],
      ["UpdateNodeTextByPath()", "void",                             "Walks visible tree nodes to find and update a node by path string."],
      ["RefreshListSizes()",     "void",                             "If _drivesView: updates drive row sizes from _folderSizes. Otherwise re-reads _folderSizes for all Folder rows in the current ListView."],
      ["FormatSize()",           "static string",                    "Converts bytes to human-readable B/KB/MB/GB string (2 dp)."],
      ["SamePath()",             "static bool",                      "Case-insensitive path comparison, trimming trailing backslashes."],
      ["CanRead()",              "static bool",                      "Tries EnumerateFileSystemEntries; returns false on any exception."],
      ["HasSubDirs()",           "static bool",                      "Tries EnumerateDirectories with AttributesToSkip=None; returns bool."],
      ["CancelAndResetCts()",    "void",                             "Cancels, disposes, and replaces _cts with a fresh CancellationTokenSource."],
      ["OnLoad()",               "override void",                    "Sets SplitContainer sizing; measures column widths via TextRenderer; snapshots _baseColWidths; computes MinimumSize; calls RedistributeColumns; subscribes to ClientSizeChanged; calls SetWindowTheme on progress bar; calls ShowDrivesInListView; sets scanStatusLabel to 'Processing'."],
      ["OnFormClosing()",        "override void",                    "Cancels _cts to stop background walks before the form closes."],
    ],
    [2200, 1700, 5460]
  ),
  ...gap(),
  h2("5.2  DriveProgress  (MainForm.cs — file-level class)"),
  body("Tracks per-drive scan progress. Instantiated in LoadDrives() for each fixed/removable drive and stored in _driveProgress. Fields are written from background threads via Interlocked.Increment and read on the UI thread via Volatile.Read."),
  ...gap(),
  dataTable(
    ["Member", "Description"],
    [
      ["int Total = 1",          "Total directories to process. Starts at 1 for the root. Grows as CalcDir discovers subdirs via Interlocked.Increment."],
      ["int Processed",          "Directories fully computed (CalcDir returned). Incremented for each subdir by CalcDir, and for the root itself by DriveWalkAsync."],
      ["volatile bool Complete",  "True when DriveWalkAsync marks the drive fully scanned."],
      ["long TotalBytes",         "Drive used bytes (TotalSize − TotalFreeSpace). Denominator for byte-based progress. Set once in LoadDrives() from DriveInfo."],
      ["long ScannedBytes",       "Running byte total across all scanned files. Incremented atomically via Interlocked.Add per file in CalcDir(). Read with Volatile.Read in UpdateProgressBar()."],
      ["int Percent { get }",     "Complete ? 100 : Min(99, ScannedBytes * 100 / TotalBytes). Capped at 99% until Complete is set, preventing a false 100% before the walk finishes."],
    ],
    [2800, 6560]
  ),
  ...gap(),
  h2("5.3  LvSorter  (MainForm.cs — file-level class)"),
  body("Implements IComparer for ListView. Constructor takes col (int) and dir (SortOrder). Folders always sort before files regardless of column. Columns: 0=name, 1=date (DateTime.Parse), 2=size (ToBytes converts formatted string back to double for numeric compare). Direction applied by negating the result for Descending."),
  ...gap(),
  h2("5.4  Icons  (MainForm.Designer.cs — file-level static class)"),
  body("Three static methods each return a new 16×16 Bitmap drawn with System.Drawing: Drive(), Folder(), File(). Called once during InitializeComponent and added to the shared ImageList."),
  ...gap(),
  h2("5.5  MainForm Controls  (partial — MainForm.Designer.cs)"),
  ...gap(),
  dataTable(
    ["Control", "Type", "Location / Purpose"],
    [
      ["toolStrip",       "ToolStrip",              "Top — holds btnRefresh"],
      ["btnRefresh",      "ToolStripButton",         "ToolStrip — ⟳ Refresh [F5]"],
      ["splitContainer",  "SplitContainer",          "Fill — left=treeView, right=listView"],
      ["treeView",        "TreeView",                "Panel1 — drive/folder hierarchy"],
      ["listView",        "ListView",                "Panel2 — folder contents in Details mode"],
      ["statusStrip",     "StatusStrip",             "Bottom — contains all four status items"],
      ["statusLabel",     "ToolStripStatusLabel",    "Status strip, Spring=true — shows current path"],
      ["scanStatusLabel", "ToolStripStatusLabel",    "Status strip — shows 'Scanning C:\\ NN%' or 'Scan complete ✓'"],
      ["progressBar",     "ToolStripProgressBar",    "Status strip — 160px wide, 0-100%, Continuous style"],
      ["statusSizeLabel", "ToolStripStatusLabel",    "Status strip — shows 'N folders, N files' count"],
      ["imageList",       "ImageList",               "Shared 16×16 icon list: [0]=drive [1]=folder [2]=file"],
    ],
    [1900, 2200, 5260]
  ),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 6  SOURCE CODE
  // ══════════════════════════════════════════════════════════════════════
  h1("6  Full Source Code"),
  ...gap(),
  h2("6.1  FileExplorer.csproj"),
  ...codeBlock(src.csproj),
  ...gap(),
  h2("6.2  app.manifest"),
  ...codeBlock(src.manifest),
  ...gap(),
  h2("6.3  Program.cs"),
  ...codeBlock(src.program),
  ...gap(),
  h2("6.4  MainForm.cs"),
  ...codeBlock(src.mainForm),
  ...gap(),
  h2("6.5  MainForm.Designer.cs"),
  ...codeBlock(src.designer),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 7  BUGS ENCOUNTERED & FIXES
  // ══════════════════════════════════════════════════════════════════════
  h1("7  Bugs Encountered & Fixes"),
  ...gap(),
  h2("Bug 1 — SplitterDistance InvalidOperationException"),
  ...gap(),
  kvTable([
    ["Exception",          "System.InvalidOperationException"],
    ["Message",            "SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize."],
    ["Source",             "System.Windows.Forms.SplitContainer.set_SplitterDistance / ApplyPanel2MinSize"],
    ["Call site",          "MainForm.InitializeComponent() → MainForm..ctor() → Program.Main()"],
    ["Root cause",         "Setting Panel2MinSize = 300 inside the object initializer fires WinForms validation while the SplitContainer has zero width. Width(0) - Panel2MinSize(300) = -300, making any SplitterDistance invalid."],
    ["First fix attempt",  "Moved only SplitterDistance to OnLoad — still failed because Panel2MinSize = 300 in the initializer still triggered ApplyPanel2MinSize at zero width."],
    ["Final fix",          "Removed Panel1MinSize, Panel2MinSize, and SplitterDistance entirely from InitializeComponent(). All three are now set together in OnLoad() after the form has real pixel dimensions."],
  ]),
  ...gap(),
  h3("Fixed code in OnLoad():"),
  ...codeBlock(
`protected override void OnLoad(EventArgs e)
{
    base.OnLoad(e);
    // SplitContainer min/distance must be set after the form has real dimensions.
    splitContainer.Panel1MinSize    = 150;
    splitContainer.Panel2MinSize    = 300;
    splitContainer.SplitterDistance = 300;
}`),
  ...gap(),
  h2("Bug 2 — Progress Bar Jumps to ~90% at Startup"),
  ...gap(),
  kvTable([
    ["Symptom",      "Progress bar appeared 80–90% complete immediately on startup, then barely moved for a long time."],
    ["Root cause",   "Progress was computed as Processed / Total (directory count). CalcDir uses DFS; it discovers and processes leaf directories nearly simultaneously, so the counter spiked before most bytes were actually scanned."],
    ["Fix",          "Switched to byte-based tracking: TotalBytes = drive used space (set once in LoadDrives); ScannedBytes accumulated via Interlocked.Add for every file in CalcDir. Progress = ScannedBytes / TotalBytes, capped at 99% until Complete."],
    ["Key change",   "CalcDir: 'Interlocked.Add(ref dp.ScannedBytes, f.Length)' per file. DriveProgress gained TotalBytes and ScannedBytes fields."],
  ]),
  ...gap(),
  h2("Bug 3 — Form Hangs ('Not Responding') on Close During Scan"),
  ...gap(),
  kvTable([
    ["Symptom",      "Clicking the × close button while scanning showed the title bar 'Not Responding'; the app froze."],
    ["Root cause",   "RedistributeColumns() called listView.BeginUpdate()/EndUpdate(). EndUpdate() triggers an internal WinForms layout pass which fires ClientSizeChanged synchronously on the same call stack. ClientSizeChanged called RedistributeColumns() again → infinite recursion → stack overflow → UI thread hang."],
    ["Fix — part 1", "Removed BeginUpdate/EndUpdate from RedistributeColumns() (they are not needed for column-width changes only)."],
    ["Fix — part 2", "Added a '_redistributing' bool re-entrancy guard: RedistributeColumns() returns immediately if _redistributing is already true."],
    ["Fix — part 3", "Replaced all raw BeginInvoke calls in CalcDir/DriveWalkAsync with SafeInvoke(), which checks IsDisposed/IsHandleCreated and catches ObjectDisposedException/InvalidOperationException. This prevents disposal-race crashes when the user closes the form while background threads are still running."],
  ]),
  ...gap(),
  h2("Bug 4 — Drives View Empty / 'Processing' Label Missing at Startup"),
  ...gap(),
  kvTable([
    ["Symptom",      "Right pane was blank at startup. The 'Processing' scanStatusLabel was not shown. After scrolling or resizing, drives appeared with zero-width columns."],
    ["Root cause",   "ShowDrivesInListView() was called from LoadDrives(), which runs inside the form constructor. At constructor time the window handle does not exist, so listView.ClientSize.Width = 0 and _baseColWidths is empty. Items were added with zero-width columns and were invisible."],
    ["Fix",          "Removed the ShowDrivesInListView() call from LoadDrives(). It is now called from OnLoad() (where the handle exists and ClientSize is valid) and from DoRefresh() when no previous path is saved. scanStatusLabel.Text = 'Processing' is also set in OnLoad() to ensure it displays on startup."],
  ]),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 8  BUILD & RUN INSTRUCTIONS
  // ══════════════════════════════════════════════════════════════════════
  h1("8  Build & Run Instructions"),
  ...gap(),
  h2("8.1  Prerequisites"),
  bullet(".NET 9 SDK (or later) — confirmed available: 9.0.x, 10.0.x"),
  bullet("Windows OS — WinForms is Windows-only"),
  bullet("Visual Studio 2026 (or any edition supporting net9.0-windows)"),
  ...gap(),
  h2("8.2  Command-Line Build"),
  ...codeBlock(
`cd C:\\Users\\RandyTaylor\\Claude\\FileExplorer

# Release build (0 warnings, 0 errors)
dotnet build -c Release

# Run directly
dotnet run`),
  ...gap(),
  h2("8.3  Visual Studio"),
  bullet("Open FileExplorer.csproj in Visual Studio 2026."),
  bullet("Build → Clean Solution, then Build → Rebuild Solution."),
  bullet("Press F5 to run with debugger, or Ctrl+F5 to run without."),
  ...gap(),
  h2("8.4  Output"),
  body("Executable: C:\\Users\\RandyTaylor\\Claude\\FileExplorer\\bin\\Release\\net9.0-windows\\FileExplorer.exe"),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 9  CONTINUING IN A NEW SESSION
  // ══════════════════════════════════════════════════════════════════════
  h1("9  Continuing in a New Claude Session"),
  ...gap(),
  body("To resume work on this solution, provide Claude with:"),
  ...gap(),
  bullet("This document (FileExplorer_TechDoc.docx) — contains full source and all context."),
  bullet("The project folder: C:\\Users\\RandyTaylor\\Claude\\FileExplorer\\"),
  ...gap(),
  body("Suggested prompt:"),
  ...gap(),
  ...codeBlock(
`Read the technical document at:
  C:\\Users\\RandyTaylor\\Claude\\FileExplorer\\FileExplorer_TechDoc.docx

Then read the source files in:
  C:\\Users\\RandyTaylor\\Claude\\FileExplorer\\

This is a C# WinForms (.NET 9) two-pane File Explorer application.
The solution builds cleanly with 0 errors and 0 warnings.
[Describe your next task here.]`),
  ...gap(),
  h2("9.1  Key Facts for Claude to Know"),
  bullet("All UI control fields are declared in MainForm.Designer.cs; all logic is in MainForm.cs."),
  bullet("SplitContainer sizing is done exclusively in OnLoad() — never in InitializeComponent()."),
  bullet("Background-to-UI marshalling uses SafeInvoke() — checks IsDisposed/IsHandleCreated, wraps BeginInvoke in try/catch for ObjectDisposedException and InvalidOperationException. Never use raw BeginInvoke from background threads."),
  bullet("_folderSizes is always accessed inside lock(_sizeLock)."),
  bullet("DriveProgress.ScannedBytes is accumulated via Interlocked.Add per file in CalcDir; TotalBytes is set once in LoadDrives. Progress = ScannedBytes / TotalBytes (byte-based, not directory-count)."),
  bullet("DriveProgress.Total / .Processed use Interlocked.Increment; read with Volatile.Read in UpdateProgressBar()."),
  bullet("CancellationTokenSource must be cancelled, disposed, and replaced (CancelAndResetCts()) before new background Tasks start."),
  bullet("ShowDrivesInListView() must be called from OnLoad() or DoRefresh() — never from the constructor or LoadDrives() — because ListView.ClientSize is zero before the window handle is created."),
  bullet("RedistributeColumns() uses a _redistributing bool guard to prevent infinite recursion. Do NOT call BeginUpdate/EndUpdate inside RedistributeColumns — EndUpdate fires ClientSizeChanged synchronously (see Bug 3)."),
  bullet("SetWindowTheme(progressBar.ProgressBar.Handle, \"\", \"\") is called in OnLoad to strip visual styles for a flat progress bar appearance."),
  bullet("The Icons static class lives in MainForm.Designer.cs and generates bitmaps at runtime — no resource files."),
  bullet("EnumerationOptions with AttributesToSkip = FileAttributes.None is used throughout to include hidden and system items."),
  bullet("LvSorter is a file-level class in MainForm.cs; it always places folders above files regardless of sort column. ToBytes() treats 'calculating…', '', and 'Not Accessible' as 0."),
  bullet("DriveProgress is a file-level class in MainForm.cs; one instance per drive, stored in _driveProgress dictionary."),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 10  CHANGE HISTORY
  // ══════════════════════════════════════════════════════════════════════
  h1("10  Change History"),
  ...gap(),
  dataTable(
    ["Version", "Date", "Change", "Files Modified"],
    [
      ["1.0", "2026-05-17", "Initial implementation: two-pane WinForms explorer with async recursive size calculation, sorting, refresh, hidden/system file display, access-denied greying.",
       "All files"],
      ["1.0", "2026-05-17", "Bug fix: SplitterDistance InvalidOperationException — moved Panel1MinSize, Panel2MinSize, SplitterDistance from InitializeComponent() to OnLoad().",
       "MainForm.cs, MainForm.Designer.cs"],
      ["1.1", "2026-05-17", "Feature: Scan progress bar — added ToolStripProgressBar and scanStatusLabel to status strip. Added DriveProgress class with Interlocked counter fields. LoadDrives() initialises per-drive trackers. CalcDir() increments Total/Processed. DriveWalkAsync() marks Complete. UpdateProgressBar() updates bar and label; called from CalcDir BeginInvoke, DriveWalkAsync completion, and NavigateTo().",
       "MainForm.cs, MainForm.Designer.cs"],
      ["1.2", "2026-05-17", "Feature: 'Not Accessible' for unreadable folders — CanRead() helper; PopulateListView sets size text to 'Not Accessible' when accessible=false instead of 'calculating…'. LvSorter.ToBytes() treats 'Not Accessible' as 0.",
       "MainForm.cs"],
      ["1.3", "2026-05-17", "Bug fix: Progress bar jumped to ~90% at startup (Bug 2). Switched from directory-count-based progress to byte-based: DriveProgress gains TotalBytes (set in LoadDrives from DriveInfo) and ScannedBytes (Interlocked.Add per file in CalcDir). UpdateProgressBar uses ScannedBytes/TotalBytes, capped at 99% until Complete.",
       "MainForm.cs"],
      ["1.4", "2026-05-17", "Feature: Column auto-sizing and distribution — Date Modified column auto-sized to fit 'yyyy-MM-dd  HH:mm:ss'; Size column auto-sized to fit 'Not Accessible' via TextRenderer.MeasureText in OnLoad. All four columns distribute extra width equally on resize via RedistributeColumns() (wired to ClientSizeChanged). Minimum form width computed from chrome + Panel1MinSize + splitter + column totals + scrollbar + list border.",
       "MainForm.cs"],
      ["1.5", "2026-05-17", "Features: (a) SafeInvoke() wrapper replaces raw BeginInvoke in CalcDir/DriveWalkAsync — checks IsDisposed/IsHandleCreated, catches disposal exceptions, allows safe form close mid-scan. (b) scanStatusLabel shows 'Processing' during scan and 'Scan complete ✓' on completion. (c) SetWindowTheme(progressBar.ProgressBar.Handle, '', '') in OnLoad for flat progress bar appearance.",
       "MainForm.cs, MainForm.Designer.cs"],
      ["1.6", "2026-05-17", "Bug fix: Form 'Not Responding' on close (Bug 3) — removed BeginUpdate/EndUpdate from RedistributeColumns (EndUpdate fires ClientSizeChanged synchronously causing infinite recursion); added _redistributing re-entrancy guard. Feature: Drives view (This PC) — ShowDrivesInListView() populates right pane with drives at startup; moved from LoadDrives to OnLoad (Bug 4 fix). Added _drivesView flag; RefreshListSizes handles drives view; DoRefresh calls ShowDrivesInListView when no path saved. scanStatusLabel initialised to 'Processing' in OnLoad.",
       "MainForm.cs, MainForm.Designer.cs"],
    ],
    [720, 1080, 5400, 2160]
  ),
  ...gap(),

  // ══════════════════════════════════════════════════════════════════════
  // 11  POTENTIAL ENHANCEMENTS
  // ══════════════════════════════════════════════════════════════════════
  h1("11  Potential Future Enhancements"),
  ...gap(),
  dataTable(
    ["Enhancement", "Notes"],
    [
      ["Shell icons",             "Use SHGetFileInfo (P/Invoke) to pull real Windows shell icons instead of drawn bitmaps."],
      ["Address bar",             "Breadcrumb ToolStrip or TextBox showing current path with navigation history (Back/Forward)."],
      ["Search / filter",         "ToolStrip TextBox to filter the right-pane list by filename pattern."],
      ["Column persistence",      "Save column widths and last sort column to user settings (JSON or Properties.Settings)."],
      ["Removable drive refresh", "WMI or DeviceChangeNotification to auto-refresh when USB drives are inserted/removed."],
      ["Size recalculation",      "FileSystemWatcher to invalidate cached sizes when folder contents change on disk."],
      ["Keyboard navigation",     "Arrow keys in ListView, Enter to open folder, Backspace to navigate up."],
      ["Right-click context menu","Copy path, open in Windows Explorer, open with default app (ShellExecute)."],
      ["Drag and drop",           "Allow dragging files out of the ListView to other applications (read-only)."],
      ["Progress cancel button",  "A 'Cancel Scan' button in the toolbar to stop long-running background walks without refreshing the whole view."],
    ],
    [2800, 6560]
  ),
  ...gap(),
];

// ── Build document ─────────────────────────────────────────────────────────
const doc = new Document({
  numbering: {
    config: [{
      reference: "bullets",
      levels: [
        { level: 0, format: LevelFormat.BULLET, text: "•",
          alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } },
        { level: 1, format: LevelFormat.BULLET, text: "◦",
          alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 1080, hanging: 360 } } } },
      ]
    }]
  },
  styles: {
    default: { document: { run: { font: "Arial", size: 20 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 28, bold: true, font: "Arial", color: WHITE },
        paragraph: { spacing: { before: 320, after: 160 }, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 24, bold: true, font: "Arial", color: LBLUE },
        paragraph: { spacing: { before: 240, after: 100 }, outlineLevel: 1 } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 22, bold: true, font: "Arial", color: "404040" },
        paragraph: { spacing: { before: 180, after: 60 }, outlineLevel: 2 } },
    ]
  },
  sections: [{
    properties: {
      page: {
        size:   { width: 12240, height: 15840 },
        margin: { top: 1080, bottom: 1080, left: 1080, right: 1080 }
      }
    },
    headers: {
      default: new Header({
        children: [new Paragraph({
          border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: LBLUE, space: 4 } },
          tabStops: [{ type: TabStopType.RIGHT, position: TabStopPosition.MAX }],
          children: [
            new TextRun({ text: "FileExplorer — Technical Reference", bold: true, size: 18, color: LBLUE }),
            new TextRun({ text: "\t" }),
            new TextRun({ text: "C# WinForms  |  .NET 9", size: 16, color: "808080" })
          ]
        })]
      })
    },
    footers: {
      default: new Footer({
        children: [new Paragraph({
          border: { top: { style: BorderStyle.SINGLE, size: 6, color: SILVER, space: 4 } },
          alignment: AlignmentType.CENTER,
          children: [
            new TextRun({ text: "Page ", size: 16, color: "808080" }),
            new TextRun({ children: [PageNumber.CURRENT], size: 16, color: "808080" }),
            new TextRun({ text: " of ", size: 16, color: "808080" }),
            new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 16, color: "808080" }),
          ]
        })]
      })
    },
    children
  }]
});

Packer.toBuffer(doc).then(buffer => {
  const out = path.join(base, "FileExplorer_TechDoc.docx");
  fs.writeFileSync(out, buffer);
  console.log("Written: " + out + "  (" + (buffer.length / 1024).toFixed(1) + " KB)");
}).catch(err => {
  console.error("FAILED:", err.message);
  process.exit(1);
});
