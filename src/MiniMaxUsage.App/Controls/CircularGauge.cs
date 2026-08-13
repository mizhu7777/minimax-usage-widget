using System.Windows;
using System.Windows.Media;

namespace MiniMaxUsage.App.Controls;

public sealed class CircularGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(CircularGauge),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(37, 131, 247)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(CircularGauge),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(36, 90, 113, 136)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(CircularGauge),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public static double CalculateSweep(double value) => Math.Clamp(value, 0, 100) / 100d * 270d;

    public static (double x, double y) CalculateTextPosition(
        double renderWidth, double renderHeight,
        double textWidth, double textHeight)
    {
        var size = Math.Min(renderWidth, renderHeight);
        var centerX = size / 2 + (renderWidth - size) / 2;
        var centerY = size / 2 + (renderHeight - size) / 2;
        return (centerX - textWidth / 2, centerY - textHeight / 2);
    }

    protected override Size MeasureOverride(Size available)
    {
        return new Size(
            double.IsNaN(Width) ? Math.Min(available.Width, 110) : Width,
            double.IsNaN(Height) ? Math.Min(available.Height, 110) : Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth > 0 ? ActualWidth : 110;
        var h = ActualHeight > 0 ? ActualHeight : 110;
        var size = Math.Min(w, h);
        var centerX = size / 2 + (w - size) / 2;
        var centerY = size / 2 + (h - size) / 2;
        var center = new Point(centerX, centerY);
        var radius = (size - 2) / 2;
        var strokeThickness = Math.Max(2, size * 0.09);
        var innerRadius = radius - strokeThickness;

        // Track arc (270 degrees, starting at 135 degrees)
        var trackPen = new Pen(TrackBrush, strokeThickness) { EndLineCap = PenLineCap.Round };
        DrawArc(dc, trackPen, center, innerRadius, 135, 270);

        // Value arc
        var sweep = CalculateSweep(Value);
        if (sweep > 0)
        {
            var valuePen = new Pen(AccentBrush, strokeThickness) { EndLineCap = PenLineCap.Round };
            DrawArc(dc, valuePen, center, innerRadius, 135, sweep);
        }

        // Center text: percentage
        var percentText = new FormattedText(
            $"{Math.Round(Value)}%",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size * 0.2, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            MaxLineCount = 1,
            MaxTextWidth = w
        };
        dc.DrawText(percentText, new Point(0, (h - percentText.Height) / 2));

        // Caption
        if (!string.IsNullOrEmpty(Caption))
        {
            var captionText = new FormattedText(
                Caption,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size * 0.08, Brushes.Gray,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                TextAlignment = TextAlignment.Center
            };
            var (captionX, _) = CalculateTextPosition(
                w, h,
                captionText.WidthIncludingTrailingWhitespace, captionText.Height);
            var captionY = centerY + percentText.Height / 2 + 2;
            dc.DrawText(captionText, new Point(captionX, captionY));
        }
    }

    private static void DrawArc(DrawingContext dc, Pen pen, Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var startRad = startDegrees * Math.PI / 180;
        var startPoint = new Point(
            center.X + radius * Math.Cos(startRad),
            center.Y + radius * Math.Sin(startRad));

        var endRad = (startDegrees + sweepDegrees) * Math.PI / 180;
        var endPoint = new Point(
            center.X + radius * Math.Cos(endRad),
            center.Y + radius * Math.Sin(endRad));

        var isLarge = sweepDegrees > 180;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLarge, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();

        dc.DrawGeometry(null, pen, geometry);
    }
}