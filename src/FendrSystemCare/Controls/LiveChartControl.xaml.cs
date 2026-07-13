using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FendrSystemCare.Controls;

/// <summary>
/// Yeniden kullanılabilir canlı grafik kontrolü. PointCollection bağlandığında
/// otomatik olarak yeniden boyutlandırılır.
/// </summary>
public partial class LiveChartControl : UserControl
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(PointCollection), typeof(LiveChartControl),
            new PropertyMetadata(null, OnPointsChanged));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(LiveChartControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(LiveChartControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x33, 0x0A, 0x84, 0xFF))));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(LiveChartControl),
            new PropertyMetadata(string.Empty));

    public PointCollection? Points
    {
        get => (PointCollection?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public LiveChartControl() => InitializeComponent();

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LiveChartControl chart) chart.Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        ChartCanvas.Children.Clear();
        var points = Points;
        if (points is null || points.Count < 2) return;

        var w = ActualWidth > 0 ? ActualWidth : 200;
        var h = ActualHeight > 0 ? ActualHeight : 80;

        var polyline = new Polyline
        {
            Stroke = StrokeBrush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Points = points
        };
        ChartCanvas.Children.Add(polyline);

        var fill = new Polygon { Fill = FillBrush };
        var fillPoints = new PointCollection(points);
        fillPoints.Add(new Point(points[^1].X, h));
        fillPoints.Add(new Point(points[0].X, h));
        fill.Points = fillPoints;
        ChartCanvas.Children.Add(fill);
    }
}
