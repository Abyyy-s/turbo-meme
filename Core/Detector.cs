using OpenCvSharp;
using OpenCvSharp.Dnn;
using CitizenRadar.Models;

namespace CitizenRadar.Core;

/// <summary>
/// Runs YOLOv8n ONNX inference via the OpenCV DNN module and returns
/// filtered vehicle detections.
///
/// Inference path: C# → OpenCvSharp4 → OpenCV DNN → yolov8n.onnx
///
/// ┌──────────────────────────────────────────────────────────────────┐
/// │ IMPORTANT: ONNX OUTPUT SHAPE VERIFICATION                       │
/// │                                                                  │
/// │ The post-processing code below is written for the standard       │
/// │ YOLOv8n ONNX export produced by:                                 │
/// │   yolo export model=yolov8n.pt format=onnx imgsz=640             │
/// │                                                                  │
/// │ Expected output tensor shape: [1, 84, 8400]                      │
/// │   - 84 = 4 (cx, cy, w, h) + 80 (COCO class scores)              │
/// │   - 8400 = number of anchor predictions                          │
/// │                                                                  │
/// │ This must be VERIFIED against the actual exported model before    │
/// │ relying on these assumptions. To verify:                         │
/// │                                                                  │
/// │ 1. Load the model and run a forward pass on a test image         │
/// │ 2. Print output.Size(0), output.Size(1), output.Size(2)         │
/// │ 3. Confirm the shape matches [1, 84, 8400]                      │
/// │ 4. Confirm bbox format is [cx, cy, w, h] (center format)        │
/// │ 5. Confirm class scores are in rows 4–83 (no separate           │
/// │    objectness score — YOLOv8 uses max class score directly)     │
/// │ 6. Confirm NMS is NOT built into the ONNX export                │
/// │                                                                  │
/// │ If the shape differs, update the parsing code accordingly.       │
/// └──────────────────────────────────────────────────────────────────┘
/// </summary>
public class Detector : IDisposable
{
    private readonly Net _net;
    private readonly int _inputSize;
    private bool _shapeVerified;

    /// <summary>
    /// Loads the YOLOv8n ONNX model via OpenCV DNN.
    /// </summary>
    /// <param name="onnxModelPath">Path to the yolov8n.onnx file.</param>
    /// <param name="inputSize">Input image size (default 640).</param>
    public Detector(string onnxModelPath, int inputSize = 640)
    {
        if (!File.Exists(onnxModelPath))
            throw new FileNotFoundException($"ONNX model not found: {onnxModelPath}");

        _net = CvDnn.ReadNetFromOnnx(onnxModelPath)
            ?? throw new InvalidOperationException($"Failed to load ONNX model via OpenCV DNN: {onnxModelPath}");
        _inputSize = inputSize;
        _shapeVerified = false;

        Console.WriteLine($"Detector: Loaded model from {onnxModelPath}");
    }

    /// <summary>
    /// Runs inference on a single frame and returns vehicle detections only.
    /// Applies confidence thresholding, NMS, and vehicle class filtering.
    /// </summary>
    public List<Detection> Detect(Mat frame)
    {
        int originalWidth = frame.Width;
        int originalHeight = frame.Height;

        // ── Preprocessing ─────────────────────────────────────────────
        // Create a blob: resize to 640×640, normalize to [0,1], swap R↔B
        var blob = CvDnn.BlobFromImage(
            frame,
            scaleFactor: 1.0 / 255.0,
            size: new Size(_inputSize, _inputSize),
            mean: new Scalar(0, 0, 0),
            swapRB: true,
            crop: false
        );

        _net.SetInput(blob);

        // ── Forward pass ──────────────────────────────────────────────
        var output = _net.Forward();

        // ── Shape verification (first frame only) ─────────────────────
        if (!_shapeVerified)
        {
            VerifyOutputShape(output);
            _shapeVerified = true;
        }

        // ── Post-processing ───────────────────────────────────────────
        var detections = ParseOutput(output, originalWidth, originalHeight);

        blob.Dispose();
        output.Dispose();

        return detections;
    }

    /// <summary>
    /// Verifies the ONNX output tensor shape and logs it for documentation.
    /// Called once on the first frame.
    /// </summary>
    private void VerifyOutputShape(Mat output)
    {
        int dims = output.Dims;
        var shapeStr = string.Join(" × ", Enumerable.Range(0, dims).Select(i => output.Size(i)));
        Console.WriteLine($"Detector: ONNX output shape = [{shapeStr}]");

        // Expected: [1, 84, 8400] for YOLOv8n with 80 COCO classes at 640×640
        if (dims == 3 && output.Size(1) == 84)
        {
            Console.WriteLine("Detector: Shape matches expected YOLOv8 format [1, 84, N].");
        }
        else
        {
            Console.WriteLine("WARNING: Output shape does not match expected [1, 84, 8400].");
            Console.WriteLine("         The post-processing code may need adjustment.");
        }
    }

    /// <summary>
    /// Parses the raw YOLOv8 output tensor into a list of vehicle detections.
    ///
    /// YOLOv8 output format (after export with default settings):
    ///   Shape: [1, 84, N]  where N = number of predictions (typically 8400)
    ///   Row 0: cx  (center x, normalised to input size)
    ///   Row 1: cy  (center y, normalised to input size)
    ///   Row 2: w   (width, normalised to input size)
    ///   Row 3: h   (height, normalised to input size)
    ///   Rows 4–83: class confidence scores for 80 COCO classes
    ///
    /// No separate objectness score — confidence = max(class scores).
    /// NMS is NOT included in the ONNX export and must be applied here.
    /// </summary>
    private List<Detection> ParseOutput(Mat output, int originalWidth, int originalHeight)
    {
        // output shape: [1, 84, N] — we need to access [row, col]
        // where row = attribute index (0–83), col = prediction index (0..N-1)
        int numAttributes = output.Size(1); // 84
        int numPredictions = output.Size(2); // 8400

        // Compute scale factors to map from YOLO input size back to original frame
        float xScale = (float)originalWidth / _inputSize;
        float yScale = (float)originalHeight / _inputSize;

        // Reshape to 2D for easier indexing: [84, N]
        var output2D = output.Reshape(1, numAttributes);

        // Collect candidates that pass the confidence threshold
        var boxes = new List<Rect>();
        var confidences = new List<float>();
        var classIds = new List<int>();

        unsafe
        {
            float* data = (float*)output2D.Data;
            int stride = numPredictions; // elements per row

            for (int i = 0; i < numPredictions; i++)
            {
                // Find the class with the highest score (rows 4–83)
                float maxScore = 0f;
                int maxClassId = -1;

                for (int c = 4; c < numAttributes; c++)
                {
                    float score = data[c * stride + i];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        maxClassId = c - 4; // COCO class ID = row index - 4
                    }
                }

                // Apply confidence threshold
                if (maxScore < Config.ConfidenceThreshold)
                    continue;

                // Filter for vehicle classes only
                if (!Config.VehicleClassIds.Contains(maxClassId))
                    continue;

                // Extract bounding box (center format → corner format)
                float cx = data[0 * stride + i];
                float cy = data[1 * stride + i];
                float w = data[2 * stride + i];
                float h = data[3 * stride + i];

                // Convert to top-left corner format and scale to original frame
                int x1 = (int)((cx - w / 2f) * xScale);
                int y1 = (int)((cy - h / 2f) * yScale);
                int bw = (int)(w * xScale);
                int bh = (int)(h * yScale);

                boxes.Add(new Rect(x1, y1, bw, bh));
                confidences.Add(maxScore);
                classIds.Add(maxClassId);
            }
        }

        output2D.Dispose();

        // ── Non-Maximum Suppression ───────────────────────────────────
        CvDnn.NMSBoxes(boxes, confidences, Config.ConfidenceThreshold, Config.NmsThreshold, out int[] indices);

        // ── Build final detection list ────────────────────────────────
        var detections = new List<Detection>();
        foreach (int idx in indices)
        {
            var box = boxes[idx];
            var bottomCenter = new Point2f(
                box.X + box.Width / 2f,
                box.Y + box.Height
            );

            detections.Add(new Detection
            {
                BBox = new Rect2f(box.X, box.Y, box.Width, box.Height),
                ClassId = classIds[idx],
                Confidence = confidences[idx],
                BottomCenter = bottomCenter
            });
        }

        return detections;
    }

    public void Dispose()
    {
        _net?.Dispose();
        GC.SuppressFinalize(this);
    }
}
