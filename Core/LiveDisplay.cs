using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace CitizenRadar.Core;

/// <summary>
/// Cross-platform live video display window.
/// Attempts to use native OpenCV HighGUI (Cv2.NamedWindow / Cv2.ImShow).
/// If unavailable (e.g. headless Linux OpenCvSharp builds), transparently
/// pipes live frames to system 'ffplay' to open an interactive desktop window.
/// </summary>
public class LiveDisplay : IDisposable
{
    private readonly bool _hasCvGui;
    private readonly Process? _ffplayProcess;
    private readonly Stream? _ffplayStream;
    private readonly byte[]? _buffer;
    private readonly string _windowName;

    public bool IsActive { get; }

    public LiveDisplay(string windowTitle, int width, int height)
    {
        _windowName = windowTitle;

        // 1. Try OpenCV HighGUI
        try
        {
            Cv2.NamedWindow(_windowName, WindowFlags.Normal);
            _hasCvGui = true;
            IsActive = true;
            return;
        }
        catch
        {
            _hasCvGui = false;
        }

        // 2. Try FFplay window
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffplay",
                Arguments = $"-f rawvideo -pixel_format bgr24 -video_size {width}x{height} -window_title \"{windowTitle}\" -loglevel error -autoexit pipe:0",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _ffplayProcess = Process.Start(startInfo);
            if (_ffplayProcess != null)
            {
                _ffplayStream = _ffplayProcess.StandardInput.BaseStream;
                _buffer = new byte[width * height * 3];
                IsActive = true;
                return;
            }
        }
        catch
        {
            // Headless mode
        }

        IsActive = false;
    }

    /// <summary>
    /// Displays the annotated frame on screen.
    /// Returns false if user pressed ESC to quit or closed the window.
    /// </summary>
    public bool Show(Mat frame)
    {
        if (!IsActive) return true;

        if (_hasCvGui)
        {
            Cv2.ImShow(_windowName, frame);
            int key = Cv2.WaitKey(1);
            if (key == 27 || key == 'q' || key == 'Q')
                return false;
            return true;
        }

        if (_ffplayStream != null && _buffer != null)
        {
            if (_ffplayProcess != null && _ffplayProcess.HasExited)
                return false; // User closed the ffplay window

            try
            {
                Marshal.Copy(frame.Data, _buffer, 0, _buffer.Length);
                _ffplayStream.Write(_buffer, 0, _buffer.Length);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_hasCvGui)
        {
            try { Cv2.DestroyAllWindows(); } catch { }
        }

        if (_ffplayStream != null)
        {
            try { _ffplayStream.Flush(); } catch { }
            try { _ffplayStream.Close(); } catch { }
            _ffplayStream.Dispose();
        }

        if (_ffplayProcess != null && !_ffplayProcess.HasExited)
        {
            try { _ffplayProcess.Kill(); } catch { }
            _ffplayProcess.Dispose();
        }
    }
}
