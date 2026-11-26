namespace Cometsnap.Video.Capture;

/// <summary>
/// Plain GDI BitBlt from the screen. Slower than desktop duplication, but works everywhere
/// (regions across monitors, rotated screens, remote desktop). Layered windows such as our own
/// recording frame are not captured, which is what we want.
/// </summary>
internal sealed class GdiFrameSource : IFrameSource
{
    private readonly ScreenRect _region;
    private readonly IntPtr _screen;

    public string Name => "GDI";

    public GdiFrameSource(ScreenRect region)
    {
        _region = region;
        _screen = Native.GetDC(IntPtr.Zero);
    }

    public bool Grab(FrameCanvas canvas)
    {
        // No CAPTUREBLT: it makes the real mouse cursor flicker. We draw the cursor ourselves.
        // BitBlt fails briefly on the secure desktop (UAC prompts): keep the last frame then.
        Native.BitBlt(canvas.Hdc, 0, 0, canvas.Width, canvas.Height, _screen, _region.X, _region.Y, Native.SRCCOPY);
        return true;
    }

    public void Dispose() => Native.ReleaseDC(IntPtr.Zero, _screen);
}
