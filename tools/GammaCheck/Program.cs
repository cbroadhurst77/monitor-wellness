using System.Runtime.InteropServices;
using MonitorWellness.Core;

[DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string? lpszOutput, IntPtr lpInitData);
[DllImport("gdi32.dll")]
static extern bool DeleteDC(IntPtr hdc);
[DllImport("gdi32.dll", SetLastError = true)]
static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

bool ApplyContrast(IntPtr hdc, int kelvin, double contrastReduction)
{
    var (r, g, b) = ColorTemperature.KelvinToRgbFactors(kelvin);
    var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
    for (int i = 0; i < 256; i++)
    {
        double n = i / 255.0;
        double compressed = contrastReduction + (1 - contrastReduction) * n;
        ramp.Red[i] = (ushort)Math.Clamp(compressed * 65535.0 * r, 0, 65535);
        ramp.Green[i] = (ushort)Math.Clamp(compressed * 65535.0 * g, 0, 65535);
        ramp.Blue[i] = (ushort)Math.Clamp(compressed * 65535.0 * b, 0, 65535);
    }
    return SetDeviceGammaRamp(hdc, ref ramp);
}

var monitors = MonitorEnumerator.GetActiveMonitors();
var hdcs = new List<IntPtr>();
foreach (var m in monitors)
{
    var hdc = CreateDC("DISPLAY", m.DeviceName, null, IntPtr.Zero);
    if (hdc != IntPtr.Zero) hdcs.Add(hdc);
}

// All at brightness assist = 1.0 (gamma's actual production behavior -- real dimming
// always comes from the overlay, never from scaling gamma), varying only contrast at the
// night-floor Kelvin, to find exactly where raising the floor alone starts failing.
foreach (double contrast in new[] { 0.10, 0.15, 0.18, 0.20, 0.25, 0.30 })
{
    var (_, _, blue) = ColorTemperature.KelvinToRgbFactors(3400);
    double floorFactor = contrast * blue; // the resulting blue channel's minimum (at i=0)
    var results = hdcs.Select(hdc => ApplyContrast(hdc, 3400, contrast)).ToList();
    Console.WriteLine($"3400K + {contrast:P0} contrast (blue floor factor={floorFactor:F3}) -> [{string.Join(", ", results)}]");
}

Console.WriteLine("Resetting to identity...");
foreach (var hdc in hdcs)
{
    var identity = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
    for (int i = 0; i < 256; i++) { ushort v = (ushort)(i * 257); identity.Red[i] = v; identity.Green[i] = v; identity.Blue[i] = v; }
    SetDeviceGammaRamp(hdc, ref identity);
    DeleteDC(hdc);
}
Console.WriteLine("Done.");

[StructLayout(LayoutKind.Sequential)]
struct RAMP
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
}
