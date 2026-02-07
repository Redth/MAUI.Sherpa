using System.Globalization;
using System.Xml.Linq;

namespace MauiSherpa.Core.Services;

/// <summary>
/// A single waypoint in a route
/// </summary>
public record RouteWaypoint(double Latitude, double Longitude, double? Altitude = null, string? Name = null);

/// <summary>
/// Parses KML and GPX files into a sequence of waypoints
/// </summary>
public static class KmlRouteParser
{
    private static readonly XNamespace KmlNs = "http://www.opengis.net/kml/2.2";
    private static readonly XNamespace GpxNs = "http://www.topografix.com/GPX/1/1";
    private static readonly XNamespace Gpx10Ns = "http://www.topografix.com/GPX/1/0";

    public static IReadOnlyList<RouteWaypoint> ParseFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var xml = XDocument.Load(filePath);

        return ext switch
        {
            ".kml" => ParseKml(xml),
            ".gpx" => ParseGpx(xml),
            _ => throw new NotSupportedException($"Unsupported file type: {ext}. Use .kml or .gpx")
        };
    }

    public static IReadOnlyList<RouteWaypoint> ParseKml(XDocument doc)
    {
        var waypoints = new List<RouteWaypoint>();
        var root = doc.Root;
        if (root == null) return waypoints;

        // Detect namespace (some KML files omit it)
        var ns = root.Name.Namespace;

        // Extract from <coordinates> elements (inside Placemark/Point, LineString, LinearRing)
        foreach (var coords in root.Descendants(ns + "coordinates"))
        {
            var text = coords.Value.Trim();
            foreach (var tuple in text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = tuple.Split(',');
                if (parts.Length >= 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                {
                    double? alt = parts.Length >= 3
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : null;
                    waypoints.Add(new RouteWaypoint(lat, lon, alt));
                }
            }
        }

        // Also look for <gx:coord> elements (Google Earth extensions)
        var gxNs = XNamespace.Get("http://www.google.com/kml/ext/2.2");
        foreach (var coord in root.Descendants(gxNs + "coord"))
        {
            var parts = coord.Value.Trim().Split(' ');
            if (parts.Length >= 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            {
                double? alt = parts.Length >= 3
                    && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : null;
                waypoints.Add(new RouteWaypoint(lat, lon, alt));
            }
        }

        return waypoints;
    }

    public static IReadOnlyList<RouteWaypoint> ParseGpx(XDocument doc)
    {
        var waypoints = new List<RouteWaypoint>();
        var root = doc.Root;
        if (root == null) return waypoints;

        var ns = root.Name.Namespace;

        // Track points (<trkpt>), route points (<rtept>), and waypoints (<wpt>)
        var pointElements = root.Descendants(ns + "trkpt")
            .Concat(root.Descendants(ns + "rtept"))
            .Concat(root.Descendants(ns + "wpt"));

        foreach (var pt in pointElements)
        {
            var latAttr = pt.Attribute("lat");
            var lonAttr = pt.Attribute("lon");
            if (latAttr == null || lonAttr == null) continue;

            if (double.TryParse(latAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(lonAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                double? alt = null;
                var eleEl = pt.Element(ns + "ele");
                if (eleEl != null && double.TryParse(eleEl.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                    alt = a;

                var nameEl = pt.Element(ns + "name");
                waypoints.Add(new RouteWaypoint(lat, lon, alt, nameEl?.Value));
            }
        }

        return waypoints;
    }
}
