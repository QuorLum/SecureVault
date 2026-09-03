using SecureVault.Core.Notes;
using Xunit;

namespace SecureVault.Core.Tests;

public class NoteDocumentTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("Hello", 1)]
    [InlineData("Hello   world!", 2)]
    [InlineData("The quick brown fox jumps over the lazy dog.", 9)]
    [InlineData("Line 1\nLine 2\r\nLine 3\tLine 4", 8)]
    public void ComputeWordCount_CalculatesAccurately(string text, int expectedCount)
    {
        int count = NoteDocument.ComputeWordCount(text);
        Assert.Equal(expectedCount, count);
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesAllFields()
    {
        var original = new NoteDocument
        {
            Title = "Secret Passwords & Keys",
            Content = "# Confidential\n\n- Key 1: `vault-alpha`\n- Key 2: `vault-beta`",
            Format = NoteFormat.Markdown,
            CreatedUtc = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(2026, 2, 20, 14, 45, 0, DateTimeKind.Utc)
        };

        byte[] serialized = original.Serialize();
        Assert.NotEmpty(serialized);

        var restored = NoteDocument.Deserialize(serialized);

        Assert.NotNull(restored);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Content, restored.Content);
        Assert.Equal(original.Format, restored.Format);
        Assert.Equal(original.CreatedUtc, restored.CreatedUtc);
        Assert.Equal(original.ModifiedUtc, restored.ModifiedUtc);
        Assert.Equal(original.WordCount, restored.WordCount);
    }

    [Fact]
    public void RenderMarkdownToHtml_GeneratesValidHtmlMarkup()
    {
        var doc = new NoteDocument
        {
            Content = "# Heading 1\n\nThis is **bold** text and *italic* text."
        };

        string html = doc.RenderMarkdownToHtml();

        Assert.Contains("<h1", html);
        Assert.Contains("Heading 1</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
    }
}
