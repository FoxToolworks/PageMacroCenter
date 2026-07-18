// PageMacroCenter
// Version 0.3.4
// Copyright (c) 2026 FoxToolworks
// Licensed under the MIT License.
// https://github.com/FoxToolworks/PageMacroCenter
//
// Ausführbares EPLAN-Skript für die Suche und Navigation in Seitenmakros.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.Scripting;

/// <summary>
/// Startpunkt für das ausführbare EPLAN-Skript PageMacroCenter.
/// </summary>
public class PageMacroCenterScript
{
    private static PageMacroCenterForm _window;

    /// <summary>
    /// Öffnet das PageMacroCenter oder aktiviert das bereits geöffnete Fenster.
    /// </summary>
    [Start]
    public void Start()
    {
        if (_window == null || _window.IsDisposed)
        {
            _window = new PageMacroCenterForm();
            _window.FormClosed += OnWindowClosed;

            IntPtr eplanMainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;

            if (eplanMainWindowHandle != IntPtr.Zero)
                _window.Show(new WindowHandleWrapper(eplanMainWindowHandle));
            else
                _window.Show();
        }
        else
        {
            if (_window.WindowState == FormWindowState.Minimized)
                _window.WindowState = FormWindowState.Normal;

            _window.Activate();
            _window.BringToFront();
        }
    }

    /// <summary>
    /// Entfernt die Referenz, sobald das Fenster geschlossen wurde.
    /// </summary>
    private static void OnWindowClosed(object sender, FormClosedEventArgs e)
    {
        _window = null;
    }
}

/// <summary>
/// Kapselt das EPLAN-Hauptfenster als WinForms-Fensterbesitzer.
/// </summary>
public sealed class WindowHandleWrapper : IWin32Window
{
    public WindowHandleWrapper(IntPtr handle)
    {
        Handle = handle;
    }

    public IntPtr Handle { get; private set; }
}

/// <summary>
/// Nicht modales Browserfenster für EPLAN-Seitenmakros.
/// Version 0.3.4 ergänzt eine wortweise Mehrfachsuche.
/// </summary>
public class PageMacroCenterForm : Form
{
    private const string MacroPathVariable = "$(MD_MACROS)";
    private const string PageMacroExtension = ".emp";

    private readonly TextBox _searchBox;
    private readonly TreeView _macroTree;
    private readonly Label _statusLabel;
    private readonly Timer _searchTimer;

    private string _macroRoot;
    private List<MacroEntry> _entries;

    /// <summary>
    /// Erstellt das Fenster und lädt den aktuellen EPLAN-Makroordner.
    /// </summary>
    public PageMacroCenterForm()
    {
        _entries = new List<MacroEntry>();

        _searchTimer = new Timer();
        _searchTimer.Interval = 200;
        _searchTimer.Tick += OnSearchTimerTick;

        Text = "PageMacroCenter 0.3.4";
        Name = "PageMacroCenterForm";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(360, 520);
        Size = new Size(430, 720);
        KeyPreview = true;
        ShowInTaskbar = false;

        TableLayoutPanel rootLayout = new TableLayoutPanel();
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.ColumnCount = 1;
        rootLayout.RowCount = 3;
        rootLayout.Padding = new Padding(8);
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(rootLayout);

        TableLayoutPanel searchLayout = new TableLayoutPanel();
        searchLayout.Dock = DockStyle.Top;
        searchLayout.AutoSize = true;
        searchLayout.ColumnCount = 2;
        searchLayout.RowCount = 1;
        searchLayout.Margin = new Padding(0, 0, 0, 6);
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(searchLayout, 0, 0);

        Label searchLabel = new Label();
        searchLabel.Text = "Suchen:";
        searchLabel.AutoSize = true;
        searchLabel.Anchor = AnchorStyles.Left;
        searchLabel.Margin = new Padding(0, 4, 8, 0);
        searchLayout.Controls.Add(searchLabel, 0, 0);

        _searchBox = new TextBox();
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0);
        _searchBox.TextChanged += OnSearchTextChanged;
        searchLayout.Controls.Add(_searchBox, 1, 0);

        _macroTree = new TreeView();
        _macroTree.Dock = DockStyle.Fill;
        _macroTree.HideSelection = false;
        _macroTree.ShowLines = true;
        _macroTree.ShowPlusMinus = true;
        _macroTree.ShowRootLines = true;
        _macroTree.AfterSelect += OnMacroTreeAfterSelect;
        _macroTree.ItemDrag += OnMacroTreeItemDrag;
        _macroTree.Margin = new Padding(0);
        rootLayout.Controls.Add(_macroTree, 0, 1);

        _statusLabel = new Label();
        _statusLabel.AutoSize = true;
        _statusLabel.Margin = new Padding(0, 6, 0, 0);
        _statusLabel.Text = "Bereit";
        rootLayout.Controls.Add(_statusLabel, 0, 2);

        Shown += OnFormShown;
        KeyDown += OnFormKeyDown;
        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// Lädt beim ersten Anzeigen die Makrostruktur.
    /// </summary>
    private void OnFormShown(object sender, EventArgs e)
    {
        ReloadMacroDirectory();
        _searchBox.Focus();
    }

    /// <summary>
    /// Aktualisiert den Bestand mit F5 und leert die Suche mit Escape.
    /// </summary>
    private void OnFormKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            ReloadMacroDirectory();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape && _searchBox.TextLength > 0)
        {
            _searchBox.Clear();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Entprellt die Suche, damit der Baum nicht bei jedem einzelnen Tastendruck
    /// neu aufgebaut wird und das Suchfeld zuverlässig den Fokus behält.
    /// </summary>
    private void OnSearchTextChanged(object sender, EventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    /// <summary>
    /// Führt die Suche nach einer kurzen Tipp-Pause aus und stellt Fokus sowie
    /// Cursorposition im Suchfeld wieder her.
    /// </summary>
    private void OnSearchTimerTick(object sender, EventArgs e)
    {
        _searchTimer.Stop();

        int selectionStart = _searchBox.SelectionStart;
        int selectionLength = _searchBox.SelectionLength;
        string searchText = _searchBox.Text;

        BuildTree(searchText);

        BeginInvoke((MethodInvoker)delegate
        {
            if (_searchBox.IsDisposed)
                return;

            _searchBox.Focus();
            _searchBox.SelectionStart = Math.Min(selectionStart, _searchBox.TextLength);
            _searchBox.SelectionLength = Math.Min(selectionLength,
                                                   _searchBox.TextLength - _searchBox.SelectionStart);
        });
    }

    /// <summary>
    /// Übergibt eine ausgewählte EMP-Datei als Windows-Dateidrop an EPLAN.
    /// Ordnerknoten und ungültige Dateien werden nicht gezogen.
    /// </summary>
    private void OnMacroTreeItemDrag(object sender, ItemDragEventArgs e)
    {
        TreeNode node = e.Item as TreeNode;

        if (node == null)
            return;

        MacroEntry entry = node.Tag as MacroEntry;

        if (entry == null || entry.IsDirectory)
            return;

        if (string.IsNullOrWhiteSpace(entry.FullPath) ||
            !File.Exists(entry.FullPath) ||
            !string.Equals(Path.GetExtension(entry.FullPath),
                           PageMacroExtension,
                           StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DataObject dragData = new DataObject();
        dragData.SetData(DataFormats.FileDrop, new string[] { entry.FullPath });

        _macroTree.DoDragDrop(dragData, DragDropEffects.Copy);
    }

    /// <summary>
    /// Übergibt ausgewählte EMP-Dateien an die native grafische EPLAN-Vorschau.
    /// </summary>
    private void OnMacroTreeAfterSelect(object sender, TreeViewEventArgs e)
    {
        MacroEntry entry = e.Node.Tag as MacroEntry;

        if (entry == null || entry.IsDirectory)
            return;

        ShowEplanMacroPreview(entry.FullPath);
    }

    /// <summary>
    /// Öffnet oder aktualisiert die native grafische EPLAN-Vorschau für das
    /// ausgewählte Seitenmakro.
    /// </summary>
    private void ShowEplanMacroPreview(string macroPath)
    {
        if (string.IsNullOrWhiteSpace(macroPath) || !File.Exists(macroPath))
        {
            _statusLabel.Text = "Vorschau nicht möglich: Datei nicht gefunden";
            return;
        }

        try
        {
            ActionCallingContext context = new ActionCallingContext();
            context.AddParameter("MACRONAME", macroPath);
            context.AddParameter("SHOW", "1");

            CommandLineInterpreter interpreter = new CommandLineInterpreter();
            bool executed = interpreter.Execute("XSDPreviewAction", context);

            if (executed)
                _statusLabel.Text = "Vorschau: " + GetEntryNameFromPath(macroPath);
            else
                _statusLabel.Text = "EPLAN-Vorschau konnte nicht geöffnet werden";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "Fehler bei der EPLAN-Vorschau: " + exception.Message;
        }
    }

    /// <summary>
    /// Liefert einen lesbaren Namen für die Statuszeile.
    /// </summary>
    private static string GetEntryNameFromPath(string macroPath)
    {
        return Path.GetFileNameWithoutExtension(macroPath);
    }


    /// <summary>
    /// Schließt die von XSDPreviewAction geöffnete Vorschau.
    /// </summary>
    private static void CloseEplanMacroPreview()
    {
        try
        {
            ActionCallingContext context = new ActionCallingContext();
            context.AddParameter("SHOW", "0");

            new CommandLineInterpreter().Execute("XSDPreviewAction", context);
        }
        catch
        {
            // Das Schließen des PageMacroCenters darf nicht durch einen
            // Vorschaufehler blockiert werden.
        }
    }

    /// <summary>
    /// Stoppt laufende UI-Aktionen und schließt die EPLAN-Vorschau gemeinsam
    /// mit dem PageMacroCenter.
    /// </summary>
    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        _searchTimer.Stop();
        CloseEplanMacroPreview();
    }

    /// <summary>
    /// Liest alle Unterordner und EMP-Dateien aus dem EPLAN-Makroverzeichnis ein.
    /// </summary>
    private void ReloadMacroDirectory()
    {
        Cursor previousCursor = Cursor;
        Cursor = Cursors.WaitCursor;

        try
        {
            _macroRoot = PathMap.SubstitutePath(MacroPathVariable);
            _entries.Clear();

            if (string.IsNullOrWhiteSpace(_macroRoot) || !Directory.Exists(_macroRoot))
            {
                _macroTree.Nodes.Clear();
                _statusLabel.Text = "Makroverzeichnis nicht gefunden: " + MacroPathVariable;
                return;
            }

            _entries = ScanDirectory(_macroRoot);
            BuildTree(_searchBox.Text);
        }
        catch (Exception exception)
        {
            _macroTree.Nodes.Clear();
            _statusLabel.Text = "Fehler beim Einlesen: " + exception.Message;
        }
        finally
        {
            Cursor = previousCursor;
        }
    }

    /// <summary>
    /// Liest einen Ordner rekursiv ein. Nicht zugängliche Unterordner werden übersprungen.
    /// </summary>
    private List<MacroEntry> ScanDirectory(string directoryPath)
    {
        List<MacroEntry> result = new List<MacroEntry>();

        string[] directories = GetDirectoriesSafe(directoryPath);
        Array.Sort(directories, StringComparer.CurrentCultureIgnoreCase);

        foreach (string directory in directories)
        {
            MacroEntry folderEntry = new MacroEntry();
            folderEntry.Name = Path.GetFileName(directory);
            folderEntry.FullPath = directory;
            folderEntry.RelativePath = GetRelativePath(directory);
            folderEntry.IsDirectory = true;
            folderEntry.Children = ScanDirectory(directory);

            if (folderEntry.Children.Count > 0)
                result.Add(folderEntry);
        }

        string[] files = GetFilesSafe(directoryPath);
        Array.Sort(files, StringComparer.CurrentCultureIgnoreCase);

        foreach (string file in files)
        {
            MacroEntry fileEntry = new MacroEntry();
            fileEntry.Name = Path.GetFileNameWithoutExtension(file);
            fileEntry.FullPath = file;
            fileEntry.RelativePath = GetRelativePath(file);
            fileEntry.IsDirectory = false;
            fileEntry.Children = new List<MacroEntry>();
            result.Add(fileEntry);
        }

        return result;
    }

    /// <summary>
    /// Erstellt den sichtbaren Baum aus dem gespeicherten Dateimodell.
    /// </summary>
    private void BuildTree(string searchText)
    {
        string filter = (searchText ?? string.Empty).Trim();

        _macroTree.BeginUpdate();

        try
        {
            _macroTree.Nodes.Clear();

            if (string.IsNullOrEmpty(_macroRoot) || !Directory.Exists(_macroRoot))
                return;

            TreeNode rootNode = new TreeNode(new DirectoryInfo(_macroRoot).Name);
            rootNode.ToolTipText = _macroRoot;

            int visibleFileCount = 0;

            foreach (MacroEntry entry in _entries)
            {
                TreeNode childNode = CreateTreeNode(entry, filter, ref visibleFileCount);

                if (childNode != null)
                    rootNode.Nodes.Add(childNode);
            }

            _macroTree.Nodes.Add(rootNode);
            rootNode.Expand();

            if (filter.Length > 0)
                rootNode.ExpandAll();

            int totalFileCount = CountFiles(_entries);

            if (filter.Length == 0)
                _statusLabel.Text = totalFileCount + " Seitenmakro(s) | F5 aktualisiert";
            else
                _statusLabel.Text = visibleFileCount + " von " + totalFileCount + " Seitenmakro(s)";
        }
        finally
        {
            _macroTree.EndUpdate();
        }
    }

    /// <summary>
    /// Erzeugt einen sichtbaren Knoten, sofern er oder ein untergeordnetes Element passt.
    /// </summary>
    private TreeNode CreateTreeNode(MacroEntry entry, string filter, ref int visibleFileCount)
    {
        bool selfMatches = MatchesFilter(entry, filter);
        TreeNode node = new TreeNode(entry.Name);
        node.Tag = entry;
        node.ToolTipText = entry.FullPath;

        if (entry.IsDirectory)
        {
            foreach (MacroEntry child in entry.Children)
            {
                TreeNode childNode = CreateTreeNode(child, filter, ref visibleFileCount);

                if (childNode != null)
                    node.Nodes.Add(childNode);
            }

            if (filter.Length > 0 && !selfMatches && node.Nodes.Count == 0)
                return null;
        }
        else
        {
            if (filter.Length > 0 && !selfMatches)
                return null;

            visibleFileCount++;
        }

        return node;
    }

    /// <summary>
    /// Prüft, ob alle eingegebenen Suchwörter im normalisierten Dateinamen oder
    /// relativen Pfad vorkommen. Die Wörter müssen dabei nicht direkt
    /// hintereinander stehen. Dadurch findet beispielsweise "Klappe 230V" auch
    /// ein Makro namens "Klappe_Belimo_230V".
    /// </summary>
    private static bool MatchesFilter(MacroEntry entry, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        string normalizedName = NormalizeSearchText(entry.Name);
        string normalizedPath = NormalizeSearchText(entry.RelativePath);
        List<string> searchTokens = GetSearchTokens(filter);

        if (searchTokens.Count == 0)
            return true;

        foreach (string token in searchTokens)
        {
            bool foundInName = normalizedName.IndexOf(token, StringComparison.Ordinal) >= 0;
            bool foundInPath = normalizedPath.IndexOf(token, StringComparison.Ordinal) >= 0;

            if (!foundInName && !foundInPath)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Zerlegt die Eingabe an Leerzeichen und anderen Trennzeichen in einzelne
    /// Suchwörter und normalisiert jedes Wort separat.
    /// </summary>
    private static List<string> GetSearchTokens(string value)
    {
        List<string> tokens = new List<string>();

        if (string.IsNullOrWhiteSpace(value))
            return tokens;

        StringBuilder currentToken = new StringBuilder();

        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                currentToken.Append(character);
            }
            else
            {
                AddSearchToken(tokens, currentToken);
            }
        }

        AddSearchToken(tokens, currentToken);
        return tokens;
    }

    /// <summary>
    /// Übernimmt ein vollständig gelesenes Suchwort in die Tokenliste.
    /// </summary>
    private static void AddSearchToken(List<string> tokens, StringBuilder currentToken)
    {
        if (currentToken.Length == 0)
            return;

        string normalizedToken = NormalizeSearchText(currentToken.ToString());
        currentToken.Clear();

        if (normalizedToken.Length > 0 && !tokens.Contains(normalizedToken))
            tokens.Add(normalizedToken);
    }

    /// <summary>
    /// Vereinheitlicht Suchbegriffe und Makronamen für eine fehlertolerante Suche.
    /// Umlaute werden in ihre Schreibweise mit "e" umgewandelt; alle Zeichen außer
    /// Buchstaben und Zahlen werden entfernt.
    /// </summary>
    private static string NormalizeSearchText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value
            .ToLowerInvariant()
            .Replace("ä", "ae")
            .Replace("ö", "oe")
            .Replace("ü", "ue")
            .Replace("ß", "ss");

        StringBuilder result = new StringBuilder(normalized.Length);

        foreach (char character in normalized)
        {
            if (char.IsLetterOrDigit(character))
                result.Append(character);
        }

        return result.ToString();
    }

    /// <summary>
    /// Zählt alle eingelesenen EMP-Dateien.
    /// </summary>
    private static int CountFiles(IEnumerable<MacroEntry> entries)
    {
        int count = 0;

        foreach (MacroEntry entry in entries)
        {
            if (entry.IsDirectory)
                count += CountFiles(entry.Children);
            else
                count++;
        }

        return count;
    }

    /// <summary>
    /// Liefert Unterordner, ohne das gesamte Einlesen bei Zugriffsfehlern abzubrechen.
    /// </summary>
    private static string[] GetDirectoriesSafe(string directoryPath)
    {
        try
        {
            return Directory.GetDirectories(directoryPath);
        }
        catch
        {
            return new string[0];
        }
    }

    /// <summary>
    /// Liefert EMP-Dateien, ohne das gesamte Einlesen bei Zugriffsfehlern abzubrechen.
    /// </summary>
    private static string[] GetFilesSafe(string directoryPath)
    {
        try
        {
            return Directory.GetFiles(directoryPath, "*" + PageMacroExtension, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return new string[0];
        }
    }

    /// <summary>
    /// Erstellt einen lesbaren Pfad relativ zum Makro-Stammordner.
    /// </summary>
    private string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(_macroRoot))
            return fullPath;

        string root = _macroRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath;
    }
}

/// <summary>
/// Repräsentiert einen Ordner oder eine Seitenmakrodatei im internen Dateimodell.
/// </summary>
public class MacroEntry
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public string RelativePath { get; set; }
    public bool IsDirectory { get; set; }
    public List<MacroEntry> Children { get; set; }
}
