using OpenCvSharp;

namespace CitizenRadar.Models;

/// <summary>
/// Stores all data needed to reproduce a homography calibration.
/// Each calibration is specific to a particular camera position and video.
/// Changing the camera angle or position requires recalibration.
///
/// ASSUMPTION: The selected road region must be approximately planar (flat).
/// The homography is a planar transformation and will produce incorrect
/// results on sloped or curved road surfaces.
/// </summary>
public class CalibrationData
{
    /// <summary>
    /// Four points clicked on the video frame, in image pixel coordinates.
    /// Order: top-left, top-right, bottom-right, bottom-left of the
    /// selected road quadrilateral.
    /// </summary>
    public required Point2f[] SourcePoints { get; init; }

    /// <summary>Real-world width of the selected road region in metres.</summary>
    public required double RealWorldWidthMetres { get; init; }

    /// <summary>Real-world height (depth) of the selected road region in metres.</summary>
    public required double RealWorldHeightMetres { get; init; }

    /// <summary>
    /// Destination points in real-world metre coordinates:
    /// (0,0), (W,0), (W,H), (0,H) where W and H are the real-world dimensions.
    /// </summary>
    public required Point2f[] DestinationPoints { get; init; }

    /// <summary>
    /// The 3×3 homography matrix, stored as a flat 9-element array (row-major).
    /// Transforms image pixel coordinates to real-world metre coordinates.
    /// </summary>
    public required double[] HomographyMatrix { get; init; }

    /// <summary>Source video file or camera identifier used during calibration.</summary>
    public string? VideoSource { get; init; }

    /// <summary>ISO 8601 timestamp when this calibration was created.</summary>
    public string? CreatedAt { get; init; }

    /// <summary>Optional human-readable notes about the calibration setup.</summary>
    public string? Notes { get; init; }
}
