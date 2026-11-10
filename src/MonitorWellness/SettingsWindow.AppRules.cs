using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonitorWellness.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MonitorWellness;

/// <summary>Foreground-application comfort rules. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
    private sealed class ApplicationRuleRow
    {
        public required Grid Container { get; init; }
        public required TextBox ProcessNameBox { get; init; }
        public required TextBox WindowTitleBox { get; init; }
        public required ComboBox ActionBox { get; init; }
        public required ComboBox ComfortPlanBox { get; init; }
        public required CheckBox EnabledBox { get; init; }
    }

    private void BuildApplicationRuleRows()
    {
        ApplicationRulesList.Children.Clear();
        _applicationRuleRows.Clear();
        foreach (ApplicationComfortRule rule in _settings.ApplicationComfortRules)
            AddApplicationRuleRow(rule);
    }

    private void AddApplicationRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_applicationRuleRows.Count >= 100)
        {
            System.Windows.MessageBox.Show(this, "You can save up to 100 application rules.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApplicationRuleRow row = AddApplicationRuleRow(new ApplicationComfortRule());
        row.ProcessNameBox.Focus();
    }

    /// <summary>
    /// Adds a generic rule from the tray's foreground-app capture. A window title is deliberately
    /// not copied: titles can reveal document or meeting names, and the user can add a scoped
    /// condition themselves if they want one.
    /// </summary>
    public void AddApplicationRuleTemplate(string processName)
    {
        if (!ApplicationComfortRules.TryNormalizeProcessName(processName, out string normalized))
            return;

        ApplicationRuleRow? existing = _applicationRuleRows.FirstOrDefault(row =>
            ApplicationComfortRules.TryNormalizeProcessName(row.ProcessNameBox.Text, out string configured)
            && string.Equals(configured, normalized, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(row.WindowTitleBox.Text));
        if (existing is not null)
        {
            existing.ActionBox.Focus();
            return;
        }

        if (_applicationRuleRows.Count >= 100)
        {
            System.Windows.MessageBox.Show(this, "You can save up to 100 application rules.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApplicationRuleRow row = AddApplicationRuleRow(new ApplicationComfortRule { ProcessName = normalized });
        row.ActionBox.Focus();
    }

    private ApplicationRuleRow AddApplicationRuleRow(ApplicationComfortRule rule)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var enabled = new CheckBox
        {
            Content = "Enabled",
            IsChecked = rule.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(enabled, "Enable application comfort rule");
        Grid.SetColumn(enabled, 0);

        var processName = new TextBox
        {
            Text = rule.ProcessName,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Executable name only, e.g. photoshop.exe or vlc.",
        };
        System.Windows.Automation.AutomationProperties.SetName(processName, "Application executable name");
        Grid.SetColumn(processName, 1);

        var windowTitle = new TextBox
        {
            Text = rule.WindowTitleContains ?? "",
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Optional window-title words, e.g. Presentation or Teams meeting.",
        };
        System.Windows.Automation.AutomationProperties.SetName(windowTitle, "Optional application window title condition");
        Grid.SetColumn(windowTitle, 2);

        var remove = new System.Windows.Controls.Button
        {
            Content = "Remove",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(remove, "Remove application comfort rule");
        Grid.SetColumn(remove, 3);

        var action = new ComboBox
        {
            MinWidth = 164,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        action.Items.Add(new ComboBoxItem { Content = "Restore native display", Tag = ApplicationComfortActions.RestoreNativeDisplay });
        action.Items.Add(new ComboBoxItem { Content = "Use comfort plan", Tag = ApplicationComfortActions.ApplySensoryComfortPlan });
        action.SelectedItem = action.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag as string, rule.Action, StringComparison.Ordinal))
            ?? action.Items.OfType<ComboBoxItem>().First();
        System.Windows.Automation.AutomationProperties.SetName(action, "Application comfort action");

        var comfortPlan = new ComboBox
        {
            MinWidth = 142,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (string plan in new[] { SensoryComfortPlans.Balanced, SensoryComfortPlans.Reading, SensoryComfortPlans.ColourCritical, SensoryComfortPlans.EarlySensitivity, SensoryComfortPlans.Recovery })
            comfortPlan.Items.Add(new ComboBoxItem { Content = plan, Tag = plan });
        comfortPlan.SelectedItem = comfortPlan.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag as string, rule.ComfortPlanName, StringComparison.Ordinal))
            ?? comfortPlan.Items.OfType<ComboBoxItem>().First();
        System.Windows.Automation.AutomationProperties.SetName(comfortPlan, "Temporary sensory comfort plan");

        var configuration = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
        };
        configuration.Children.Add(new TextBlock { Text = "When matched:", VerticalAlignment = VerticalAlignment.Center });
        configuration.Children.Add(action);
        configuration.Children.Add(comfortPlan);
        Grid.SetRow(configuration, 1);
        Grid.SetColumn(configuration, 1);
        Grid.SetColumnSpan(configuration, 2);

        void UpdateComfortPlanAvailability()
        {
            bool usesComfortPlan = string.Equals((action.SelectedItem as ComboBoxItem)?.Tag as string,
                ApplicationComfortActions.ApplySensoryComfortPlan, StringComparison.Ordinal);
            comfortPlan.IsEnabled = usesComfortPlan;
            comfortPlan.Opacity = usesComfortPlan ? 1.0 : 0.55;
        }
        action.SelectionChanged += (_, _) => UpdateComfortPlanAvailability();
        UpdateComfortPlanAvailability();

        var row = new ApplicationRuleRow
        {
            Container = grid,
            ProcessNameBox = processName,
            WindowTitleBox = windowTitle,
            ActionBox = action,
            ComfortPlanBox = comfortPlan,
            EnabledBox = enabled,
        };
        remove.Click += (_, _) =>
        {
            ApplicationRulesList.Children.Remove(grid);
            _applicationRuleRows.Remove(row);
        };

        grid.Children.Add(enabled);
        grid.Children.Add(processName);
        grid.Children.Add(windowTitle);
        grid.Children.Add(remove);
        grid.Children.Add(configuration);
        ApplicationRulesList.Children.Add(grid);
        _applicationRuleRows.Add(row);
        return row;
    }
}
