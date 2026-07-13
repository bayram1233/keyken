using CommunityToolkit.Mvvm.ComponentModel;

namespace FendrSystemCare.Models;

/// <summary>
/// Describes a single repair action (e.g. "SFC /scannow"). The command and its
/// arguments are executed by <c>IRepairService</c>. Observable members let the
/// repair view show live status per task.
/// </summary>
public sealed partial class RepairTask : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }

    /// <summary>Executable to launch (e.g. "sfc", "DISM", "cmd").</summary>
    public string FileName { get; }

    /// <summary>Command-line arguments passed to the executable.</summary>
    public string Arguments { get; }

    /// <summary>If true the task changes the system and needs confirmation.</summary>
    public bool RequiresConfirmation { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private OperationStatus _status = OperationStatus.Pending;

    public RepairTask(string key, string name, string description, string icon,
        string fileName, string arguments, bool requiresConfirmation = true)
    {
        Key = key;
        Name = name;
        Description = description;
        Icon = icon;
        FileName = fileName;
        Arguments = arguments;
        RequiresConfirmation = requiresConfirmation;
    }
}

/// <summary>Outcome of running a <see cref="RepairTask"/>.</summary>
public sealed class RepairResult
{
    public required string TaskKey { get; init; }
    public OperationStatus Status { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}
