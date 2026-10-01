using OpenCvSharp;
using CitizenRadar.Models;

namespace CitizenRadar.Core;

/// <summary>
/// Estimates vehicle speed by transforming pixel-space trajectories
/// into real-world metre coordinates via a pre-computed homography,
/// then computing displacement over time.
///
/// Pipeline for a single track:
///   BottomCenter pixel (u,v)
///     → Horizon gate check (reject if in top 40% of frame)
///     → Homography transform → real-world (X,Y) in metres
///     → Displacement along Y-axis over sliding window → distance in metres
///     → Time delta from frame count and FPS → speed in km/h
///     → Optional EMA smoothing
///     → Overspeed detection
///
/// Key accuracy decisions:
///   - Only Y-axis displacement is measured (road direction), ignoring
///     horizontal bounding-box wobble that inflates Euclidean distance.
///   - A 30-frame (1-second) sliding window averages out per-frame jitter.
///   - Detections in the top 40% of the frame are near the perspective
///     vanishing point where 1px of noise = 5+ metres; these are skipped.
///
/// NOTE: This provides vehicle speed estimation, not legally calibrated
/// speed measurement. Accuracy depends on calibration quality, camera
/// stability, detection precision, and the planarity assumption of the
/// road surface.
/// </summary>
public class SpeedEstimator
{
    private Mat _homography;
    private readonly double _fps;
    private readonly int _frameHeight;

    /// <summary>
    /// Initialises the speed estimator.
    /// </summary>
    /// <param name="homography">
    /// The 3×3 homography matrix that maps image pixel coordinates
    /// to real-world metre coordinates on the road plane.
    /// </param>
    /// <param name="fps">
    /// The video's frame rate. Used to compute time deltas between frames.
    /// </param>
    /// <param name="frameHeight">
    /// The video frame height in pixels. Used for the horizon distance gate.
    /// </param>
    public SpeedEstimator(Mat homography, double fps, int frameHeight = 540)
    {
        _homography = homography ?? throw new ArgumentNullException(nameof(homography));
        _fps = fps > 0 ? fps : throw new ArgumentException("FPS must be positive.", nameof(fps));
        _frameHeight = frameHeight > 0 ? frameHeight : 540;
    }

    /// <summary>Updates the homography matrix on the fly.</summary>
    public void UpdateHomography(Mat newHomography)
    {
        _homography = newHomography ?? throw new ArgumentNullException(nameof(newHomography));
    }

    /// <summary>
    /// Estimates the speed of a tracked vehicle and updates the track's
    /// SpeedKmh and IsViolation fields.
    ///
    /// Returns 0 and skips computation when:
    ///   - Insufficient position history (less than MinFramesBeforeSpeed)
    ///   - The detection is in the horizon gate zone (top 40% of frame)
    ///   - The homography transform fails (point behind horizon)
    /// </summary>
    public double Estimate(Track track, double speedLimitKmh)
    {
        int historyCount = track.PositionHistory.Count;

        // Need at least MinFramesBeforeSpeed entries for a stable reading
        if (historyCount < Config.MinFramesBeforeSpeed)
        {
            track.SpeedKmh = 0;
            track.IsViolation = false;
            return 0;
        }

        // Get the current and past positions from the sliding window
        int windowSize = Math.Min(Config.SpeedWindowFrames, historyCount - 1);
        var (frameNow, pixelNow) = track.PositionHistory[historyCount - 1];
        var (framePast, pixelPast) = track.PositionHistory[historyCount - 1 - windowSize];

        // ── Horizon Distance Gate ──────────────────────────────────────
        // If the current detection is in the top portion of the frame
        // (near the vanishing point), perspective compression is extreme.
        // Skip speed computation and keep the last stable estimate.
        double horizonLine = _frameHeight * Config.HorizonGateRatio;
        if (pixelNow.Y < horizonLine)
        {
            // Don't update speed — keep whatever we had
            return track.SpeedKmh;
        }

        // Transform pixel coordinates to real-world metres via homography
        if (!TryTransformToWorld(pixelNow, out var worldNow) ||
            !TryTransformToWorld(pixelPast, out var worldPast))
        {
            return track.SpeedKmh;
        }

        // ── Y-Axis Only Distance ───────────────────────────────────────
        // Measure displacement along the road direction (Y in bird's-eye view)
        // only. This eliminates horizontal bounding-box wobble that inflates
        // Euclidean distance and produces falsely high speed readings.
        double distanceMetres = Math.Abs(worldNow.Y - worldPast.Y);

        // Compute time delta in seconds
        int frameDelta = frameNow - framePast;
        double deltaTimeSeconds = frameDelta / _fps;

        if (deltaTimeSeconds <= 0)
        {
            track.SpeedKmh = 0;
            track.IsViolation = false;
            return 0;
        }

        // Speed in km/h: (metres / seconds) × 3.6
        double rawSpeedKmh = (distanceMetres / deltaTimeSeconds) * 3.6;

        // Reject unphysical speed spikes (e.g. tracks entering at horizon or tracking jumps)
        if (rawSpeedKmh > Config.MaxPlausibleSpeedKmh)
        {
            return track.SpeedKmh;
        }

        double speedKmh = rawSpeedKmh;

        // Optional EMA smoothing
        if (Config.EnableSmoothing && track.SpeedKmh > 0)
        {
            speedKmh = Config.SmoothingAlpha * speedKmh
                      + (1 - Config.SmoothingAlpha) * track.SpeedKmh;
        }

        // Update track
        track.SpeedKmh = speedKmh;
        track.IsViolation = speedKmh > speedLimitKmh;

        return speedKmh;
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Transforms a single pixel coordinate to real-world metre coordinates
    /// using the homography matrix, checking for perspective validity.
    ///
    /// Applies the perspective transform:
    ///   [X, Y, W]^T = H × [u, v, 1]^T
    ///   x_real = X / W
    ///   y_real = Y / W
    ///
    /// Returns false if the point is at or behind the horizon (W <= 0.05).
    /// </summary>
    private bool TryTransformToWorld(Point2f pixel, out Point2f world)
    {
        double h20 = _homography.At<double>(2, 0);
        double h21 = _homography.At<double>(2, 1);
        double h22 = _homography.At<double>(2, 2);

        double w = h20 * pixel.X + h21 * pixel.Y + h22;
        if (w <= 0.05)
        {
            world = default;
            return false;
        }

        var srcPoints = new Point2f[] { pixel };
        var dstPoints = Cv2.PerspectiveTransform(srcPoints, _homography);
        world = dstPoints[0];
        return true;
    }
}
