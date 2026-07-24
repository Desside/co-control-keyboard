using System.Globalization;
using CoControl.Audio;
using CoControl.HAL;
using CoControl.Rgb;
using CoControl.Sensors;
using CoControl.Service;
using CoControl.State;

// cocontrol — CLI for the Aula F75 (VID 258A, PID 010C).
// Effects run until Ctrl+C (Direct Mode needs keepalive from the host).

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

string command = args[0].ToLowerInvariant();

// ---- connect ----
var path = DeviceEnumerator.FindVendorInterfacePath();
if (path == null)
{
    Console.Error.WriteLine("Aula F75 not found. Connect via USB-C (wired mode).");
    return 1;
}

using var device = new HidDevice();
device.Open(path);
using var shadow = new ShadowConfig();
using var engine = new ProtocolEngine(device, shadow);

using var ctrlC = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctrlC.Cancel(); };

try
{
    switch (command)
    {
        case "off":
        {
            await using var anim = new AnimationEngine(engine);
            anim.Play(new SolidColorAnimation(0, 0, 0));
            await Task.Delay(300);
            Console.WriteLine("Lights off (firmware effect resumes after a few seconds).");
            return 0;
        }

        case "color":
        {
            var (r, g, b) = ParseColor(RequireArg(args, 1, "color <hex|r,g,b>"));
            Console.WriteLine($"Solid RGB({r},{g},{b}) — Ctrl+C to stop.");
            await RunAnimation(new SolidColorAnimation(r, g, b));
            return 0;
        }

        case "pulse":
        {
            var (r, g, b) = ParseColor(RequireArg(args, 1, "pulse <hex|r,g,b> [periodSec]"));
            float period = args.Length > 2 ? ParseFloat(args[2]) : 2f;
            Console.WriteLine($"Pulse RGB({r},{g},{b}) period {period:0.##}s — Ctrl+C to stop.");
            await RunAnimation(new PulseAnimation(r, g, b, period));
            return 0;
        }

        case "rainbow":
        {
            float speed = args.Length > 1 ? ParseFloat(args[1]) : 0.5f;
            Console.WriteLine($"Rainbow stripes, speed {speed:0.##} — Ctrl+C to stop.");
            await RunAnimation(new RainbowWaveAnimation(speed, hueSpanPerKey: 1f / 16f));
            return 0;
        }

        case "temp":
        {
            float min = args.Length > 1 ? ParseFloat(args[1]) : 30f;
            float max = args.Length > 2 ? ParseFloat(args[2]) : 90f;
            Console.WriteLine($"CPU temp → color ({min:0}–{max:0} °C). Run elevated for sensor access. Ctrl+C to stop.");
            using var sensors = new LibreHardwareMonitorProvider();
            if (sensors.Read(SensorChannel.CpuTemperature) is null)
                Console.WriteLine("Warning: CPU temperature unavailable — keys will stay dark (try admin).");
            await RunAnimation(new SensorAnimation(
                sensors, SensorChannel.CpuTemperature, ColorGradient.Temperature(), min, max));
            return 0;
        }

        case "spectrum":
        {
            Console.WriteLine("Audio spectrum visualizer (system mix) — Ctrl+C to stop.");
            using var audio = new WasapiLoopbackSource();
            audio.Start();
            await RunAnimation(new AudioSpectrumAnimation(audio.Analyzer), fps: 60);
            audio.Stop();
            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintHelp();
            return 1;
    }
}
catch (OperationCanceledException)
{
    return 0;
}

async Task RunAnimation(IAnimation animation, int fps = 45)
{
    await using var anim = new AnimationEngine(engine, fps);
    anim.Play(animation);
    try
    {
        await Task.Delay(Timeout.Infinite, ctrlC.Token);
    }
    catch (OperationCanceledException) { /* Ctrl+C */ }
    if (anim.LastError != null)
        Console.Error.WriteLine($"Last device error: {anim.LastError.Message}");
    Console.WriteLine($"Stopped. Frames sent: {anim.FramesSent}");
}

static string RequireArg(string[] args, int index, string usage)
{
    if (args.Length <= index)
        throw new ArgumentException($"Usage: cocontrol {usage}");
    return args[index];
}

static float ParseFloat(string s) => float.Parse(s, CultureInfo.InvariantCulture);

static (byte R, byte G, byte B) ParseColor(string input)
{
    input = input.Trim().TrimStart('#');

    if (input.Contains(','))
    {
        var parts = input.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 3)
            return (byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
    }
    else if (input.Length == 6)
    {
        return (
            byte.Parse(input[..2], NumberStyles.HexNumber),
            byte.Parse(input[2..4], NumberStyles.HexNumber),
            byte.Parse(input[4..6], NumberStyles.HexNumber));
    }
    else
    {
        var named = input.ToLowerInvariant() switch
        {
            "red" => ((byte)255, (byte)0, (byte)0),
            "green" => ((byte)0, (byte)255, (byte)0),
            "blue" => ((byte)0, (byte)0, (byte)255),
            "white" => ((byte)255, (byte)255, (byte)255),
            "yellow" => ((byte)255, (byte)255, (byte)0),
            "cyan" => ((byte)0, (byte)255, (byte)255),
            "magenta" => ((byte)255, (byte)0, (byte)255),
            "orange" => ((byte)255, (byte)128, (byte)0),
            "purple" => ((byte)128, (byte)0, (byte)255),
            _ => default((byte, byte, byte)?),
        };
        if (named is { } c) return c;
    }

    throw new ArgumentException($"Cannot parse color '{input}'. Use hex (FF6600), r,g,b (255,102,0) or a name (red).");
}

static void PrintHelp()
{
    Console.WriteLine("""
        cocontrol — Aula F75 keyboard control

        Usage:
          cocontrol color <hex|r,g,b|name>     Solid color (e.g. FF6600, 255,102,0, red)
          cocontrol pulse <color> [periodSec]  Breathing effect (default period 2s)
          cocontrol rainbow [speed]            Rainbow stripes (default speed 0.5)
          cocontrol temp [min] [max]           CPU temp → green..red (default 30..90 °C, run as admin)
          cocontrol spectrum                   Audio visualizer from system audio mix
          cocontrol off                        Turn lights off

        Effects run until Ctrl+C (the keyboard needs a host keepalive in Direct Mode).
        """);
}
