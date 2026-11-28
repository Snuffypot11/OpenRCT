using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using OpenCMTools.Core.Models;

namespace OpenCMTools.UI.Controls
{
    public partial class ErrorDrawerControl : UserControl
    {
        public static readonly DependencyProperty ErrorsProperty =
            DependencyProperty.Register(nameof(Errors), typeof(ObservableCollection<DeviceErrorEntry>), typeof(ErrorDrawerControl),
                new PropertyMetadata(null, OnErrorsChanged));

        private bool _isExpanded;

        public ErrorDrawerControl()
        {
            InitializeComponent();
            Errors = new ObservableCollection<DeviceErrorEntry>();
        }

        public ObservableCollection<DeviceErrorEntry>? Errors
        {
            get => (ObservableCollection<DeviceErrorEntry>?)GetValue(ErrorsProperty);
            set => SetValue(ErrorsProperty, value);
        }

        private static void OnErrorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (ErrorDrawerControl)d;
            if (e.OldValue is ObservableCollection<DeviceErrorEntry> oldCol)
            {
                oldCol.CollectionChanged -= ctrl.OnCollectionChanged;
            }
            if (e.NewValue is ObservableCollection<DeviceErrorEntry> newCol)
            {
                newCol.CollectionChanged += ctrl.OnCollectionChanged;
                ctrl.ErrorsGrid.ItemsSource = newCol;
            }
            ctrl.UpdateCount();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCount();

        private void UpdateCount()
        {
            var count = Errors?.Count ?? 0;
            CountText.Text = $"Errors {count}";
            CountText.Foreground = count > 0
                ? (System.Windows.Media.Brush)(TryFindResource("DangerBrush") ?? System.Windows.Media.Brushes.Crimson)
                : (System.Windows.Media.Brush)(TryFindResource("SecondaryTextBrush") ?? System.Windows.Media.Brushes.Gray);
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;
            DrawerContent.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            ChevronText.Text = _isExpanded ? "▼" : "▲";
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => Errors?.Clear();

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (Errors == null || Errors.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var err in Errors)
            {
                sb.AppendLine($"[{err.Timestamp:HH:mm:ss}] {err.Source}: {err.Message}");
                if (!string.IsNullOrWhiteSpace(err.Details))
                {
                    sb.AppendLine(err.Details);
                }
            }
            Clipboard.SetText(sb.ToString());
        }
    }
}
