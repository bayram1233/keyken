namespace FendrSystemCare.Models;

/// <summary>Represents a single Windows Update available or installed.</summary>
public sealed class WindowsUpdateItem
{
    public required string Title { get; init; }
    public string KbArticle { get; init; } = string.Empty;
    public double SizeMb { get; init; }
    public bool IsInstalled { get; init; }
    public bool IsMandatory { get; init; }
    public DateTime? Date { get; init; }
}
