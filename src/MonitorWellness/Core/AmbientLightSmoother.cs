namespace MonitorWellness.Core;

/// <summary>
/// Limits changes in ambient-light compensation between schedule ticks. A sensor may jump when
/// a hand passes it or a light is switched; display comfort should adapt gradually, never chase
/// those instantaneous readings with a noticeable brightness step.
/// </summary>
public sealed class AmbientLightSmoother
{
    /// <summary>Largest brightness adjustment change allowed per sample (two percentage points).</summary>
    public const double MaximumStep = 0.02;

    /// <summary>The spacing the per-step limit was chosen for: the normal schedule tick.</summary>
    public static readonly TimeSpan NominalPeriod = TimeSpan.FromSeconds(30);

    private double? _currentAdjustment;
    private DateTime? _lastUpdateUtc;

    /// <summary>
    /// Time-based version: the allowed change is <see cref="MaximumStep"/> per <see cref="NominalPeriod"/>
    /// of real time, however often this is called. (Migraine fade-out and application-rule changes
    /// also trigger schedule computations, so per-call limiting made the adjustment speed depend on
    /// how often the app happened to recompute.)
    /// </summary>
    public double Update(double targetAdjustment, DateTime nowUtc)
    {
        double periods = _lastUpdateUtc is DateTime last
            ? Math.Clamp((nowUtc - last) / NominalPeriod, 0.0, 4.0)
            : 1.0;
        _lastUpdateUtc = nowUtc;
        return UpdateWithStep(targetAdjustment, MaximumStep * periods);
    }

    public double Update(double targetAdjustment) => UpdateWithStep(targetAdjustment, MaximumStep);

    private double UpdateWithStep(double targetAdjustment, double maximumStep)
    {
        targetAdjustment = Math.Clamp(targetAdjustment, -AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.MaxAdjustment);
        if (!_currentAdjustment.HasValue)
        {
            _currentAdjustment = targetAdjustment;
            return targetAdjustment;
        }

        _currentAdjustment = _currentAdjustment.Value + Math.Clamp(
            targetAdjustment - _currentAdjustment.Value,
            -maximumStep,
            maximumStep);
        return _currentAdjustment.Value;
    }

    public void Reset()
    {
        _currentAdjustment = null;
        _lastUpdateUtc = null;
    }
}
