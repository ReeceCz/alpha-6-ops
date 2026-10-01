using System.Globalization;

namespace Alpha6Ops.Server.Pages.Home;

/// <summary>
/// The landing hero: General Mitchell International (KMKE) at night, seen from above the runway 19R threshold
/// looking south, the runway KMKE prefers for night departures. Everything is placed from real coordinates
/// (OpenStreetMap; runway data from the FAA via AirNav, October 2026) and projected through one perspective camera
/// into a 1600×900 SVG viewBox.
///
/// Deliberate liberties, for legibility at hero size: building heights are drawn 3× true, the holding-position
/// sign 6× true, and the KMKE → KORD route is a schematic overlay rather than a 3D flight path.
/// </summary>
public static class HeroScene
{
    public const double Horizon = 560;
    public sealed record Light(string X, string Y, string R, string Kind);
    public sealed record Shape(string Path, string Kind);
    public sealed record Glyph(string Text, string Matrix);

    private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    // Local metres around the 19R end, then runway axes: A along 19R (south), B to the right of it (west).
    private const double Lat0 = 42.9576936, Lon0 = -87.8929972, MPerLat = 111132, MPerLon = 81463;
    private static readonly (double E, double N) U, R;
    public static readonly double RunwayLength;

    // Camera: 26 m above the runway centre line, 200 m past the runway end, turned 14° right towards the terminal.
    private const double CamA = 200, Eye = 26, Focal = 640, CentreX = 1200, Near = 30;
    private static readonly double Cos = Math.Cos(14 * Math.PI / 180), Sin = Math.Sin(14 * Math.PI / 180);

    // Other runways and taxiways (OpenStreetMap). Widths in metres: 150 ft runways, 100 ft 7L/25R, 75 ft taxiways.
    private static readonly (string Name, (double Lat, double Lon)[] Line, double Width, bool Lit)[] OtherRunways =
    [
        ("13/31", [(42.9581215, -87.9033938), (42.9479182, -87.8881020)], 45.7, true),
        ("7R/25L", [(42.9390912, -87.9177192), (42.9462554, -87.8883270)], 45.7, true),
        ("7L/25R", [(42.9527235, -87.9054067), (42.9568989, -87.8883009)], 30.5, true),
        ("1R/19L", [(42.9394191, -87.8923605), (42.9507520, -87.8904280)], 45.7, false), // closed except taxiing: unlit
    ];
    private static readonly (string Name, (double Lat, double Lon)[] Line)[] Taxiways =
    [
        ("E", [(42.9574338, -87.8940495), (42.9458398, -87.8965338)]),
        ("T", [(42.9458398, -87.8965338), (42.9403489, -87.8988472)]),
        ("Z", [(42.9574320, -87.8954240), (42.9557125, -87.8948478)]),
        ("V", [(42.9527743, -87.9051905), (42.9537022, -87.8936834)]),
    ];

    // Terminal buildings and the tower: centre, east–west and north–south size in metres, true height in metres.
    private static readonly (string Name, double Lat, double Lon, double SizeE, double SizeN, double Height)[] Buildings =
    [
        ("International Arrivals", 42.9506946, -87.9039399, 120, 70, 12),
        ("Main Terminal", 42.9478237, -87.9020936, 120, 280, 16),
        ("Concourse C", 42.9497219, -87.9009980, 300, 40, 13),
        ("Concourse D", 42.9477915, -87.9000827, 330, 45, 13),
    ];
    private const double Exaggerate = 3;
    private static readonly (double Lat, double Lon, double Height) Tower = (42.9477920, -87.9063680, 61); // 200 ft

    public static string Runway { get; private set; } = "";
    public static IReadOnlyList<Shape> Pavement { get; private set; } = [];
    public static IReadOnlyList<Shape> Markings { get; private set; } = [];
    public static IReadOnlyList<Glyph> Designator { get; private set; } = [];
    public static IReadOnlyList<Light> Lights { get; private set; } = [];
    public static IReadOnlyList<Light> Greens { get; private set; } = [];
    public static IReadOnlyList<Shape> Structures { get; private set; } = [];
    public static IReadOnlyList<Light> Windows { get; private set; } = [];
    public static IReadOnlyList<Light> Beacons { get; private set; } = [];
    public static IReadOnlyList<Light> Distant { get; private set; } = [];
    /// <summary>Apron masts: X/Y are the lamp head, R the glow radius, Kind the foot's Y.</summary>
    public static IReadOnlyList<Light> Masts { get; private set; } = [];
    public static (string X, string Y, string Scale) Sign { get; private set; }

    static HeroScene()
    {
        double e = (-87.8976527 - Lon0) * MPerLon, n = (42.9304965 - Lat0) * MPerLat; // the 1L end
        RunwayLength = Math.Sqrt(e * e + n * n);
        U = (e / RunwayLength, n / RunwayLength);
        R = (U.N, -U.E);
        Build();
    }

    private static (double A, double B) Local(double lat, double lon)
    {
        double e = (lon - Lon0) * MPerLon, n = (lat - Lat0) * MPerLat;
        return (e * U.E + n * U.N, e * R.E + n * R.N);
    }
    private static (double F, double R) Cam(double a, double b) { var da = a - CamA; return (da * Cos + b * Sin, b * Cos - da * Sin); }
    private static (double X, double Y) Project(double a, double b, double h = 0)
    {
        var (f, r) = Cam(a, b);
        return (CentreX + Focal * r / f, Horizon + Focal * (Eye - h) / f);
    }

    // A ground polygon in runway coordinates, clipped to what is in front of the camera, as SVG path data.
    private static string Ground(IReadOnlyList<(double A, double B)> poly)
    {
        var clipped = new List<(double A, double B)>();
        for (var i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            double fp = Cam(p.A, p.B).F, fq = Cam(q.A, q.B).F;
            if (fp >= Near) clipped.Add(p);
            if ((fp >= Near) != (fq >= Near)) { var t = (Near - fp) / (fq - fp); clipped.Add((p.A + t * (q.A - p.A), p.B + t * (q.B - p.B))); }
        }
        if (clipped.Count < 3) return "";
        return "M" + string.Join(" L", clipped.Select(p => { var (x, y) = Project(p.A, p.B); return $"{N(x)} {N(y)}"; })) + " Z";
    }
    private static (double A, double B)[] Rect(double a1, double b1, double a2, double b2) => [(a1, b1), (a1, b2), (a2, b2), (a2, b1)];
    private static (double A, double B)[] Ribbon((double A, double B) p, (double A, double B) q, double width)
    {
        double da = q.A - p.A, db = q.B - p.B, len = Math.Sqrt(da * da + db * db), oa = -db / len * width / 2, ob = da / len * width / 2;
        return [(p.A + oa, p.B + ob), (q.A + oa, q.B + ob), (q.A - oa, q.B - ob), (p.A - oa, p.B - ob)];
    }
    private static Light Lamp(double a, double b, string kind, double h = 0)
    {
        var (f, _) = Cam(a, b); var (x, y) = Project(a, b, h);
        return new(N(x), N(y), N(Math.Clamp(150 / f, 0.5, 4.5)), kind);
    }
    private static bool Visible(double a, double b) { var (f, r) = Cam(a, b); return f >= Near && Math.Abs(CentreX + Focal * r / f - 800) < 900; }

    private static void Build()
    {
        const double half = 30.5; // 200 ft runway
        var L = RunwayLength;
        var pavement = new List<Shape>();
        var lights = new List<Light>();

        foreach (var (_, line, width, lit) in OtherRunways)
        {
            var p = Local(line[0].Lat, line[0].Lon); var q = Local(line[1].Lat, line[1].Lon);
            pavement.Add(new(Ground(Ribbon(p, q, width)), lit ? "other-runway" : "closed-runway"));
            if (!lit) continue;
            var len = Math.Sqrt(Math.Pow(q.A - p.A, 2) + Math.Pow(q.B - p.B, 2));
            for (var s = 0.0; s <= len; s += 61)
                foreach (var side in new[] { -1, 1 })
                {
                    double t = s / len, a = p.A + t * (q.A - p.A) - (q.B - p.B) / len * width / 2 * side, b = p.B + t * (q.B - p.B) + (q.A - p.A) / len * width / 2 * side;
                    var onMainRunway = Math.Abs(b) <= half + 2 && a >= 0 && a <= L;
                    if (!onMainRunway && Visible(a, b)) lights.Add(Lamp(a, b, "edge"));
                }
        }
        var greens = new List<Light>();
        foreach (var (name, line) in Taxiways)
        {
            var p = Local(line[0].Lat, line[0].Lon); var q = Local(line[1].Lat, line[1].Lon);
            pavement.Add(new(Ground(Ribbon(p, q, 22.9)), "taxiway"));
            pavement.Add(new(Ground(Ribbon(p, q, 0.6)), "taxi-line"));
            var len = Math.Sqrt(Math.Pow(q.A - p.A, 2) + Math.Pow(q.B - p.B, 2));
            for (var s = 0.0; s <= len; s += 50)
            {
                double t = s / len, a = p.A + t * (q.A - p.A), b = p.B + t * (q.B - p.B), oa = -(q.B - p.B) / len * 12.5, ob = (q.A - p.A) / len * 12.5;
                foreach (var side in new[] { -1, 1 }) if (Visible(a + oa * side, b + ob * side)) lights.Add(Lamp(a + oa * side, b + ob * side, "taxi-edge"));
                if (name == "E" && Visible(a, b)) greens.Add(Lamp(a, b, "green"));
            }
        }
        Pavement = pavement;
        Runway = Ground(Rect(0, -half, L, half));

        // Markings for a 200 ft precision runway with a 785 ft displaced threshold (19R).
        var marks = new List<Shape>();
        void Mark((double A, double B)[] poly) { var d = Ground(poly); if (d.Length > 0) marks.Add(new(d, "paint")); }
        const double threshold = 239.3; // 785 ft
        Mark(Rect(threshold, -half, threshold + 3, half));                                    // threshold bar
        foreach (var b in new[] { -21.0, -7.0, 7.0, 21.0 })                                    // arrowheads before it
            Mark([(threshold - 22, b - 4), (threshold - 6, b), (threshold - 22, b + 4), (threshold - 18, b)]);
        for (var k = 0; k < 8; k++)                                                             // 16 threshold stripes
            foreach (var side in new[] { -1, 1 }) Mark(Rect(threshold + 6, side * (3.5 + k * 3.5), threshold + 52, side * (5.25 + k * 3.5)));
        foreach (var side in new[] { -1, 1 }) Mark(Rect(0, side * (half - 0.9), L, side * half)); // side stripes
        double aim = threshold + 304.8;
        foreach (var (at, bars) in new[] { (threshold + 152.4, 3), (threshold + 457.2, 2), (threshold + 609.6, 2), (threshold + 762, 1), (threshold + 914.4, 1) })
            for (var k = 0; k < bars; k++)
                foreach (var side in new[] { -1, 1 }) Mark(Rect(at, side * (10.7 + k * 3.4), at + 22.9, side * (12.5 + k * 3.4))); // touchdown zone
        foreach (var side in new[] { -1, 1 }) Mark(Rect(aim, side * 10.7, aim + 45.7, side * 19.8)); // aiming points
        for (var a = threshold + 115; a < L - 120; a += 61) Mark(Rect(a, -0.45, a + 36.6, 0.45)); // centre line
        Markings = marks;

        // "19" over "R": the letter is painted nearest the threshold. Each block is mapped to its perspective footprint.
        Glyph Paint(string text, double a1, double a2, double b1, double b2)
        {
            var fl = Project(a2, b1); var fr = Project(a2, b2); var nl = Project(a1, b1);
            return new(text, $"matrix({N4((fr.X - fl.X) / 100)} {N4((fr.Y - fl.Y) / 100)} {N4((nl.X - fl.X) / 100)} {N4((nl.Y - fl.Y) / 100)} {N(fl.X)} {N(fl.Y)})");
        }
        Designator = [Paint("R", threshold + 64, threshold + 82, -3.5, 3.5), Paint("19", threshold + 88, threshold + 106, -8, 8)];

        // Runway lights as a departing pilot sees them: white edges turning yellow for the last 2,000 ft, centre line
        // turning red/white for the last 3,000 ft and red for the last 1,000 ft, red end lights. 19R has no TDZ lights.
        for (var a = 0.0; a <= L; a += 61)
            foreach (var side in new[] { -1, 1 }) if (Visible(a, side * half)) lights.Add(Lamp(a, side * half, L - a < 610 ? "edge-caution" : "edge"));
        for (var a = threshold + 6; a <= L; a += 30.5)
            if (Visible(a, 0)) lights.Add(Lamp(a, 0, L - a < 305 ? "centre-red" : L - a < 914 ? ((int)(a / 30.5) % 2 == 0 ? "centre-red" : "centre") : "centre"));
        for (var b = -half; b <= half; b += 8.7) lights.Add(Lamp(L, b, "end"));
        Lights = lights;
        Greens = greens.Take(14).ToArray();

        // Buildings west of the runway: convex hull of each footprint extruded to its (exaggerated) height.
        var structures = new List<Shape>(); var windows = new List<Light>(); var beacons = new List<Light>();
        foreach (var (_, lat, lon, sizeE, sizeN, height) in Buildings)
        {
            var corners = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(c =>
            {
                double ce = (lon - Lon0) * MPerLon + c.Item1 * sizeE / 2, cn = (lat - Lat0) * MPerLat + c.Item2 * sizeN / 2;
                return (A: ce * U.E + cn * U.N, B: ce * R.E + cn * R.N);
            }).ToArray();
            var h = height * Exaggerate;
            var pts = corners.SelectMany(c => new[] { Project(c.A, c.B), Project(c.A, c.B, h) }).ToList();
            structures.Add(new(Hull(pts), "building"));
            var (minX, maxX) = (pts.Min(p => p.X), pts.Max(p => p.X));
            var nearest = corners.OrderBy(c => Cam(c.A, c.B).F).First();
            double baseY = Project(nearest.A, nearest.B).Y, topY = Project(nearest.A, nearest.B, h).Y;
            for (var x = minX + 4; x < maxX - 3; x += 7) windows.Add(new(N(x), N(baseY - (baseY - topY) * 0.45), "1.1", "window"));
            beacons.Add(new(N((minX + maxX) / 2), N(pts.Min(p => p.Y) - 2), "2", "obstruction"));
        }
        var (ta, tb) = Local(Tower.Lat, Tower.Lon);
        double th = Tower.Height * Exaggerate, shaft = 10, cab = 22; // widths doubled with the height, so it reads as a tower
        var bottomL = Project(ta, tb - shaft); var bottomR = Project(ta, tb + shaft);
        var cabBottomL = Project(ta, tb - shaft, th * 0.82); var cabBottomR = Project(ta, tb + shaft, th * 0.82);
        var cabL = Project(ta, tb - cab, th * 0.86); var cabR = Project(ta, tb + cab, th * 0.86);
        var topL = Project(ta, tb - cab * 0.8, th); var topR = Project(ta, tb + cab * 0.8, th);
        structures.Add(new($"M{N(bottomL.X)} {N(bottomL.Y)} L{N(cabBottomL.X)} {N(cabBottomL.Y)} L{N(cabL.X)} {N(cabL.Y)} L{N(topL.X)} {N(topL.Y)} L{N(topR.X)} {N(topR.Y)} L{N(cabR.X)} {N(cabR.Y)} L{N(cabBottomR.X)} {N(cabBottomR.Y)} L{N(bottomR.X)} {N(bottomR.Y)} Z", "tower"));
        var cabMid = Project(ta, tb, th * 0.93);
        structures.Add(new($"M{N(cabL.X + 2)} {N(cabMid.Y - 3)} H{N(cabR.X - 2)} V{N(cabMid.Y + 3)} H{N(cabL.X + 2)} Z", "tower-cab"));
        var towerTop = Project(ta, tb, th + 2);
        beacons.Add(new(N(towerTop.X), N(towerTop.Y), "2.6", "obstruction"));
        // High-mast apron floodlights east of Concourses C and D, between the gates and Taxiway E.
        var masts = new List<Light>();
        foreach (var (lat, lon) in new[] { (42.9508, -87.8988), (42.9493, -87.8984), (42.9478, -87.8980), (42.9463, -87.8984) })
        {
            var (ma, mb) = Local(lat, lon);
            var foot = Project(ma, mb); var top = Project(ma, mb, 30 * Exaggerate);
            masts.Add(new(N(top.X), N(top.Y), N(Math.Clamp(9000 / Cam(ma, mb).F, 4, 14)), N(foot.Y)));
        }
        Masts = masts;
        Structures = structures; Windows = windows; Beacons = beacons;

        // Suburbs to the south and south-west on the horizon; to the east (left) lies Lake Michigan, so no lights.
        var distant = new List<Light>();
        var seed = 7u;
        double Next() { seed = seed * 1664525 + 1013904223; return (seed >> 8) / 16777216.0; }
        for (var i = 0; i < 70; i++)
        {
            var x = 1000 + Next() * 600; var y = Horizon - 1 + Next() * 5;
            distant.Add(new(N(x), N(y), N(0.6 + Next() * 0.7), Next() < 0.7 ? "warm" : "cool"));
        }
        Distant = distant;

        // The holding-position sign array at the 19R end of Taxiway E, between the runway and the taxiway.
        var (sx, sy) = Project(330, 55);
        var signScale = Focal * 3.0 * 6 / Cam(330, 55).F / 120; // a 3 m wide array, drawn 6× true, in a 120-unit design
        Sign = (N(sx), N(sy), N4(signScale));
    }

    private static string N4(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // Monotone-chain convex hull, for building silhouettes.
    private static string Hull(List<(double X, double Y)> points)
    {
        var p = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<(double X, double Y)>();
        foreach (var pass in new[] { p, Enumerable.Reverse(p).ToList() })
        {
            var start = hull.Count;
            foreach (var q in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], q) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return "M" + string.Join(" L", hull.Select(q => $"{N(q.X)} {N(q.Y)}")) + " Z";
    }

    // The sample route, KMKE to KORD, as a schematic arc above the field with the aircraft 62% along it.
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
