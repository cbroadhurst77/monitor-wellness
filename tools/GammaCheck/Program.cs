using System.Drawing;
using System.Drawing.Drawing2D;

using var src = new Bitmap(@"C:\tools\MonitorWellness\src\MonitorWellness\Assets\migraine_off.ico");
using var canvas = new Bitmap(400, 100);
using var g = Graphics.FromImage(canvas);
g.Clear(Color.LightGray);
g.SmoothingMode = SmoothingMode.HighQuality;
g.InterpolationMode = InterpolationMode.HighQualityBicubic;

int x = 10;
foreach (int size in new[] { 16, 20, 24, 32 })
{
    g.DrawImage(src, x, 10, size, size);
    x += size + 20;
}
canvas.Save(@"C:\tools\MonitorWellness\src\MonitorWellness\Assets\size_check.png");
Console.WriteLine("saved");
