namespace SessionDeck.Models;

/// <summary>
/// The external tasks file (T-0116): a read-only JSON document produced by an external
/// tool (e.g. TaskDeck's export script). SessionDeck only displays it — the producer owns
/// the content, the order and the status→color semantics. Unknown JSON keys are ignored
/// (forward-compat); only id+name are required per task.
/// </summary>
public class TasksDocument
{
    public int Version { get; set; }
    public string? Generated { get; set; }           // ISO timestamp, display only
    /// <summary>status → color (name or #RRGGBB). Data, not config: the producer owns it.</summary>
    public Dictionary<string, string> StatusColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Template for a new session opened FROM a task, with &lt;id&gt;/&lt;name&gt;
    /// placeholders. Missing = new sessions start empty.</summary>
    public string? NewSessionPrompt { get; set; }
    public List<TaskEntry> Tasks { get; set; } = new();
}

public class TaskEntry
{
    public string? Id { get; set; }                  // required
    public string? Name { get; set; }                // required
    public string? Description { get; set; }
    public string? Status { get; set; }              // free string, colored via StatusColors
    public bool Pinned { get; set; }
    public string? Workspace { get; set; }           // full folder path — matched to a card by path
    public List<string> Sessions { get; set; } = new();
    public string? Url { get; set; }                 // opened via ShellExecute (e.g. obsidian://)
}
