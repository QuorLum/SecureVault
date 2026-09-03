using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Tests;

public class VirtualFolderServiceTests
{
    [Fact]
    public void RootFolder_ContainsTopLevelFoldersAndFiles()
    {
        var index = new VaultIndex();
        var service = new VirtualFolderService(index);

        var folderA = service.CreateFolder("FolderA");
        var folderB = service.CreateFolder("FolderB");

        index.Entries.Add(new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "root_file.txt",
            ParentFolderGuid = null
        });

        var root = service.GetRoot();
        Assert.Equal("/", root.Name);
        Assert.Contains(folderA, root.Children);
        Assert.Contains(folderB, root.Children);

        var rootFiles = service.GetFiles(null);
        Assert.Single(rootFiles);
        Assert.Equal("root_file.txt", rootFiles[0].FileName);
    }

    [Fact]
    public void NestedFolders_ResolvesFullPathAccurately()
    {
        var index = new VaultIndex();
        var service = new VirtualFolderService(index);

        var a = service.CreateFolder("A");
        var b = service.CreateFolder("B", a);
        var c = service.CreateFolder("C", b);

        Assert.Equal("/A", service.GetFullPath(a));
        Assert.Equal("/A/B", service.GetFullPath(b));
        Assert.Equal("/A/B/C", service.GetFullPath(c));
    }

    [Fact]
    public void DeleteFolder_DefaultKeepsFilesByUnfolderingToRoot()
    {
        var index = new VaultIndex();
        var service = new VirtualFolderService(index);

        var folder = service.CreateFolder("Photos");
        var fileGuid = Guid.NewGuid();
        index.Entries.Add(new IndexEntry
        {
            FileGuid = fileGuid,
            FileName = "vacation.jpg",
            ParentFolderGuid = folder,
            VirtualFolderPath = "/Photos"
        });

        // Delete folder with deleteFiles = false
        service.DeleteFolder(folder, deleteFiles: false);

        var deletedFolder = service.GetFolder(folder);
        Assert.Null(deletedFolder);

        var file = index.Entries.First(e => e.FileGuid == fileGuid);
        Assert.False(file.IsDeleted);
        Assert.Null(file.ParentFolderGuid);
        Assert.Equal("/", file.VirtualFolderPath);
    }

    [Fact]
    public void UnlimitedNesting_WorksAcross20Levels()
    {
        var index = new VaultIndex();
        var service = new VirtualFolderService(index);

        Guid? parent = null;
        for (int i = 1; i <= 20; i++)
        {
            parent = service.CreateFolder($"Level{i}", parent);
        }

        string fullPath = service.GetFullPath(parent);
        Assert.StartsWith("/Level1/Level2/", fullPath);
        Assert.EndsWith("/Level20", fullPath);
    }
}
