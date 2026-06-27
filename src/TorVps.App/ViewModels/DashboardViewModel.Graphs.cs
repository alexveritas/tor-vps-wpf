using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TorVps.Core.Models;

namespace TorVps.App.ViewModels;

public partial class DashboardViewModel
{
    private const double TopGraphLogicalHeight = 50;
    private const double VpsGraphLogicalHeight = 24;

    private void RecordGraphHistory(bool torAlive, double downMbit, double upMbit, int monitorIntervalSec)
    {
        var onionMs = _onionProbe?.Success == true ? _onionProbe.ElapsedMs : (double?)null;
        var chainMs = _chainProbe?.Success == true ? _chainProbe.ElapsedMs : (double?)null;
        var ping = onionMs ?? chainMs ?? 0.0;

        var now = DateTimeOffset.UtcNow;
        _graphHistory.Enqueue(new GraphHistoryPoint { Timestamp = now, Down = downMbit, Up = upMbit, Ping = ping, Valid = torAlive });
        while (_graphHistory.Count > GraphHistoryCapacity)
            _graphHistory.Dequeue();

        var freshLimit = TimeSpan.FromSeconds(Math.Max(30, monitorIntervalSec * 3));
        var serverForGraph = Server.Ok
            ? Server
            : _lastGoodServerMetrics is not null && now - _lastGoodServerMetricsAt <= freshLimit
                ? _lastGoodServerMetrics
                : null;

        var vpsPoint = serverForGraph is not null
            ? new VpsHistoryPoint { Timestamp = now, Down = serverForGraph.Down ?? 0, Up = serverForGraph.Up ?? 0, Cpu = serverForGraph.Cpu ?? 0, Mem = serverForGraph.Mem ?? 0, Valid = true }
            : new VpsHistoryPoint { Timestamp = now, Valid = false };
        _vpsHistory.Enqueue(vpsPoint);
        while (_vpsHistory.Count > VpsGraphCapacity)
            _vpsHistory.Dequeue();

        ServerTooltip = Server.Ok
            ? "Glances API OK" + (string.IsNullOrEmpty(Server.Iface) ? string.Empty : $" / {Server.Iface}")
            : string.IsNullOrEmpty(Server.Error) ? "server metrics unavailable" : Server.Error;

        UpdateGraphPointCollections();
    }

    private void UpdateGraphPointCollections()
    {
        var points = _graphHistory.ToArray();
        var validPoints = points.Where(p => p.Valid).ToArray();

        var observedSpeed = validPoints.Length > 0 ? validPoints.Max(p => Math.Max(p.Down, p.Up)) : 1.0;
        _speedScaleMax = NextStableScale(_speedScaleMax, observedSpeed, 1.0);

        var pingValues = validPoints.Select(p => p.Ping).Where(p => p > 0).ToArray();
        if (pingValues.Length > 0)
            _pingScaleMax = NextStableScale(_pingScaleMax, pingValues.Max(), 250.0);

        DownAreaPoints = BuildPoints(points, p => p.Down, _speedScaleMax, GraphHistoryCapacity, TopGraphLogicalHeight);
        UpAreaPoints = BuildPoints(points, p => p.Up, _speedScaleMax, GraphHistoryCapacity, TopGraphLogicalHeight);
        PingAreaPoints = BuildPoints(points, p => p.Ping, _pingScaleMax, GraphHistoryCapacity, TopGraphLogicalHeight);
        DownFillPoints = ToAreaPolygon(DownAreaPoints, TopGraphLogicalHeight);
        UpFillPoints = ToAreaPolygon(UpAreaPoints, TopGraphLogicalHeight);

        var lastValid = validPoints.LastOrDefault();
        DownCurrentText = FormatSpeed(lastValid?.Down ?? 0);
        UpCurrentText = FormatSpeed(lastValid?.Up ?? 0);
        DownPeakText = FormatSpeed(validPoints.Length > 0 ? validPoints.Max(p => p.Down) : 0);
        UpPeakText = FormatSpeed(validPoints.Length > 0 ? validPoints.Max(p => p.Up) : 0);
        DownAverageText = FormatSpeed(validPoints.Length > 0 ? validPoints.Average(p => p.Down) : 0);
        UpAverageText = FormatSpeed(validPoints.Length > 0 ? validPoints.Average(p => p.Up) : 0);

        PingCurrentText = FormatPing(lastValid?.Ping ?? 0);
        PingPeakText = FormatPing(pingValues.Length > 0 ? pingValues.Max() : 0);
        PingAverageText = FormatPing(pingValues.Length > 0 ? pingValues.Average() : 0);

        var vpsPoints = _vpsHistory.ToArray();
        var validVps = vpsPoints.Where(p => p.Valid).ToArray();
        var observedVpsSpeed = validVps.Length > 0 ? validVps.Max(p => Math.Max(p.Down, p.Up)) : 1.0;
        _vpsScaleMax = NextStableScale(_vpsScaleMax, observedVpsSpeed, 1.0);

        VpsDownAreaPoints = BuildPoints(vpsPoints, p => p.Down, _vpsScaleMax, VpsGraphCapacity, VpsGraphLogicalHeight);
        VpsUpAreaPoints = BuildPoints(vpsPoints, p => p.Up, _vpsScaleMax, VpsGraphCapacity, VpsGraphLogicalHeight);
        VpsDownFillPoints = ToAreaPolygon(VpsDownAreaPoints, VpsGraphLogicalHeight);
        VpsUpFillPoints = ToAreaPolygon(VpsUpAreaPoints, VpsGraphLogicalHeight);

        var lastValidVps = validVps.LastOrDefault();
        VpsCpuText = $"{Math.Round(lastValidVps?.Cpu ?? 0)}%";
        VpsRamText = $"{Math.Round(lastValidVps?.Mem ?? 0)}%";
        VpsDownCurrentText = FormatSpeed(lastValidVps?.Down ?? 0);
        VpsUpCurrentText = FormatSpeed(lastValidVps?.Up ?? 0);
        VpsDownPeakText = FormatSpeed(validVps.Length > 0 ? validVps.Max(p => p.Down) : 0);
        VpsUpPeakText = FormatSpeed(validVps.Length > 0 ? validVps.Max(p => p.Up) : 0);
    }

    private static PointCollection BuildPoints<T>(IReadOnlyList<T> history, Func<T, double> selector, double scaleMax, int capacity, double logicalHeight)
    {
        var collection = new PointCollection(capacity);
        var paddingCount = Math.Max(0, capacity - history.Count);
        for (var i = 0; i < paddingCount; i++)
            collection.Add(new Point(i, logicalHeight));

        for (var i = 0; i < history.Count; i++)
        {
            var value = selector(history[i]);
            var normalized = scaleMax > 0 ? Math.Clamp(value / scaleMax, 0, 1) : 0;
            collection.Add(new Point(paddingCount + i, (1 - normalized) * logicalHeight));
        }
        return collection;
    }

    /// <summary>Closes a line's points into a filled area shape by dropping straight down to the baseline and back.</summary>
    private static PointCollection ToAreaPolygon(PointCollection linePoints, double logicalHeight)
    {
        if (linePoints.Count == 0)
            return new PointCollection();

        var polygon = new PointCollection(linePoints.Count + 2);
        foreach (var point in linePoints)
            polygon.Add(point);
        polygon.Add(new Point(linePoints[^1].X, logicalHeight));
        polygon.Add(new Point(linePoints[0].X, logicalHeight));
        return polygon;
    }

    private static double NextStableScale(double current, double observed, double floor)
    {
        var basis = Math.Max(floor, observed * 1.15);
        if (basis <= current) return current;
        if (basis <= 10) return Math.Ceiling(basis * 2) / 2;
        if (basis <= 100) return Math.Ceiling(basis / 5) * 5;
        return Math.Ceiling(basis / 50) * 50;
    }

    private static string FormatSpeed(double value)
    {
        if (value >= 100) return value.ToString("F0", CultureInfo.InvariantCulture);
        if (value >= 10) return value.ToString("F1", CultureInfo.InvariantCulture);
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string FormatPing(double value) => value > 0 ? $"{Math.Round(value)} ms" : "— ms";
}
