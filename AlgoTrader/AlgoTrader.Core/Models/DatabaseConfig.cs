namespace AlgoTrader.Core.Models;

/// <summary>Database folder and file configuration.</summary>
public record DatabaseConfig(string FolderPath, string FileName)
{
    /// <summary>Full database file path.</summary>
    public string FullPath => Path.Combine(FolderPath, FileName);
}
