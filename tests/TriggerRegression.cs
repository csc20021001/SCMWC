using System;
using System.Drawing;
using System.Reflection;
using SCMWC;

class TriggerVerification
{
    const int Width = 300, Height = 96;
    static int checks;
    static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception(message);
    }
    static void Pixels(Capturer capture, int percent, Color fill, Color empty)
    {
        var pixels = (byte[])typeof(Capturer).GetField("buf", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(capture);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Color color = x < Width * percent / 100 ? fill : empty;
                int at = (y * Width + x) * 4;
                pixels[at] = color.B; pixels[at + 1] = color.G; pixels[at + 2] = color.R;
            }
    }
    static int Main()
    {
        try
        {
            Rectangle region = new Rectangle(0, 0, Width, Height);
            using (var capture = new Capturer(Width, Height))
            {
                Color green = Color.FromArgb(20, 200, 60), dark = Color.FromArgb(20, 20, 20);
                var full = new Trigger(); full.Kind = TriggerKind.Full;
                Pixels(capture, 40, green, dark);
                Check(full.Evaluate(capture, region, 0) == null, "partial progress must not complete");
                Check(full.LastPercent > 35 && full.LastPercent < 45, "partial progress must be measured");
                Pixels(capture, 98, green, dark);
                Check(full.Evaluate(capture, region, 0) != null, "98 percent must complete despite borders");
                Check(full.Evaluate(capture, region, 0) == null, "completion must notify only once");
                Pixels(capture, 0, green, dark);
                Check(full.Evaluate(capture, region, 0) == null, "reset must rearm without notification");
                Pixels(capture, 98, green, dark);
                Check(full.Evaluate(capture, region, 0) != null, "a second cycle must notify");

                var threshold = new Trigger(); threshold.Kind = TriggerKind.PercentReached; threshold.PercentTarget = 50;
                Pixels(capture, 40, green, dark);
                Check(threshold.Evaluate(capture, region, 0) == null, "progress below target must not notify");
                Pixels(capture, 60, green, dark);
                Check(threshold.Evaluate(capture, region, 0) != null, "crossing the target must notify");
                Check(threshold.Evaluate(capture, region, 0) == null, "unchanged progress must not notify repeatedly");

                var change = new Trigger(); change.ChangeThreshold = 1;
                Check(change.Evaluate(capture, region, 0.5) == null, "small changes must be filtered");
                Check(change.Evaluate(capture, region, 2) != null, "significant changes must notify");

                var color = new Trigger(); color.Kind = TriggerKind.ColorChanged;
                Pixels(capture, 100, green, dark);
                Check(color.Evaluate(capture, region, 0) == null, "initial color must calibrate");
                Pixels(capture, 100, dark, dark);
                Check(color.Evaluate(capture, region, 0) != null, "color change must notify");
                Check(color.Evaluate(capture, region, 0) == null, "unchanged color must not notify");

                var quiet = new Trigger(); quiet.Kind = TriggerKind.WentQuiet; quiet.QuietSeconds = 0;
                Check(quiet.Evaluate(capture, region, 0) == null, "a static initial image must not finish");
                for (int i = 0; i < 3; i++) Check(quiet.Evaluate(capture, region, 2) == null, "active samples must not finish");
                Check(quiet.Evaluate(capture, region, 0) != null, "quiet after activity must finish");
                Check(quiet.Evaluate(capture, region, 0) == null, "quiet must notify only once");
            }
            Console.WriteLine("PASS: " + checks + " trigger regression checks (synthetic pixel input; no desktop capture or network).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
