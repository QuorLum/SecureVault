using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.ViewModels;
using SecureVault.Core;
using SecureVault.Core.Format;

namespace SecureVault.App.Views;

public sealed partial class MediaPlayerPage : Page
{
    public MediaPlayerViewModel? ViewModel { get; private set; }
    private bool _isFullScreen;

    public MediaPlayerPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is (VaultManager vault, IndexEntry entry))
        {
            ViewModel = new MediaPlayerViewModel(vault, entry);
            DataContext = ViewModel;

            if (ViewModel.Player != null && !ViewModel.IsAudioOnly)
            {
                VlcVideoView.MediaPlayer = ViewModel.Player;
            }

            ViewModel.OnCloseRequested = () =>
            {
                if (Frame.CanGoBack) Frame.GoBack();
            };
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel?.Dispose();
        ViewModel = null;
    }

    private void OnSeekSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.FocusState != FocusState.Unfocused)
        {
            ViewModel?.Seek(e.NewValue);
        }
    }

    private void OnRateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RateCombo.SelectedItem is ComboBoxItem item && float.TryParse(item.Tag as string, out float rate))
        {
            ViewModel?.SetRate(rate);
        }
    }

    private void OnToggleFullscreenClicked(object sender, RoutedEventArgs e)
    {
        var appWindow = App.CurrentWindow.AppWindow;
        if (!_isFullScreen)
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            _isFullScreen = true;
        }
        else
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            _isFullScreen = false;
        }
    }
}
