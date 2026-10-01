using OpenCvSharp;

namespace CitizenRadar.Models;

/// <summary>
/// Represents a single vehicle detection from one frame.
/// Produced by: Detector
/// Consumed by: Tracker
/// </summary>
public record Detection
{
    /// <summary>Bounding box in original frame pixel coordinates.</summary>
    public required Rect2f BBox { get; init; }

    /// <summary>COCO class ID (car=2, motorcycle=3, bus=5, truck=7).</summary>
    public required int ClassId { get; init; }

    /// <summary>Detection confidence score (0.0 – 1.0).</summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// Ground-plane anchor point: the bottom-center of the bounding box.
    /// This is the point used for homography transformation and speed calculation.
    /// Computed as: (BBox.X + BBox.Width/2, BBox.Y + BBox.Height)
    /// </summary>
    public required Point2f BottomCenter { get; init; }

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
