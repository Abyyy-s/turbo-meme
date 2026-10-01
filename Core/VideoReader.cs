using System.Diagnostics;
using OpenCvSharp;

namespace CitizenRadar.Core;

/// <summary>
/// Cross-platform video frame reader.
/// Attempts to open video via native OpenCV VideoCapture.
/// If VideoCapture fails (e.g. headless Linux OpenCvSharp build lacking FFmpeg),
/// transparently falls back to streaming decoded raw BGR24 frames via system FFmpeg.
/// </summary>
public class VideoReader : IDisposable
{
    private readonly VideoCapture? _capture;
    private readonly Process? _ffmpegProcess;
    private readonly Stream? _ffmpegStream;
    private readonly byte[]? _buffer;
    private readonly bool _useFfmpeg;

    public int Width { get; }
    public int Height { get; }
    public double Fps { get; }
    public int TotalFrames { get; }
    public bool IsLiveCamera => TotalFrames < 0;

    private VideoReader(VideoCapture capture, int width, int height, double fps, int totalFrames)
    {
        _capture = capture;
        _useFfmpeg = false;
        Width = width;
        Height = height;
        Fps = fps;
        TotalFrames = totalFrames;
    }

    private VideoReader(Process ffmpegProcess, int width, int height, double fps, int totalFrames)
    {
        _ffmpegProcess = ffmpegProcess;
        _ffmpegStream = ffmpegProcess.StandardOutput.BaseStream;
        _useFfmpeg = true;
        _buffer = new byte[width * height * 3];
        Width = width;
        Height = height;
        Fps = fps;
        TotalFrames = totalFrames;
    }

    public static VideoReader OpenCamera(int deviceIndex = 0)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-loglevel error -f v4l2 -video_size 960x540 -framerate 30 -i /dev/video{deviceIndex} -f rawvideo -pix_fmt bgr24 pipe:1",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to launch ffmpeg for camera: {deviceIndex}");

        return new VideoReader(process, 960, 540, 30.0, -1);
    }

    /// <summary>
    /// Opens a video file using either native OpenCV or FFmpeg streaming.
    /// </summary>
    public static VideoReader Open(string videoPath)
    {
        if (!File.Exists(videoPath))
            throw new FileNotFoundException($"Video file not found: {videoPath}");

        // 1. Try OpenCV VideoCapture first
        try
        {
            var cap = new VideoCapture(videoPath);
            if (cap.IsOpened())
            {
                int w = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                int h = (int)cap.Get(VideoCaptureProperties.FrameHeight);
                double fps = cap.Get(VideoCaptureProperties.Fps);
                int total = (int)cap.Get(VideoCaptureProperties.FrameCount);
                if (w > 0 && h > 0)
                {
                    return new VideoReader(cap, w, h, fps > 0 ? fps : 30.0, total);
                }
            }
            cap.Dispose();
        }
        catch
        {
            // Fall back to FFmpeg
        }

        // 2. Fall back to system FFmpeg pipe
        return OpenWithFfmpeg(videoPath);
    }

    private static VideoReader OpenWithFfmpeg(string videoPath)
    {
        int width = 960, height = 540, totalFrames = 0;
        double fps = 30.0;

        // Query metadata using ffprobe
        try
        {
            var probe = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,nb_frames -of default=noprint_wrappers=1 \"{videoPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(probe);
            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim().Split('=');
                    if (parts.Length != 2) continue;
                    if (parts[0] == "width" && int.TryParse(parts[1], out int w)) width = w;
                    if (parts[0] == "height" && int.TryParse(parts[1], out int h)) height = h;
                    if (parts[0] == "nb_frames" && int.TryParse(parts[1], out int nf)) totalFrames = nf;
                    if (parts[0] == "r_frame_rate")
                    {
                        var rateParts = parts[1].Split('/');
                        if (rateParts.Length == 2 && double.TryParse(rateParts[0], out double num) && double.TryParse(rateParts[1], out double den) && den > 0)
                            fps = num / den;
                    }
                }
            }
        }
        catch
        {
            // Use defaults if ffprobe fails
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-loglevel error -i \"{videoPath}\" -f rawvideo -pix_fmt bgr24 pipe:1",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to launch ffmpeg to decode video: {videoPath}");

        return new VideoReader(process, width, height, fps, totalFrames);
    }

    /// <summary>
    /// Reads the next video frame into the provided Mat.
    /// Returns true if a frame was read, or false if EOF.
    /// </summary>
    public bool Read(Mat frame)
    {
        if (!_useFfmpeg)
        {
            return _capture!.Read(frame) && !frame.Empty();
        }

        int frameSize = Width * Height * 3;
        int bytesRead = 0;
        while (bytesRead < frameSize)
        {
            int r = _ffmpegStream!.Read(_buffer!, bytesRead, frameSize - bytesRead);
            if (r <= 0) break;
            bytesRead += r;
        }

        if (bytesRead < frameSize)
            return false;

        using var temp = Mat.FromPixelData(Height, Width, MatType.CV_8UC3, _buffer!);
        temp.CopyTo(frame);
        return true;
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _ffmpegStream?.Dispose();
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try { _ffmpegProcess.Kill(); } catch { }
            _ffmpegProcess.Dispose();
        }
    }
}
