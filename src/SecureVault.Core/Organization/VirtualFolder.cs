namespace SecureVault.Core.Organization;

/// <summary>
/// In-memory representation of a virtual folder (D01).
/// Virtual folders exist purely as metadata within the VaultIndex with zero disk directories.
/// </summary>
public sealed class VirtualFolder
{
    public Guid FolderGuid { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? ParentGuid { get; set; }
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public List<Guid> Children { get; } = new();

    public VirtualFolder() { }

    public VirtualFolder(string name, Guid? parentGuid = null)
    {
        Name = name;
        ParentGuid = parentGuid;
    }
}
