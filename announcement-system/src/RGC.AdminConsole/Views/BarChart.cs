using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RGC.AdminConsole.ViewModels;

namespace RGC.AdminConsole.Views;

/// <summary>
/// Minimal single-series column chart (one bar per day). Thin bars with rounded tops on a
/// recessive baseline and one gridline; hovering a bar shows its value above it.
/// </summary>
public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<ChartPoint>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartPoint>? Points
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    private const double AxisLeft = 30, AxisBottom = 20, Top = 22;
    private int _hover = -1;

    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x0E, 0x16, 0x26));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x5B, 0x6B, 0x72));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(0xDD, 0xE4, 0xE2)), 1);
    private static readonly Typeface Face = new("Segoe UI");

    static BarChart()
    {
        Ink.Freeze();
        Muted.Freeze();
        GridPen.Freeze();
    }

    public BarChart() => MinHeight = 120;

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent background so the whole plot area receives mouse moves (hit target > bar).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var points = Points;
        var width = ActualWidth - AxisLeft;
        var height = ActualHeight - AxisBottom - Top;
        if (points is null || points.Count == 0 || width <= 0 || height <= 0) return;

        var max = NiceMax(points.Max(p => p.Value));
        var baseY = Top + height;
        var slot = width / points.Count;
        var barWidth = Math.Clamp(slot * 0.62, 2, 22);

        // Gridline at the top value and the baseline; labels in muted text, never series colour.
        dc.DrawLine(GridPen, new Point(AxisLeft, Top), new Point(ActualWidth, Top));
        dc.DrawLine(GridPen, new Point(AxisLeft, baseY), new Point(ActualWidth, baseY));
        DrawText(dc, max.ToString("0", CultureInfo.CurrentCulture), new Point(AxisLeft - 6, Top), Muted, 11, alignRight: true, centerY: true);
        DrawText(dc, "0", new Point(AxisLeft - 6, baseY), Muted, 11, alignRight: true, centerY: true);

        if (points.All(p => p.Value <= 0))
            DrawText(dc, "No data in this period", new Point(AxisLeft + width / 2, Top + height / 2), Muted, 12, center: true, centerY: true);

        for (var i = 0; i < points.Count; i++)
        {
            var value = points[i].Value;
            if (value <= 0) continue;
            var h = Math.Max(2, height * value / max);
            var x = AxisLeft + slot * i + (slot - barWidth) / 2;
            DrawBar(dc, new Rect(x, baseY - h, barWidth, h), i == _hover || _hover < 0 ? 1 : 0.55);
        }

        // First, middle and last date under the axis.
        foreach (var i in new[] { 0, points.Count / 2, points.Count - 1 }.Distinct())
            DrawText(dc, points[i].Label, new Point(AxisLeft + slot * i + slot / 2, baseY + 4), Muted, 11, center: true);

        if (_hover >= 0 && _hover < points.Count)
        {
            var p = points[_hover];
            var cx = AxisLeft + slot * _hover + slot / 2;
            var h = p.Value <= 0 ? 0 : Math.Max(2, height * p.Value / max);
            DrawTooltip(dc, p.Tooltip, new Point(cx, baseY - h - 6));
        }
    }

    /// <summary>Column with rounded top corners and a square base sitting on the axis.</summary>
    private void DrawBar(DrawingContext dc, Rect r, double opacity)
    {
        var radius = Math.Min(3, r.Width / 2);
        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(BarBrush, null, r, radius, radius);
        if (r.Height > radius)
            dc.DrawRectangle(BarBrush, null, new Rect(r.X, r.Bottom - radius, r.Width, radius));
        dc.Pop();
    }

    private void DrawTooltip(DrawingContext dc, string text, Point anchorBottom)
    {
        var ft = Text(text, Brushes.White, 12);
        var box = new Rect(anchorBottom.X - ft.Width / 2 - 8, anchorBottom.Y - ft.Height - 8, ft.Width + 16, ft.Height + 8);
        // Keep inside the control.
        if (box.Left < 0) box.X = 0;
        if (box.Right > ActualWidth) box.X = ActualWidth - box.Width;
        if (box.Top < 0) box.Y = 0;
        dc.DrawRoundedRectangle(Ink, null, box, 4, 4);
        dc.DrawText(ft, new Point(box.X + 8, box.Y + 4));
    }

    private void DrawText(DrawingContext dc, string text, Point at, Brush brush, double size,
        bool center = false, bool alignRight = false, bool centerY = false)
    {
        var ft = Text(text, brush, size);
        var x = center ? at.X - ft.Width / 2 : alignRight ? at.X - ft.Width : at.X;
        var y = centerY ? at.Y - ft.Height / 2 : at.Y;
        dc.DrawText(ft, new Point(x, y));
    }

    private FormattedText Text(string text, Brush brush, double size) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Rounds the axis maximum up to 1, 2 or 5 × 10^n.</summary>
    private static double NiceMax(double value)
    {
        if (value <= 1) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 5, 10 })
            if (value <= step * magnitude) return step * magnitude;
        return 10 * magnitude;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var count = Points?.Count ?? 0;
        var x = e.GetPosition(this).X - AxisLeft;
        var index = count == 0 || x < 0 ? -1 : Math.Min(count - 1, (int)(x / ((ActualWidth - AxisLeft) / count)));
        if (index == _hover) return;
        _hover = index;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }
}
