using EduFocus.Parent.Core.Presentation;
using Microsoft.Maui.Graphics;

namespace EduFocus.Parent.App.Controls;

public sealed class WeeklyBarChart : GraphicsView
{
    public static readonly BindableProperty BarsProperty = BindableProperty.Create(
        nameof(Bars),
        typeof(IReadOnlyList<ChartBarItem>),
        typeof(WeeklyBarChart),
        defaultValue: null,
        propertyChanged: OnBarsChanged);

    public WeeklyBarChart()
    {
        Drawable = new BarDrawable(this);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }
        Invalidate();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
        Invalidate();

    public IReadOnlyList<ChartBarItem>? Bars
    {
        get => (IReadOnlyList<ChartBarItem>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    private static void OnBarsChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((WeeklyBarChart)bindable).Invalidate();

    private sealed class BarDrawable(WeeklyBarChart chart) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bars = chart.Bars;
            if (bars is null || bars.Count == 0)
                return;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var barColor = Color.FromArgb(isDark ? "#2DD4BF" : "#0F766E");
            var todayColor = Color.FromArgb(isDark ? "#FBBF24" : "#B45309");
            var trackColor = Color.FromArgb(isDark ? "#1B2637" : "#E8EFEF");
            var gridColor = Color.FromArgb(isDark ? "#223046" : "#DCE7E7");
            var labelColor = Color.FromArgb(isDark ? "#94A3B8" : "#64748B");
            var valueColor = Color.FromArgb(isDark ? "#CBD5E1" : "#475569");

            const float labelHeight = 22f;
            const float topPadding = 18f;
            var chartHeight = Math.Max(12f, dirtyRect.Height - labelHeight - topPadding);
            var maxSeconds = Math.Max(1L, bars.Max(b => b.Seconds));
            var slotWidth = dirtyRect.Width / bars.Count;
            var barWidth = Math.Min(28f, slotWidth * 0.46f);
            var chartTop = dirtyRect.Y + topPadding;

            canvas.StrokeSize = 1f;
            canvas.StrokeColor = gridColor;
            for (var line = 0; line <= 2; line++)
            {
                var y = chartTop + chartHeight * (line / 2f);
                canvas.DrawLine(dirtyRect.X, y, dirtyRect.Right, y);
            }

            for (var i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                var slotX = dirtyRect.X + i * slotWidth;
                var x = slotX + (slotWidth - barWidth) / 2;

                canvas.FillColor = trackColor;
                canvas.FillRoundedRectangle(x, chartTop, barWidth, chartHeight, 6f);

                if (bar.Seconds > 0)
                {
                    var height = (float)(bar.Seconds / (double)maxSeconds) * chartHeight * 0.94f;
                    var y = chartTop + chartHeight - height;

                    canvas.FillColor = bar.IsToday ? todayColor : barColor;
                    canvas.FillRoundedRectangle(x, y, barWidth, height, 6f);

                    canvas.FontColor = bar.IsToday ? todayColor : valueColor;
                    canvas.FontSize = 9.5f;
                    canvas.DrawString(
                        FormatValue(bar.Seconds),
                        slotX,
                        y - topPadding,
                        slotWidth,
                        topPadding,
                        HorizontalAlignment.Center,
                        VerticalAlignment.Bottom);
                }

                canvas.FontColor = bar.IsToday ? todayColor : labelColor;
                canvas.FontSize = 10f;
                canvas.DrawString(
                    bar.Label,
                    slotX,
                    dirtyRect.Y + topPadding + chartHeight,
                    slotWidth,
                    labelHeight,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
        }

        private static string FormatValue(long seconds)
        {
            var minutes = (long)Math.Round(seconds / 60d);
            if (minutes < 60)
                return $"{Math.Max(1, minutes)}p";

            var hours = minutes / 60;
            var rest = minutes % 60;
            return rest < 5 ? $"{hours}g" : $"{hours}g{rest:00}";
        }
    }
}
