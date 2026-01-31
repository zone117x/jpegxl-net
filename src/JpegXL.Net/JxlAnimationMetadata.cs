// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license.

namespace JpegXL.Net;

/// <summary>
/// Animation metadata including basic info and all frame headers.
/// </summary>
public readonly struct JxlAnimationMetadata
{
    /// <summary>
    /// Per-frame header information.
    /// </summary>
    public readonly IReadOnlyList<JxlFrameHeader> Frames;

    /// <summary>
    /// Per-frame names, or null if names were not requested.
    /// Use <see cref="string.Empty"/> for frames without names.
    /// Index corresponds to <see cref="Frames"/>.
    /// </summary>
    public readonly IReadOnlyList<string>? FrameNames;

    /// <summary>
    /// Total duration of the animation in milliseconds.
    /// </summary>
    public float GetTotalDurationMs() => Frames?.Sum(f => f.DurationMs) ?? 0;

    /// <summary>
    /// Array of frame durations in milliseconds.
    /// </summary>
    public float[] GetFrameDurationsMs() => Frames?.Select(f => f.DurationMs > 0 ? f.DurationMs : 100f).ToArray() ?? [];

    /// <summary>
    /// Number of frames in the animation.
    /// </summary>
    public int FrameCount => Frames?.Count ?? 0;

    /// <summary>
    /// Creates a new animation metadata instance.
    /// </summary>
    public JxlAnimationMetadata(IReadOnlyList<JxlFrameHeader> frames, IReadOnlyList<string>? frameNames)
    {
        Frames = frames;
        FrameNames = frameNames;
    }
}
