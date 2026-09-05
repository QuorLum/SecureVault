using System.Text.Json;
using System.Text.Json.Serialization;
using Markdig;

namespace SecureVault.Core.Notes;

[JsonConverter(typeof(JsonStringEnumConverter<NoteFormat>))]
public enum NoteFormat
{
    PlainText = 0,
    Markdown = 1,
    RichText = 2
}

/// <summary>
/// Data model for encrypted notes created and edited within the vault (J01-J08).
/// </summary>
public sealed class NoteDocument
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public string Title { get; set; } = "Untitled Note";

    public string Content { get; set; } = string.Empty;

    public NoteFormat Format { get; set; } = NoteFormat.Markdown;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public int WordCount => ComputeWordCount(Content);

    public byte[] Serialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this, SecureVault.Core.IO.SecureVaultJsonContext.Default.NoteDocument);
    }

    public static NoteDocument Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var doc = JsonSerializer.Deserialize(data, SecureVault.Core.IO.SecureVaultJsonContext.Default.NoteDocument);
        return doc ?? new NoteDocument();
    }

    public void ClearAndZero()
    {
        SecureVault.Core.Format.VaultIndex.WipeString(Title);
        Title = string.Empty;

        SecureVault.Core.Format.VaultIndex.WipeString(Content);
        Content = string.Empty;
    }

    public string RenderMarkdownToHtml()
    {
        if (string.IsNullOrWhiteSpace(Content))
            return string.Empty;

        return Markdown.ToHtml(Content, MarkdownPipeline);
    }

    public static int ComputeWordCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        char[] delimiters = { ' ', '\r', '\n', '\t', '\f', '\v' };
        return text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
