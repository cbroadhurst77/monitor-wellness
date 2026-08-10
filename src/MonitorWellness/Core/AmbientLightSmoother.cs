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

    private double? _currentAdjustment;

    public double Update(double targetAdjustment)
    {
        targetAdjustment = Math.Clamp(targetAdjustment, -AmbientLightAdapter.MaxAdjustment, AmbientLightAdapter.MaxAdjustment);
        if (!_currentAdjustment.HasValue)
        {
            _currentAdjustment = targetAdjustment;
            return targetAdjustment;
        }

        _currentAdjustment = _currentAdjustment.Value + Math.Clamp(
            targetAdjustment - _currentAdjustment.Value,
            -MaximumStep,
            MaximumStep);
        return _currentAdjustment.Value;
    }

    public void Reset() => _currentAdjustment = null;
}
