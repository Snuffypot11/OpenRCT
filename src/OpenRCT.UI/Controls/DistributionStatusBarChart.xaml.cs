using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRCT.Core.Models;

namespace OpenRCT.UI.Controls
{
    public partial class DistributionStatusBarChart : UserControl
    {
        private List<DistributionPointSummary> _points = new List<DistributionPointSummary>();
        private string? _selectedServerName;

        public event EventHandler<DistributionPointSummary?>? DistributionPointSelected;

        public DistributionStatusBarChart()
        {
            InitializeComponent();
            SizeChanged += (_, __) => RenderBars();
        }

        public string? SelectedServerName
        {
            get => _selectedServerName;
            set
            {
                _selectedServerName = value;
                ClearSelectionButton.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
                RenderBars();
            }
        }

        public void SetDistributionPoints(IEnumerable<DistributionPointSummary>? points)
        {
            _points = (points ?? Enumerable.Empty<DistributionPointSummary>()).ToList();
            RenderBars();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            SelectedServerName = null;
            DistributionPointSelected?.Invoke(this, null);
        }

        private void RenderBars()
        {
            BarsPanel.Children.Clear();

            if (_points.Count == 0)
            {
                BarsPanel.Children.Add(new TextBlock
                {
                    Text = "No Distribution Points loaded. Click Refresh to query the SMS Provider.",
                    FontSize = 12,
                    Foreground = (Brush)(TryFindResource("SecondaryTextBrush") ?? Brushes.Gray),
                    Margin = new Thickness(6, 24, 6, 6),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            int maxTotal = Math.Max(1, _points.Max(p => p.TotalCount));
            double availableBarWidth = Math.Max(180, ActualWidth - 260);

            var successBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
            var inProgressBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            var errorBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            var emptyTrackBrush = (Brush)(TryFindResource("BorderBrushSubtle") ?? Brushes.LightGray);

            foreach (var dp in _points)
            {
                var captured = dp;
                bool isSelected = !string.IsNullOrWhiteSpace(_selectedServerName) &&
                                  string.Equals(_selectedServerName, dp.ServerName, StringComparison.OrdinalIgnoreCase);

                var rowBorder = new Border
                {
                    Padding = new Thickness(6, 5, 6, 5),
                    Margin = new Thickness(0, 2, 0, 2),
                    CornerRadius = new CornerRadius(4),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    ToolTip = $"{dp.ServerName}\nSuccess: {dp.SuccessCount} | In Progress: {dp.InProgressCount} | Error: {dp.ErrorCount} | Total: {dp.TotalCount}"
                };

                if (isSelected)
                {
                    rowBorder.SetResourceReference(Border.BackgroundProperty, "SelectionFillBrush");
                    rowBorder.SetResourceReference(Border.BorderBrushProperty, "SelectionBorderBrush");
                }
                else
                {
                    rowBorder.Background = Brushes.Transparent;
                    rowBorder.BorderBrush = Brushes.Transparent;
                }

                rowBorder.MouseLeftButtonUp += (_, __) =>
                {
                    if (string.Equals(_selectedServerName, captured.ServerName, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedServerName = null;
                        DistributionPointSelected?.Invoke(this, null);
                    }
                    else
                    {
                        SelectedServerName = captured.ServerName;
                        DistributionPointSelected?.Invoke(this, captured);
                    }
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = dp.ServerName,
                    FontSize = 11,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);

                var barStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Height = 22
                };

                if (dp.TotalCount == 0)
                {
                    barStack.Children.Add(new Border
                    {
                        Width = 36,
                        Height = 22,
                        CornerRadius = new CornerRadius(2),
                        Background = emptyTrackBrush,
                        Child = new TextBlock
                        {
                            Text = "0",
                            FontSize = 10,
                            Foreground = (Brush)(TryFindResource("SecondaryTextBrush") ?? Brushes.Gray),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                }
                else
                {
                    AddSegment(barStack, dp.SuccessCount, maxTotal, availableBarWidth, successBrush);
                    AddSegment(barStack, dp.InProgressCount, maxTotal, availableBarWidth, inProgressBrush);
                    AddSegment(barStack, dp.ErrorCount, maxTotal, availableBarWidth, errorBrush);
                }

                Grid.SetColumn(barStack, 1);
                grid.Children.Add(barStack);

                rowBorder.Child = grid;
                BarsPanel.Children.Add(rowBorder);
            }
        }

        private static void AddSegment(StackPanel panel, int count, int maxTotal, double availableWidth, Brush brush)
        {
            if (count <= 0) return;

            double rawWidth = (count / (double)maxTotal) * availableWidth;
            double segmentWidth = Math.Max(6, rawWidth);

            var seg = new Border
            {
                Width = segmentWidth,
                Height = 22,
                Background = brush,
                Margin = new Thickness(0, 0, 1, 0)
            };

            if (segmentWidth >= 24)
            {
                seg.Child = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            panel.Children.Add(seg);
        }
    }
}
