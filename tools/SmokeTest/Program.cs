using MonitorWellness.Core;

foreach (int k in new[] { 4000, 3400, 2500 })
{
    var (r, g, b) = ColorTemperature.KelvinToRgbFactors(k);
    Console.WriteLine($"{k}K: R={r:F3} G={g:F3} B={b:F3} min={Math.Min(r, Math.Min(g, b)):F3}");
}
