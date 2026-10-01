using OpenCvSharp;
using CitizenRadar.Models;

namespace CitizenRadar.Core;

/// <summary>
/// Lightweight IoU-based multi-object tracker.
///
/// Assigns persistent IDs to vehicle detections across frames using greedy
/// IoU (Intersection over Union) matching. This is a straightforward tracker
/// suitable for controlled and demonstration traffic scenes.
///
/// For dense, heavily occluded, multi-lane highway scenarios, a more
/// sophisticated tracker (such as ByteTrack or DeepSORT) would be a
/// future improvement.
///
/// Algorithm per frame:
/// 1. Compute IoU between every active track's last bounding box and
///    every new detection's bounding box.
/// 2. Sort all (track, detection) pairs by IoU descending.
/// 3. Greedily assign: highest IoU pair first; skip already-matched tracks
///    or detections.
/// 4. Matched pairs → update the track's position history.
/// 5. Unmatched detections → create new tracks with fresh IDs.
/// 6. Unmatched tracks → increment missedFrames; deregister after timeout.
/// </summary>
public class Tracker
{
    private readonly Dictionary<int, Track> _activeTracks = new();
    private int _nextId = 1;

    /// <summary>
    /// Takes this frame's detections, matches them to existing tracks using
    /// greedy IoU matching, and returns all currently active tracks.
    /// </summary>
    /// <param name="detections">Vehicle detections for the current frame.</param>
    /// <param name="frameNumber">Current frame index (0-based).</param>
    /// <returns>List of all active tracks with updated positions.</returns>
    public List<Track> Update(List<Detection> detections, int frameNumber)
    {
        if (_activeTracks.Count == 0 && detections.Count == 0)
            return new List<Track>();

        // ── Step 1: Compute IoU for all (track, detection) pairs ──────
        var iouPairs = new List<(int TrackId, int DetIdx, float Iou)>();

        var trackIds = _activeTracks.Keys.ToList();
        for (int t = 0; t < trackIds.Count; t++)
        {
            var track = _activeTracks[trackIds[t]];
            for (int d = 0; d < detections.Count; d++)
            {
                float iou = ComputeIoU(track.LastBBox, detections[d].BBox);
                if (iou >= Config.IouThreshold)
                {
                    iouPairs.Add((trackIds[t], d, iou));
                }
            }
        }

        // ── Step 2: Sort by IoU descending ────────────────────────────
        iouPairs.Sort((a, b) => b.Iou.CompareTo(a.Iou));

        // ── Step 3: Greedy assignment ─────────────────────────────────
        var matchedTrackIds = new HashSet<int>();
        var matchedDetIndices = new HashSet<int>();

        foreach (var (trackId, detIdx, iou) in iouPairs)
        {
            if (matchedTrackIds.Contains(trackId) || matchedDetIndices.Contains(detIdx))
                continue;

            // Match found: update the track
            var track = _activeTracks[trackId];
            var det = detections[detIdx];

            track.LastBBox = det.BBox;
            track.MissedFrames = 0;
            track.ClassId = det.ClassId;
            track.PositionHistory.Add((frameNumber, det.BottomCenter));

            // Cap history size to prevent unbounded memory growth
            // Speed estimator uses at most SpeedWindowFrames (30), so 60 is ample
            if (track.PositionHistory.Count > 60)
                track.PositionHistory.RemoveAt(0);

            matchedTrackIds.Add(trackId);
            matchedDetIndices.Add(detIdx);
        }

        // ── Step 4: Create new tracks for unmatched detections ────────
        for (int d = 0; d < detections.Count; d++)
        {
            if (matchedDetIndices.Contains(d))
                continue;

            var det = detections[d];
            var newTrack = new Track(_nextId++, det.ClassId, det.BBox, frameNumber, det.BottomCenter);
            _activeTracks[newTrack.TrackId] = newTrack;
        }

        // ── Step 5: Handle unmatched tracks ───────────────────────────
        var tracksToRemove = new List<int>();
        foreach (var trackId in _activeTracks.Keys)
        {
            if (matchedTrackIds.Contains(trackId))
                continue;

            _activeTracks[trackId].MissedFrames++;

            if (_activeTracks[trackId].MissedFrames > Config.TrackMaxMissedFrames)
            {
                tracksToRemove.Add(trackId);
            }
        }

        foreach (var id in tracksToRemove)
        {
            _activeTracks.Remove(id);
        }

        // Return all active tracks (including newly created ones)
        return _activeTracks.Values.ToList();
    }

    /// <summary>
    /// Returns the total number of unique vehicle IDs ever assigned.
    /// This represents the total number of distinct vehicles observed.
    /// </summary>
    public int TotalVehiclesCounted => _nextId - 1;

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Computes the Intersection over Union (IoU) between two bounding boxes.
    /// IoU = Area(Intersection) / Area(Union)
    /// Returns 0 if the boxes do not overlap.
    /// </summary>
    private static float ComputeIoU(Rect2f a, Rect2f b)
    {
        float x1 = Math.Max(a.X, b.X);
        float y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        float interWidth = Math.Max(0, x2 - x1);
        float interHeight = Math.Max(0, y2 - y1);
        float interArea = interWidth * interHeight;

        if (interArea == 0)
            return 0f;

        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;
        float unionArea = areaA + areaB - interArea;

        return unionArea > 0 ? interArea / unionArea : 0f;
    }
}
