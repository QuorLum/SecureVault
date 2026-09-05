using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace SecureVault.App.Views;

public sealed partial class RecoveryKeyConfirmationDialog : ContentDialog
{
    private readonly string[] _words;
    private readonly int _idx1, _idx2, _idx3;
    private bool _isStep2;

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

        // Select 3 random word positions (0-indexed)
        var rng = new Random();
        var indices = Enumerable.Range(0, 24).OrderBy(_ => rng.Next()).Take(3).OrderBy(i => i).ToArray();
        _idx1 = indices[0];
        _idx2 = indices[1];
        _idx3 = indices[2];

        PromptWord1.Text = $"Word #{_idx1 + 1}";
        PromptWord2.Text = $"Word #{_idx2 + 1}";
        PromptWord3.Text = $"Word #{_idx3 + 1}";

        // Step 1: Initially disabled until user acknowledges
        IsPrimaryButtonEnabled = false;

        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
    }

    private void OnAcknowledgeChanged(object sender, RoutedEventArgs e)
    {
        if (!_isStep2)
        {
            IsPrimaryButtonEnabled = AcknowledgeCheckBox.IsChecked == true;
        }
    }

    private void OnCopyWordsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(string.Join(" ", _words));
            Clipboard.SetContent(package);
            CopyStatusText.Text = "Copied 24 words to clipboard!";
            CopyStatusText.Visibility = Visibility.Visible;
        }
        catch
        {
            CopyStatusText.Text = "Unable to copy to clipboard";
            CopyStatusText.Visibility = Visibility.Visible;
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!_isStep2)
        {
            // Transition from Step 1 (Display) to Step 2 (Blind Verification)
            args.Cancel = true;
            _isStep2 = true;

            Step1Container.Visibility = Visibility.Collapsed;
            Step2Container.Visibility = Visibility.Visible;

            PrimaryButtonText = "Verify and Proceed";
            SecondaryButtonText = "‹ Back to View Words";
            IsPrimaryButtonEnabled = true;

            InputWord1.Text = string.Empty;
            InputWord2.Text = string.Empty;
            InputWord3.Text = string.Empty;
            ErrorText.Visibility = Visibility.Collapsed;

            InputWord1.Focus(FocusState.Programmatic);
            return;
        }

        // Step 2: Validate the 3 entered words against the hidden recovery phrase
        string w1 = InputWord1.Text.Trim();
        string w2 = InputWord2.Text.Trim();
        string w3 = InputWord3.Text.Trim();

        bool match1 = string.Equals(w1, _words[_idx1], StringComparison.OrdinalIgnoreCase);
        bool match2 = string.Equals(w2, _words[_idx2], StringComparison.OrdinalIgnoreCase);
        bool match3 = string.Equals(w3, _words[_idx3], StringComparison.OrdinalIgnoreCase);

        if (!match1 || !match2 || !match3)
        {
            args.Cancel = true;
            ErrorText.Text = "One or more words do not match the recovery phrase. Please check your backup and try again.";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isStep2)
        {
            // Allow user to go back to Step 1 to re-read words
            args.Cancel = true;
            _isStep2 = false;

            Step2Container.Visibility = Visibility.Collapsed;
            Step1Container.Visibility = Visibility.Visible;

            PrimaryButtonText = "Continue to Verification >";
            SecondaryButtonText = string.Empty;
            ErrorText.Visibility = Visibility.Collapsed;

            IsPrimaryButtonEnabled = AcknowledgeCheckBox.IsChecked == true;
        }
    }
}
