using System.Globalization;

namespace Alpha6Ops.Server.Pages.Home;

/// <summary>
/// The landing hero: General Mitchell International (KMKE) at night, seen from above the runway 19R threshold
/// looking south, the runway KMKE prefers for night departures. Down the runway, at the far (1L) end, the ALSF-2
/// approach lights and their sequenced flashers; to the right, the terminal and tower; to the left, towards Lake
/// Michigan, darkness.
///
/// Placed from real coordinates (OpenStreetMap: runways, taxiways, terminal outlines, gates; FAA runway and lighting
/// data via AirNav, October 2026) and projected through one perspective camera into a 1600×900 SVG viewBox.
/// Deliberate liberties, for legibility at hero size: the terminal is drawn 4× true height, aircraft 2.5×, the
/// tower 3× (and 2× wide), the holding-position sign 6×. On the page it is dimmed and desaturated as a background. On the page it is dimmed and desaturated as a background.
/// </summary>
public static class HeroScene
{
    public const double Horizon = 560;
    public sealed record Light(string X, string Y, string R, string Kind);
    public sealed record Shape(string Path, string Kind);
    public sealed record Stroke(string X1, string Y1, string X2, string Y2, string Width, string Kind);
    public sealed record Glyph(string Text, string Matrix);

    private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string N4(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // Local metres around the 19R end; A runs along the runway towards 1L (south), B to its west.
    private const double Lat0 = 42.9576936, Lon0 = -87.8929972, MPerLat = 111132, MPerLon = 81463;
    private static readonly (double E, double N) U, R;
    private static readonly double L; // runway length, 9,990 ft

    // Camera: 26 m above the runway centre line, 200 m past the 19R end, turned 14° right towards the terminal.
    private const double Eye = 26, Focal = 640, CentreX = 1200, Near = 30;
    private static readonly (double A, double B) CamPos = (200, 0), Fwd, Right;

    private const double Half = 30.5;          // 200 ft runway
    private const double Threshold19R = 239.3; // displaced 785 ft
    private static readonly double Threshold1L; // displaced 300 ft from the 1L end

    public static string Runway { get; private set; } = "";
    public static string Apron { get; private set; } = "";
    public static IReadOnlyList<Shape> Pavement { get; private set; } = [];
    public static IReadOnlyList<Shape> Markings { get; private set; } = [];
    public static IReadOnlyList<Glyph> Designator { get; private set; } = [];
    public static IReadOnlyList<Light> Lights { get; private set; } = [];
    public static IReadOnlyList<Light> Rabbit { get; private set; } = [];
    public static IReadOnlyList<Light> Greens { get; private set; } = [];
    public static IReadOnlyList<Light> City { get; private set; } = [];
    public static IReadOnlyList<Shape> Walls { get; private set; } = [];
    public static IReadOnlyList<Stroke> Aircraft { get; private set; } = [];
    public static IReadOnlyList<Light> AircraftLights { get; private set; } = [];
    /// <summary>Apron masts: X/Y are the lamp head, R the glow radius, Kind the foot's Y.</summary>
    public static IReadOnlyList<Light> Masts { get; private set; } = [];
    public static IReadOnlyList<Light> Beacons { get; private set; } = [];
    public static (string X, string Y, string Scale) Sign { get; private set; }

    static HeroScene()
    {
        double e = (-87.8976527 - Lon0) * MPerLon, n = (42.9304965 - Lat0) * MPerLat; // the 1L end
        L = Math.Sqrt(e * e + n * n);
        U = (e / L, n / L);
        R = (U.N, -U.E);
        Threshold1L = L - 91.4;
        var yaw = 14 * Math.PI / 180;
        Fwd = (Math.Cos(yaw), Math.Sin(yaw));
        Right = (-Fwd.B, Fwd.A);
        Build();
    }

    private static (double A, double B) Local(double lat, double lon)
    {
        double e = (lon - Lon0) * MPerLon, n = (lat - Lat0) * MPerLat;
        return (e * U.E + n * U.N, e * R.E + n * R.N);
    }
    private static (double F, double R) Cam(double a, double b)
    {
        double da = a - CamPos.A, db = b - CamPos.B;
        return (da * Fwd.A + db * Fwd.B, da * Right.A + db * Right.B);
    }
    private static (double X, double Y) Project(double a, double b, double h = 0)
    {
        var (f, r) = Cam(a, b);
        return (CentreX + Focal * r / f, Horizon + Focal * (Eye - h) / f);
    }
    private static bool Visible(double a, double b, double h = 0)
    {
        if (Cam(a, b).F < Near) return false;
        var (x, y) = Project(a, b, h);
        return x > -100 && x < 1700 && y < 960;
    }
    // Lamp radius follows distance, never below minRadius: the brightest systems (approach lights, flashers, PAPI)
    // stay visible as points at any range, as they do at night.
    private static Light Lamp(double a, double b, string kind, double h = 0, double size = 1, double minRadius = 0.5)
    {
        var (f, _) = Cam(a, b); var (x, y) = Project(a, b, h);
        return new(N(x), N(y), N(Math.Clamp(size * 150 / f, minRadius, 4.5)), kind);
    }

    // A polygon at height h, clipped to what is in front of the camera, as SVG path data.
    private static string Ground(IReadOnlyList<(double A, double B)> poly, double h = 0)
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
        return "M" + string.Join(" L", clipped.Select(p => { var (x, y) = Project(p.A, p.B, h); return $"{N(x)} {N(y)}"; })) + " Z";
    }
    private static string Quad(IEnumerable<(double X, double Y)> pts) => "M" + string.Join(" L", pts.Select(p => $"{N(p.X)} {N(p.Y)}")) + " Z";
    private static (double A, double B)[] Rect(double a1, double b1, double a2, double b2) => [(a1, b1), (a1, b2), (a2, b2), (a2, b1)];
    private static (double A, double B)[] Ribbon((double A, double B) p, (double A, double B) q, double width)
    {
        double da = q.A - p.A, db = q.B - p.B, len = Math.Sqrt(da * da + db * db), oa = -db / len * width / 2, ob = da / len * width / 2;
        return [(p.A + oa, p.B + ob), (q.A + oa, q.B + ob), (q.A - oa, q.B - ob), (p.A - oa, p.B - ob)];
    }
    private static double Dist((double A, double B) p, (double A, double B) q) => Math.Sqrt(Math.Pow(p.A - q.A, 2) + Math.Pow(p.B - q.B, 2));
    private static (double A, double B) Nearest((double A, double B) g, (double A, double B) p, (double A, double B) q)
    {
        double da = q.A - p.A, db = q.B - p.B, len2 = da * da + db * db;
        var t = len2 == 0 ? 0 : Math.Clamp(((g.A - p.A) * da + (g.B - p.B) * db) / len2, 0, 1);
        return (p.A + t * da, p.B + t * db);
    }

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

    // Terminal outlines (OpenStreetMap ways 53759954, 543449387, 53333086, 543449389), simplified, true heights.
    private static readonly (string Name, double Height, (double Lat, double Lon)[] Outline)[] Terminal =
    [
        ("International Arrivals", 11, [(42.9509715, -87.9039740), (42.9509053, -87.9036269), (42.9508636, -87.9037183), (42.9506586, -87.9037760), (42.9505113, -87.9037223), (42.9504587, -87.9038352), (42.9504176, -87.9040330), (42.9505123, -87.9041987), (42.9507960, -87.9042145), (42.9509158, -87.9040838)]),
        ("Main Terminal", 16, [(42.9487501, -87.9010657), (42.9488096, -87.9014333), (42.9486565, -87.9019136), (42.9485622, -87.9020428), (42.9486722, -87.9024552), (42.9485533, -87.9027128), (42.9474668, -87.9030282), (42.9471478, -87.9031215), (42.9468378, -87.9027558), (42.9472349, -87.9025460), (42.9471568, -87.9020251), (42.9470657, -87.9017348), (42.9476792, -87.9015632), (42.9479617, -87.9016365), (42.9479788, -87.9012930), (42.9484703, -87.9011482)]),
        ("Concourse C", 12, [(42.9489868, -87.9020370), (42.9496615, -87.9015625), (42.9504404, -87.9007395), (42.9506231, -87.9009960), (42.9508816, -87.9007610), (42.9507352, -87.9005734), (42.9505840, -87.9003358), (42.9504633, -87.9001012), (42.9503573, -87.8998411), (42.9501266, -87.9001213), (42.9503055, -87.9005101), (42.9497118, -87.9011453), (42.9492970, -87.9015892), (42.9489891, -87.9019179), (42.9485622, -87.9020428)]),
        ("Concourse D", 12, [(42.9476792, -87.9015632), (42.9479617, -87.9016365), (42.9479005, -87.9008913), (42.9477583, -87.8999822), (42.9477147, -87.8992187), (42.9478678, -87.8989114), (42.9486882, -87.8987643), (42.9487798, -87.8986667), (42.9487670, -87.8985121), (42.9486589, -87.8984548), (42.9476384, -87.8986387), (42.9474333, -87.8986076), (42.9468953, -87.8987531), (42.9467954, -87.8989314), (42.9468629, -87.8990547), (42.9471421, -87.8990283), (42.9474500, -87.8993045), (42.9475731, -87.9000583), (42.9476558, -87.9006090), (42.9477583, -87.9012522), (42.9476511, -87.9013770)]),
    ];
    // Gates with an aircraft parked nose-in (OpenStreetMap gate nodes), on the north and east faces this view sees.
    private static readonly (string Ref, double Lat, double Lon)[] OccupiedGates =
    [
        ("C12", 42.9498307, -87.9014928), ("C14", 42.9501376, -87.9011677), ("C18", 42.9506845, -87.9010127), ("C20", 42.9508436, -87.9006470),
        ("C22", 42.9505238, -87.9001453), ("C24", 42.9502837, -87.8998648), ("D36", 42.9478318, -87.9001615), ("D38", 42.9477875, -87.8996426),
        ("D42", 42.9481424, -87.8989360), ("D44", 42.9484519, -87.8988993), ("D41", 42.9480215, -87.8984972), ("D45", 42.9486158, -87.8983944),
    ];
    private const double TerminalScale = 4, AircraftScale = 2.5;
    private static readonly (double Lat, double Lon, double Height) Tower = (42.9477920, -87.9063680, 61); // 200 ft

    private static void Build()
    {
        var pavement = new List<Shape>();
        var lights = new List<Light>();

        Apron = Ground(new[] { (42.9513, -87.9046), (42.9513, -87.8978), (42.9462, -87.8976), (42.9462, -87.9034) }.Select(p => Local(p.Item1, p.Item2)).ToArray());

        foreach (var (_, line, width, lit) in OtherRunways)
        {
            var p = Local(line[0].Lat, line[0].Lon); var q = Local(line[1].Lat, line[1].Lon);
            pavement.Add(new(Ground(Ribbon(p, q, width)), lit ? "other-runway" : "closed-runway"));
            if (!lit) continue;
            var len = Dist(p, q);
            for (var s = 0.0; s <= len; s += 61)
                foreach (var side in new[] { -1, 1 })
                {
                    double t = s / len, a = p.A + t * (q.A - p.A) - (q.B - p.B) / len * width / 2 * side, b = p.B + t * (q.B - p.B) + (q.A - p.A) / len * width / 2 * side;
                    if (!(Math.Abs(b) <= Half + 2 && a >= 0 && a <= L) && Visible(a, b)) lights.Add(Lamp(a, b, "edge"));
                }
        }
        var greens = new List<Light>();
        foreach (var (name, line) in Taxiways)
        {
            var p = Local(line[0].Lat, line[0].Lon); var q = Local(line[1].Lat, line[1].Lon);
            pavement.Add(new(Ground(Ribbon(p, q, 22.9)), "taxiway"));
            pavement.Add(new(Ground(Ribbon(p, q, 0.6)), "taxi-line"));
            var len = Dist(p, q);
            for (var s = 0.0; s <= len; s += 50)
            {
                double t = s / len, a = p.A + t * (q.A - p.A), b = p.B + t * (q.B - p.B), oa = -(q.B - p.B) / len * 12.5, ob = (q.A - p.A) / len * 12.5;
                foreach (var side in new[] { -1, 1 }) if (Visible(a + oa * side, b + ob * side)) lights.Add(Lamp(a + oa * side, b + ob * side, "taxi-edge"));
                if (name == "E" && Visible(a, b)) greens.Add(Lamp(a, b, "green"));
            }
        }
        Pavement = pavement;
        Greens = greens.Take(14).ToArray();
        Runway = Ground(Rect(0, -Half, L, Half));

        // 19R markings: displaced threshold arrowheads and bar, 16 stripes, side stripes, touchdown zone, aiming points.
        var marks = new List<Shape>();
        void Mark((double A, double B)[] poly) { var d = Ground(poly); if (d.Length > 0) marks.Add(new(d, "paint")); }
        const double t0 = Threshold19R;
        Mark(Rect(t0, -Half, t0 + 3, Half));
        foreach (var b in new[] { -21.0, -7.0, 7.0, 21.0 })
            Mark([(t0 - 22, b - 4), (t0 - 6, b), (t0 - 22, b + 4), (t0 - 18, b)]);
        for (var k = 0; k < 8; k++)
            foreach (var side in new[] { -1, 1 }) Mark(Rect(t0 + 6, side * (3.5 + k * 3.5), t0 + 52, side * (5.25 + k * 3.5)));
        foreach (var side in new[] { -1, 1 }) Mark(Rect(0, side * (Half - 0.9), L, side * Half));
        foreach (var (at, bars) in new[] { (t0 + 152.4, 3), (t0 + 457.2, 2), (t0 + 609.6, 2), (t0 + 762, 1), (t0 + 914.4, 1) })
            for (var k = 0; k < bars; k++)
                foreach (var side in new[] { -1, 1 }) Mark(Rect(at, side * (10.7 + k * 3.4), at + 22.9, side * (12.5 + k * 3.4)));
        foreach (var side in new[] { -1, 1 }) Mark(Rect(t0 + 304.8, side * 10.7, t0 + 350.5, side * 19.8));
        for (var a = t0 + 115; a < L - 120; a += 61) Mark(Rect(a, -0.45, a + 36.6, 0.45));
        Markings = marks;

        // "19" over "R": the letter is painted nearest the threshold. Each block is mapped to its perspective footprint.
        Glyph Paint(string text, double a1, double a2, double b1, double b2)
        {
            var fl = Project(a2, b1); var fr = Project(a2, b2); var nl = Project(a1, b1);
            return new(text, $"matrix({N4((fr.X - fl.X) / 100)} {N4((fr.Y - fl.Y) / 100)} {N4((nl.X - fl.X) / 100)} {N4((nl.Y - fl.Y) / 100)} {N(fl.X)} {N(fl.Y)})");
        }
        Designator = [Paint("R", t0 + 64, t0 + 82, -3.5, 3.5), Paint("19", t0 + 88, t0 + 106, -8, 8)];

        // 19R lighting as a departing pilot sees it: white edges turning yellow for the last 2,000 ft, centre line
        // red/white for the last 3,000 ft and red for the last 1,000 ft, red end lights. 19R has no TDZ lights.
        for (var a = 0.0; a <= L; a += 61)
            foreach (var side in new[] { -1, 1 }) if (Visible(a, side * Half)) lights.Add(Lamp(a, side * Half, L - a < 610 ? "edge-caution" : "edge"));
        for (var a = t0 + 6; a <= L; a += 30.5)
            if (Visible(a, 0)) lights.Add(Lamp(a, 0, L - a < 305 ? "centre-red" : L - a < 914 ? ((int)(a / 30.5) % 2 == 0 ? "centre-red" : "centre") : "centre"));
        for (var b = -Half; b <= Half; b += 8.7) lights.Add(Lamp(L, b, "end"));
        // 19R PAPI on the right (west, +B), about 1,000 ft past the threshold. From this height, above the 3° path,
        // all four units show white.
        for (var k = 0; k < 4; k++) lights.Add(Lamp(t0 + 305, Half + 15 + k * 9, "papi-white", 1, 1.6, 1.6));

        // At the far end, 1L's ALSF-2: centre-line barrettes every 100 ft to 2,400 ft, red side rows to 1,000 ft,
        // crossbars at 500 and 1,000 ft, and sequenced flashers from 1,000 ft out to 2,400 ft that run north, towards
        // the runway and this camera.
        var rabbit = new List<Light>();
        for (var d = 100; d <= 2400; d += 100)
        {
            var a = Threshold1L + d * 0.3048;
            if (Visible(a, 0, 1)) lights.Add(Lamp(a, 0, "approach", 1, 1.4, 0.9));
            if (d <= 1000) foreach (var side in new[] { -1, 1 }) if (Visible(a, side * 12.5, 1)) lights.Add(Lamp(a, side * 12.5, "approach-red", 1, 1.4, 0.8));
            if (d is 500 or 1000) foreach (var side in new[] { -1, 1 }) for (var k = 5.0; k <= (d == 1000 ? 15.0 : 9.0); k += 5) if (Visible(a, side * k, 1)) lights.Add(Lamp(a, side * k, "approach", 1, 1.4, 0.9));
            if (d >= 1000 && Visible(a, 0, 1.5)) rabbit.Add(Lamp(a, 0, "flasher", 1.5, 2.6, 2.2));
        }
        Rabbit = Enumerable.Reverse(rabbit).ToArray(); // outermost first, so the CSS sequence runs towards the runway
        Lights = lights;

        // Terminal: real outlines extruded; walls facing the camera drawn far to near with a glazed band and mullions.
        var walls = new List<(double Depth, Shape Shape)>();
        foreach (var (_, height, outline) in Terminal)
        {
            var pts = outline.Select(p => Local(p.Lat, p.Lon)).ToArray();
            var h = height * TerminalScale;
            double ca = pts.Average(p => p.A), cb = pts.Average(p => p.B);
            for (var i = 0; i < pts.Length; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % pts.Length];
                double ma = (p.A + q.A) / 2, mb = (p.B + q.B) / 2, na = -(q.B - p.B), nb = q.A - p.A;
                if (na * (ma - ca) + nb * (mb - cb) < 0) { na = -na; nb = -nb; } // outward
                if (na * (CamPos.A - ma) + nb * (CamPos.B - mb) <= 0) continue;   // faces away
                if (Cam(p.A, p.B).F < Near || Cam(q.A, q.B).F < Near) continue;
                var depth = Cam(ma, mb).F;
                walls.Add((depth, new(Quad([Project(p.A, p.B), Project(q.A, q.B), Project(q.A, q.B, h), Project(p.A, p.B, h)]), "wall")));
                var len = Dist(p, q);
                if (len < 12) continue;
                (double A, double B) At(double t) => (p.A + t * (q.A - p.A), p.B + t * (q.B - p.B));
                var (g1, g2) = (At(0.04), At(0.96));
                walls.Add((depth - 0.1, new(Quad([Project(g1.A, g1.B, h * 0.3), Project(g2.A, g2.B, h * 0.3), Project(g2.A, g2.B, h * 0.78), Project(g1.A, g1.B, h * 0.78)]), "glass")));
                for (var s = 9.0; s < len - 4; s += 9)
                {
                    var m = At(s / len); var top = Project(m.A, m.B, h * 0.78); var bottom = Project(m.A, m.B, h * 0.3);
                    walls.Add((depth - 0.2, new($"M{N(bottom.X)} {N(bottom.Y)} L{N(top.X)} {N(top.Y)}", "mullion")));
                }
            }
            walls.Add((pts.Min(p => Cam(p.A, p.B).F) - 0.3, new(Ground(pts, h), "roof")));
        }
        var (ta, tb) = Local(Tower.Lat, Tower.Lon);
        double th = Tower.Height * 3, shaft = 10, cab = 22;
        (double X, double Y) TP(double db, double h) => Project(ta, tb + db, h);
        walls.Add((Cam(ta, tb).F, new(Quad([TP(-shaft, 0), TP(-shaft, th * 0.82), TP(-cab, th * 0.86), TP(-cab * 0.8, th), TP(cab * 0.8, th), TP(cab, th * 0.86), TP(shaft, th * 0.82), TP(shaft, 0)]), "tower")));
        walls.Add((Cam(ta, tb).F - 0.1, new(Quad([TP(-cab + 2, th * 0.875), TP(cab - 2, th * 0.875), TP(cab - 2, th * 0.95), TP(-cab + 2, th * 0.95)]), "tower-cab")));
        Walls = walls.OrderByDescending(w => w.Depth).Select(w => w.Shape).Where(s => s.Path.Length > 0).ToArray();

        // Aircraft parked nose-in: nose at the gate, tail away from the building, with nav lights and a red beacon.
        var aircraft = new List<Stroke>(); var acLights = new List<Light>();
        var buildings = Terminal.Select(t => t.Outline.Select(p => Local(p.Lat, p.Lon)).ToArray()).ToArray();
        foreach (var (_, lat, lon) in OccupiedGates.OrderByDescending(g => { var lg = Local(g.Lat, g.Lon); return Cam(lg.A, lg.B).F; }))
        {
            var g = Local(lat, lon);
            var wall = buildings.SelectMany(b => b.Select((p, i) => Nearest(g, p, b[(i + 1) % b.Length]))).OrderBy(p => Dist(p, g)).First();
            double da = g.A - wall.A, db = g.B - wall.B, len = Math.Max(Dist(g, wall), 1);
            (double A, double B) Along(double m) => (g.A + da / len * m, g.B + db / len * m);
            (double A, double B) Side(double m, double off) => (g.A + da / len * m - db / len * off, g.B + db / len * m + da / len * off);
            const double s = AircraftScale;
            var f = Cam(g.A, g.B).F;
            var nose = Along(2); var tail = Along(37); var wingRoot = Along(16); var wl = Side(18, 17); var wr = Side(18, -17);
            var bridge = Project(wall.A, wall.B, 5 * s); var bridgeEnd = Project(nose.A, nose.B, 5 * s);
            aircraft.Add(new(N(bridge.X), N(bridge.Y), N(bridgeEnd.X), N(bridgeEnd.Y), N(Math.Clamp(Focal * 3.5 / f, 1, 6)), "bridge"));
            var n1 = Project(nose.A, nose.B, 3 * s); var t2 = Project(tail.A, tail.B, 3 * s);
            aircraft.Add(new(N(n1.X), N(n1.Y), N(t2.X), N(t2.Y), N(Math.Clamp(Focal * 4 * s / f / 2.2, 1, 7)), "fuselage"));
            var w1 = Project(wl.A, wl.B, 2 * s); var w2 = Project(wr.A, wr.B, 2 * s);
            aircraft.Add(new(N(w1.X), N(w1.Y), N(w2.X), N(w2.Y), N(Math.Clamp(Focal * 1.2 / f, 0.6, 2.5)), "wing"));
            var finBase = Project(tail.A, tail.B, 4 * s); var finTop = Project(tail.A, tail.B, 12 * s);
            aircraft.Add(new(N(finBase.X), N(finBase.Y), N(finTop.X), N(finTop.Y), N(Math.Clamp(Focal * 3 / f, 1, 5)), "fin"));
            acLights.Add(new(N(w1.X), N(w1.Y), "1.2", "nav-red"));
            acLights.Add(new(N(w2.X), N(w2.Y), "1.2", "nav-green"));
            var beacon = Project(wingRoot.A, wingRoot.B, 5.5 * s);
            acLights.Add(new(N(beacon.X), N(beacon.Y), "1.5", "ac-beacon"));
        }
        Aircraft = aircraft; AircraftLights = acLights;

        var masts = new List<Light>();
        foreach (var (lat, lon) in new[] { (42.9509, -87.8993), (42.9496, -87.8986), (42.9481, -87.8978), (42.9466, -87.8981), (42.9500, -87.9027), (42.9475, -87.9006) })
        {
            var (ma, mb) = Local(lat, lon);
            var foot = Project(ma, mb); var top = Project(ma, mb, 30 * TerminalScale);
            masts.Add(new(N(top.X), N(top.Y), N(Math.Clamp(9000 / Cam(ma, mb).F, 4, 16)), N(foot.Y)));
        }
        Masts = masts;
        var beacons = new List<Light> { Lamp(ta, tb, "obstruction", th + 3, 2.5) };
        foreach (var (_, height, outline) in Terminal)
        {
            var lc = outline.Select(p => Local(p.Lat, p.Lon)).OrderBy(p => Cam(p.A, p.B).F).First();
            beacons.Add(Lamp(lc.A, lc.B, "obstruction", height * TerminalScale + 2, 2));
        }
        Beacons = beacons;

        // The city at night: street lights on the grid around the field, off airport property and west of the Lake
        // Michigan shoreline (to the left, towards the lake, it stays dark), thinning with distance.
        var city = new List<Light>();
        var seed = 11u;
        double Next() { seed = seed * 1664525 + 1013904223; return (seed >> 8) / 16777216.0; }
        static double Shoreline(double lat) => -87.845 - (lat - 42.93) * 0.40;
        void Street(double lat, double lon)
        {
            if (lon > Shoreline(lat)) return;
            if (lat > 42.9290 && lat < 42.9620 && lon > -87.9120 && lon < -87.8790) return;
            var (a, b) = Local(lat, lon);
            if (Math.Abs(b) < 60 && a > L && a < L + 800) return;
            if (!Visible(a, b, 7)) return;
            var f = Cam(a, b).F;
            if (f > 9000 || Next() > Math.Min(1, 900 / f)) return;
            city.Add(Lamp(a, b, "street", 7, 1.4));
        }
        for (var lat = 42.8900; lat < 42.9700; lat += 0.0018) for (var lon = -87.9600; lon < -87.8400; lon += 0.0009) Street(lat, lon);
        for (var lon = -87.9600; lon < -87.8400; lon += 0.0025) for (var lat = 42.8900; lat < 42.9700; lat += 0.0009) Street(lat, lon);
        City = city;

        // The holding-position sign array at the 19R end of Taxiway E, between the runway and the taxiway.
        var (sx, sy) = Project(330, 55);
        Sign = (N(sx), N(sy), N4(Focal * 3.0 * 6 / Cam(330, 55).F / 120)); // a 3 m array, drawn 6× true, in a 120-unit design
    }
}
