namespace Cometsnap.Video;

public enum RecordingFormat
{
    Mp4,
    Gif,
}

/// <summary>A rectangle of the virtual screen in physical pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(ScreenRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
}

/// <summary>What to record and how.</summary>
public sealed record RecordingOptions
{
    public required ScreenRect Region { get; init; }
    public required string OutputPath { get; init; }
    public RecordingFormat Format { get; init; } = RecordingFormat.Mp4;

    /// <summary>Video frame rate (MP4). GIFs use <see cref="GifFramesPerSecond"/>.</summary>
    public int FramesPerSecond { get; init; } = 30;
    public int GifFramesPerSecond { get; init; } = 15;

    public bool ShowCursor { get; init; } = true;

    /// <summary>Sound playing on the PC (MP4 only).</summary>
    public bool SystemAudio { get; init; } = true;

    /// <summary>Default microphone (MP4 only).</summary>
    public bool Microphone { get; init; }

    /// <summary>Video bitrate in bits per second; 0 = picked from size and frame rate.</summary>
    public int VideoBitrate { get; init; }
}

/// <summary>What a finished recording produced.</summary>
public sealed record RecordingResult(string Path, TimeSpan Duration, long Bytes, RecordingFormat Format, bool HasAudio);
