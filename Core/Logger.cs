using CitizenRadar.Models;

namespace CitizenRadar.Core;

/// <summary>
/// Handles CSV speed log writing, per-vehicle records, and summary statistics.
/// Separated from Visualizer to keep file I/O and frame drawing as independent modules.
/// </summary>
public class Logger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly List<(int TrackId, double SpeedKmh, bool IsViolation, int ClassId)> _allRecords = new();

    /// <summary>
    /// Creates the CSV file and writes the header row.
    /// </summary>
    /// <param name="csvPath">Path to the output CSV file.</param>
    public Logger(string csvPath)
    {
        var dir = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(csvPath, append: false);
        _writer.WriteLine("frame,track_id,class_id,class_name,speed_kmh,is_violation");
        Console.WriteLine($"Logger: Writing speed log to {csvPath}");
    }

    /// <summary>
    /// Writes a single speed record for a tracked vehicle.
    /// Only writes records for vehicles with a computed speed > 0.
    /// </summary>
    public void WriteRecord(int frameNumber, Track track)
    {
        if (track.SpeedKmh <= 0)
            return;

        string violation = track.IsViolation ? "true" : "false";
        _writer.WriteLine($"{frameNumber},{track.TrackId},{track.ClassId},{track.ClassName},{track.SpeedKmh:F2},{violation}");

        _allRecords.Add((track.TrackId, track.SpeedKmh, track.IsViolation, track.ClassId));
    }

    /// <summary>
    /// Computes and prints summary statistics to the console.
    /// Call this after all frames have been processed.
    /// </summary>
    /// <param name="totalVehicles">Total unique vehicles observed.</param>
    public void WriteSummary(int totalVehicles)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine("         CitizenRadar — Summary        ");
        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine($"  Total vehicles observed: {totalVehicles}");

        if (_allRecords.Count > 0)
        {
            double avgSpeed = _allRecords.Average(r => r.SpeedKmh);
            double maxSpeed = _allRecords.Max(r => r.SpeedKmh);
            int violations = _allRecords.Count(r => r.IsViolation);
            double violationPct = (double)violations / _allRecords.Count * 100;

            // Count by class
            var classCounts = _allRecords
                .Select(r => r.TrackId)
                .Distinct()
                .Count();

            Console.WriteLine($"  Total speed records:     {_allRecords.Count}");
            Console.WriteLine($"  Average speed:           {avgSpeed:F1} km/h");
            Console.WriteLine($"  Maximum speed:           {maxSpeed:F1} km/h");
            Console.WriteLine($"  Violations:              {violations} ({violationPct:F1}%)");
        }
        else
        {
            Console.WriteLine("  No speed records collected.");
        }

        Console.WriteLine("═══════════════════════════════════════");
    }

    /// <summary>
    /// Returns summary statistics for use by Visualizer.
    /// </summary>
    public (double AvgSpeed, double ViolationPercent) GetLiveStats()
    {
        if (_allRecords.Count == 0)
            return (0, 0);

        double avgSpeed = _allRecords.Average(r => r.SpeedKmh);
        int violations = _allRecords.Count(r => r.IsViolation);
        double violationPct = (double)violations / _allRecords.Count * 100;
        return (avgSpeed, violationPct);
    }

    /// <summary>
    /// Flushes and closes the CSV file.
    /// </summary>
    public void Close()
    {
        _writer.Flush();
        _writer.Close();
    }

    public void Dispose()
    {
        Close();
        _writer.Dispose();
        GC.SuppressFinalize(this);
    }
}
