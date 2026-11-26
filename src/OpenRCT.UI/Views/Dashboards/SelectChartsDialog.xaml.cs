using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OpenRCT.Core.Analytics;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Dashboards
{
    public partial class SelectChartsDialog : Window
    {
        private readonly Dictionary<HardwareAuditDimension, CheckBox> _boxes = new Dictionary<HardwareAuditDimension, CheckBox>();

        public List<HardwareAuditDimension> SelectedDimensions { get; private set; }

        public SelectChartsDialog(IEnumerable<HardwareAuditDimension> currentlySelected, OpenRctSettings settings)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();

            var selectedSet = new HashSet<HardwareAuditDimension>(currentlySelected ?? Enumerable.Empty<HardwareAuditDimension>());
            SelectedDimensions = selectedSet.ToList();

            foreach (HardwareAuditDimension dim in Enum.GetValues(typeof(HardwareAuditDimension)))
            {
                var cb = new CheckBox
                {
                    Content = HardwareFirmwareAnalyzer.GetDimensionDisplayName(dim),
                    IsChecked = selectedSet.Contains(dim),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                _boxes[dim] = cb;
                CheckboxesPanel.Children.Add(cb);
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var chosen = _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Select at least one chart to display.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedDimensions = chosen;
            DialogResult = true;
            Close();
        }
    }
}
