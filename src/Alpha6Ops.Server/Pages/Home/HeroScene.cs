using System.Globalization;

namespace Alpha6Ops.Server.Pages.Home;

/// <summary>
/// Geometry for the landing hero: a night airfield in a 1600×900 SVG viewBox, looking down runway 19R from the
/// holding point. Computed once so the markup stays declarative and the perspective stays consistent.
/// </summary>
public static class HeroScene
{
    public const double Horizon = 560;
    private const double VanishX = 1060, Bottom = 900;

    public sealed record Light(string X, string Y, string R);

    private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    // Runway edges run from the vanishing point to the bottom of the frame.
    private static (double Left, double Right) RunwayAt(double y)
    {
        var t = (y - Horizon) / (Bottom - Horizon);
        return (VanishX - 26 - 430 * t, VanishX + 26 + 460 * t);
    }

    // Perspective spacing: equal steps along the ground get closer together towards the horizon.
    private static IEnumerable<double> Depths(int count, double near = Bottom - 6) =>
        Enumerable.Range(0, count).Select(i => Horizon + 4 + (near - Horizon - 4) / Math.Pow(1.32, i));

    public static string Runway
    {
        get
        {
            var (l0, r0) = RunwayAt(Horizon); var (l1, r1) = RunwayAt(Bottom);
            return $"M{N(l0)} {N(Horizon)} H{N(r0)} L{N(r1)} {N(Bottom)} H{N(l1)} Z";
        }
    }

    public static IReadOnlyList<Light> RunwayEdgeLights { get; } = Depths(14)
        .SelectMany(y => { var (l, r) = RunwayAt(y); var size = 0.6 + 3.2 * (y - Horizon) / (Bottom - Horizon); return new[] { new Light(N(l), N(y), N(size)), new Light(N(r), N(y), N(size)) }; })
        .ToArray();

    // Centre-line stripes as quads, each a fixed share of the remaining distance.
    public static string CentreLine => string.Concat(Depths(9).Select(y =>
    {
        var y2 = Horizon + (y - Horizon) * 0.82;
        var (l, r) = RunwayAt(y); var (l2, r2) = RunwayAt(y2);
        double c = (l + r) / 2, c2 = (l2 + r2) / 2, w = (r - l) * 0.008, w2 = (r2 - l2) * 0.008;
        return $"M{N(c - w)} {N(y)} L{N(c2 - w2)} {N(y2)} L{N(c2 + w2)} {N(y2)} L{N(c + w)} {N(y)} Z ";
    }));

    // Threshold "piano keys" at the near end.
    public static string Threshold
    {
        get
        {
            double y = 876, y2 = 846; var (l, r) = RunwayAt(y); var (l2, r2) = RunwayAt(y2);
            return string.Concat(Enumerable.Range(0, 8).Select(i =>
            {
                double a = 0.06 + i * 0.112, b = a + 0.07;
                double x1 = l + (r - l) * a, x2 = l + (r - l) * b, x3 = l2 + (r2 - l2) * b, x4 = l2 + (r2 - l2) * a;
                return $"M{N(x1)} {N(y)} L{N(x2)} {N(y)} L{N(x3)} {N(y2)} L{N(x4)} {N(y2)} Z ";
            }));
        }
    }

    // Taxiway: a quadratic curve from the right foreground onto the runway's right edge.
    private static readonly (double X, double Y) T0 = (1660, 900), T1 = (1470, 724);
    private static (double X, double Y) T2 => (RunwayAt(650).Right - 6, 650);
    private static (double X, double Y, double Nx, double Ny, double Half) TaxiAt(double t)
    {
        var u = 1 - t;
        double x = u * u * T0.X + 2 * u * t * T1.X + t * t * T2.X, y = u * u * T0.Y + 2 * u * t * T1.Y + t * t * T2.Y;
        double dx = 2 * u * (T1.X - T0.X) + 2 * t * (T2.X - T1.X), dy = 2 * u * (T1.Y - T0.Y) + 2 * t * (T2.Y - T1.Y);
        var len = Math.Sqrt(dx * dx + dy * dy);
        return (x, y, -dy / len, dx / len, 70 - 58 * t);
    }

    public static string Taxiway
    {
        get
        {
            var steps = Enumerable.Range(0, 25).Select(i => TaxiAt(i / 24.0)).ToArray();
            var left = steps.Select(s => $"{N(s.X + s.Nx * s.Half)} {N(s.Y + s.Ny * s.Half)}");
            var right = steps.Reverse().Select(s => $"{N(s.X - s.Nx * s.Half)} {N(s.Y - s.Ny * s.Half)}");
            return "M" + string.Join(" L", left.Concat(right)) + " Z";
        }
    }

    public static string TaxiCentre => $"M{N(T0.X)} {N(T0.Y)} Q{N(T1.X)} {N(T1.Y)} {N(T2.X)} {N(T2.Y)}";

    // Blue edge lights on both sides; green centre-line lights ("follow the greens") lit in sequence by CSS.
    public static IReadOnlyList<Light> TaxiEdgeLights { get; } = Enumerable.Range(0, 11).Select(i => TaxiAt(1 - Math.Pow(1 - i / 11.0, 1.4)))
        .SelectMany(s => new[] { new Light(N(s.X + s.Nx * s.Half), N(s.Y + s.Ny * s.Half), N(1 + s.Half / 26)), new Light(N(s.X - s.Nx * s.Half), N(s.Y - s.Ny * s.Half), N(1 + s.Half / 26)) })
        .ToArray();

    public static IReadOnlyList<Light> TaxiCentreLights { get; } = Enumerable.Range(0, 12).Select(i => TaxiAt(0.04 + i * 0.08))
        .Select(s => new Light(N(s.X), N(s.Y), N(0.8 + s.Half / 30))).ToArray();

    // Terminal and apron lights along the horizon.
    public static IReadOnlyList<Light> TerminalLights { get; } = Enumerable.Range(0, 34)
        .Select(i => new Light(N(760 + i * 15.5), N(i % 4 == 0 ? Horizon - 22 : Horizon - 10), "1.3")).ToArray();

    // The sample route, KMKE to KORD, as an arc above the field with the aircraft 62% along it.
    private static readonly (double X, double Y) R0 = (1010, 410), R1 = (1250, 280), R2 = (1530, 430);
    public static string Route => $"M{N(R0.X)} {N(R0.Y)} Q{N(R1.X)} {N(R1.Y)} {N(R2.X)} {N(R2.Y)}";
    public const double Progress = 0.62;
    public static (string X, string Y, string Angle) Aircraft
    {
        get
        {
            const double t = Progress; var u = 1 - t;
            double x = u * u * R0.X + 2 * u * t * R1.X + t * t * R2.X, y = u * u * R0.Y + 2 * u * t * R1.Y + t * t * R2.Y;
            double dx = 2 * u * (R1.X - R0.X) + 2 * t * (R2.X - R1.X), dy = 2 * u * (R1.Y - R0.Y) + 2 * t * (R2.Y - R1.Y);
            return (N(x), N(y), N(Math.Atan2(dy, dx) * 180 / Math.PI + 90));
        }
    }
    public static (string X, string Y) Origin => (N(R0.X), N(R0.Y));
    public static (string X, string Y) Destination => (N(R2.X), N(R2.Y));
}
