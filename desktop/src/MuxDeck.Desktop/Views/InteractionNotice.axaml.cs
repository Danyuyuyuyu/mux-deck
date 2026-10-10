using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class InteractionNotice : UserControl
{
    private InteractionNotification? observed;
    private IDisposable? expiry;
    public InteractionNotice()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => { expiry?.Dispose(); expiry = null; if (observed is not null) observed.PropertyChanged -= Changed; observed = null; };
    }
    private void Observe()
    {
        if (ReferenceEquals(observed, DataContext)) return;
        if (observed is not null) observed.PropertyChanged -= Changed;
        observed = DataContext as InteractionNotification;
        if (observed is not null) observed.PropertyChanged += Changed;
        Schedule();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InteractionNotification.Revision)) Schedule();
        if (e.PropertyName == nameof(InteractionNotification.IsVisible) && observed?.IsVisible != true) { expiry?.Dispose(); expiry = null; }
    }
    private void Schedule()
    {
        expiry?.Dispose(); expiry = null;
        if (observed is not { IsVisible: true, Lifetime: { } lifetime } notification) return;
        var revision = notification.Revision;
        expiry = DispatcherTimer.RunOnce(() => notification.Dismiss(revision), lifetime);
    }
    private void DismissNotice(object? sender, RoutedEventArgs e) { if (observed is { } notification) notification.Dismiss(notification.Revision); }
}
