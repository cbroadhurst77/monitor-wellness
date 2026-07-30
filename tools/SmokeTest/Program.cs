using MonitorWellness.Core;

foreach (int k in new[] { 6500, 4000, 3400, 3000, 2500 })
{
    var (r, g, b) = ColorTemperature.KelvinToRgbFactors(k);
    double min = Math.Min(r, Math.Min(g, b));
    Console.WriteLine($"{k}K: R={r:F4} G={g:F4} B={b:F4} min={min:F4}  IsSafeForGammaRamp={ColorTemperature.IsSafeForGammaRamp(k)}");
}
