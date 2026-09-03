using Microsoft.UI.Xaml.Controls;

namespace SecureVault.App.Views;

public sealed partial class RecoveryKeyConfirmationDialog : ContentDialog
{
    private readonly string[] _words;
    private readonly int _idx1, _idx2, _idx3;

    public RecoveryKeyConfirmationDialog(string[] words)
    {
        InitializeComponent();
        _words = words;

        var displayItems = new List<string>();
        for (int i = 0; i < words.Length; i++)
        {
            displayItems.Add($"{i + 1}. {words[i]}");
        }
        WordsRepeater.ItemsSource = displayItems;

        // Select 3 random word positions (1-indexed)
        var rng = new Random();
        var indices = Enumerable.Range(0, 24).OrderBy(_ => rng.Next()).Take(3).OrderBy(i => i).ToArray();
        _idx1 = indices[0];
        _idx2 = indices[1];
        _idx3 = indices[2];

        PromptWord1.Text = $"Word #{_idx1 + 1}";
        PromptWord2.Text = $"Word #{_idx2 + 1}";
        PromptWord3.Text = $"Word #{_idx3 + 1}";

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string w1 = InputWord1.Text.Trim();
        string w2 = InputWord2.Text.Trim();
        string w3 = InputWord3.Text.Trim();

        bool match1 = string.Equals(w1, _words[_idx1], StringComparison.OrdinalIgnoreCase);
        bool match2 = string.Equals(w2, _words[_idx2], StringComparison.OrdinalIgnoreCase);
        bool match3 = string.Equals(w3, _words[_idx3], StringComparison.OrdinalIgnoreCase);

        if (!match1 || !match2 || !match3)
        {
            args.Cancel = true;
            ErrorText.Text = "One or more words do not match the recovery phrase. Please check and try again.";
            ErrorText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }
}
