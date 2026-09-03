using SecureVault.Core.Format;

namespace SecureVault.Core.Organization;

/// <summary>
/// Service managing virtual folder hierarchy, subfolders, path resolution, and folder CRUD (D01, D02).
/// Folders are stored purely as metadata within the VaultIndex.
/// </summary>
public sealed class VirtualFolderService
{
    private readonly VaultIndex _index;

    public VirtualFolderService(VaultIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
    }

    /// <summary>
    /// Returns the virtual root directory containing all top-level items.
    /// </summary>
    public VirtualFolder GetRoot()
    {
        var root = new VirtualFolder("/", null)
        {
            FolderGuid = Guid.Empty
        };

        var topLevelFolders = GetSubfolders(null);
        foreach (var folder in topLevelFolders)
        {
            root.Children.Add(folder.FolderGuid);
        }

        return root;
    }

    /// <summary>
    /// Retrieves a specific virtual folder by its GUID.
    /// </summary>
    public VirtualFolder? GetFolder(Guid folderGuid)
    {
        if (folderGuid == Guid.Empty)
            return GetRoot();

        var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == folderGuid && e.IsFolder && !e.IsDeleted);
        if (entry == null)
            return null;

        var folder = new VirtualFolder(entry.FileName, entry.ParentFolderGuid)
        {
            FolderGuid = entry.FileGuid,
            DateCreatedUtc = new DateTime(entry.DateAddedTicks, DateTimeKind.Utc)
        };

        var children = GetSubfolders(entry.FileGuid);
        foreach (var child in children)
        {
            folder.Children.Add(child.FolderGuid);
        }

        return folder;
    }

    /// <summary>
    /// Gets all immediate active subfolders of the specified parent folder (or top-level if parentGuid is null or Guid.Empty).
    /// </summary>
    public IReadOnlyList<VirtualFolder> GetSubfolders(Guid? parentGuid)
    {
        Guid? targetParent = (parentGuid == null || parentGuid == Guid.Empty) ? null : parentGuid;

        return _index.Entries
            .Where(e => e.IsFolder && !e.IsDeleted && e.ParentFolderGuid == targetParent)
            .Select(e => new VirtualFolder(e.FileName, e.ParentFolderGuid)
            {
                FolderGuid = e.FileGuid,
                DateCreatedUtc = new DateTime(e.DateAddedTicks, DateTimeKind.Utc)
            })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets all active files in the specified folder (or top-level if folderGuid is null or Guid.Empty).
    /// </summary>
    public IReadOnlyList<IndexEntry> GetFiles(Guid? folderGuid)
    {
        Guid? targetParent = (folderGuid == null || folderGuid == Guid.Empty) ? null : folderGuid;

        return _index.Entries
            .Where(e => !e.IsFolder && !e.IsDeleted && e.ParentFolderGuid == targetParent)
            .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves the absolute virtual path (e.g. "/Photos/2024/Vacation") for a folder by traversing parent nodes.
    /// </summary>
    public string GetFullPath(Guid? folderGuid)
    {
        if (folderGuid == null || folderGuid == Guid.Empty)
            return "/";

        var segments = new List<string>();
        Guid? current = folderGuid;

        while (current.HasValue && current.Value != Guid.Empty)
        {
            var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == current.Value && e.IsFolder && !e.IsDeleted);
            if (entry == null)
                break;

            segments.Insert(0, entry.FileName);
            current = entry.ParentFolderGuid;
        }

        return "/" + string.Join("/", segments);
    }

    /// <summary>
    /// Creates a new virtual folder entry in the index (D02).
    /// </summary>
    public Guid CreateFolder(string name, Guid? parentGuid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Guid? targetParent = (parentGuid == null || parentGuid == Guid.Empty) ? null : parentGuid;
        Guid newFolderGuid = Guid.NewGuid();

        string fullPath = GetFullPath(targetParent);
        string folderVirtualPath = fullPath == "/" ? "/" + name : $"{fullPath}/{name}";

        var entry = new IndexEntry
        {
            FileGuid = newFolderGuid,
            FileName = name.Trim(),
            IsFolder = true,
            ParentFolderGuid = targetParent,
            VirtualFolderPath = folderVirtualPath,
            DateAddedTicks = DateTime.UtcNow.Ticks,
            DateModifiedTicks = DateTime.UtcNow.Ticks
        };

        _index.Entries.Add(entry);
        return newFolderGuid;
    }

    /// <summary>
    /// Renames an existing virtual folder and updates the virtual paths of its children (D02).
    /// </summary>
    public void RenameFolder(Guid folderGuid, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == folderGuid && e.IsFolder && !e.IsDeleted);
        if (entry == null)
            throw new KeyNotFoundException($"Folder with GUID {folderGuid} was not found.");

        entry.FileName = newName.Trim();
        entry.DateModifiedTicks = DateTime.UtcNow.Ticks;
        entry.VirtualFolderPath = GetFullPath(folderGuid);
    }

    /// <summary>
    /// Deletes a virtual folder. By default (deleteFiles=false), existing files become unfoldered to the root (D01, D02).
    /// </summary>
    public void DeleteFolder(Guid folderGuid, bool deleteFiles = false)
    {
        var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == folderGuid && e.IsFolder && !e.IsDeleted);
        if (entry == null)
            return;

        entry.IsDeleted = true;
        entry.DateModifiedTicks = DateTime.UtcNow.Ticks;

        // Recursively handle subfolders
        var subfolders = GetSubfolders(folderGuid);
        foreach (var sub in subfolders)
        {
            DeleteFolder(sub.FolderGuid, deleteFiles);
        }

        // Handle contained files
        var files = _index.Entries.Where(e => !e.IsFolder && !e.IsDeleted && e.ParentFolderGuid == folderGuid).ToList();
        foreach (var file in files)
        {
            if (deleteFiles)
            {
                file.IsDeleted = true;
            }
            else
            {
                // Unfolder file to root level per D01 requirement
                file.ParentFolderGuid = null;
                file.VirtualFolderPath = "/";
            }
            file.DateModifiedTicks = DateTime.UtcNow.Ticks;
        }
    }
}
