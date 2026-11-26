using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenRCT.Core.Analytics;
using OpenRCT.Core.Models;

namespace OpenRCT.UI.Mvvm
{
    public sealed class DeviceFilterOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public TargetDevice? Device { get; set; }
        public bool IsAllDevices => Device == null;

        public override string ToString() => DisplayName;

        public static List<DeviceFilterOption> BuildOptions(IReadOnlyList<TargetDevice> devices)
        {
            var list = new List<DeviceFilterOption>();
            if (devices == null || devices.Count == 0) return list;

            if (devices.Count > 1)
            {
                list.Add(new DeviceFilterOption
                {
                    DisplayName = $"All Devices ({devices.Count})",
                    Device = null
                });
            }

            foreach (var d in devices)
            {
                list.Add(new DeviceFilterOption
                {
                    DisplayName = d.DisplayName,
                    Device = d
                });
            }

            return list;
        }
    }

    public sealed class ExpandableChildRow : ObservableObject
    {
        private bool _isSelected;
        private readonly Action _onSelectionChanged;

        public ExpandableChildRow(Action onSelectionChanged)
        {
            _onSelectionChanged = onSelectionChanged;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _onSelectionChanged();
                }
            }
        }

        internal void SetSelectedSilent(bool value)
        {
            SetProperty(ref _isSelected, value, nameof(IsSelected));
        }

        public string DeviceName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SecondaryDetail { get; set; } = string.Empty;
        public string ExtraDetail { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public string? TimestampText { get; set; }
        public string TimestampDisplay => !string.IsNullOrEmpty(TimestampText)
            ? TimestampText!
            : (Timestamp.HasValue ? Timestamp.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty);
        public string ErrorCode { get; set; } = string.Empty;
        public object? Payload { get; set; }
    }

    public sealed class ExpandableItemGroup : ObservableObject
    {
        private bool _isExpanded;
        private bool? _isSelected = false;
        private bool _updatingSelection;

        public string GroupKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SecondaryInfo { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string StatusSummary { get; set; } = string.Empty;
        public string ExtraInfo { get; set; } = string.Empty;
        public int DeviceCount { get; set; }

        public ObservableCollection<ExpandableChildRow> Children { get; } = new ObservableCollection<ExpandableChildRow>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    OnPropertyChanged(nameof(ExpandGlyph));
                }
            }
        }

        public string ExpandGlyph => _isExpanded ? "\u25BE" : "\u25B8";

        public bool? IsSelected
        {
            get => _isSelected;
            set
            {
                var normalized = value ?? false;
                if (SetProperty(ref _isSelected, normalized))
                {
                    if (!_updatingSelection)
                    {
                        _updatingSelection = true;
                        try
                        {
                            foreach (var child in Children)
                            {
                                child.SetSelectedSilent(normalized);
                            }
                        }
                        finally
                        {
                            _updatingSelection = false;
                        }
                    }
                    OnPropertyChanged(nameof(SelectedChildCount));
                }
            }
        }

        public int SelectedChildCount => Children.Count(c => c.IsSelected);

        private void SyncParentSelectionFromChildren()
        {
            if (_updatingSelection) return;
            _updatingSelection = true;
            try
            {
                var selected = Children.Count(c => c.IsSelected);
                bool? next = selected == 0
                    ? (bool?)false
                    : (selected == Children.Count ? (bool?)true : null);
                SetProperty(ref _isSelected, next, nameof(IsSelected));
                OnPropertyChanged(nameof(SelectedChildCount));
            }
            finally
            {
                _updatingSelection = false;
            }
        }

        public static List<ExpandableItemGroup> FromGroupedData<T>(IEnumerable<GroupedItemData<T>> groups)
        {
            var list = new List<ExpandableItemGroup>();
            foreach (var g in groups)
            {
                var vm = new ExpandableItemGroup
                {
                    GroupKey = g.GroupKey,
                    Name = g.Name,
                    SecondaryInfo = g.SecondaryInfo,
                    Version = g.Version,
                    StatusSummary = g.StatusSummary,
                    ExtraInfo = g.ExtraInfo,
                    DeviceCount = g.DeviceCount
                };

                foreach (var c in g.Children)
                {
                    vm.Children.Add(new ExpandableChildRow(vm.SyncParentSelectionFromChildren)
                    {
                        DeviceName = c.DeviceName,
                        Status = c.Status,
                        SecondaryDetail = c.SecondaryDetail,
                        ExtraDetail = c.ExtraDetail,
                        Timestamp = c.Timestamp,
                        TimestampText = c.TimestampText,
                        ErrorCode = c.ErrorCode,
                        Payload = c.Payload
                    });
                }

                list.Add(vm);
            }
            return list;
        }
    }
}
