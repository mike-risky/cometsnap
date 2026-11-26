using System.Runtime.CompilerServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Cometsnap.Video.Capture;

/// <summary>
/// DXGI Desktop Duplication: the GPU hands us the composed desktop of one monitor, we copy just
/// the recorded region into a small staging texture and read it back. Fast enough for 4K at 60 fps.
/// Only works when the region lies on a single, unrotated monitor.
/// </summary>
internal sealed class DuplicationFrameSource : IFrameSource
{
    private readonly ScreenRect _region;
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGIOutput1 _output = null!;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D _staging = null!;
    private int _offsetX, _offsetY;
    private bool _hasFrame;
    private int _failures;

    public string Name => "Desktop Duplication";

    private DuplicationFrameSource(ScreenRect region) => _region = region;

    /// <summary>Returns null when duplication cannot record this region (caller falls back to GDI).</summary>
    public static DuplicationFrameSource? TryCreate(ScreenRect region)
    {
        var source = new DuplicationFrameSource(region);
        try
        {
            if (source.Init()) return source;
        }
        catch (Exception ex)
        {
            VideoLog.E("Desktop duplication unavailable", ex);
        }
        source.Dispose();
        return null;
    }

    private bool Init()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var desc = output.Description;
                        var bounds = new ScreenRect(desc.DesktopCoordinates.Left, desc.DesktopCoordinates.Top,
                            desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left,
                            desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top);
                        if (!desc.AttachedToDesktop || !bounds.Contains(_region)) continue;
                        if (desc.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
                        {
                            VideoLog.I("Monitor is rotated: using GDI capture");
                            return false;
                        }

                        _offsetX = _region.X - bounds.X;
                        _offsetY = _region.Y - bounds.Y;
                        _output = output.QueryInterface<IDXGIOutput1>();
                        var hr = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 },
                            out ID3D11Device device, out ID3D11DeviceContext context);
                        if (hr.Failure || device is null || context is null) return false;
                        _device = device;
                        _context = context;
                        _staging = _device.CreateTexture2D(new Texture2DDescription
                        {
                            Width = (uint)_region.Width,
                            Height = (uint)_region.Height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = Format.B8G8R8A8_UNorm,
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Staging,
                            CPUAccessFlags = CpuAccessFlags.Read,
                            BindFlags = BindFlags.None,
                        });
                        return Duplicate() && WarmUp();
                    }
                }
            }
        }
        VideoLog.I("Region spans several monitors: using GDI capture");
        return false;
    }

    private bool Duplicate()
    {
        _duplication?.Dispose();
        _duplication = null;
        try
        {
            _duplication = _output.DuplicateOutput(_device);
            return true;
        }
        catch (SharpGenException ex)
        {
            VideoLog.E("DuplicateOutput failed", ex);
            return false;
        }
    }

    /// <summary>The first frame always holds the full desktop image; wait for it briefly.</summary>
    private bool WarmUp()
    {
        for (var i = 0; i < 10 && !_hasFrame; i++) Acquire(50);
        return true;
    }

    public bool Grab(FrameCanvas canvas)
    {
        if (!Acquire(0)) return false;
        if (!_hasFrame)
        {
            canvas.Span.Clear();
            return true;
        }
        var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            CopyRows(mapped.DataPointer, (int)mapped.RowPitch, canvas);
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
        return true;
    }

    private static unsafe void CopyRows(IntPtr src, int pitch, FrameCanvas canvas)
    {
        var dst = (byte*)canvas.Bits;
        var s = (byte*)src;
        var rowBytes = canvas.Stride;
        for (var y = 0; y < canvas.Height; y++)
            Unsafe.CopyBlockUnaligned(dst + (long)y * rowBytes, s + (long)y * pitch, (uint)rowBytes);
    }

    /// <summary>Pulls a new desktop frame into the staging texture if there is one. False = broken for good.</summary>
    private bool Acquire(uint timeoutMs)
    {
        if (_duplication is null && !Duplicate()) return Fail();
        var result = _duplication!.AcquireNextFrame(timeoutMs, out var info, out var resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout) return true;
        if (result == Vortice.DXGI.ResultCode.AccessLost)
        {
            // Mode change, UAC prompt, full-screen game... Re-create on the next tick.
            _duplication.Dispose();
            _duplication = null;
            return Fail();
        }
        if (result.Failure) return Fail();

        try
        {
            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            var box = new Vortice.Mathematics.Box(_offsetX, _offsetY, 0, _offsetX + _region.Width, _offsetY + _region.Height, 1);
            _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, texture, 0, box);
            _hasFrame = true;
            _failures = 0;
        }
        finally
        {
            resource.Dispose();
            _duplication.ReleaseFrame();
        }
        _ = info;
        return true;
    }

    /// <summary>Short hiccups are fine (the last frame is repeated); a long streak means give up.</summary>
    private bool Fail() => ++_failures < 300;

    public void Dispose()
    {
        _duplication?.Dispose();
        _staging?.Dispose();
        _output?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}
