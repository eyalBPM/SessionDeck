using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SessionDeck.Services;
using SessionDeck.ViewModels;

namespace SessionDeck;

/// <summary>
/// Tasks-panel feature (T-0116): the external tasks file, its watcher, the tasks page
/// navigation and the task→workspace/session click flows. Everything here is inert until
/// a file path is configured (strict opt-in).
/// </summary>
public partial class MainWindow
{
    private TasksFileWatcher? _tasksWatcher;

    /// <summary>(Re)apply the configured path: tear down the old watcher, reset all task
    /// state, and start watching the new file. null/empty turns the feature off.
    /// Public — shared by the ⚙ dialog and the `tasks` CLI command.</summary>
    public void ApplyTasksFile(string? path)
    {
        _tasksWatcher?.Dispose();
        _tasksWatcher = null;
        Vm.TasksFilePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        bool enabled = Vm.TasksFilePath != null;
        Vm.TasksPanel.Clear();
        Vm.TasksPanel.Enabled = enabled;
        RefreshWorkspaceTaskLinks();
        if (!enabled)
        {
            CloseTasksPage();
            return;
        }
        _tasksWatcher = new TasksFileWatcher(Vm.TasksFilePath!, OnTasksLoaded);
    }

    private void OnTasksLoaded(TasksLoadResult result)
    {
        Vm.TasksPanel.Apply(result);
        RefreshWorkspaceTaskLinks();
        if (result.FileError is { } err)
        {
            LogService.Info("tasks", $"load failed: {err}");
            SetStatus("קובץ המשימות: " + err);
        }
        else
        {
            int count = Vm.TasksPanel.PinnedTasks.Count + Vm.TasksPanel.OtherTasks.Count;
            LogService.Debug("tasks", $"loaded {count} tasks, {result.RecordWarnings.Count} skipped");
        }
    }

    /// <summary>Match tasks to workspace cards by path. Also keeps TasksEnabled in sync on
    /// cards added after the feature was configured (called from CollectionChanged).</summary>
    private void RefreshWorkspaceTaskLinks()
    {
        bool enabled = Vm.TasksPanel.Enabled;
        var byPath = Vm.TasksPanel.AllTasks
            .Where(t => t.HasWorkspace)
            .GroupBy(t => WorkspaceMetadata.NormalizePath(t.WorkspacePath))
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var ws in Vm.Workspaces)
        {
            ws.TasksEnabled = enabled;
            ws.WorkspaceTasks.Clear();
            if (!enabled || ws.Path.Length == 0) { ws.TasksExpanded = false; continue; }
            if (byPath.TryGetValue(WorkspaceMetadata.NormalizePath(ws.Path), out var tasks))
                foreach (var t in tasks) ws.WorkspaceTasks.Add(t);
            if (ws.WorkspaceTasks.Count == 0) ws.TasksExpanded = false;
        }
    }

    // ---- page navigation ----

    public void ShowTasksPage()
    {
        if (!Vm.TasksPanel.Enabled || Vm.TasksPanel.PageBlocked) return;
        Vm.TasksPanel.PageOpen = true;
        DeckView.Visibility = Visibility.Collapsed;
        TasksPage.Visibility = Visibility.Visible;
        // Mutual exclusion (T-0116): no search while the tasks page is up.
        SearchToggleButton.IsEnabled = false;
    }

    public void CloseTasksPage()
    {
        Vm.TasksPanel.PageOpen = false;
        TasksPage.Visibility = Visibility.Collapsed;
        DeckView.Visibility = Visibility.Visible;
        SearchToggleButton.IsEnabled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Vm.TasksPanel.PageOpen)
        {
            CloseTasksPage();
            e.Handled = true;
        }
    }

    private void TasksFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TasksFileDialog(Vm.TasksFilePath) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ApplyTasksFile(dialog.PathText);
        QueueSave();
        SetStatus(Vm.TasksFilePath == null ? "פאנל המשימות כובה" : $"קובץ המשימות: {Vm.TasksFilePath}");
    }

    // ---- task activation (T-0116 click behavior) ----

    /// <summary>Click on a task square / its sessions button: no sessions → straight to a
    /// new session in its workspace; otherwise a dropdown of "new session" + the task's
    /// sessions (live → focus, dead → real resume).</summary>
    public void HandleTaskActivate(TaskItemViewModel task, FrameworkElement anchor)
    {
        if (!task.HasTarget)
        {
            SetStatus($"‏\"{task.Name}\" — למשימה אין workspace או sessions");
            return;
        }
        if (task.Sessions.Count == 0)
        {
            StartTaskNewSession(task);
            return;
        }

        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        var newItem = new MenuItem { Header = "+ סשן חדש" };
        newItem.Click += (_, _) => StartTaskNewSession(task);
        newItem.IsEnabled = task.HasWorkspace;
        menu.Items.Add(newItem);
        menu.Items.Add(new Separator());
        foreach (string sid in task.Sessions)
        {
            var found = Vm.FindSession(sid);
            var session = found?.Item2;
            bool live = session is { Closed: false, Phantom: false };
            string title = session?.DisplayTitle
                           ?? "session " + (sid.Length > 8 ? sid[..8] : sid);
            var item = new MenuItem
            {
                Header = (live ? "🟢 " : "▶ ") + title,
                ToolTip = live ? "סשן פעיל — מעבר אליו" : "סשן לא רץ — פתיחה מחדש (resume)",
            };
            string capturedSid = sid;
            item.Click += (_, _) => OpenTaskSession(task, capturedSid);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    public void OpenTaskUrl(TaskItemViewModel task)
    {
        if (!task.HasUrl) return;
        try
        {
            Process.Start(new ProcessStartInfo(task.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"פתיחת הקישור נכשלה: {ex.Message}");
        }
    }

    /// <summary>Open a session from a task: on the deck → the regular click flow (focus /
    /// resume); unknown to the deck → real resume by id, launching VSCode on the task's
    /// workspace if needed and parking the open request until the connector appears.</summary>
    private void OpenTaskSession(TaskItemViewModel task, string sessionId)
    {
        if (Vm.FindSession(sessionId) is { } found)
        {
            HandleSessionClick(found.Item1, found.Item2);
            return;
        }
        if (!task.HasWorkspace)
        {
            SetStatus($"‏\"{task.Name}\" — הסשן לא מוכר ולמשימה אין workspace לפתוח בו");
            return;
        }
        if (Vm.FindByPath(task.WorkspacePath) is { } ws)
        {
            FocusWorkspace(ws);
            var conn = FindConnector(ws);
            if (conn == null)
                _pendingOpens[WorkspaceMetadata.NormalizePath(ws.Path)] = (sessionId, null, DateTime.Now);
            else if (!conn.TrySend(new { Cmd = "openSession", SessionId = sessionId, Maximize = Vm.OpenSessionMaximized }))
                _connectors.Remove(conn);
            SetStatus($"פותח סשן של \"{task.Name}\" ב-VSCode…");
            return;
        }
        if (!Directory.Exists(task.WorkspacePath))
        {
            SetStatus($"‏\"{task.Name}\" — תיקיית ה-workspace לא קיימת: {task.WorkspacePath}");
            return;
        }
        // Workspace isn't on the deck — launch VSCode directly; the extension connects and
        // the parked request resumes the session (no card is auto-added).
        if (WindowActions.LaunchVsCode(task.WorkspacePath))
        {
            _pendingOpens[WorkspaceMetadata.NormalizePath(task.WorkspacePath)] = (sessionId, null, DateTime.Now);
            SetStatus($"פותח VSCode עבור \"{task.Name}\" — הסשן יקום כשהחיבור יעלה");
        }
        else
            SetStatus($"‏\"{task.Name}\" — פתיחת VSCode נכשלה");
    }

    private void StartTaskNewSession(TaskItemViewModel task)
    {
        if (!task.HasWorkspace)
        {
            SetStatus($"‏\"{task.Name}\" — למשימה אין workspace לפתוח בו סשן");
            return;
        }
        string? prompt = BuildNewSessionPrompt(task);
        if (Vm.FindByPath(task.WorkspacePath) is { } ws)
        {
            NewSessionInVscode(ws, prompt);
            return;
        }
        if (!Directory.Exists(task.WorkspacePath))
        {
            SetStatus($"‏\"{task.Name}\" — תיקיית ה-workspace לא קיימת: {task.WorkspacePath}");
            return;
        }
        if (WindowActions.LaunchVsCode(task.WorkspacePath))
        {
            _pendingOpens[WorkspaceMetadata.NormalizePath(task.WorkspacePath)] = (null, prompt, DateTime.Now);
            SetStatus($"פותח VSCode עבור \"{task.Name}\" — סשן חדש ייפתח כשהחיבור יעלה");
        }
        else
            SetStatus($"‏\"{task.Name}\" — פתיחת VSCode נכשלה");
    }

    /// <summary>newSessionPrompt from the file's envelope with &lt;id&gt;/&lt;name&gt;
    /// filled in; null (no template) = the session opens empty. Applies ONLY to new
    /// sessions opened from a task (T-0116).</summary>
    private string? BuildNewSessionPrompt(TaskItemViewModel task)
        => Vm.TasksPanel.NewSessionPrompt?
            .Replace("<id>", task.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("<name>", task.Name, StringComparison.OrdinalIgnoreCase);
}
