using System.Windows;
using MonitorWellness.Core;

namespace MonitorWellness;

/// <summary>
/// Shown once, on the very first launch (AppSettings.HasCompletedOnboarding starts false).
/// Any of the dismiss/skip/set-location paths just closes the window — App marks onboarding
/// complete and saves on Closed, regardless of which path was taken, so there's no way to end
/// up stuck re-showing this.
///
/// Presented as short sequential steps (one idea per screen) rather than one dense wall of
/// text — see MonitorWellness_UX_Accessibility_Audit.html §2.3/§3.1/§6 (P1).
/// </summary>
public partial class OnboardingWindow : Window
{
    private static readonly string[] StepTitles =
    {
        "What Monitor Wellness does",
        "Migraine Mode",
        "A quick safety note",
        "One last thing",
    };

    private readonly Action _openSettings;
    private int _step;

    public OnboardingWindow(Action openSettings)
    {
        InitializeComponent();
        ThemeDetector.ApplyDarkThemeIfNeeded(this);
        ThemeDetector.EnableLiveThemeUpdates(this);
        Icon = AppIconSource.Default;
        _openSettings = openSettings;
        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _step = step;

        Step1Panel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        StepProgressText.Text = $"Step {step + 1} of {StepTitles.Length}";
        StepTitleText.Text = StepTitles[step];

        bool isLastStep = step == StepTitles.Length - 1;
        BackButton.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = isLastStep ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = isLastStep ? Visibility.Visible : Visibility.Collapsed;
        SetLocationButton.Visibility = isLastStep ? Visibility.Visible : Visibility.Collapsed;

        // Only one button should respond to Enter at a time, and it should always be whichever
        // primary action is actually on screen right now.
        NextButton.IsDefault = !isLastStep;
        SetLocationButton.IsDefault = isLastStep;
    }

    private void Next_Click(object sender, RoutedEventArgs e) => ShowStep(_step + 1);

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _openSettings();
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
