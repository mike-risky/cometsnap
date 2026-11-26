namespace Cometsnap.Video.Capture;

/// <summary>Fills a frame with the current pixels of the recorded region.</summary>
internal interface IFrameSource : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Writes the latest screen content into <paramref name="canvas"/> (always the whole frame,
    /// so the previous cursor is overwritten). Returns false if the source broke for good.
    /// </summary>
    bool Grab(FrameCanvas canvas);
}
