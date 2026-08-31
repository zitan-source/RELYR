using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace RELYR;

internal sealed class DeckMonitorView : Grid
{
    const int HistoryLength = 12;
    readonly DeckMonitorDefinition definition;
    readonly TextBlock label;
    readonly TextBlock value;
    readonly TextBlock detail;
    readonly Border? track;
    readonly Border? progress;
    readonly Polyline? sparkline;
    readonly Polyline? sparklineGlow;
    readonly Grid visualizationRoot;
    readonly List<Border> columnBars = [];
    readonly List<Border> dotItems = [];
    readonly Queue<double> history = new();
    SystemMonitorReading? lastReading;
    bool subscribed;

    internal string MonitorId => definition.Id;
    internal double? CurrentPercent => lastReading?.Level is double level ? Math.Clamp(level * 100, 0, 100) : null;

    internal DeckMonitorView(DeckMonitorDefinition definition)
    {
        this.definition = definition;
        IsHitTestVisible = false;
        ClipToBounds = true;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });

        label = new TextBlock
        {
            Text = definition.Name.ToUpperInvariant(),
            FontSize = 7.4,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        SetZIndex(label, 2);
        Children.Add(label);

        value = new TextBlock
        {
            Text = "\u2014",
            FontSize = 14.2,
            FontWeight = FontWeights.Bold,
            FontFamily = new System.Windows.Media.FontFamily("Bahnschrift, Segoe UI Variable Text, Segoe UI"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        value.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        SetRow(value, 1);
        SetZIndex(value, 2);
        Children.Add(value);

        detail = new TextBlock
        {
            Text = definition.Glyph,
            FontSize = 6.8,
            Opacity = .82,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        SetRow(detail, 2);
        SetZIndex(detail, 2);
        Children.Add(detail);

        visualizationRoot = new Grid
        {
            Margin = new Thickness(1, 1, 1, 0),
            ClipToBounds = true,
            IsHitTestVisible = false
        };
        MonitorVisualKind visualKind = VisualKind(definition.Id);
        switch (visualKind)
        {
            case MonitorVisualKind.Sparkline:
                sparklineGlow = new Polyline
                {
                    StrokeThickness = 4.2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Stretch = Stretch.None,
                    SnapsToDevicePixels = true,
                    Opacity = .16
                };
                sparkline = new Polyline
                {
                    StrokeThickness = 1.55,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Stretch = Stretch.None,
                    SnapsToDevicePixels = true,
                    Opacity = .86
                };
                visualizationRoot.Children.Add(sparklineGlow);
                visualizationRoot.Children.Add(sparkline);
                break;
            case MonitorVisualKind.Columns:
                var columns = new UniformGrid { Columns = HistoryLength };
                for (int index = 0; index < HistoryLength; index++)
                {
                    var cell = new Grid { Margin = new Thickness(.45, 0, .45, 0) };
                    var bar = new Border
                    {
                        Height = 1,
                        CornerRadius = new CornerRadius(1),
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Opacity = .68
                    };
                    cell.Children.Add(bar);
                    columns.Children.Add(cell);
                    columnBars.Add(bar);
                }
                visualizationRoot.Children.Add(columns);
                break;
            case MonitorVisualKind.Dots:
                var dots = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                for (int index = 0; index < 8; index++)
                {
                    var dot = new Border
                    {
                        Width = 3,
                        Height = 3,
                        Margin = new Thickness(1.2, 0, 1.2, 0),
                        CornerRadius = new CornerRadius(1.5),
                        Opacity = .18
                    };
                    dots.Children.Add(dot);
                    dotItems.Add(dot);
                }
                visualizationRoot.Children.Add(dots);
                break;
            default:
                track = new Border
                {
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Opacity = .35
                };
                track.SetResourceReference(BackgroundProperty, "DividerBrush");
                progress = new Border
                {
                    Height = 3,
                    Width = 0,
                    CornerRadius = new CornerRadius(1.5),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                visualizationRoot.Children.Add(track);
                visualizationRoot.Children.Add(progress);
                break;
        }
        if (visualKind is MonitorVisualKind.Sparkline or MonitorVisualKind.Columns)
        {
            SetRow(visualizationRoot, 1);
            SetRowSpan(visualizationRoot, 3);
        }
        else
        {
            SetRow(visualizationRoot, 3);
        }
        SetZIndex(visualizationRoot, 0);
        Children.Add(visualizationRoot);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => RefreshVisualization();
        ApplyTheme();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (subscribed)
            return;
        subscribed = true;
        ThemeService.ThemeChanged += ApplyTheme;
        ArchiveAutomationState.Changed += ArchiveAutomationStateChanged;
        SystemMonitorService.Shared.Subscribe(SnapshotChanged);
        if (definition.Id.Equals("auto-extract", StringComparison.OrdinalIgnoreCase))
            Apply(ArchiveAutomationState.Reading());
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!subscribed)
            return;
        subscribed = false;
        ThemeService.ThemeChanged -= ApplyTheme;
        ArchiveAutomationState.Changed -= ArchiveAutomationStateChanged;
        SystemMonitorService.Shared.Unsubscribe(SnapshotChanged);
    }

    void ArchiveAutomationStateChanged()
    {
        if (!definition.Id.Equals("auto-extract", StringComparison.OrdinalIgnoreCase))
            return;
        if (Dispatcher.CheckAccess())
            Apply(ArchiveAutomationState.Reading());
        else
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (subscribed)
                    Apply(ArchiveAutomationState.Reading());
            });
    }

    void ApplyTheme()
    {
        var brush = new SolidColorBrush(MonitorAccentColor(definition.Id, ThemeService.UsesDark));
        brush.Freeze();
        label.Foreground = brush;
        if (progress != null)
            progress.Background = brush;
        if (sparkline != null)
            sparkline.Stroke = brush;
        if (sparklineGlow != null)
            sparklineGlow.Stroke = brush;
        foreach (Border bar in columnBars)
            bar.Background = brush;
        foreach (Border dot in dotItems)
            dot.Background = brush;
    }

    void SnapshotChanged(object? sender, SystemMonitorSnapshot snapshot)
    {
        try
        {
            if (Dispatcher.CheckAccess())
                Apply(definition.Id.Equals("auto-extract", StringComparison.OrdinalIgnoreCase)
                    ? ArchiveAutomationState.Reading()
                    : snapshot.Get(definition.Id));
            else
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (subscribed)
                        Apply(definition.Id.Equals("auto-extract", StringComparison.OrdinalIgnoreCase)
                            ? ArchiveAutomationState.Reading()
                            : snapshot.Get(definition.Id));
                });
        }
        catch (Exception error) { LifecycleDiagnostics.Write("deck-monitor-view-failed", error.ToString()); }
    }

    void Apply(SystemMonitorReading reading)
    {
        lastReading = reading;
        value.Text = reading.Text;
        value.FontSize = reading.Text.Length switch
        {
            > 10 => 9.2,
            > 7 => 10.6,
            > 5 => 12,
            _ => 14.2
        };
        detail.Text = string.IsNullOrWhiteSpace(reading.Detail) ? definition.Glyph : reading.Detail;
        value.Opacity = reading.Available ? 1 : .62;
        if (reading.Level is double level)
        {
            history.Enqueue(Math.Clamp(level, 0, 1));
            while (history.Count > HistoryLength)
                _ = history.Dequeue();
        }
        ApplyTheme();
        if (reading.Warning)
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            progress?.SetResourceReference(BackgroundProperty, "DangerBrush");
            sparkline?.SetResourceReference(Shape.StrokeProperty, "DangerBrush");
            sparklineGlow?.SetResourceReference(Shape.StrokeProperty, "DangerBrush");
            foreach (Border bar in columnBars)
                bar.SetResourceReference(BackgroundProperty, "DangerBrush");
            foreach (Border dot in dotItems)
                dot.SetResourceReference(BackgroundProperty, "DangerBrush");
        }
        RefreshVisualization();
        ToolTip = $"{definition.Name}\n{reading.Detail}";
    }

    internal void ApplyInteractivePercent(double percent, string detailText)
        => Apply(new SystemMonitorReading($"{Math.Clamp(percent, 0, 100):0}%", detailText, Math.Clamp(percent / 100, 0, 1)));

    void RefreshVisualization()
    {
        MonitorVisualKind kind = VisualKind(definition.Id);
        double width = Math.Max(0, ActualWidth - 2);
        if (kind == MonitorVisualKind.Gauge && progress != null)
        {
            AnimateLength(progress, FrameworkElement.WidthProperty,
                lastReading?.Level is double level ? width * Math.Clamp(level, 0, 1) : 0);
            return;
        }

        double[] samples = history.Count == 0 ? [0d] : history.ToArray();
        double[] displaySamples = NormalizeHistoryForDisplay(samples);
        if (kind == MonitorVisualKind.Columns)
        {
            double availableHeight = Math.Max(7, visualizationRoot.ActualHeight - 1);
            for (int index = 0; index < HistoryLength; index++)
            {
                int sampleIndex = Math.Clamp(displaySamples.Length - HistoryLength + index, 0, displaySamples.Length - 1);
                AnimateLength(columnBars[index], FrameworkElement.HeightProperty, 1 + displaySamples[sampleIndex] * (availableHeight - 1));
            }
            return;
        }
        if (kind == MonitorVisualKind.Dots)
        {
            double level = lastReading?.Level ?? 0;
            int active = (int)Math.Round(Math.Clamp(level, 0, 1) * dotItems.Count);
            for (int index = 0; index < dotItems.Count; index++)
                AnimateOpacity(dotItems[index], index < active ? .92 : .18);
            return;
        }

        double height = Math.Max(7, visualizationRoot.ActualHeight - 1);
        var points = new PointCollection(HistoryLength);
        for (int index = 0; index < HistoryLength; index++)
        {
            int sampleIndex = Math.Clamp(displaySamples.Length - HistoryLength + index, 0, displaySamples.Length - 1);
            double x = width * index / (HistoryLength - 1d);
            points.Add(new System.Windows.Point(x, height - displaySamples[sampleIndex] * (height - 1)));
        }
        if (sparkline != null)
            sparkline.Points = points;
        if (sparklineGlow != null)
            sparklineGlow.Points = points;
    }

    internal static double[] NormalizeHistoryForDisplay(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
            return [0.5];

        double minimum = samples.Min();
        double maximum = samples.Max();
        double observedSpan = maximum - minimum;
        if (observedSpan < .0001)
            return Enumerable.Repeat(.5, samples.Count).ToArray();

        // A Deck cell is only a few dozen pixels high. Use a local window so
        // ordinary CPU and throughput changes remain visible without changing
        // the absolute value shown in the foreground text.
        double displaySpan = Math.Max(.035, observedSpan * 1.35);
        double center = (minimum + maximum) / 2;
        double low = center - displaySpan / 2;
        double high = center + displaySpan / 2;
        if (low < 0)
        {
            high -= low;
            low = 0;
        }
        if (high > 1)
        {
            low -= high - 1;
            high = 1;
        }
        low = Math.Max(0, low);
        double denominator = Math.Max(.0001, high - low);
        return samples.Select(sample => Math.Clamp(.08 + ((sample - low) / denominator) * .84, .08, .92)).ToArray();
    }

    internal static bool UsesSparkline(string id)
        => VisualKind(id) == MonitorVisualKind.Sparkline;

    internal static MonitorVisualKind VisualKind(string id)
        => id switch
        {
            "cpu" or "temperature" or "gpu-temperature" or "gpu" or "fan" or "network-latency" => MonitorVisualKind.Sparkline,
            "disk-read" or "disk-write" or "network-up" or "network-down" => MonitorVisualKind.Columns,
            "memory" or "vram" or "wifi" or "bluetooth" or "network-status" or "system-status" or "virtual-desktop" or "auto-extract" => MonitorVisualKind.Dots,
            _ => MonitorVisualKind.Gauge
        };

    static void AnimateLength(FrameworkElement element, DependencyProperty property, double target)
    {
        target = Math.Max(0, target);
        UiMotionService.RunSafely("deck-monitor-length", () =>
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, target);
        });
    }

    static void AnimateOpacity(UIElement element, double target)
    {
        UiMotionService.RunSafely("deck-monitor-dots", () =>
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = target;
        });
    }

    internal static WpfColor MonitorAccentColor(string id, bool dark)
    {
        string value = id switch
        {
            "cpu" => dark ? "#55A7FF" : "#1267B3",
            "memory" or "vram" => dark ? "#B687FF" : "#7641BA",
            "temperature" or "gpu-temperature" => dark ? "#FF735B" : "#C23C27",
            "gpu" => dark ? "#7BCE59" : "#398824",
            "fan" => dark ? "#4EC6FF" : "#087CAD",
            "disk" => dark ? "#F1B82D" : "#9A6B00",
            "disk-read" => dark ? "#69DBD4" : "#087E79",
            "disk-write" => dark ? "#D990FF" : "#8A3DAD",
            "network-up" => dark ? "#47D9B1" : "#087A5E",
            "network-down" => dark ? "#38BDE8" : "#08799C",
            "network-status" or "wifi" => dark ? "#8FDB57" : "#4A871B",
            "network-latency" => dark ? "#F0BC38" : "#8C6500",
            "battery" => dark ? "#8FDA54" : "#4E8A20",
            "volume" or "microphone" => dark ? "#F0809D" : "#B42A50",
            "brightness" => dark ? "#FFD34E" : "#946D00",
            "timer" => dark ? "#F1B82D" : "#9A6B00",
            "bluetooth" => dark ? "#7F9DFF" : "#405DB8",
            "clock" or "date" or "uptime" or "virtual-desktop" => dark ? "#A98CFF" : "#6246A9",
            "system-status" => dark ? "#74DD77" : "#2F8132",
            "auto-extract" => dark ? "#35D0C5" : "#087B69",
            _ => dark ? "#35D0C5" : "#087B69"
        };
        return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(value);
    }
}

internal enum MonitorVisualKind
{
    Sparkline,
    Columns,
    Dots,
    Gauge
}
