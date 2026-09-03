using System.IO.Compression;
using System.Text;
using SecureVault.Core.Archives;
using Xunit;

namespace SecureVault.Core.Tests;

public class ArchiveReaderTests
{
    private byte[] CreateSampleZip()
    {
        var utf8WithoutBom = new UTF8Encoding(false);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry1 = zip.CreateEntry("documents/hello.txt");
            using (var writer = new StreamWriter(entry1.Open(), utf8WithoutBom))
            {
                writer.Write("Hello SecureVault Archive!");
            }

            var entry2 = zip.CreateEntry("notes.md");
            using (var writer = new StreamWriter(entry2.Open(), utf8WithoutBom))
            {
                writer.Write("# Markdown Note Inside Zip");
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void ListContents_ReturnsAllEntries()
    {
        byte[] zipBytes = CreateSampleZip();
        using var reader = new ArchiveReader(zipBytes);

        var contents = reader.ListContents();
        Assert.Equal(2, contents.Count);
        Assert.Contains(contents, c => c.Path.EndsWith("hello.txt"));
        Assert.Contains(contents, c => c.Path.EndsWith("notes.md"));
    }

    [Fact]
    public void ExtractSingle_ReturnsExactBytesWithoutTouchingDisk()
    {
        byte[] zipBytes = CreateSampleZip();
        using var reader = new ArchiveReader(zipBytes);

        byte[] extracted = reader.ExtractSingle("documents/hello.txt");
        string text = Encoding.UTF8.GetString(extracted);
        Assert.Equal("Hello SecureVault Archive!", text);
    }

    [Fact]
    public void ExtractAll_ExtractsAllFilesToMemory()
    {
        byte[] zipBytes = CreateSampleZip();
        using var reader = new ArchiveReader(zipBytes);

        var all = reader.ExtractAll();
        Assert.Equal(2, all.Count);

        var helloItem = all.First(a => a.RelativePath.EndsWith("hello.txt"));
        Assert.Equal("Hello SecureVault Archive!", Encoding.UTF8.GetString(helloItem.Data));

        var notesItem = all.First(a => a.RelativePath.EndsWith("notes.md"));
        Assert.Equal("# Markdown Note Inside Zip", Encoding.UTF8.GetString(notesItem.Data));
    }
}
