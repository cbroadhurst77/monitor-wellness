using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

public partial class ProfileNameDialog : Window
{
    public string ProfileName { get; private set; } = "";

    public ProfileNameDialog(string initialName)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        NameBox.Text = initialName;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            System.Windows.MessageBox.Show(this, "Enter a profile name.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProfileName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
