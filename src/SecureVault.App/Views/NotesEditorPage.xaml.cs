using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.ViewModels;
using SecureVault.Core;
using SecureVault.Core.Format;
using Windows.System;

namespace SecureVault.App.Views;

public sealed partial class NotesEditorPage : Page
{
    public NotesEditorViewModel? ViewModel { get; private set; }

    public NotesEditorPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ValueTuple<VaultManager, IndexEntry?> tuple)
        {
            ViewModel = new NotesEditorViewModel(tuple.Item1, tuple.Item2);
            DataContext = ViewModel;

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

    protected override async void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel == null) return;

        if (e.Key == VirtualKey.S && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            await ViewModel.SaveAsync();
            e.Handled = true;
        }
    }
}
