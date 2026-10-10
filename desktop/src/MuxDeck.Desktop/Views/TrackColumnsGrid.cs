using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

// Grid 定义不能共享为同一个控件集合；共享尺寸状态，各行维护自己的定义实例。
public sealed class TrackColumnsGrid : Grid
{
    public static readonly StyledProperty<TrackColumnLayout?> LayoutProperty = AvaloniaProperty.Register<TrackColumnsGrid, TrackColumnLayout?>(nameof(Layout));
    public TrackColumnLayout? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    private TrackColumnLayout? subscribed;
    public TrackColumnsGrid()
    {
        PropertyChanged += (_, e) => { if (e.Property == LayoutProperty) Observe(); };
        AttachedToVisualTree += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => { if (subscribed is not null) subscribed.PropertyChanged -= Changed; subscribed = null; };
    }
    private void Observe()
    {
        if (subscribed is not null) subscribed.PropertyChanged -= Changed;
        subscribed = Layout;
        if (subscribed is not null) subscribed.PropertyChanged += Changed;
        Update();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(TrackColumnLayout.Widths)) Update(); }
    private void Update()
    {
        if (Layout is null) return;
        if (ColumnDefinitions.Count != Layout.Widths.Count)
        {
            ColumnDefinitions.Clear();
            foreach (var width in Layout.Widths) ColumnDefinitions.Add(new ColumnDefinition(width, GridUnitType.Pixel));
        }
        else for (var i = 0; i < ColumnDefinitions.Count; i++) ColumnDefinitions[i].Width = new GridLength(Layout.Widths[i]);
        Width = Layout.TotalWidth;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
    }
}
