using OpenCvSharp;
using CitizenRadar.Models;

namespace CitizenRadar.Core;

/// <summary>
/// Handles all frame annotation and visual overlays.
/// Does NOT write any files — file I/O is the responsibility of Logger.
///
/// Visual elements:
/// - Green bounding box + speed label for vehicles under the speed limit
/// - Red bounding box + "OVER LIMIT" label for vehicles exceeding the limit
/// - Speed text format: "ID:3 — 42.1 km/h"
/// - Bird's-eye minimap showing tracked vehicles on the calibrated road region
/// - Statistics bar: total vehicles, average speed, violation %
/// </summary>
public class Visualizer
{
    /// <summary>
    /// Draws bounding boxes, speed labels, and violation indicators on the frame.
    /// </summary>
    /// <param name="frame">The video frame to annotate (modified in place).</param>
    /// <param name="tracks">Active tracks with current speed and violation status.</param>
    /// <param name="speedLimitKmh">Speed limit for display.</param>
    public void DrawOverlay(Mat frame, List<Track> tracks, double speedLimitKmh)
    {
        foreach (var track in tracks)
        {
            // Skip tracks that are currently unmatched (coasting)
            if (track.MissedFrames > 0)
                continue;

            var color = track.IsViolation ? Config.ColorViolation : Config.ColorSafe;
            var bbox = track.LastBBox;

            // Draw bounding box
            Cv2.Rectangle(frame,
                new Point((int)bbox.X, (int)bbox.Y),
                new Point((int)(bbox.X + bbox.Width), (int)(bbox.Y + bbox.Height)),
                color, 2);

            // Draw bottom-center anchor dot
            var lastPos = track.PositionHistory.Last().PixelPosition;
            Cv2.Circle(frame, new Point((int)lastPos.X, (int)lastPos.Y), 4, color, -1);

            // Build label text
            string label = track.SpeedKmh > 0
                ? $"ID:{track.TrackId} {track.ClassName} {track.SpeedKmh:F1} km/h"
                : $"ID:{track.TrackId} {track.ClassName}";

            if (track.IsViolation)
                label += " OVER LIMIT";

            // Draw label background
            int baseline;
            var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.55, 2, out baseline);
            int labelY = Math.Max((int)bbox.Y - 8, textSize.Height + 4);

            Cv2.Rectangle(frame,
                new Point((int)bbox.X, labelY - textSize.Height - 4),
                new Point((int)bbox.X + textSize.Width + 8, labelY + 4),
                Config.ColorLabelBg, -1);

            // Draw label text
            Cv2.PutText(frame, label,
                new Point((int)bbox.X + 4, labelY),
                HersheyFonts.HersheySimplex, 0.55, Config.ColorText, 2);
        }

        // Draw speed limit indicator in top-left
        Cv2.PutText(frame, $"Speed Limit: {speedLimitKmh:F0} km/h",
            new Point(10, 30), HersheyFonts.HersheySimplex, 0.8, Config.ColorText, 2);
    }

    /// <summary>
    /// Draws a bird's-eye minimap in the top-right corner showing tracked
    /// vehicles as coloured dots on the calibrated road region.
    /// </summary>
    /// <param name="frame">The video frame to draw on (modified in place).</param>
    /// <param name="tracks">Active tracks.</param>
    /// <param name="homography">Homography matrix (pixel → metres).</param>
    /// <param name="realWidthMetres">Real-world width of calibrated region.</param>
    /// <param name="realHeightMetres">Real-world height of calibrated region.</param>
    public void DrawMinimap(Mat frame, List<Track> tracks, Mat homography,
                            double realWidthMetres, double realHeightMetres)
    {
        // Minimap dimensions in pixels (displayed in top-right corner)
        int mapWidth = 150;
        int mapHeight = (int)(mapWidth * (realHeightMetres / realWidthMetres));
        mapHeight = Math.Max(mapHeight, 80);

        int mapX = frame.Width - mapWidth - 20;
        int mapY = 50;

        // Draw minimap background
        Cv2.Rectangle(frame,
            new Point(mapX - 2, mapY - 2),
            new Point(mapX + mapWidth + 2, mapY + mapHeight + 2),
            new Scalar(200, 200, 200), -1);
        Cv2.Rectangle(frame,
            new Point(mapX, mapY),
            new Point(mapX + mapWidth, mapY + mapHeight),
            new Scalar(60, 60, 60), -1);

        // Label
        Cv2.PutText(frame, "Bird's Eye", new Point(mapX, mapY - 8),
            HersheyFonts.HersheySimplex, 0.45, Config.ColorText, 1);

        // Scale factors: metres → minimap pixels
        double scaleX = mapWidth / realWidthMetres;
        double scaleY = mapHeight / realHeightMetres;

        foreach (var track in tracks)
        {
            if (track.MissedFrames > 0 || track.PositionHistory.Count == 0)
                continue;

            // Transform the latest pixel position to real-world metres
            var pixel = track.PositionHistory.Last().PixelPosition;
            var worldPoints = Cv2.PerspectiveTransform(new[] { pixel }, homography);
            var world = worldPoints[0];

            // Map world metres to minimap pixel coordinates
            int dotX = mapX + (int)(world.X * scaleX);
            int dotY = mapY + (int)(world.Y * scaleY);

            // Clamp to minimap bounds
            dotX = Math.Clamp(dotX, mapX, mapX + mapWidth);
            dotY = Math.Clamp(dotY, mapY, mapY + mapHeight);

            var dotColor = track.IsViolation ? Config.ColorViolation : Config.ColorSafe;
            Cv2.Circle(frame, new Point(dotX, dotY), 5, dotColor, -1);

            // Small ID label next to dot
            Cv2.PutText(frame, $"{track.TrackId}", new Point(dotX + 7, dotY + 3),
                HersheyFonts.HersheySimplex, 0.35, Config.ColorText, 1);
        }
    }

    /// <summary>
    /// Draws a summary statistics bar at the bottom of the frame.
    /// </summary>
    public void DrawStats(Mat frame, int totalVehicles, double avgSpeed, double violationPercent)
    {
        int barY = frame.Height - 40;

        // Draw dark background bar
        Cv2.Rectangle(frame,
            new Point(0, barY),
            new Point(frame.Width, frame.Height),
            Config.ColorLabelBg, -1);

        string stats = $"Vehicles: {totalVehicles}  |  Avg Speed: {avgSpeed:F1} km/h  |  Violations: {violationPercent:F1}%";

        Cv2.PutText(frame, stats,
            new Point(10, frame.Height - 12),
            HersheyFonts.HersheySimplex, 0.6, Config.ColorText, 2);
    }
}
