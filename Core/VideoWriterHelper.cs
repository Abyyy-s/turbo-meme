using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace CitizenRadar.Core;

/// <summary>
/// Cross-platform video writer that outputs H.264 MP4 videos.
/// Attempts to open via OpenCV VideoWriter; if unavailable (headless Linux build),
/// transparently pipes raw BGR frames to FFmpeg to encode standard H.264 MP4.
/// </summary>
public class VideoWriterHelper : IDisposable
{
    private readonly VideoWriter? _cvWriter;
    private readonly Process? _ffmpegProcess;
    private readonly Stream? _ffmpegStream;
    private readonly byte[]? _buffer;
    private readonly bool _useFfmpeg;

    public bool IsOpened { get; }

    public VideoWriterHelper(string outputPath, int width, int height, double fps)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // 1. Try OpenCV VideoWriter first
        try
        {
            var writer = new VideoWriter(outputPath, FourCC.FromString("mp4v"), fps, new Size(width, height));
            if (writer.IsOpened())
            {
                _cvWriter = writer;
                _useFfmpeg = false;
                IsOpened = true;
                return;
            }
            writer.Dispose();
        }
        catch
        {
            // Fall back to FFmpeg
        }

        // 2. Fall back to FFmpeg pipe
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -f rawvideo -vcodec rawvideo -s {width}x{height} -pix_fmt bgr24 -r {fps:F2} -i pipe:0 -c:v libx264 -pix_fmt yuv420p -preset fast -loglevel error \"{outputPath}\"",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _ffmpegProcess = Process.Start(startInfo);
            if (_ffmpegProcess != null)
            {
                _ffmpegStream = _ffmpegProcess.StandardInput.BaseStream;
                _buffer = new byte[width * height * 3];
                _useFfmpeg = true;
                IsOpened = true;
                return;
            }
        }
        catch
        {
            // Logging will continue even if video writer fails
        }

        IsOpened = false;
    }

    public void Write(Mat frame)
    {
        if (!IsOpened) return;

        if (!_useFfmpeg)
        {
            _cvWriter?.Write(frame);
            return;
        }

        if (_ffmpegStream != null && _buffer != null)
        {
            Marshal.Copy(frame.Data, _buffer, 0, _buffer.Length);
            _ffmpegStream.Write(_buffer, 0, _buffer.Length);
        }
    }

    public void Dispose()
    {
        _cvWriter?.Dispose();
        if (_ffmpegStream != null)
        {
            try { _ffmpegStream.Flush(); } catch { }
            try { _ffmpegStream.Close(); } catch { }
            _ffmpegStream.Dispose();
        }
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try { _ffmpegProcess.WaitForExit(3000); } catch { }
            _ffmpegProcess.Dispose();
        }
    }
}
