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
    }

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

            var maxSeconds = Math.Max(1L, bars.Max(b => b.Seconds));
            var slotWidth = dirtyRect.Width / bars.Count;
            const float labelHeight = 20f;
            var chartHeight = Math.Max(10f, dirtyRect.Height - labelHeight);
            var barWidth = Math.Min(30f, slotWidth * 0.5f);

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var barColor = Color.FromArgb(isDark ? "#2DD4BF" : "#0F766E");
            var todayColor = Color.FromArgb(isDark ? "#FBBF24" : "#B45309");
            var labelColor = Color.FromArgb(isDark ? "#94A3B8" : "#64748B");

            for (var i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                var slotX = dirtyRect.X + i * slotWidth;
                var height = bar.Seconds <= 0
                    ? 2f
                    : (float)(bar.Seconds / (double)maxSeconds) * chartHeight * 0.92f;
                var x = slotX + (slotWidth - barWidth) / 2;
                var y = dirtyRect.Y + chartHeight - height;

                canvas.FillColor = bar.IsToday ? todayColor : barColor;
                canvas.FillRoundedRectangle(x, y, barWidth, height, 4f);

                canvas.FontColor = bar.IsToday ? todayColor : labelColor;
                canvas.FontSize = 10f;
                canvas.DrawString(
                    bar.Label,
                    slotX,
                    dirtyRect.Y + chartHeight,
                    slotWidth,
                    labelHeight,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
        }
    }
}
