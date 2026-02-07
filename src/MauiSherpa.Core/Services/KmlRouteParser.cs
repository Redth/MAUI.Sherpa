using System.Globalization;
using System.Xml.Linq;
using SharpKml.Base;
using SharpKml.Dom;
using SharpKml.Dom.GX;

namespace MauiSherpa.Core.Services;

/// <summary>
/// A single waypoint in a route
/// </summary>
public record RouteWaypoint(double Latitude, double Longitude, double? Altitude = null, string? Name = null);

/// <summary>
/// Parses KML and GPX files into a sequence of waypoints.
/// Uses SharpKml.Core for robust KML parsing, manual XML for GPX.
/// </summary>
public static class KmlRouteParser
{
    public static IReadOnlyList<RouteWaypoint> ParseFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".kml" => ParseKml(filePath),
            ".gpx" => ParseGpx(XDocument.Load(filePath)),
            _ => throw new NotSupportedException($"Unsupported file type: {ext}. Use .kml or .gpx")
        };
    }

    public static IReadOnlyList<RouteWaypoint> ParseKml(string filePath)
    {
        var parser = new Parser();
        using var stream = File.OpenRead(filePath);
        parser.Parse(stream);

        var root = parser.Root;
        if (root == null) return [];

        var waypoints = new List<RouteWaypoint>();
        ExtractFromElement(root, waypoints);
        return waypoints;
    }

    private static void ExtractFromElement(Element element, List<RouteWaypoint> waypoints)
    {
        switch (element)
        {
            case Point point when point.Coordinate != null:
                waypoints.Add(ToWaypoint(point.Coordinate));
                break;

            case LineString line when line.Coordinates != null:
                foreach (var v in line.Coordinates)
                    waypoints.Add(ToWaypoint(v));
                break;

            case LinearRing ring when ring.Coordinates != null:
                foreach (var v in ring.Coordinates)
                    waypoints.Add(ToWaypoint(v));
                break;

            case Track track:
                foreach (var v in track.Coordinates)
                    waypoints.Add(ToWaypoint(v));
                break;

            case MultipleTrack multiTrack:
                foreach (var child in multiTrack.Tracks)
                    ExtractFromElement(child, waypoints);
                break;
        }

        foreach (var child in element.Children)
            ExtractFromElement(child, waypoints);
    }

    private static RouteWaypoint ToWaypoint(Vector v) =>
        new(v.Latitude, v.Longitude, v.Altitude);

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
