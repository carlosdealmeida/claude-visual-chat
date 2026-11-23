using System;

namespace ClaudeVisualChat;

/// <summary>Where a task of <see cref="ChatConversation.BackgroundTasks"/> is.</summary>
public enum BackgroundTaskStatus
{
    /// <summary>The claude's last snapshot has it.</summary>
    Running,

    /// <summary>It left the snapshot after its notification said <c>completed</c>.</summary>
    Finished,

    /// <summary>It left the snapshot after its notification said <c>failed</c>.</summary>
    Failed,

    /// <summary>It left the snapshot after its notification said <c>stopped</c> (or <c>killed</c>).</summary>
    Stopped,

    /// <summary>It left the snapshot with no notification telling how, or the conversation was cleared while it ran.</summary>
    Ended,
}

/// <summary>
/// One task running in the background (see <see cref="ChatConversation.BackgroundTasks"/>), from the claude's
/// <c>system/background_tasks_changed</c> snapshots and its <c>system/task_*</c> lines. The conversation fills it; the host
/// only reads it.
/// </summary>
public sealed class BackgroundTaskInfo : ChatObservableObject
{
    private string _description;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _endedAt;
    private BackgroundTaskStatus _status;
    private bool _isStopping;
    private bool _stopFailed;

    internal BackgroundTaskInfo(string taskId, string taskType, string description)
    {
        TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        TaskType = taskType ?? "";
        _description = description ?? "";
    }

    /// <summary>The claude's id for the task (<c>task_id</c>).</summary>
    public string TaskId { get; }

    /// <summary>The kind of task (<c>local_workflow</c> for a workflow).</summary>
    public string TaskType { get; }

    /// <summary>What the task does, as the last snapshot described it.</summary>
    public string Description
    {
        get => _description;
        internal set => SetField(ref _description, value ?? "");
    }

    /// <summary>When its <c>task_started</c> line arrived (by <see cref="ChatConversation.Clock"/>); null until it does.</summary>
    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        internal set => SetField(ref _startedAt, value);
    }

    /// <summary>
    /// When it left the snapshot, or the conversation was cleared (by <see cref="ChatConversation.Clock"/>); null while it runs.
    /// </summary>
    public DateTimeOffset? EndedAt
    {
        get => _endedAt;
        internal set => SetField(ref _endedAt, value);
    }

    /// <summary>
    /// <see cref="BackgroundTaskStatus.Running"/> while the snapshot has it; when it leaves, what its notification said,
    /// else <see cref="BackgroundTaskStatus.Ended"/>.
    /// </summary>
    public BackgroundTaskStatus Status
    {
        get => _status;
        internal set => SetField(ref _status, value);
    }

    /// <summary>True while a request to stop it waits for the claude's answer.</summary>
    public bool IsStopping
    {
        get => _isStopping;
        internal set => SetField(ref _isStopping, value);
    }

    /// <summary>True when the claude refused the last request to stop it.</summary>
    public bool StopFailed
    {
        get => _stopFailed;
        internal set => SetField(ref _stopFailed, value);
    }
}
