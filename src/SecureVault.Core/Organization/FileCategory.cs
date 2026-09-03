namespace SecureVault.Core.Organization;

/// <summary>
/// File categorization classifications for vault contents (D03).
/// </summary>
public enum FileCategory : byte
{
    Photos = 0,        // Images, raw camera files, vector graphics
    Videos = 1,        // Video containers and streams
    Audio = 2,         // Music, recordings, audiobooks
    Documents = 3,     // PDF, Office docs, eBooks
    TextNotes = 4,     // Plain text, markdown, code, config files
    Applications = 5,  // Executables, installers, scripts
    Archives = 6,      // Compressed archives and disc images
    Other = 7          // Uncategorized or unknown formats
}
