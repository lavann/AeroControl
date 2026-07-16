using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;

namespace AeroControl.Controls;

public sealed class TelemetryChart : FrameworkElement
{
    public static readonly DependencyProperty Series1Property = DependencyProperty.Register(
        nameof(Series1),
        typeof(IReadOnlyList<double?>),
        typeof(TelemetryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Series2Property = DependencyProperty.Register(
        nameof(Series2),
        typeof(IReadOnlyList<double?>),
        typeof(TelemetryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(TelemetryChart),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(TelemetryChart),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Series1BrushProperty = DependencyProperty.Register(
        nameof(Series1Brush),
        typeof(MediaBrush),
        typeof(TelemetryChart),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Series2BrushProperty = DependencyProperty.Register(
        nameof(Series2Brush),
        typeof(MediaBrush),
        typeof(TelemetryChart),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?>? Series1
    {
        get => (IReadOnlyList<double?>?)GetValue(Series1Property);
        set => SetValue(Series1Property, value);
    }

    public IReadOnlyList<double?>? Series2
    {
        get => (IReadOnlyList<double?>?)GetValue(Series2Property);
        set => SetValue(Series2Property, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public MediaBrush Series1Brush
    {
        get => (MediaBrush)GetValue(Series1BrushProperty);
        set => SetValue(Series1BrushProperty, value);
    }

    public MediaBrush Series2Brush
    {
        get => (MediaBrush)GetValue(Series2BrushProperty);
        set => SetValue(Series2BrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(217, 225, 220)), 1), bounds, 4, 4);
        if (ActualWidth < 40 || ActualHeight < 40)
        {
            return;
        }

        var plot = new Rect(38, 12, ActualWidth - 50, ActualHeight - 30);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 228)), 1);
        for (var index = 0; index <= 4; index++)
        {
            var y = plot.Top + plot.Height * index / 4d;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawLabel(drawingContext, Maximum - (Maximum - Minimum) * index / 4d, y - 7);
        }

        DrawSeries(drawingContext, Series1, Series1Brush, plot);
        DrawSeries(drawingContext, Series2, Series2Brush, plot);

        if (Series1?.Any(value => value.HasValue) != true &&
            Series2?.Any(value => value.HasValue) != true)
        {
            var text = new FormattedText(
                "Waiting for samples",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Variable Text"),
                12,
                new SolidColorBrush(Color.FromRgb(96, 112, 106)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(
                text,
                new Point(plot.Left + (plot.Width - text.Width) / 2, plot.Top + (plot.Height - text.Height) / 2));
        }
    }

    private void DrawSeries(
        DrawingContext drawingContext,
        IReadOnlyList<double?>? values,
        MediaBrush brush,
        Rect plot)
    {
        if (values is null || values.Count == 0 || Maximum <= Minimum)
        {
            return;
        }

        if (values.Count == 1)
        {
            if (values[0].HasValue)
            {
                var value = Math.Clamp(values[0]!.Value, Minimum, Maximum);
                var y = plot.Bottom - plot.Height * (value - Minimum) / (Maximum - Minimum);
                drawingContext.DrawEllipse(brush, null, new Point(plot.Left, y), 3, 3);
            }

            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var drawing = false;
            for (var index = 0; index < values.Count; index++)
            {
                if (!values[index].HasValue)
                {
                    drawing = false;
                    continue;
                }

                var value = Math.Clamp(values[index]!.Value, Minimum, Maximum);
                var x = plot.Left + plot.Width * index / Math.Max(1, values.Count - 1);
                var y = plot.Bottom - plot.Height * (value - Minimum) / (Maximum - Minimum);
                if (!drawing)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                    drawing = true;
                }
                else
                {
                    context.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(brush, 2), geometry);
    }

    private void DrawLabel(DrawingContext drawingContext, double value, double y)
    {
        var text = new FormattedText(
            value.ToString("0", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            9,
            new SolidColorBrush(Color.FromRgb(96, 112, 106)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point(6, y));
    }
}
