using OpenCvSharp;

namespace CitizenRadar.Models;

/// <summary>
/// Represents a tracked vehicle with a persistent identity across frames.
/// Produced by: Tracker (ID, position history)
/// Updated by: SpeedEstimator (speed, violation flag)
/// Consumed by: Visualizer, Logger
/// </summary>
public class Track
{
    /// <summary>Unique persistent ID assigned when the vehicle first appears.</summary>
    public int TrackId { get; }

    /// <summary>COCO class ID of the tracked vehicle.</summary>
    public int ClassId { get; set; }

    /// <summary>
    /// History of ground-plane anchor positions (bottom-center of bounding box)
    /// in image pixel coordinates. Each entry is (frameNumber, pixelPosition).
    /// Used by SpeedEstimator to compute displacement over time.
    /// </summary>
    public List<(int FrameNumber, Point2f PixelPosition)> PositionHistory { get; } = new();

    /// <summary>The most recent bounding box, used for IoU matching in the next frame.</summary>
    public Rect2f LastBBox { get; set; }

    /// <summary>
    /// Number of consecutive frames where this track was not matched to any detection.
    /// When this exceeds Config.TrackMaxMissedFrames, the track is deregistered.
    /// </summary>
    public int MissedFrames { get; set; }

    /// <summary>Estimated speed in km/h. Updated by SpeedEstimator each frame.</summary>
    public double SpeedKmh { get; set; }

    /// <summary>True if SpeedKmh exceeds the configured speed limit.</summary>
    public bool IsViolation { get; set; }

    public Track(int trackId, int classId, Rect2f initialBBox, int frameNumber, Point2f initialPosition)
    {
        TrackId = trackId;
        ClassId = classId;
        LastBBox = initialBBox;
        MissedFrames = 0;
        SpeedKmh = 0;
        IsViolation = false;
        PositionHistory.Add((frameNumber, initialPosition));
    }

    /// <summary>Human-readable class name for display.</summary>
    public string ClassName => ClassId switch
    {
        2 => "Car",
        3 => "Motorcycle",
        5 => "Bus",
        7 => "Truck",
        _ => "Vehicle"
    };
}
