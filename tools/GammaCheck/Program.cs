using MonitorWellness.Core;

var monitors = MonitorEnumerator.GetActiveMonitors();
var controllers = new List<GammaRampController>();
foreach (var m in monitors)
{
    try { controllers.Add(new GammaRampController(m.DeviceName)); }
    catch (InvalidOperationException) { }
}

foreach (int k in new[] { 4000, 3400, 3000 })
{
    var results = controllers.Select(c => c.ApplyColorTemperature(k)).ToList();
    Console.WriteLine($"{k}K -> [{string.Join(", ", results)}]");
}

Console.WriteLine("Resetting to identity...");
foreach (var c in controllers) { c.ResetToIdentity(); c.Dispose(); }
