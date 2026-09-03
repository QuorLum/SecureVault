using System.Text;
using SecureVault.Core.Media;
using Xunit;

namespace SecureVault.Core.Tests;

public class PdfRendererTests
{
    private static readonly byte[] MinimalValidPdf = Encoding.ASCII.GetBytes(
@"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>
endobj
xref
0 4
0000000000 65535 f 
0000000009 00000 n 
0000000052 00000 n 
0000000103 00000 n 
trailer
<< /Size 4 /Root 1 0 R >>
startxref
167
%%EOF");

    [Fact]
    public void PageCount_ReadsPageCountAccuratelyFromMemory()
    {
        using var renderer = new PdfRenderer(MinimalValidPdf);
        Assert.Equal(1, renderer.PageCount);
    }

    [Fact]
    public void RenderPage_GeneratesRawBgraBufferInMemory()
    {
        using var renderer = new PdfRenderer(MinimalValidPdf);
        var (bgraBytes, width, height) = renderer.RenderPage(0, scale: 1.0);

        Assert.NotEmpty(bgraBytes);
        Assert.True(width > 0);
        Assert.True(height > 0);
        // 4 bytes per pixel (BGRA)
        Assert.Equal(width * height * 4, bgraBytes.Length);
    }

    [Fact]
    public void RenderPage_ThrowsOnInvalidPageIndex()
    {
        using var renderer = new PdfRenderer(MinimalValidPdf);

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderPage(99));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderPage(-1));
    }
}
