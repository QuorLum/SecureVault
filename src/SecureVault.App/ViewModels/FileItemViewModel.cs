using CommunityToolkit.Mvvm.ComponentModel;
using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.App.ViewModels;

/// <summary>
/// Observable ViewModel representing a file or virtual folder item within the UI (N09, E07).
/// </summary>
public partial class FileItemViewModel : ObservableObject
{
    public IndexEntry Entry { get; }

    public Guid FileGuid => Entry.FileGuid;
    public bool IsFolder => Entry.IsFolder;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFavorite;

    public ulong OriginalSize => Entry.OriginalSize;
    public ProtectionMode ProtectionMode => Entry.ProtectionMode;
    public FileCategory Category => (FileCategory)Entry.Category;

    public string FormattedSize => IsFolder ? "Folder" : FormatBytes(Entry.OriginalSize);

    public string ProtectionModeBadge => Entry.ProtectionMode == ProtectionMode.SecureMode ? "Secure (AES-GCM)" : "Fast (Obfuscated)";

    public string FormattedDate => new DateTime(Entry.DateModifiedTicks, DateTimeKind.Utc).ToLocalTime().ToString("g");

    public string CategoryName => Category.ToString();

    public string IconGlyph => IsFolder ? "\uE8B7" : Category switch
    {
        FileCategory.Photos => "\uEB9F",
        FileCategory.Videos => "\uE714",
        FileCategory.Audio => "\uE8D6",
        FileCategory.Documents => "\uE8A5",
        FileCategory.TextNotes => "\uE70B",
        FileCategory.Applications => "\uE7B8",
        FileCategory.Archives => "\uF012",
        _ => "\uE7C3"
    };

    public FileItemViewModel(IndexEntry entry)
    {
        Entry = entry;
        _fileName = entry.FileName;
        _isFavorite = entry.IsFavorite;
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }
}
