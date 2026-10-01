# 🛰️ CitizenRadar — Complete AI Handover & Project Context

> **Target Audience:** Future AI coding agents and human engineers taking over development, maintenance, or analysis of this codebase.  
> **Repository:** `https://github.com/Abyyy-s/CitizenRadar.git`  
> **Workspace Root:** `/home/abyyy/Projects/CitezensRadar/CitizenRadar`  
> **Created Date:** October 2026  
> **Build Status:** `.NET 8.0` — **0 Warnings, 0 Errors** ✅

---

## 1. Executive Summary & Core Mission

**CitizenRadar** is a high-performance, real-time AI Traffic Watchdog and monocular vehicle speed estimator built in **C# (.NET 8)**. It runs an end-to-end Computer Vision pipeline coupled to an embedded **ASP.NET Core (Kestrel)** web server, broadcasting a live annotated MJPEG video stream and telemetry dashboard to any web browser or mobile phone on the local network.

### The Pipeline:
$$\text{Video / Camera Feed} \xrightarrow{\text{VideoReader}} \text{YOLOv8 ONNX} \xrightarrow{\text{Detector}} \text{IoU Tracking} \xrightarrow{\text{SpeedEstimator (Homography)}} \text{Visualizer} \xrightarrow{\text{Kestrel HTTP}} \text{Web UI}$$

---

## 2. Technical Stack & Environmental Constraints

> [!IMPORTANT]
> **Operating System & Native Runtime Constraints (DO NOT OVERRIDE):**
> * **OS:** Fedora Linux (x86_64, Wayland session).
> * **SDK:** `Microsoft.NET.Sdk.Web` (.NET 8.0.131).
> * **OpenCvSharp Packages:** Standard NuGet Linux runtimes crash on modern Fedora with `SIGABRT` symbol resolution errors. **ONLY** the following runtime package works:
>   - `OpenCvSharp4` v4.10.0.20241108
>   - `OpenCvSharp4.unofficial.runtime.linux-x64` v4.10.0.20241108
> * **Headless OpenCV Limitation:** The Linux NuGet packages are compiled **WITHOUT** HighGUI and **WITHOUT** FFmpeg support. Calling `new VideoCapture(path)` or `Cv2.ImShow()` fails or crashes on Linux.
>   - **Solution in place:** `Core/VideoReader.cs` transparently falls back to spawning system `ffmpeg` (v8.1.3) via a process pipe (`-f rawvideo -pix_fmt bgr24 pipe:1`). **Always use `VideoReader`, never use raw `VideoCapture`.**

---

## 3. Architecture & File Registry

```
CitizenRadar/
├── CitizenRadar.csproj           # Web SDK project file with Unofficially patched OpenCvSharp
├── Config.cs                    # Central tuning hub (thresholds, window sizes, speed limits)
├── Program.cs                   # Kestrel Minimal API, REST endpoints, hosted background worker
│
├── Core/
│   ├── VideoReader.cs           # Cross-platform video decoder (FFmpeg pipe fallback + webcam v4l2)
│   ├── Detector.cs              # YOLOv8 ONNX runtime inference, unsafe tensor output parsing
│   ├── Tracker.cs               # Multi-object greedy IoU tracker (caps history at 60 frames)
│   ├── SpeedEstimator.cs        # Perspective homography, Y-axis speed calculation, horizon gate
│   ├── Calibrator.cs            # Homography computation, JSON serialization, Roboflow fallback
│   ├── Visualizer.cs            # Frame annotation (boxes, speeds, bird's-eye minimap, stats bar)
│   └── VideoWriterHelper.cs     # Video export utility via FFmpeg pipe
│
├── Models/
│   ├── Detection.cs             # Detection record (BBox, ClassId, Confidence, BottomCenter anchor)
│   ├── Track.cs                 # Track class (TrackId, PositionHistory, SpeedKmh, IsViolation)
│   └── CalibrationData.cs       # Serialized homography matrix and road dimensions
│
├── Web/
│   ├── WatchdogService.cs       # BackgroundService running the CV loop; dynamic source switcher
│   ├── FrameBroadcaster.cs      # MJPEG multipart stream broadcaster to HTTP clients
│   └── TelemetryStore.cs        # Thread-safe telemetry snapshot & recent violations queue
│
├── wwwroot/                     # High-performance Vanilla HTML/CSS/JS frontend
│   ├── index.html               # Semantic UI (video player, controls, metrics, violation log)
│   ├── app.css                  # Dark-mode radar theme, scanlines, glow effects, responsive grid
│   └── app.js                   # Telemetry polling, interactive 4-point canvas calibration, toasts
│
├── models/
│   └── yolov8n.onnx             # YOLOv8 Nano model weights (640×640 input, ~12 MB)
│
└── data/
    ├── traffic.mp4              # Test highway video (960×540, 29.97 fps, 335 frames)
    └── uploads/                 # Storage for user-uploaded videos via the web dashboard
```

---

## 4. Key Endpoints & REST API

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/stream` | Continuous MJPEG stream (`multipart/x-mixed-replace; boundary=--frame`) |
| `GET` | `/api/status` | JSON telemetry snapshot (FPS, counts, avg speed, source mode, `needsCalibration`) |
| `GET` | `/api/violations` | Returns recent 50 violation events with timestamp, class, and overspeed delta |
| `POST` | `/api/config` | Update speed limit live (`{ "speedLimitKmh": 50.0 }`) |
| `POST` | `/api/calibrate` | Submit 4 canvas points + real-world width/height to update homography live |
| `POST` | `/api/source/upload` | Multipart file upload (`.mp4, .avi, .mkv, .mov, .webm` up to 500 MB) |
| `POST` | `/api/source/live` | Switch to live webcam (`{ "device": 0 }` via `/dev/video0`) |
| `GET` | `/api/source` | Get current active video source and mode (`file` or `camera`) |

---

## 5. Chronological History & Problems Overcome

### Phase 1: Native Library Crisis (Fedora Linux)
* **Problem:** Application crashed immediately with `SIGABRT` and core dump.
* **Fix:** Replaced standard `OpenCvSharp4.runtime.ubuntu` / `centos` packages with `OpenCvSharp4.unofficial.runtime.linux-x64` v4.10.0.20241108.

### Phase 2: Missing OpenCV HighGUI & VideoCapture FFmpeg
* **Problem:** OpenCV `VideoCapture` returned empty frames because Linux NuGet builds lacked FFmpeg integration. `Cv2.ImShow` threw `NotImplementedException`.
* **Fix:** Architected `Core/VideoReader.cs` and `Core/VideoWriterHelper.cs` to pipe decoded raw BGR24 frames directly through system FFmpeg.

### Phase 3: The 7154 km/h Speed Spike Bug
* **Problem:** Vehicles near the top of the screen projected behind or on the perspective horizon, causing division by near-zero $W$ in $[X, Y, W]^T = H \times [u, v, 1]^T$, yielding astronomical speed calculations.
* **Fix:** Implemented horizon guard ($W > 0.05$) and `MaxPlausibleSpeedKmh = 180.0` clamp.

### Phase 4: Full Web Dashboard Implementation
* **Problem:** Terminal-only interface was inaccessible on modern Wayland desktops and mobile.
* **Fix:** Migrated project to `Microsoft.NET.Sdk.Web`, embedded Kestrel, and created a sleek dark-mode radar dashboard with real-time MJPEG streaming and interactive controls.

### Phase 5: Dashboard V2 — Uploads & Live Camera
* **Additions:** Implemented drag-and-drop / button video uploads (up to 500 MB), dynamic pipeline source switching in `WatchdogService.cs`, and live webcam streaming via `ffmpeg -f v4l2 -i /dev/video0`.

### Phase 6: Speed Accuracy Overhaul (Roboflow Tutorial Comparison)
* **Problem:** Speeds on `data/traffic.mp4` were wildly inaccurate (showing 25–45 km/h for 100 km/h highway traffic; side-by-side cars differed by 50+ km/h).
* **Investigation:** Analyzed the reference YouTube video (`https://www.youtube.com/watch?v=uWP6UjDeZvY`) by Piotr Skalski (Roboflow), which uses this **exact same video file**.
* **Fixes Applied:**
  1. **Calibration Scale Corrected:** Default dimensions changed from $10\text{m} \times 50\text{m}$ to **$25\text{m} \times 250\text{m}$** (the true length of that highway stretch).
  2. **Window Size Expanded:** Window expanded from 5 frames (0.16s) to **30 frames (1.0s)** with a 15-frame minimum requirement (`MinFramesBeforeSpeed = 15`), extinguishing frame-to-frame bounding box jitter.
  3. **Y-Axis Only Distance:** Discarded horizontal bounding box wobble (`Math.Abs(worldNow.Y - worldPast.Y)` instead of 2D Euclidean distance).
  4. **Horizon Distance Gate:** Detections in the top 40% of the frame (`HorizonGateRatio = 0.40`) are skipped for speed estimation because perspective compression makes them inherently noisy.
  5. **New Video Calibration Prompt:** Added `NeedsCalibration` flag in `TelemetryStore`. When a new video is uploaded, an amber banner prompts the user to calibrate road geometry.

---

## 6. The Mathematics of Monocular Speed Estimation

```
Camera Frame (Pixel Space)
    │
    ▼ Anchor: (BBox.X + BBox.Width / 2, BBox.Y + BBox.Height) [Bottom-Center]
[u, v, 1]^T
    │
    ▼ Homography Transformation Matrix H (3×3)
[X, Y, W]^T = H × [u, v, 1]^T
    │
    ▼ Perspective Division (if W > 0.05 and v > FrameHeight * 0.40)
x_world = X / W  (meters)
y_world = Y / W  (meters)
    │
    ▼ Displacement along road over 30-frame window
Δdistance = |y_world(t) - y_world(t - Δt)|  (meters)
Δt = (Frame(t) - Frame(t - Δt)) / FPS       (seconds)
    │
    ▼ Speed Conversion & EMA Smoothing
Speed = (Δdistance / Δt) × 3.6              (km/h)
SmoothedSpeed = α × Speed + (1 - α) × PrevSpeed  (α = 0.3)
```

---

## 7. Current Project Status & Backlog

### ✅ What is Fully Functional:
1. YOLOv8 Nano object detection on CPU/Edge.
2. Cross-platform FFmpeg video decoding & live webcam capture (`/dev/video0`).
3. HTTP MJPEG stream + live stats overlay + minimap.
4. Interactive in-browser 4-point road calibration tool.
5. Real-time speed limit tuning with instant violation flagging.
6. Video file upload and dynamic switching.
7. Speed accuracy fixes (25m×250m scale, 30-frame window, Y-axis measurement, horizon gate, calibration banner).

### ⏳ Future Backlog / Recommended Next Steps:
1. **Kalman Filter / ByteTrack Implementation (`Core/Tracker.cs`):**
   * *Current State:* Tracker uses simple greedy IoU matching. If a car moves rapidly or detection drops for 1 frame, track ID can flicker.
   * *Next Step:* Implement an 8-state linear Kalman filter ($[x, y, w, h, \dot{x}, \dot{y}, \dot{w}, \dot{h}]$) or port ByteTrack to C# so tracks persist across temporary occlusions.
2. **Polygon Zone Masking:**
   * Drop detections that fall outside the calibrated road trapezoid to avoid picking up trees, oncoming lanes, or off-ramps.
3. **Motion Trace Visualizer:**
   * Draw smooth motion trail lines behind vehicles (like Roboflow's `TraceAnnotator`).
4. **Database Persistence:**
   * Currently, violations are kept in an in-memory ring buffer (last 50 items). Add SQLite/PostgreSQL persistence.

---

## 8. Quickstart Guide (How to Run & Verify)

### Build:
```bash
cd /home/abyyy/Projects/CitezensRadar/CitizenRadar
dotnet build
```

### Run with Default Highway Video:
```bash
dotnet run -- data/traffic.mp4
```

### Access Dashboard:
* Open `http://localhost:5000` in any web browser.
* From mobile / local network: `http://<LAN_IP>:5000`.

---
*Authored by Antigravity AI Pair Programmer for Abyyy-s/CitizenRadar.*
