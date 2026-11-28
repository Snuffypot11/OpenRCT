using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OpenCMTools.UI.Mvvm;

namespace OpenCMTools.UI.Controls
{
    public sealed class SelectableItem : ObservableObject
    {
        private bool _isSelected;
        public string Key { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public object? Tag { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public partial class MultiSelectDropdown : UserControl
    {
        private List<SelectableItem> _allItems = new List<SelectableItem>();

        public event EventHandler? SelectionChanged;

        public string PlaceholderText { get; set; } = "(All / Select items...)";

        public MultiSelectDropdown()
        {
            InitializeComponent();
            UpdateSummary();
        }

        public void SetItems(IEnumerable<SelectableItem> items)
        {
            _allItems = items?.ToList() ?? new List<SelectableItem>();
            ApplyFilter();
            UpdateSummary();
        }

        public IReadOnlyList<SelectableItem> GetSelectedItems() =>
            _allItems.Where(i => i.IsSelected).ToList();

        public IReadOnlyList<string> GetSelectedKeys() =>
            _allItems.Where(i => i.IsSelected).Select(i => i.Key).ToList();

        private void ApplyFilter()
        {
            var term = SearchBox?.Text?.Trim() ?? string.Empty;
            ItemsList.ItemsSource = string.IsNullOrEmpty(term)
                ? _allItems
                : _allItems.Where(i => i.DisplayText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private void UpdateSummary()
        {
            var selected = _allItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                SummaryText.Text = PlaceholderText;
            }
            else if (selected.Count <= 2)
            {
                SummaryText.Text = string.Join(", ", selected.Select(s => s.DisplayText));
            }
            else
            {
                SummaryText.Text = $"{selected.Count} items selected";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ItemCheckChanged(object sender, RoutedEventArgs e)
        {
            UpdateSummary();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allItems) item.IsSelected = true;
            UpdateSummary();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allItems) item.IsSelected = false;
            UpdateSummary();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
