using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRCT.UI.Mvvm;

namespace OpenRCT.UI.Controls
{
    public sealed class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }

    public sealed class CheckedPayloadEntry<T>
    {
        public string DeviceName { get; set; } = string.Empty;
        public T Payload { get; set; } = default!;
    }

    public partial class ExpandableGroupedGridControl : UserControl, INotifyPropertyChanged
    {
        private List<ExpandableItemGroup> _groups = new List<ExpandableItemGroup>();
        private bool _allExpanded;

        private string _childStatusHeader = "Status";
        private string _childSecondaryHeader = "Details";
        private string _childTimestampHeader = "Last Run / Eval";
        private string _childErrorHeader = "Error Code";
        private Visibility _childTimestampVisibility = Visibility.Visible;
        private Visibility _childErrorVisibility = Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ExpandableGroupedGridControl()
        {
            InitializeComponent();
            if (Resources["GridProxy"] is BindingProxy proxy)
            {
                proxy.Data = this;
            }
        }

        public string ChildStatusHeader
        {
            get => _childStatusHeader;
            set { _childStatusHeader = value; OnPropertyChanged(nameof(ChildStatusHeader)); }
        }

        public string ChildSecondaryHeader
        {
            get => _childSecondaryHeader;
            set { _childSecondaryHeader = value; OnPropertyChanged(nameof(ChildSecondaryHeader)); }
        }

        public string ChildTimestampHeader
        {
            get => _childTimestampHeader;
            set { _childTimestampHeader = value; OnPropertyChanged(nameof(ChildTimestampHeader)); }
        }

        public string ChildErrorHeader
        {
            get => _childErrorHeader;
            set { _childErrorHeader = value; OnPropertyChanged(nameof(ChildErrorHeader)); }
        }

        public Visibility ChildTimestampVisibility
        {
            get => _childTimestampVisibility;
            set { _childTimestampVisibility = value; OnPropertyChanged(nameof(ChildTimestampVisibility)); }
        }

        public Visibility ChildErrorVisibility
        {
            get => _childErrorVisibility;
            set { _childErrorVisibility = value; OnPropertyChanged(nameof(ChildErrorVisibility)); }
        }

        public void ConfigureColumns(
            string nameHeader,
            string secondaryHeader,
            string versionHeader,
            string statusHeader,
            string childStatusHeader = "Status",
            string childSecondaryHeader = "Details",
            string childTimestampHeader = "Last Run / Eval",
            string childErrorHeader = "Error Code",
            bool showSecondary = true,
            bool showVersion = true,
            bool showChildTimestamp = true,
            bool showChildError = true)
        {
            NameColumn.Header = nameHeader;
            SecondaryColumn.Header = secondaryHeader;
            SecondaryColumn.Visibility = showSecondary ? Visibility.Visible : Visibility.Collapsed;
            VersionColumn.Header = versionHeader;
            VersionColumn.Visibility = showVersion ? Visibility.Visible : Visibility.Collapsed;
            StatusColumn.Header = statusHeader;

            ChildStatusHeader = childStatusHeader;
            ChildSecondaryHeader = childSecondaryHeader;
            ChildTimestampHeader = childTimestampHeader;
            ChildErrorHeader = childErrorHeader;
            ChildTimestampVisibility = showChildTimestamp ? Visibility.Visible : Visibility.Collapsed;
            ChildErrorVisibility = showChildError ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetGroups(IEnumerable<ExpandableItemGroup> groups, bool preserveExpandedKeys = true)
        {
            var expandedKeys = preserveExpandedKeys
                ? new HashSet<string>(_groups.Where(g => g.IsExpanded).Select(g => g.GroupKey), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _groups = groups?.ToList() ?? new List<ExpandableItemGroup>();
            foreach (var g in _groups)
            {
                if (expandedKeys.Contains(g.GroupKey))
                {
                    g.IsExpanded = true;
                }
            }

            _allExpanded = _groups.Count > 0 && _groups.All(g => g.IsExpanded);
            ExpandAllButton.Content = _allExpanded ? "\u25BE" : "\u25B8";
            SelectAllCheckBox.IsChecked = false;
            ParentGrid.ItemsSource = _groups;
        }

        public List<CheckedPayloadEntry<T>> GetCheckedOrSelectedPayloads<T>()
        {
            var result = new List<CheckedPayloadEntry<T>>();
            foreach (var group in _groups)
            {
                foreach (var child in group.Children)
                {
                    if (child.IsSelected && child.Payload is T payload)
                    {
                        result.Add(new CheckedPayloadEntry<T>
                        {
                            DeviceName = child.DeviceName,
                            Payload = payload
                        });
                    }
                }
            }

            // Fallback: if nothing is checked, use all children of highlighted parent row(s)
            if (result.Count == 0)
            {
                foreach (var selectedGroup in ParentGrid.SelectedItems.OfType<ExpandableItemGroup>())
                {
                    foreach (var child in selectedGroup.Children)
                    {
                        if (child.Payload is T payload)
                        {
                            result.Add(new CheckedPayloadEntry<T>
                            {
                                DeviceName = child.DeviceName,
                                Payload = payload
                            });
                        }
                    }
                }
            }

            return result;
        }

        public int GroupCount => _groups.Count;
        public int TotalChildCount => _groups.Sum(g => g.Children.Count);

        private void ToggleExpandRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ExpandableItemGroup group)
            {
                group.IsExpanded = !group.IsExpanded;
                e.Handled = true;
            }
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            _allExpanded = !_allExpanded;
            foreach (var g in _groups)
            {
                g.IsExpanded = _allExpanded;
            }
            ExpandAllButton.Content = _allExpanded ? "\u25BE" : "\u25B8";
        }

        private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var isChecked = SelectAllCheckBox.IsChecked == true;
            foreach (var g in _groups)
            {
                g.IsSelected = isChecked;
            }
        }

        private void ParentGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null)
            {
                if (dep is CheckBox || dep is Button) return;
                if (dep is DataGridRow row && row.Item is ExpandableItemGroup group)
                {
                    group.IsExpanded = !group.IsExpanded;
                    e.Handled = true;
                    return;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
        }

        private void ChildGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Forward scroll wheel events up to the outer parent DataGrid so scrolling never gets trapped.
            if (!e.Handled && sender is UIElement ui)
            {
                e.Handled = true;
                var routed = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                ParentGrid.RaiseEvent(routed);
            }
        }

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
