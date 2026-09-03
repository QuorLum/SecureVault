namespace SecureVault.Core.Organization;

/// <summary>
/// Automatically classifies file names into FileCategory based on extensions (D04).
/// </summary>
public static class AutoCategorizer
{
    private static readonly Dictionary<string, FileCategory> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Photos
        [".jpg"] = FileCategory.Photos,
        [".jpeg"] = FileCategory.Photos,
        [".png"] = FileCategory.Photos,
        [".gif"] = FileCategory.Photos,
        [".webp"] = FileCategory.Photos,
        [".bmp"] = FileCategory.Photos,
        [".svg"] = FileCategory.Photos,
        [".tiff"] = FileCategory.Photos,
        [".tif"] = FileCategory.Photos,
        [".ico"] = FileCategory.Photos,
        [".heic"] = FileCategory.Photos,
        [".heif"] = FileCategory.Photos,
        [".cr2"] = FileCategory.Photos,
        [".nef"] = FileCategory.Photos,
        [".arw"] = FileCategory.Photos,
        [".dng"] = FileCategory.Photos,
        [".rw2"] = FileCategory.Photos,

        // Videos
        [".mp4"] = FileCategory.Videos,
        [".mkv"] = FileCategory.Videos,
        [".avi"] = FileCategory.Videos,
        [".mov"] = FileCategory.Videos,
        [".webm"] = FileCategory.Videos,
        [".flv"] = FileCategory.Videos,
        [".wmv"] = FileCategory.Videos,
        [".m4v"] = FileCategory.Videos,
        [".3gp"] = FileCategory.Videos,
        [".ts"] = FileCategory.Videos,

        // Audio
        [".mp3"] = FileCategory.Audio,
        [".flac"] = FileCategory.Audio,
        [".wav"] = FileCategory.Audio,
        [".aac"] = FileCategory.Audio,
        [".ogg"] = FileCategory.Audio,
        [".wma"] = FileCategory.Audio,
        [".opus"] = FileCategory.Audio,
        [".m4a"] = FileCategory.Audio,
        [".aiff"] = FileCategory.Audio,

        // Documents
        [".pdf"] = FileCategory.Documents,
        [".doc"] = FileCategory.Documents,
        [".docx"] = FileCategory.Documents,
        [".xls"] = FileCategory.Documents,
        [".xlsx"] = FileCategory.Documents,
        [".ppt"] = FileCategory.Documents,
        [".pptx"] = FileCategory.Documents,
        [".odt"] = FileCategory.Documents,
        [".ods"] = FileCategory.Documents,
        [".odp"] = FileCategory.Documents,
        [".rtf"] = FileCategory.Documents,
        [".epub"] = FileCategory.Documents,

        // Text & Notes
        [".txt"] = FileCategory.TextNotes,
        [".md"] = FileCategory.TextNotes,
        [".json"] = FileCategory.TextNotes,
        [".xml"] = FileCategory.TextNotes,
        [".html"] = FileCategory.TextNotes,
        [".htm"] = FileCategory.TextNotes,
        [".css"] = FileCategory.TextNotes,
        [".js"] = FileCategory.TextNotes,
        [".ts"] = FileCategory.TextNotes,
        [".cs"] = FileCategory.TextNotes,
        [".py"] = FileCategory.TextNotes,
        [".yaml"] = FileCategory.TextNotes,
        [".yml"] = FileCategory.TextNotes,
        [".log"] = FileCategory.TextNotes,
        [".ini"] = FileCategory.TextNotes,
        [".cfg"] = FileCategory.TextNotes,
        [".sql"] = FileCategory.TextNotes,
        [".c"] = FileCategory.TextNotes,
        [".cpp"] = FileCategory.TextNotes,
        [".h"] = FileCategory.TextNotes,

        // Applications
        [".exe"] = FileCategory.Applications,
        [".msi"] = FileCategory.Applications,
        [".appx"] = FileCategory.Applications,
        [".msix"] = FileCategory.Applications,
        [".bat"] = FileCategory.Applications,
        [".cmd"] = FileCategory.Applications,
        [".ps1"] = FileCategory.Applications,
        [".vbs"] = FileCategory.Applications,

        // Archives
        [".zip"] = FileCategory.Archives,
        [".rar"] = FileCategory.Archives,
        [".7z"] = FileCategory.Archives,
        [".tar"] = FileCategory.Archives,
        [".gz"] = FileCategory.Archives,
        [".bz2"] = FileCategory.Archives,
        [".xz"] = FileCategory.Archives,
        [".iso"] = FileCategory.Archives
    };

    public static FileCategory Categorize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return FileCategory.Other;

        string ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
            return FileCategory.Other;

        return ExtensionMap.TryGetValue(ext, out var category) ? category : FileCategory.Other;
    }
}
