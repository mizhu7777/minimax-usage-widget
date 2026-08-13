using System.Windows;
using System.Windows.Media;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Controls;

public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty FiveHourSegmentsProperty =
        DependencyProperty.Register(nameof(FiveHourSegments), typeof(IReadOnlyList<TrendSegment>), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WeeklySegmentsProperty =
        DependencyProperty.Register(nameof(WeeklySegments), typeof(IReadOnlyList<TrendSegment>), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<TrendSegment>? FiveHourSegments
    {
        get => (IReadOnlyList<TrendSegment>?)GetValue(FiveHourSegmentsProperty);
        set => SetValue(FiveHourSegmentsProperty, value);
    }

    public IReadOnlyList<TrendSegment>? WeeklySegments
    {
        get => (IReadOnlyList<TrendSegment>?)GetValue(WeeklySegmentsProperty);
        set => SetValue(WeeklySegmentsProperty, value);
    }

    private static readonly Brush FiveHourColor = new SolidColorBrush(Color.FromRgb(37, 131, 247));
    private static readonly Brush WeeklyColor = new SolidColorBrush(Color.FromRgb(139, 92, 246));
    private static readonly Brush GridColor = new SolidColorBrush(Color.FromArgb(40, 100, 116, 139));
    private static readonly Brush GridLabelColor = new SolidColorBrush(Color.FromArgb(80, 100, 116, 139));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var segments5h = FiveHourSegments;
        var segmentsWeekly = WeeklySegments;

        if (segments5h is null || segmentsWeekly is null || (segments5h.Count == 0 && segmentsWeekly.Count == 0))
        {
            var emptyText = new FormattedText(
                "开始记录后将显示趋势",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface, 12, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                TextAlignment = TextAlignment.Center
            };
            dc.DrawText(emptyText, new Point((RenderSize.Width - emptyText.WidthIncludingTrailingWhitespace) / 2,
                (RenderSize.Height - emptyText.Height) / 2));
            return;
        }

        var allPoints = new List<TrendPoint>();
        foreach (var seg in segments5h.Concat(segmentsWeekly))
            allPoints.AddRange(seg.Points);

        if (allPoints.Count == 0) return;

        var minTime = allPoints.Min(p => p.RecordedAt);
        var maxTime = allPoints.Max(p => p.RecordedAt);
        if (maxTime == minTime) maxTime = minTime.AddHours(1);

        var padding = new Thickness(40, 20, 20, 35);
        var chartWidth = RenderSize.Width - padding.Left - padding.Right;
        var chartHeight = RenderSize.Height - padding.Top - padding.Bottom;
        if (chartWidth <= 0 || chartHeight <= 0) return;

        // Grid lines
        var gridPen = new Pen(GridColor, 0.5);
        for (int y = 0; y <= 4; y++)
        {
            var yPos = padding.Top + chartHeight * (1 - y / 4.0);
            dc.DrawLine(gridPen, new Point(padding.Left, yPos), new Point(padding.Left + chartWidth, yPos));

            var label = new FormattedText(
                $"{y * 25}%",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface, 9, GridLabelColor, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(padding.Left - label.WidthIncludingTrailingWhitespace - 4, yPos - label.Height / 2));
        }

        double XPos(DateTimeOffset t) => padding.Left + chartWidth * (t.ToUniversalTime().Ticks - minTime.ToUniversalTime().Ticks) / (maxTime.ToUniversalTime().Ticks - minTime.ToUniversalTime().Ticks);
        double YPos(double v) => padding.Top + chartHeight * (1 - v / 100.0);

        // Draw segments
        DrawSegments(dc, segments5h, FiveHourColor, XPos, YPos);
        DrawSegments(dc, segmentsWeekly, WeeklyColor, XPos, YPos);

        // X-axis time labels
        DrawTimeLabel(dc, minTime, padding.Left, padding.Top + chartHeight + 4, TextAlignment.Left);
        DrawTimeLabel(dc, minTime.AddTicks((maxTime.ToUniversalTime().Ticks - minTime.ToUniversalTime().Ticks) / 2), padding.Left + chartWidth / 2, padding.Top + chartHeight + 4, TextAlignment.Center);
        DrawTimeLabel(dc, maxTime, padding.Left + chartWidth, padding.Top + chartHeight + 4, TextAlignment.Right);
    }

    private void DrawTimeLabel(DrawingContext dc, DateTimeOffset time, double x, double y, TextAlignment alignment)
    {
        var label = new FormattedText(
            time.ToLocalTime().ToString("MM-dd HH:mm"),
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface, 9, GridLabelColor, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = alignment
        };
        var drawX = alignment switch
        {
            TextAlignment.Left => x,
            TextAlignment.Right => x - label.WidthIncludingTrailingWhitespace,
            _ => x - label.WidthIncludingTrailingWhitespace / 2
        };
        dc.DrawText(label, new Point(drawX, y));
    }

    private static void DrawSegments(
        DrawingContext dc,
        IReadOnlyList<TrendSegment> segments,
        Brush color,
        Func<DateTimeOffset, double> xPos,
        Func<double, double> yPos)
    {
        if (segments.Count == 0) return;
        var pen = new Pen(color, 1.5) { EndLineCap = PenLineCap.Round, StartLineCap = PenLineCap.Round };

        foreach (var segment in segments)
        {
            if (segment.Points.Count < 2) continue;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var first = segment.Points[0];
                ctx.BeginFigure(new Point(xPos(first.RecordedAt), yPos(first.Value)), false, false);
                for (int i = 1; i < segment.Points.Count; i++)
                {
                    var p = segment.Points[i];
                    ctx.LineTo(new Point(xPos(p.RecordedAt), yPos(p.Value)), true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }
}