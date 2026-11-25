using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRCT.Core.Models;

namespace OpenRCT.UI.Controls
{
    public partial class InteractivePieChart : UserControl
    {
        public static readonly DependencyProperty ChartTitleProperty =
            DependencyProperty.Register(nameof(ChartTitle), typeof(string), typeof(InteractivePieChart),
                new PropertyMetadata(string.Empty, OnTitleChanged));

        public static readonly DependencyProperty SlicesProperty =
            DependencyProperty.Register(nameof(Slices), typeof(IEnumerable<PieSliceData>), typeof(InteractivePieChart),
                new PropertyMetadata(null, OnSlicesChanged));

        public static readonly DependencyProperty SelectedSliceKeyProperty =
            DependencyProperty.Register(nameof(SelectedSliceKey), typeof(string), typeof(InteractivePieChart),
                new PropertyMetadata(null, OnSlicesChanged));

        public event EventHandler<PieSliceData?>? SliceClicked;

        public InteractivePieChart()
        {
            InitializeComponent();
        }

        public string ChartTitle
        {
            get => (string)GetValue(ChartTitleProperty);
            set => SetValue(ChartTitleProperty, value);
        }

        public IEnumerable<PieSliceData>? Slices
        {
            get => (IEnumerable<PieSliceData>?)GetValue(SlicesProperty);
            set => SetValue(SlicesProperty, value);
        }

        public string? SelectedSliceKey
        {
            get => (string?)GetValue(SelectedSliceKeyProperty);
            set => SetValue(SelectedSliceKeyProperty, value);
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = (InteractivePieChart)d;
            var title = e.NewValue as string;
            chart.TitleText.Text = title ?? string.Empty;
            chart.TitleText.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static void OnSlicesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((InteractivePieChart)d).RenderChart();
        }

        private void RenderChart()
        {
            PieCanvas.Children.Clear();
            LegendPanel.Children.Clear();

            var list = (Slices ?? Enumerable.Empty<PieSliceData>()).ToList();
            int total = list.Sum(s => Math.Max(0, s.Value));

            const double centerX = 110;
            const double centerY = 110;
            const double radius = 98;

            if (total <= 0)
            {
                var emptyCircle = new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = new SolidColorBrush(Color.FromRgb(235, 238, 242)),
                    Stroke = new SolidColorBrush(Color.FromRgb(210, 215, 222)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(emptyCircle, centerX - radius);
                Canvas.SetTop(emptyCircle, centerY - radius);
                PieCanvas.Children.Add(emptyCircle);

                var noDataText = new TextBlock
                {
                    Text = "No Data",
                    FontSize = 13,
                    Foreground = Brushes.Gray
                };
                Canvas.SetLeft(noDataText, centerX - 26);
                Canvas.SetTop(noDataText, centerY - 10);
                PieCanvas.Children.Add(noDataText);
            }
            else
            {
                double startAngle = -90.0;
                foreach (var slice in list)
                {
                    if (slice.Value <= 0) continue;

                    double sweepAngle = (slice.Value / (double)total) * 360.0;
                    var fill = ParseBrush(slice.ColorHex);
                    var isSelected = !string.IsNullOrEmpty(SelectedSliceKey) &&
                                     string.Equals(SelectedSliceKey, slice.Key, StringComparison.OrdinalIgnoreCase);

                    UIElement shape;
                    if (sweepAngle >= 359.99)
                    {
                        var full = new Ellipse
                        {
                            Width = radius * 2,
                            Height = radius * 2,
                            Fill = fill,
                            Stroke = Brushes.White,
                            StrokeThickness = isSelected ? 3 : 1.5,
                            Cursor = Cursors.Hand,
                            ToolTip = $"{slice.Label}: {slice.Value} ({(slice.Value * 100.0 / total):0.#}%)"
                        };
                        Canvas.SetLeft(full, centerX - radius);
                        Canvas.SetTop(full, centerY - radius);
                        shape = full;
                    }
                    else
                    {
                        var path = CreateSlicePath(centerX, centerY, radius, startAngle, sweepAngle, fill, isSelected);
                        path.ToolTip = $"{slice.Label}: {slice.Value} ({(slice.Value * 100.0 / total):0.#}%)";
                        shape = path;
                    }

                    var captured = slice;
                    shape.MouseLeftButtonUp += (_, __) => ToggleSlice(captured);
                    PieCanvas.Children.Add(shape);

                    // Numeric label inside slice when wide enough
                    if (sweepAngle >= 18)
                    {
                        double midRad = (startAngle + sweepAngle / 2.0) * Math.PI / 180.0;
                        double labelRadius = sweepAngle >= 359.99 ? 0 : radius * 0.6;
                        double lx = centerX + labelRadius * Math.Cos(midRad);
                        double ly = centerY + labelRadius * Math.Sin(midRad);

                        var lbl = new TextBlock
                        {
                            Text = slice.Value.ToString(),
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = Brushes.White,
                            IsHitTestVisible = false
                        };
                        lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Canvas.SetLeft(lbl, lx - lbl.DesiredSize.Width / 2);
                        Canvas.SetTop(lbl, ly - lbl.DesiredSize.Height / 2);
                        PieCanvas.Children.Add(lbl);
                    }

                    startAngle += sweepAngle;
                }
            }

            foreach (var slice in list)
            {
                var captured = slice;
                var isSelected = !string.IsNullOrEmpty(SelectedSliceKey) &&
                                 string.Equals(SelectedSliceKey, slice.Key, StringComparison.OrdinalIgnoreCase);

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 3),
                    Cursor = Cursors.Hand
                };
                row.MouseLeftButtonUp += (_, __) => ToggleSlice(captured);

                row.Children.Add(new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = ParseBrush(slice.ColorHex),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                row.Children.Add(new TextBlock
                {
                    Text = $"{slice.Label} ({slice.Value})",
                    FontSize = 12,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = (Brush)(TryFindResource("PrimaryTextBrush") ?? Brushes.Black),
                    VerticalAlignment = VerticalAlignment.Center
                });

                LegendPanel.Children.Add(row);
            }
        }

        private void ToggleSlice(PieSliceData slice)
        {
            if (string.Equals(SelectedSliceKey, slice.Key, StringComparison.OrdinalIgnoreCase))
            {
                SelectedSliceKey = null;
                SliceClicked?.Invoke(this, null);
            }
            else
            {
                SelectedSliceKey = slice.Key;
                SliceClicked?.Invoke(this, slice);
            }
        }

        private static Path CreateSlicePath(
            double cx, double cy, double r, double startAngleDeg, double sweepAngleDeg, Brush fill, bool isSelected)
        {
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;

            double offset = isSelected ? 5.0 : 0.0;
            double midRad = (startAngleDeg + sweepAngleDeg / 2.0) * Math.PI / 180.0;
            double ox = cx + offset * Math.Cos(midRad);
            double oy = cy + offset * Math.Sin(midRad);

            var startPoint = new Point(ox + r * Math.Cos(startRad), oy + r * Math.Sin(startRad));
            var endPoint = new Point(ox + r * Math.Cos(endRad), oy + r * Math.Sin(endRad));

            var figure = new PathFigure { StartPoint = new Point(ox, oy), IsClosed = true };
            figure.Segments.Add(new LineSegment(startPoint, true));
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(r, r),
                IsLargeArc = sweepAngleDeg > 180.0,
                SweepDirection = SweepDirection.Clockwise
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Data = geometry,
                Fill = fill,
                Stroke = Brushes.White,
                StrokeThickness = isSelected ? 2.5 : 1.5,
                Cursor = Cursors.Hand
            };
        }

        private static Brush ParseBrush(string hex)
        {
            try
            {
                return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            }
            catch
            {
                return Brushes.SteelBlue;
            }
        }
    }
}
