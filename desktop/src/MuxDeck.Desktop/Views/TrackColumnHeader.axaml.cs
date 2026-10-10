using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class TrackColumnHeader : UserControl
{
    public static readonly StyledProperty<int> IndexProperty = AvaloniaProperty.Register<TrackColumnHeader, int>(nameof(Index));
    public static readonly StyledProperty<TrackColumnLayout?> LayoutProperty = AvaloniaProperty.Register<TrackColumnHeader, TrackColumnLayout?>(nameof(Layout));
    public static readonly StyledProperty<bool> ShowGuideProperty = AvaloniaProperty.Register<TrackColumnHeader, bool>(nameof(ShowGuide));
    public static readonly DirectProperty<TrackColumnHeader, string> TitleProperty = AvaloniaProperty.RegisterDirect<TrackColumnHeader, string>(nameof(Title), h => h.Title);
    public static readonly DirectProperty<TrackColumnHeader, string> ResizeHintProperty = AvaloniaProperty.RegisterDirect<TrackColumnHeader, string>(nameof(ResizeHint), h => h.ResizeHint);
    public static readonly DirectProperty<TrackColumnHeader, HorizontalAlignment> TitleAlignmentProperty = AvaloniaProperty.RegisterDirect<TrackColumnHeader, HorizontalAlignment>(nameof(TitleAlignment), h => h.TitleAlignment);
    public int Index { get => GetValue(IndexProperty); set => SetValue(IndexProperty, value); }
    public TrackColumnLayout? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public bool ShowGuide { get => GetValue(ShowGuideProperty); set => SetValue(ShowGuideProperty, value); }
    public string Title => Index == 0 ? "" : TrackColumnLayout.Columns[Index].Title;
    public string ResizeHint => $"调整{TrackColumnLayout.Columns[Index].Title}列宽；左右键调整，Home 或双击恢复默认";
    public HorizontalAlignment TitleAlignment => Index is 1 or 5 or 6 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    private double startX, startWidth;
    private bool dragging;
    public event EventHandler? ResizeCompleted;
    public TrackColumnHeader()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IndexProperty) { RaisePropertyChanged(TitleProperty, "", Title); RaisePropertyChanged(ResizeHintProperty, "", ResizeHint); RaisePropertyChanged(TitleAlignmentProperty, HorizontalAlignment.Left, TitleAlignment); }
        };
        ResizeGrip.PointerEntered += (_, _) => ShowGuide = true;
        ResizeGrip.PointerExited += (_, _) => { if (!dragging) ShowGuide = ResizeGrip.IsFocused; };
        ResizeGrip.GotFocus += (_, _) => ShowGuide = true;
        ResizeGrip.LostFocus += (_, _) => { if (!dragging) ShowGuide = false; };
        ResizeGrip.PointerPressed += (_, e) =>
        {
            if (Layout is null || !e.GetCurrentPoint(ResizeGrip).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (e.ClickCount == 2) { Layout.Reset(Index); ResizeCompleted?.Invoke(this, EventArgs.Empty); return; }
            startX = e.GetPosition(TopLevel.GetTopLevel(this)).X;
            startWidth = Layout.Widths[Index]; dragging = true; ShowGuide = true;
            ResizeGrip.Focus(); e.Pointer.Capture(ResizeGrip);
            ResizeGrip.Classes.Add("dragging");
        };
        ResizeGrip.PointerMoved += (_, e) =>
        {
            if (dragging) Layout?.Resize(Index, startWidth + e.GetPosition(TopLevel.GetTopLevel(this)).X - startX);
        };
        ResizeGrip.PointerReleased += (_, e) => { if (dragging) { Complete(); e.Pointer.Capture(null); e.Handled = true; } };
        ResizeGrip.PointerCaptureLost += (_, _) => { if (dragging) Complete(); };
        ResizeGrip.KeyDown += (_, e) =>
        {
            if (Layout is null) return;
            if (e.Key is Key.Left or Key.Right) Layout.Resize(Index, Layout.Widths[Index] + (e.Key == Key.Right ? 1 : -1) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 8));
            else if (e.Key == Key.Home) Layout.Reset(Index);
            else return;
            e.Handled = true; ResizeCompleted?.Invoke(this, EventArgs.Empty);
        };
    }
    private void Complete()
    {
        dragging = false; ShowGuide = ResizeGrip.IsFocused;
        ResizeGrip.Classes.Remove("dragging"); ResizeCompleted?.Invoke(this, EventArgs.Empty);
    }
}
