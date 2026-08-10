using System.IO;
using System.Xml.Linq;

namespace MonitorWellness.Tests;

/// <summary>
/// Fast CI regression checks for accessibility-critical WPF markup. Real screen-reader and
/// keyboard testing remains in QA_CHECKLIST because it requires a live desktop and hardware.
/// </summary>
public class AccessibilityMarkupTests
{
    [Fact]
    public void SettingsWindow_HasKeyboardRecoveryAndAnnouncedStatusRegions()
    {
        XDocument document = XDocument.Load(GetProjectFile("src", "MonitorWellness", "SettingsWindow.xaml"));
        XElement? root = document.Root;
        Assert.NotNull(root);

        Assert.Equal("SettingsWindow_PreviewKeyDown", root!.Attribute("PreviewKeyDown")?.Value);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.LiveSetting" && attribute.Value == "Polite"));
    }

    [Fact]
    public void HardwareBrightnessTestDialog_IsTopmostAndHasAutomaticRecoveryStatus()
    {
        XDocument document = XDocument.Load(GetProjectFile("src", "MonitorWellness", "HardwareBrightnessTestDialog.xaml"));
        XElement? root = document.Root;
        Assert.NotNull(root);

        Assert.Equal("True", root!.Attribute("Topmost")?.Value);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.LiveSetting" && attribute.Value == "Polite"));
    }

    private static string GetProjectFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate project file {Path.Combine(segments)} from {AppContext.BaseDirectory}.");
    }
}
