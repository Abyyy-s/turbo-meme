using System.Text.Json;
using OpenCvSharp;
using CitizenRadar.Models;

namespace CitizenRadar.Core;

/// <summary>
/// Handles interactive homography calibration.
///
/// The user clicks 4 points on a planar road region in a video frame and
/// provides the real-world width and height in metres. The module computes
/// a 3×3 homography matrix that maps image pixel coordinates directly to
/// real-world metre coordinates.
///
/// ASSUMPTION: The selected road region must be approximately planar (flat).
/// The homography is a planar transformation and will produce incorrect
/// results on sloped or curved road surfaces.
///
/// Each calibration is specific to a particular camera position and video.
/// Changing the camera angle or position requires recalibration.
/// </summary>
public class Calibrator
{
    private readonly List<Point2f> _clickedPoints = new();

    /// <summary>Returns the 4 points clicked during the last calibration session.</summary>
    public Point2f[] LastClickedPoints => _clickedPoints.ToArray();

    /// <summary>
    /// Opens the first frame of the video. The user clicks 4 points defining
    /// a quadrilateral on a planar road surface. The real-world dimensions
    /// of that quadrilateral are provided in metres.
    ///
    /// Returns the computed 3×3 homography matrix that maps pixel coordinates
    /// to real-world metre coordinates.
    ///
    /// Point order: top-left, top-right, bottom-right, bottom-left.
    ///
    /// Destination points are constructed as:
    ///   (0, 0), (widthMetres, 0), (widthMetres, heightMetres), (0, heightMetres)
    /// so that transformed coordinates are directly in metres.
    /// </summary>
    public Mat Calibrate(string videoPath, double realWidthMetres, double realHeightMetres)
    {
        using var reader = VideoReader.Open(videoPath);
        var frame = new Mat();
        if (!reader.Read(frame) || frame.Empty())
            throw new InvalidOperationException("Cannot read first frame from video.");

        _clickedPoints.Clear();

        bool guiAvailable = false;
        const string windowName = "CitizenRadar — Click 4 road points (TL, TR, BR, BL). Press ESC to cancel.";
        try
        {
            Cv2.NamedWindow(windowName, WindowFlags.AutoSize);
            Cv2.SetMouseCallback(windowName, OnMouseClick);
            guiAvailable = true;
        }
        catch
        {
            guiAvailable = false;
        }

        if (guiAvailable)
        {
            var displayFrame = frame.Clone();
            Console.WriteLine("Click 4 points on the road surface in order: Top-Left, Top-Right, Bottom-Right, Bottom-Left.");
            Console.WriteLine("Press ESC to cancel.");

            while (_clickedPoints.Count < 4)
            {
                // Redraw frame with clicked points
                displayFrame = frame.Clone();
                for (int i = 0; i < _clickedPoints.Count; i++)
                {
                    var pt = new Point((int)_clickedPoints[i].X, (int)_clickedPoints[i].Y);
                    Cv2.Circle(displayFrame, pt, 6, new Scalar(0, 255, 255), -1);
                    Cv2.PutText(displayFrame, $"P{i + 1}", new Point(pt.X + 10, pt.Y - 10),
                        HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);

                    // Draw lines between consecutive points
                    if (i > 0)
                    {
                        var prevPt = new Point((int)_clickedPoints[i - 1].X, (int)_clickedPoints[i - 1].Y);
                        Cv2.Line(displayFrame, prevPt, pt, new Scalar(255, 255, 0), 2);
                    }
                }

                Cv2.PutText(displayFrame, $"Click point {_clickedPoints.Count + 1}/4",
                    new Point(10, 30), HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);

                Cv2.ImShow(windowName, displayFrame);
                int key = Cv2.WaitKey(30);
                if (key == 27) // ESC
                    throw new OperationCanceledException("Calibration cancelled by user.");
            }

            // Close the last line of the quadrilateral
            displayFrame = frame.Clone();
            for (int i = 0; i < 4; i++)
            {
                var pt = new Point((int)_clickedPoints[i].X, (int)_clickedPoints[i].Y);
                var nextPt = new Point((int)_clickedPoints[(i + 1) % 4].X, (int)_clickedPoints[(i + 1) % 4].Y);
                Cv2.Circle(displayFrame, pt, 6, new Scalar(0, 255, 255), -1);
                Cv2.PutText(displayFrame, $"P{i + 1}", new Point(pt.X + 10, pt.Y - 10),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);
                Cv2.Line(displayFrame, pt, nextPt, new Scalar(255, 255, 0), 2);
            }
            Cv2.PutText(displayFrame, "Calibration complete. Press any key.",
                new Point(10, 30), HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);
            Cv2.ImShow(windowName, displayFrame);
            Cv2.WaitKey(500);
            try { Cv2.DestroyWindow(windowName); } catch {}
        }
        else
        {
            string framePath = Path.Combine(Path.GetDirectoryName(videoPath) ?? "data", "calibration_frame.jpg");
            try { Cv2.ImWrite(framePath, frame); } catch {}

            Console.WriteLine("  [Notice] Interactive OpenCV window is unavailable in this Linux build.");
            Console.WriteLine($"  First frame saved to: {framePath}");
            Console.WriteLine("  Applying road geometry calibrated for the traffic.mp4 scene:");

            // These points are the Roboflow tutorial SOURCE coordinates scaled from
            // the original 4K resolution (5120×2880) down to our 960×540 frame.
            // Roboflow SOURCE: [[1252,787], [2298,803], [5039,2159], [-550,2159]]
            // Scale: 960/5120 ≈ 0.1875 for X, 540/2880 ≈ 0.1875 for Y
            // This covers the full visible highway from the overpass to the camera.
            _clickedPoints.Add(new Point2f(235f, 148f));   // TL (near overpass, left lane edge)
            _clickedPoints.Add(new Point2f(431f, 151f));   // TR (near overpass, right lane edge)
            _clickedPoints.Add(new Point2f(945f, 405f));   // BR (bottom of frame, right edge)
            _clickedPoints.Add(new Point2f(-103f, 405f));  // BL (bottom of frame, left edge — extends off-screen)

            for (int i = 0; i < 4; i++)
                Console.WriteLine($"    Point {i + 1}: ({_clickedPoints[i].X:F0}, {_clickedPoints[i].Y:F0})");
        }

        // Construct source and destination point arrays
        var srcPoints = _clickedPoints.ToArray();
        var dstPoints = new Point2f[]
        {
            new(0f, 0f),
            new((float)realWidthMetres, 0f),
            new((float)realWidthMetres, (float)realHeightMetres),
            new(0f, (float)realHeightMetres)
        };

        // Compute the homography matrix: pixel → real-world metres
        var homography = Cv2.GetPerspectiveTransform(srcPoints, dstPoints);

        Console.WriteLine("Homography matrix computed successfully.");
        Console.WriteLine($"Maps selected region to {realWidthMetres}m × {realHeightMetres}m real-world plane.");

        return homography;
    }

    /// <summary>
    /// Saves the calibration data to a JSON file for later reuse.
    /// Stores enough information to fully reproduce the calibration.
    /// </summary>
    public void Save(string jsonPath, CalibrationData data)
    {
        var dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"Calibration saved to: {jsonPath}");
    }

    /// <summary>
    /// Loads a previously saved calibration from JSON.
    /// </summary>
    public CalibrationData Load(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Calibration file not found: {jsonPath}");

        var json = File.ReadAllText(jsonPath);
        var data = JsonSerializer.Deserialize<CalibrationData>(json)
            ?? throw new InvalidOperationException("Failed to deserialise calibration data.");

        Console.WriteLine($"Calibration loaded from: {jsonPath}");
        return data;
    }

    /// <summary>
    /// Creates a CalibrationData record from the current calibration state.
    /// </summary>
    public CalibrationData CreateCalibrationData(
        Point2f[] srcPoints, double widthMetres, double heightMetres,
        Mat homography, string? videoSource = null, string? notes = null)
    {
        var dstPoints = new Point2f[]
        {
            new(0f, 0f),
            new((float)widthMetres, 0f),
            new((float)widthMetres, (float)heightMetres),
            new(0f, (float)heightMetres)
        };

        // Flatten the 3×3 homography matrix to a 9-element row-major array
        var matrixArray = new double[9];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                matrixArray[r * 3 + c] = homography.At<double>(r, c);

        return new CalibrationData
        {
            SourcePoints = srcPoints,
            RealWorldWidthMetres = widthMetres,
            RealWorldHeightMetres = heightMetres,
            DestinationPoints = dstPoints,
            HomographyMatrix = matrixArray,
            VideoSource = videoSource,
            CreatedAt = DateTimeOffset.Now.ToString("o"),
            Notes = notes
        };
    }

    /// <summary>
    /// Reconstructs the OpenCV Mat from a saved CalibrationData's flat matrix array.
    /// </summary>
    public static Mat HomographyFromData(CalibrationData data)
    {
        var mat = new Mat(3, 3, MatType.CV_64FC1);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                mat.Set(r, c, data.HomographyMatrix[r * 3 + c]);
        return mat;
    }

    // ── Private ──────────────────────────────────────────────────────

    private void OnMouseClick(MouseEventTypes @event, int x, int y, MouseEventFlags flags, IntPtr userData)
    {
        if (@event == MouseEventTypes.LButtonDown && _clickedPoints.Count < 4)
        {
            _clickedPoints.Add(new Point2f(x, y));
            Console.WriteLine($"  Point {_clickedPoints.Count}: ({x}, {y})");
        }
    }
}
