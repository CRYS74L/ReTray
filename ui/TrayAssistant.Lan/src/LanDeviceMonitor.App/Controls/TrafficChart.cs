using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

/// <summary>
/// Clash Verge 风格的固定采样流量图。
/// 横坐标使用固定的一秒时间槽，不能因为启动阶段样本较少或一次 HTTP
/// 请求耗时变化而拉伸、压缩或左右替换曲线。
/// </summary>
public sealed class TrafficChart : FrameworkElement
{
    private static readonly Brush FallbackGridBrush = CreateBrush(0x55, 0x9A, 0xA2, 0xB2);
    private static readonly Brush FallbackLabelBrush = CreateBrush(0xFF, 0x7C, 0x86, 0x98);
    private static readonly Brush FallbackUploadBrush = CreateBrush(0xFF, 0xFF, 0x96, 0x72);
    private static readonly Brush FallbackDownloadBrush = CreateBrush(0xFF, 0x0B, 0x80, 0xFF);

    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(
            nameof(Samples),
            typeof(IReadOnlyList<TrafficSample>),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                Array.Empty<TrafficSample>(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSamplesChanged));

    public static readonly DependencyProperty GridBrushProperty =
        DependencyProperty.Register(
            nameof(GridBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackGridBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty =
        DependencyProperty.Register(
            nameof(LabelBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackLabelBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UploadBrushProperty =
        DependencyProperty.Register(
            nameof(UploadBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackUploadBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DownloadBrushProperty =
        DependencyProperty.Register(
            nameof(DownloadBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackDownloadBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeMinutesProperty =
        DependencyProperty.Register(
            nameof(RangeMinutes),
            typeof(int),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                TrafficHistoryBuffer.WindowMinutes,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRangeChanged));

    public static readonly DependencyProperty DeviceKeyProperty =
        DependencyProperty.Register(
            nameof(DeviceKey),
            typeof(string),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnDeviceKeyChanged));

    private int _hoveredIndex = -1;
    private Point _hoverPosition;
    private IReadOnlyList<TrafficSample> _renderSamples = Array.Empty<TrafficSample>();
    private bool _hasRenderSamples;

    private static void OnSamplesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (TrafficChart)dependencyObject;
        chart.SetRenderSamples(
            (IReadOnlyList<TrafficSample>?)args.NewValue ?? Array.Empty<TrafficSample>());
    }

    private static void OnRangeChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (TrafficChart)dependencyObject;
        chart._renderSamples = GetVisibleSamples(
            chart.Samples ?? Array.Empty<TrafficSample>(),
            NormalizeRange((int)args.NewValue));
        chart._hasRenderSamples = true;
        chart._hoveredIndex = -1;
        chart.InvalidateVisual();
    }

    private static void OnDeviceKeyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (TrafficChart)dependencyObject;
        chart._renderSamples = Array.Empty<TrafficSample>();
        chart._hasRenderSamples = false;
        chart._hoveredIndex = -1;
        chart.InvalidateVisual();
    }

    private void SetRenderSamples(IReadOnlyList<TrafficSample> allSamples)
    {
        var nextSamples = GetVisibleSamples(
            allSamples,
            NormalizeRange(RangeMinutes));
        if (_hasRenderSamples && AreSamplesEqual(_renderSamples, nextSamples))
        {
            return;
        }

        _renderSamples = nextSamples;
        _hasRenderSamples = true;
        // Clash Verge 的增强图表在数据列表稳定后直接重绘；它不会在每次
        // 一秒采样时再叠加一段窗口平移动画。这里也只替换一个稳定快照，
        // 避免启动和轮询期间左右抽搐。
        InvalidateVisual();
    }

    private static bool AreSamplesEqual(
        IReadOnlyList<TrafficSample> first,
        IReadOnlyList<TrafficSample> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (first[index] != second[index])
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<TrafficSample> Samples
    {
        get => (IReadOnlyList<TrafficSample>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush UploadBrush
    {
        get => (Brush)GetValue(UploadBrushProperty);
        set => SetValue(UploadBrushProperty, value);
    }

    public Brush DownloadBrush
    {
        get => (Brush)GetValue(DownloadBrushProperty);
        set => SetValue(DownloadBrushProperty, value);
    }

    public int RangeMinutes
    {
        get => (int)GetValue(RangeMinutesProperty);
        set => SetValue(RangeMinutesProperty, value);
    }

    public string DeviceKey
    {
        get => (string?)GetValue(DeviceKeyProperty) ?? string.Empty;
        set => SetValue(DeviceKeyProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80)
        {
            return;
        }

        GetPlotBounds(width, height, out var left, out var right, out var top, out var bottom);
        var rangeMinutes = NormalizeRange(RangeMinutes);
        var dataSamples = _hasRenderSamples
            ? _renderSamples
            : GetVisibleSamples(Samples ?? Array.Empty<TrafficSample>(), rangeMinutes);
        var samples = dataSamples;
        var sampleCountForX = dataSamples.Count;
        var scale = CalculateScale(dataSamples);
        const double animationOffset = 0d;

        DrawGrid(drawingContext, left, right, top, bottom);
        DrawAxisLabels(drawingContext, left, top, bottom, scale);
        DrawTimeAxis(
            drawingContext,
            left,
            right,
            bottom,
            dataSamples,
            rangeMinutes);
        if (samples.Count > 0)
        {
            // Clash Verge 先画下载线，再画上传线，让前景线在交叉处保持清晰。
            DrawTrafficSeries(
                drawingContext,
                samples,
                left,
                right,
                top,
                bottom,
                scale,
                DownloadBrush,
                sample => sample.DownloadBytesPerSecond,
                isUpload: false,
                sampleCountForX,
                animationOffset);
            DrawTrafficSeries(
                drawingContext,
                samples,
                left,
                right,
                top,
                bottom,
                scale,
                UploadBrush,
                sample => sample.UploadBytesPerSecond,
                isUpload: true,
                sampleCountForX,
                animationOffset);
        }

        if (_hoveredIndex >= 0 && samples.Count > 0)
        {
            if (_hoveredIndex >= dataSamples.Count ||
                dataSamples[_hoveredIndex].IsPlaceholder)
            {
                return;
            }

            DrawHoverState(
                drawingContext,
                dataSamples,
                left,
                right,
                top,
                bottom,
                scale,
                Math.Min(_hoveredIndex, samples.Count - 1),
                sampleCountForX,
                0,
                animationOffset);
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80)
        {
            ClearHover();
            return;
        }

        GetPlotBounds(width, height, out var left, out var right, out _, out _);
        if (position.X < left || position.X > right)
        {
            ClearHover();
            return;
        }

        var rangeMinutes = NormalizeRange(RangeMinutes);
        var samples = _hasRenderSamples
            ? _renderSamples
            : GetVisibleSamples(Samples ?? Array.Empty<TrafficSample>(), rangeMinutes);
        if (samples.Count == 0)
        {
            ClearHover();
            return;
        }

        var ratio = Math.Clamp(
            (position.X - left) / Math.Max(1d, right - left),
            0d,
            1d);
        var index = (int)Math.Round(
            ratio * (samples.Count - 1),
            MidpointRounding.AwayFromZero);
        index = Math.Clamp(index, 0, samples.Count - 1);
        // 固定窗口前段在启动时是“尚未采到”，不是一个可悬停的零速率点。
        // 只在真实采样点上显示 Clash Verge 风格的时间/速度提示。
        if (samples[index].IsPlaceholder)
        {
            ClearHover();
            return;
        }
        var changed = _hoveredIndex != index || Math.Abs(_hoverPosition.X - position.X) > 0.5d;
        _hoveredIndex = index;
        _hoverPosition = position;
        if (changed)
        {
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHover();
    }

    private void ClearHover()
    {
        if (_hoveredIndex < 0)
        {
            return;
        }

        _hoveredIndex = -1;
        InvalidateVisual();
    }

    private static IReadOnlyList<TrafficSample> GetVisibleSamples(
        IReadOnlyList<TrafficSample> allSamples,
        int rangeMinutes)
    {
        if (allSamples.Count == 0)
        {
            return Array.Empty<TrafficSample>();
        }

        var normalizedRange = NormalizeRange(rangeMinutes);
        // 以最新成功采样作为当前窗口右端。TrafficHistoryBuffer 已将它们
        // 对齐到整秒，所以窗口会稳定地前移一个槽位，而不会随着样本数
        // 增长改变横坐标比例。
        var windowEnd = allSamples[^1].Timestamp;
        var slotCount = GetFixedSlotCount(normalizedRange);
        var windowStart = windowEnd.AddSeconds(-(slotCount - 1));
        var samplesByTimestamp = allSamples
            .Where(sample => sample.Timestamp >= windowStart &&
                             sample.Timestamp <= windowEnd)
            .GroupBy(sample => sample.Timestamp)
            .ToDictionary(group => group.Key, group => group.Last());

        var fixedSamples = new TrafficSample[slotCount];
        for (var index = 0; index < slotCount; index++)
        {
            var timestamp = windowStart.AddSeconds(index);
            if (samplesByTimestamp.TryGetValue(timestamp, out var sample))
            {
                fixedSamples[index] = sample with { IsPlaceholder = false };
                continue;
            }

            // 缺失槽位只表示没有收到新的路由器流量数据，不生成新的
            // 数值点；绘制层会跳过该槽位，但连接前后两个真实采样点。
            fixedSamples[index] = new TrafficSample(timestamp, 0, 0)
            {
                IsPlaceholder = true,
                HasUploadSample = false,
                HasDownloadSample = false
            };
        }

        return fixedSamples;
    }

    private static int NormalizeRange(int rangeMinutes) =>
        rangeMinutes is 1 or 5 or 10 ? rangeMinutes : 10;

    internal static int GetFixedSlotCount(int rangeMinutes) =>
        NormalizeRange(rangeMinutes) * 60 + 1;

    internal static int GetTimeAxisMaximumLabelCount(int rangeMinutes) =>
        NormalizeRange(rangeMinutes) == 10 ? 8 : 6;

    internal static double GetPlotLeftInset() => 60d;

    private static void GetPlotBounds(
        double width,
        double height,
        out double left,
        out double right,
        out double top,
        out double bottom)
    {
        left = GetPlotLeftInset();
        right = Math.Max(left + 1d, width - 16d);
        top = 23d;
        bottom = Math.Max(top + 1d, height - 32d);
    }

    private void DrawGrid(
        DrawingContext drawingContext,
        double left,
        double right,
        double top,
        double bottom)
    {
        var gridPen = new Pen(WithOpacity(GridBrush, 0.62), 0.7);
        for (var index = 0; index <= 4; index++)
        {
            var y = top + (bottom - top) * index / 4d;
            drawingContext.DrawLine(gridPen, new Point(left, y), new Point(right, y));
        }

        for (var index = 0; index <= 6; index++)
        {
            var x = left + (right - left) * index / 6d;
            drawingContext.DrawLine(gridPen, new Point(x, top), new Point(x, bottom));
        }
    }

    private void DrawAxisLabels(
        DrawingContext drawingContext,
        double left,
        double top,
        double bottom,
        TrafficScale scale)
    {
        var axisBrush = WithOpacity(LabelBrush, 0.9);
        DrawText(
            drawingContext,
            FormatRate(scale.TopValue),
            new Point(left - 7, top - 7),
            10,
            axisBrush,
            TextAlignment.Right);
        DrawText(
            drawingContext,
            FormatRate((scale.TopValue + scale.BottomValue) / 2d),
            new Point(left - 7, (top + bottom) / 2d - 7),
            10,
            axisBrush,
            TextAlignment.Right);
        DrawText(
            drawingContext,
            FormatRate(scale.BottomValue),
            new Point(left - 7, bottom - 7),
            10,
            axisBrush,
            TextAlignment.Right);
    }

    private void DrawTimeAxis(
        DrawingContext drawingContext,
        double left,
        double right,
        double bottom,
        IReadOnlyList<TrafficSample> samples,
        int rangeMinutes)
    {
        if (samples.Count == 0)
        {
            return;
        }

        var labelBrush = WithOpacity(LabelBrush, 0.72);
        var maxLabels = GetTimeAxisMaximumLabelCount(rangeMinutes);
        var minPixelDistance = rangeMinutes switch
        {
            1 => 35d,
            5 => 38d,
            _ => 40d
        };
        var targetLabels = Math.Min(maxLabels, samples.Count);
        var nominalStep = Math.Max(
            1,
            (int)Math.Floor(samples.Count / (double)Math.Max(1, targetLabels - 1)));
        var pixelStep = Math.Max(
            1,
            (int)Math.Ceiling(
                samples.Count * minPixelDistance / Math.Max(1d, right - left)));
        var actualStep = Math.Max(nominalStep, pixelStep);
        var format = rangeMinutes == 1 ? "mm:ss" : "HH:mm";
        var timePoints = new List<(int Index, double X, string Label)>();
        timePoints.Add((
            0,
            left,
            samples[0].Timestamp.ToString(format, CultureInfo.CurrentCulture)));

        for (var sampleIndex = actualStep;
             sampleIndex < samples.Count - actualStep;
             sampleIndex += actualStep)
        {
            timePoints.Add((
                sampleIndex,
                GetX(sampleIndex, samples.Count, left, right),
                samples[sampleIndex].Timestamp.ToString(
                    format,
                    CultureInfo.CurrentCulture)));
        }

        if (samples.Count > 1)
        {
            var lastX = right;
            if (timePoints.Count == 0 ||
                lastX - timePoints[^1].X >= minPixelDistance)
            {
                timePoints.Add((
                    samples.Count - 1,
                    lastX,
                    samples[^1].Timestamp.ToString(
                        format,
                        CultureInfo.CurrentCulture)));
            }
        }

        for (var index = 0; index < timePoints.Count; index++)
        {
            var timePoint = timePoints[index];
            var alignment = index == 0
                ? TextAlignment.Left
                : index == timePoints.Count - 1
                    ? TextAlignment.Right
                    : TextAlignment.Center;
            DrawText(
                drawingContext,
                timePoint.Label,
                new Point(timePoint.X, bottom + 8),
                10,
                labelBrush,
                alignment);
        }
    }

    private void DrawTrafficSeries(
        DrawingContext drawingContext,
        IReadOnlyList<TrafficSample> samples,
        double left,
        double right,
        double top,
        double bottom,
        TrafficScale scale,
        Brush brush,
        Func<TrafficSample, long> valueSelector,
        bool isUpload,
        int sampleCountForX,
        double xOffset)
    {
        foreach (var segment in GetRenderableSampleSegments(samples, isUpload))
        {
            var points = segment
                .Select(index => new Point(
                    GetX(index, sampleCountForX, left, right) + xOffset,
                    GetY(valueSelector(samples[index]), scale, top, bottom)))
                .ToList();
            if (points.Count > 1)
            {
                var area = BuildQuadraticGeometry(points, bottom, closeToBaseline: true);
                var fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                fill.GradientStops.Add(new GradientStop(GetColor(brush, 0.15), 0));
                fill.GradientStops.Add(new GradientStop(GetColor(brush, 0), 1));
                fill.Freeze();
                drawingContext.DrawGeometry(fill, null, area);

                var line = BuildQuadraticGeometry(points, bottom, closeToBaseline: false);
                var pen = new Pen(WithOpacity(brush, 0.9), 2.5)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                drawingContext.DrawGeometry(null, pen, line);
            }
            else
            {
                drawingContext.DrawEllipse(brush, null, points[0], 2.5, 2.5);
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<int>> GetRenderableSampleSegments(
        IReadOnlyList<TrafficSample> samples,
        bool isUpload)
    {
        var segments = new List<IReadOnlyList<int>>();
        List<int>? currentSegment = null;
        foreach (var (sample, index) in samples.Select((sample, index) => (sample, index)))
        {
            var isRenderable = !sample.IsPlaceholder &&
                (isUpload ? sample.HasUploadSample : sample.HasDownloadSample);
            if (!isRenderable)
            {
                continue;
            }

            if (currentSegment is null)
            {
                currentSegment = new List<int>();
                segments.Add(currentSegment);
            }

            currentSegment.Add(index);
        }

        return segments;
    }

    private void DrawHoverState(
        DrawingContext drawingContext,
        IReadOnlyList<TrafficSample> samples,
        double left,
        double right,
        double top,
        double bottom,
        TrafficScale scale,
        int index,
        int sampleCountForX,
        int sampleIndexOffset,
        double xOffset)
    {
        var sample = samples[index];
        var x = GetX(
                    index + sampleIndexOffset,
                    sampleCountForX,
                    left,
                    right) +
                xOffset;

        var indicatorPen = new Pen(WithOpacity(LabelBrush, 0.72), 1)
        {
            DashStyle = new DashStyle([4, 4], 0)
        };
        drawingContext.DrawLine(indicatorPen, new Point(x, top), new Point(x, bottom));

        if (sample.HasUploadSample)
        {
            var uploadY = GetY(sample.UploadBytesPerSecond, scale, top, bottom);
            DrawHoverPoint(drawingContext, new Point(x, uploadY), UploadBrush);
        }

        if (sample.HasDownloadSample)
        {
            var downloadY = GetY(sample.DownloadBytesPerSecond, scale, top, bottom);
            DrawHoverPoint(drawingContext, new Point(x, downloadY), DownloadBrush);
        }

        DrawTooltip(
            drawingContext,
            sample,
            x,
            top,
            left,
            right,
            bottom);
    }

    private void DrawHoverPoint(DrawingContext drawingContext, Point point, Brush brush)
    {
        drawingContext.DrawEllipse(GetTooltipBackgroundBrush(), null, point, 5, 5);
        drawingContext.DrawEllipse(brush, null, point, 3.5, 3.5);
    }

    private void DrawTooltip(
        DrawingContext drawingContext,
        TrafficSample sample,
        double pointX,
        double top,
        double left,
        double right,
        double bottom)
    {
        var isDark = GetColor(GridBrush, 1).R < 160;
        var background = GetTooltipBackgroundBrush();
        var foreground = CreateBrush(
            0xFF,
            isDark ? (byte)0xF2 : (byte)0x18,
            isDark ? (byte)0xF5 : (byte)0x20,
            isDark ? (byte)0xFB : (byte)0x34);
        var titleBrush = WithOpacity(foreground, 0.7);
        var borderPen = new Pen(WithOpacity(LabelBrush, 0.78), 1);

        var title = CreateFormattedText(
            sample.Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            10,
            titleBrush,
            TextAlignment.Left);
        var upload = CreateFormattedText(
            sample.HasUploadSample
                ? $"↑ {FormatRate(sample.UploadBytesPerSecond)}/s"
                : "↑ —",
            11,
            UploadBrush,
            TextAlignment.Left,
            isBold: true);
        var download = CreateFormattedText(
            sample.HasDownloadSample
                ? $"↓ {FormatRate(sample.DownloadBytesPerSecond)}/s"
                : "↓ —",
            11,
            DownloadBrush,
            TextAlignment.Left,
            isBold: true);

        const double paddingX = 10;
        const double paddingY = 8;
        const double lineGap = 3;
        var contentWidth = Math.Max(title.Width, Math.Max(upload.Width, download.Width));
        var contentHeight = title.Height + upload.Height + download.Height + lineGap * 2;
        var boxWidth = contentWidth + paddingX * 2;
        var boxHeight = contentHeight + paddingY * 2;
        var boxX = pointX + 12;
        if (boxX + boxWidth > right)
        {
            boxX = pointX - boxWidth - 12;
        }

        var boxY = top + 8;
        if (boxY + boxHeight > bottom)
        {
            boxY = Math.Max(top + 2, bottom - boxHeight - 2);
        }

        var box = new Rect(boxX, boxY, boxWidth, boxHeight);
        drawingContext.DrawRoundedRectangle(background, borderPen, box, 7, 7);
        var textX = boxX + paddingX;
        var textY = boxY + paddingY;
        drawingContext.DrawText(title, new Point(textX, textY));
        textY += title.Height + lineGap;
        drawingContext.DrawText(upload, new Point(textX, textY));
        textY += upload.Height + lineGap;
        drawingContext.DrawText(download, new Point(textX, textY));
    }

    private static StreamGeometry BuildQuadraticGeometry(
        IReadOnlyList<Point> points,
        double baseline,
        bool closeToBaseline)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], closeToBaseline, isClosed: false);
            AppendClashVergeQuadraticCurve(context, points);

            if (closeToBaseline)
            {
                context.LineTo(
                    new Point(points[^1].X, baseline),
                    isStroked: false,
                    isSmoothJoin: false);
                context.LineTo(
                    new Point(points[0].X, baseline),
                    isStroked: false,
                    isSmoothJoin: false);
                context.Close();
            }
        }

        geometry.Freeze();
        return geometry;
    }

    // 与 Clash Verge traffic-graph.tsx 的 quadraticCurveTo 完全对应：
    // current 点是控制点，current 与 next 的中点是本段终点。
    private static void AppendClashVergeQuadraticCurve(
        StreamGeometryContext context,
        IReadOnlyList<Point> points)
    {
        for (var index = 1; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[Math.Min(index + 1, points.Count - 1)];
            var midpoint = new Point(
                (current.X + next.X) / 2d,
                (current.Y + next.Y) / 2d);
            context.QuadraticBezierTo(
                current,
                midpoint,
                isStroked: true,
                isSmoothJoin: true);
        }
    }

    private static double GetX(int index, int count, double left, double right)
    {
        if (count <= 1)
        {
            return (left + right) / 2d;
        }

        return left + (right - left) * index / (count - 1d);
    }

    private static double GetY(
        long value,
        TrafficScale scale,
        double top,
        double bottom)
    {
        var range = Math.Max(1d, scale.TopValue - scale.BottomValue);
        var normalized = Math.Clamp(
            (Math.Max(scale.BottomValue, value) - scale.BottomValue) / range,
            0d,
            1d);
        return bottom - normalized * (bottom - top);
    }

    private static TrafficScale CalculateScale(IReadOnlyList<TrafficSample> samples)
    {
        var actualSamples = samples
            .Where(sample =>
                !sample.IsPlaceholder &&
                (sample.HasUploadSample || sample.HasDownloadSample))
            .ToArray();
        if (actualSamples.Length == 0)
        {
            return new TrafficScale(1024d, 0d);
        }

        var maxValue = actualSamples.Max(sample => Math.Max(
            sample.HasUploadSample ? sample.UploadBytesPerSecond : 0,
            sample.HasDownloadSample ? sample.DownloadBytesPerSecond : 0));
        var minValue = actualSamples.Min(sample => Math.Min(
            sample.HasUploadSample ? sample.UploadBytesPerSecond : 0,
            sample.HasDownloadSample ? sample.DownloadBytesPerSecond : 0));
        if (maxValue == 0)
        {
            return new TrafficScale(1024d, 0d);
        }

        var range = maxValue - minValue;
        if (range <= 0)
        {
            return new TrafficScale(maxValue * 1.2d, 0d);
        }

        const double margin = 0.1d;
        return new TrafficScale(
            maxValue + range * margin,
            Math.Max(0d, minValue - range * margin));
    }

    // 速率使用二进制单位、最多三位有效数字、最多两位小数，并以 KB 为最低单位。
    internal static string FormatRate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond))
        {
            return "NaN";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        var exponent = bytesPerSecond < 1d
            ? 0
            : Math.Min(
                (int)Math.Floor(Math.Log2(bytesPerSecond) / 10d),
                units.Length - 1);
        exponent = Math.Max(exponent, 1);
        var scaled = bytesPerSecond / Math.Pow(1024d, exponent);
        var value = scaled >= 1000d
            ? scaled.ToString("0", CultureInfo.CurrentCulture)
            : FormatThreeSignificantDigits(scaled, zeroAsInteger: true);
        return $"{value}\u2009{units[Math.Max(0, exponent)]}";
    }

    private static string FormatThreeSignificantDigits(
        double value,
        bool zeroAsInteger = false)
    {
        if (value == 0)
        {
            return zeroAsInteger ? "0" : "0.00";
        }

        var digitsBeforeDecimal = (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1;
        var decimalPlaces = Math.Min(2, Math.Max(0, 3 - digitsBeforeDecimal));
        var roundedValue = Math.Round(
            value,
            decimalPlaces,
            MidpointRounding.AwayFromZero);
        if (zeroAsInteger && roundedValue == 0d)
        {
            return "0";
        }

        return roundedValue.ToString(
            $"F{decimalPlaces}",
            CultureInfo.CurrentCulture);
    }

    private FormattedText CreateFormattedText(
        string text,
        double fontSize,
        Brush brush,
        TextAlignment alignment,
        bool isBold = false) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(
                (System.Windows.Media.FontFamily)FindResource("AppTextFont"),
                FontStyles.Normal,
                isBold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            brush,
            1d)
        {
            TextAlignment = alignment
        };

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        Point anchor,
        double fontSize,
        Brush brush,
        TextAlignment alignment,
        bool isBold = false)
    {
        var formattedText = CreateFormattedText(text, fontSize, brush, TextAlignment.Left, isBold);
        var drawPoint = alignment switch
        {
            TextAlignment.Right => new Point(anchor.X - formattedText.Width, anchor.Y),
            TextAlignment.Center => new Point(anchor.X - formattedText.Width / 2d, anchor.Y),
            _ => anchor
        };
        drawingContext.DrawText(formattedText, drawPoint);
    }

    private Brush GetTooltipBackgroundBrush()
    {
        var gridColor = GetColor(GridBrush, 1);
        return gridColor.R < 160
            ? CreateBrush(0xEE, 0x2E, 0x2E, 0x2E)
            : CreateBrush(0xEE, 0xFF, 0xFF, 0xFF);
    }

    private static Brush WithOpacity(Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solidBrush)
        {
            return brush;
        }

        var color = solidBrush.Color;
        color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
        return CreateBrush(color.A, color.R, color.G, color.B);
    }

    private static Color GetColor(Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solidBrush)
        {
            return Colors.Transparent;
        }

        var color = solidBrush.Color;
        color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
        return color;
    }

    private static Brush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private readonly record struct TrafficScale(double TopValue, double BottomValue);
}
