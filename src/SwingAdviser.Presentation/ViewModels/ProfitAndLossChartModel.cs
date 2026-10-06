using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using SwingAdviser.Application.Positions;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>損益タブのグラフ。系列の組み立てだけを担い、損益の計算は <see cref="ProfitAndLossHistoryReader"/> に任せる。</summary>
public sealed class ProfitAndLossChartModel : ObservableObject
{
    private bool _hasData;

    public ProfitAndLossChartModel()
    {
        PlotModel = new PlotModel();
        PlotModel.Legends.Add(new Legend { LegendPosition = LegendPosition.TopLeft });
        // 目盛りが1日未満になると同じ日付ラベルが並ぶため、最小間隔を1日にする。
        PlotModel.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "yyyy-MM-dd",
            IntervalType = DateTimeIntervalType.Days,
            MinimumMajorStep = 1,
            MinimumMinorStep = 1,
        });
        PlotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "損益（円）", StringFormat = "#,##0" });
    }

    public PlotModel PlotModel { get; }

    public bool HasNoData => !_hasData;

    private bool HasData
    {
        set
        {
            if (Set(ref _hasData, value))
            {
                OnPropertyChanged(nameof(HasNoData));
            }
        }
    }

    public void Update(IReadOnlyList<ProfitAndLossPoint> points)
    {
        PlotModel.Series.Clear();

        var total = new LineSeries { Title = "通算損益（実現＋含み）", Color = OxyColors.SteelBlue, StrokeThickness = 2 };
        var realized = new StairStepSeries { Title = "実現損益（累計）", Color = OxyColors.Gray, StrokeThickness = 1.5 };
        foreach (var point in points)
        {
            var x = DateTimeAxis.ToDouble(point.Date.ToDateTime(TimeOnly.MinValue));
            total.Points.Add(new DataPoint(x, (double)point.Total));
            realized.Points.Add(new DataPoint(x, (double)point.Realized));
        }

        PlotModel.Series.Add(realized);
        PlotModel.Series.Add(total);
        HasData = points.Count > 0;
        PlotModel.InvalidatePlot(true);
    }
}
