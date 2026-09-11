using System;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class MiniPlaybackBar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(MiniPlaybackBar),
            new PropertyMetadata(null));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MiniPlaybackBar()
    {
        this.InitializeComponent();
    }

    private const double SEEK_BAR_HEIGHT = 4.0;
    private const double SEEK_BAR_HOVER_HEIGHT = 6.0;

    private async void OnProgressBarPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel is null || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        double fraction = e.GetCurrentPoint(element).Position.X / element.ActualWidth;
        await ViewModel.SeekToFractionAsync(fraction);
    }

    private void OnSeekBarPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SeekHoverThumb.Visibility = Visibility.Visible;
        SeekProgressBar.Height = SEEK_BAR_HOVER_HEIGHT;
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SeekHoverThumb.Visibility = Visibility.Collapsed;
        SeekProgressBar.Height = SEEK_BAR_HEIGHT;
    }

    private void PositionSeekThumb(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        double half = SeekHoverThumb.Width / 2.0;
        double pointerX = e.GetCurrentPoint(element).Position.X;
        SeekHoverThumbTransform.X = Math.Clamp(
            pointerX - half,
            0,
            Math.Max(0, element.ActualWidth - SeekHoverThumb.Width));
    }

    private async void OnSkipPreviousClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.SkipToPreviousAsync();
    }

    private async void OnTogglePlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.TogglePlayPauseAsync();
    }

    private async void OnSkipNextClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.SkipToNextAsync();
    }

    private void OnToggleKioskClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleKioskMode();
    }
}
