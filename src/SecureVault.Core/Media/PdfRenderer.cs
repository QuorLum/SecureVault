using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace SecureVault.Core.Media;

/// <summary>
/// In-memory PDF document renderer powered by Docnet.Core (Pdfium) (L01-L06).
/// Guarantees zero temporary files written to disk; renders directly from memory-resident byte arrays.
/// </summary>
public sealed class PdfRenderer : IDisposable
{
    private readonly byte[] _pdfBytes;
    private readonly IDocReader _docReader;
    private bool _disposed;

    public int PageCount => _docReader.GetPageCount();

    public PdfRenderer(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length == 0)
            throw new ArgumentException("PDF byte array cannot be empty.", nameof(pdfBytes));

        _pdfBytes = pdfBytes;
        _docReader = DocLib.Instance.GetDocReader(_pdfBytes, new PageDimensions(1080, 1920));
    }

    public PdfRenderer(Stream pdfStream)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        using var ms = new MemoryStream();
        pdfStream.CopyTo(ms);
        _pdfBytes = ms.ToArray();
        if (_pdfBytes.Length == 0)
            throw new ArgumentException("PDF stream contains no data.", nameof(pdfStream));

        _docReader = DocLib.Instance.GetDocReader(_pdfBytes, new PageDimensions(1080, 1920));
    }

    /// <summary>
    /// Renders a specific 0-based page into a raw 32-bit BGRA pixel buffer at the specified scale factor.
    /// </summary>
    public (byte[] BgraBytes, int Width, int Height) RenderPage(int pageIndex, double scale = 1.0)
    {
        EnsureNotDisposed();

        if (pageIndex < 0 || pageIndex >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), $"Page index {pageIndex} is out of range (Total: {PageCount}).");

        if (scale <= 0) scale = 1.0;

        using var pageReader = _docReader.GetPageReader(pageIndex);
        int origWidth = pageReader.GetPageWidth();
        int origHeight = pageReader.GetPageHeight();

        if (Math.Abs(scale - 1.0) < 0.01)
        {
            byte[] rawBgra = pageReader.GetImage();
            return (rawBgra, origWidth, origHeight);
        }

        // Calculate scaled dimensions (clamped to sensible bounds)
        int scaledWidth = Math.Clamp((int)(origWidth * scale), 10, 8192);
        int scaledHeight = Math.Clamp((int)(origHeight * scale), 10, 8192);

        using var scaledDocReader = DocLib.Instance.GetDocReader(_pdfBytes, new PageDimensions(scaledWidth, scaledHeight));
        using var scaledPageReader = scaledDocReader.GetPageReader(pageIndex);
        byte[] scaledBgra = scaledPageReader.GetImage();
        int actualWidth = scaledPageReader.GetPageWidth();
        int actualHeight = scaledPageReader.GetPageHeight();

        return (scaledBgra, actualWidth, actualHeight);
    }

    /// <summary>
    /// Extracts plain text from a page in memory (L09).
    /// </summary>
    public string GetPageText(int pageIndex)
    {
        EnsureNotDisposed();
        if (pageIndex < 0 || pageIndex >= PageCount) return string.Empty;
        using var pageReader = _docReader.GetPageReader(pageIndex);
        return pageReader.GetText() ?? string.Empty;
    }

    /// <summary>
    /// Searches document text across pages in memory and returns 1-based page numbers with hits (L07).
    /// </summary>
    public IReadOnlyList<int> SearchText(string query)
    {
        EnsureNotDisposed();
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<int>();

        var hits = new List<int>();
        for (int i = 0; i < PageCount; i++)
        {
            string text = GetPageText(i);
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(i + 1);
            }
        }
        return hits;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PdfRenderer));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _docReader.Dispose();
        _disposed = true;
    }
}
