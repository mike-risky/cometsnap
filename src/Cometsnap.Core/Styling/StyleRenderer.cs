namespace Cometsnap.Core.Styling;

/// <summary>A styled capture: 32-bit BGRA pixels with straight (not premultiplied) alpha, rows of Width * 4 bytes.</summary>
public sealed record StyledPixels(int Width, int Height, byte[] Bgra)
{
    public int Stride => Width * 4;
}

/// <summary>
/// Draws a capture in a <see cref="SnapStyle"/>: backdrop, drop shadow, glow, border, then the
/// capture with rounded corners. Plain pixel code with no UI framework, shared by Windows and
/// Linux (the Mac has a line-by-line Swift port). The inside of the capture is a straight row
/// copy; only the frame around it is computed pixel by pixel, so it is fast enough for every capture.
/// </summary>
public static class StyleRenderer
{
    /// <summary>The measurements of a styled capture, in output pixels.</summary>
    public readonly record struct Layout(int Width, int Height, int ImageX, int ImageY);

    /// <summary>
    /// Canvas size and where the capture sits. <paramref name="scale"/> multiplies every size of the
    /// style (2 on a Retina Mac, so 10 px of shadow looks the same as on a normal screen).
    /// </summary>
    public static Layout Measure(int width, int height, SnapStyle style, double scale = 1)
    {
        var st = style.Normalized();
        var bw = st.Border ? st.BorderWidth * scale : 0;
        double left, right, top, bottom;
        if (st.Backdrop != StyleBackdrop.None)
        {
            left = right = top = bottom = st.Padding * scale + bw;
        }
        else
        {
            var glow = st.Glow ? st.GlowSize * scale * 1.3 + LineWidth(st, scale) : 0;
            var shadow = st.Shadow && st.ShadowOpacity > 0 ? st.ShadowBlur * scale * 1.6 + 1 : 0;
            var sx = st.Shadow ? st.ShadowX * scale : 0;
            var sy = st.Shadow ? st.ShadowY * scale : 0;
            left = bw + Math.Max(glow, shadow > 0 ? shadow - sx : 0);
            right = bw + Math.Max(glow, shadow > 0 ? shadow + sx : 0);
            top = bw + Math.Max(glow, shadow > 0 ? shadow - sy : 0);
            bottom = bw + Math.Max(glow, shadow > 0 ? shadow + sy : 0);
        }
        int L(double v) => (int)Math.Ceiling(Math.Max(0, v));
        return new Layout(width + L(left) + L(right), height + L(top) + L(bottom), L(left), L(top));
    }

    /// <summary>Width of the bright line of a glow: thin, like a laser, whatever the reach.</summary>
    private static double LineWidth(SnapStyle st, double scale) => Math.Max(1.5 * scale, Math.Min(4 * scale, st.GlowSize * scale / 8));

    /// <summary>
    /// Styles a capture. <paramref name="source"/> is 32-bit BGRA or BGRX (its alpha is ignored: a
    /// screenshot is opaque).
    /// </summary>
    public static StyledPixels Render(byte[] source, int width, int height, int stride, SnapStyle style, double scale = 1)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Empty image");
        var st = style.Normalized();
        var m = Measure(width, height, st, scale);
        var outW = m.Width;
        var outH = m.Height;
        var dst = new byte[outW * outH * 4];

        FillBackdrop(dst, outW, outH, st, source, width, height, stride);

        double left = m.ImageX, top = m.ImageY;
        var cx = left + width / 2.0;
        var cy = top + height / 2.0;
        var hx = width / 2.0;
        var hy = height / 2.0;
        var r = Math.Min(st.CornerRadius * scale, Math.Min(width, height) / 2.0);
        var bw = st.Border ? st.BorderWidth * scale : 0;
        var ohx = hx + bw;
        var ohy = hy + bw;
        var orad = r > 0 ? r + bw : 0;

        var shadowOn = st.Shadow && st.ShadowOpacity > 0;
        var shadowAlpha = st.ShadowOpacity / 100.0;
        var sigma = st.ShadowBlur * scale / 2;
        var sx = st.ShadowX * scale;
        var sy = st.ShadowY * scale;
        var (shr, shg, shb) = Split(StyleColors.Parse(st.ShadowColor, 0));

        var glowOn = st.Glow && st.GlowStrength > 0;
        var glowReach = st.GlowSize * scale;
        var glowStrength = st.GlowStrength / 100.0;
        var line = LineWidth(st, scale);
        var (gr, gg, gb) = Split(StyleColors.Parse(st.GlowColor, 0x3D6BFF));
        var (br, bg, bb) = Split(StyleColors.Parse(st.BorderColor, 0xFFFFFF));

        var inner = (int)Math.Ceiling(r) + 1; // rows this far inside are plain image in the middle

        Parallel.For(0, outH, y =>
        {
            var row = y * outW * 4;
            var py = y + 0.5;
            var insideRows = y >= top + inner && y < top + height - inner;
            for (var x = 0; x < outW; x++)
            {
                if (insideRows && x == m.ImageX + 1 && width > 2)
                {
                    // Fast path: the whole middle of this row is the capture, as is.
                    var count = width - 2;
                    CopyOpaque(source, (y - m.ImageY) * stride + 4, dst, row + x * 4, count);
                    x += count - 1;
                    continue;
                }

                var px = x + 0.5;
                var i = row + x * 4;
                // Start from the backdrop (straight alpha), work in premultiplied 0..1.
                double a = dst[i + 3] / 255.0;
                double b = dst[i] / 255.0 * a, g = dst[i + 1] / 255.0 * a, rr = dst[i + 2] / 255.0 * a;

                var d = RoundedBoxDistance(px - cx, py - cy, hx, hy, r);
                var dOuter = bw > 0 ? RoundedBoxDistance(px - cx, py - cy, ohx, ohy, orad) : d;

                if (shadowOn && dOuter > -1)
                {
                    var ds = RoundedBoxDistance(px - cx - sx, py - cy - sy, ohx, ohy, orad);
                    var cover = sigma < 0.5 ? Math.Clamp(0.5 - ds, 0, 1) : 0.5 * Erfc(ds / (sigma * 1.41421356));
                    Over(ref b, ref g, ref rr, ref a, shb / 255.0, shg / 255.0, shr / 255.0, shadowAlpha * cover);
                }

                if (glowOn && dOuter > -1)
                {
                    var dd = Math.Max(0, dOuter);
                    var t = Math.Max(0, 1 - dd / glowReach);
                    var ga = 0.8 * t * t + (dd < line ? 1 - dd / line : 0);
                    Over(ref b, ref g, ref rr, ref a, gb / 255.0, gg / 255.0, gr / 255.0, glowStrength * Math.Min(1, ga));
                }

                if (bw > 0)
                {
                    var cover = Math.Clamp(0.5 - dOuter, 0, 1);
                    if (cover > 0) Over(ref b, ref g, ref rr, ref a, bb / 255.0, bg / 255.0, br / 255.0, cover);
                }

                // The capture, with an anti-aliased rounded edge.
                var imageCover = Math.Clamp(0.5 - d, 0, 1);
                if (imageCover > 0)
                {
                    var ix = Math.Clamp(x - m.ImageX, 0, width - 1);
                    var iy = Math.Clamp(y - m.ImageY, 0, height - 1);
                    var si = iy * stride + ix * 4;
                    Over(ref b, ref g, ref rr, ref a, source[si] / 255.0, source[si + 1] / 255.0, source[si + 2] / 255.0, imageCover);
                }

                if (a <= 0.0001)
                {
                    dst[i] = dst[i + 1] = dst[i + 2] = dst[i + 3] = 0;
                    continue;
                }
                dst[i] = ToByte(b / a);
                dst[i + 1] = ToByte(g / a);
                dst[i + 2] = ToByte(rr / a);
                dst[i + 3] = ToByte(a);
            }
        });

        return new StyledPixels(outW, outH, dst);
    }

    /// <summary>The same picture on a solid color, without transparency (for JPG files and apps that ignore alpha).</summary>
    public static StyledPixels Flatten(StyledPixels image, uint rgb)
    {
        var (r, g, b) = Split(rgb);
        var src = image.Bgra;
        var dst = new byte[src.Length];
        for (var i = 0; i < src.Length; i += 4)
        {
            var a = src[i + 3];
            dst[i] = (byte)((src[i] * a + b * (255 - a) + 127) / 255);
            dst[i + 1] = (byte)((src[i + 1] * a + g * (255 - a) + 127) / 255);
            dst[i + 2] = (byte)((src[i + 2] * a + r * (255 - a) + 127) / 255);
            dst[i + 3] = 255;
        }
        return image with { Bgra = dst };
    }

    /// <summary>True when some pixel is not fully opaque.</summary>
    public static bool HasTransparency(StyledPixels image)
    {
        var p = image.Bgra;
        for (var i = 3; i < p.Length; i += 4)
            if (p[i] != 255) return true;
        return false;
    }

    /// <summary>Signed distance from a point (relative to the box center) to a rounded box: negative inside.</summary>
    internal static double RoundedBoxDistance(double px, double py, double hx, double hy, double r)
    {
        var qx = Math.Abs(px) - (hx - r);
        var qy = Math.Abs(py) - (hy - r);
        var ox = Math.Max(qx, 0);
        var oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    /// <summary>Complementary error function (Abramowitz and Stegun 7.1.26), plenty for a shadow.</summary>
    internal static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + 0.3275911 * z);
        var y = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429)))) * Math.Exp(-z * z);
        return x >= 0 ? y : 2 - y;
    }

    // ------------------------------------------------------------------ backdrops

    private static void FillBackdrop(byte[] dst, int w, int h, SnapStyle st, byte[] src, int sw, int sh, int stride)
    {
        var c1 = StyleColors.Parse(st.BackdropColor, 0x3D6BFF);
        var c2 = StyleColors.Parse(st.BackdropColor2, 0x07113A);
        switch (st.Backdrop)
        {
            case StyleBackdrop.Color:
                FillGradient(dst, w, h, c1, c1);
                break;
            case StyleBackdrop.Gradient:
                FillGradient(dst, w, h, c1, c2);
                break;
            case StyleBackdrop.Space:
                FillGradient(dst, w, h, c1, c2);
                Stars(dst, w, h);
                break;
            case StyleBackdrop.Blur:
                FillBlur(dst, w, h, src, sw, sh, stride);
                break;
            default:
                break; // transparent
        }
    }

    /// <summary>Diagonal gradient, top-left <paramref name="from"/> to bottom-right <paramref name="to"/>.</summary>
    private static void FillGradient(byte[] dst, int w, int h, uint from, uint to)
    {
        var (r0, g0, b0) = Split(from);
        var (r1, g1, b1) = Split(to);
        var span = Math.Max(1.0, w + (double)h - 2);
        Parallel.For(0, h, y =>
        {
            var row = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var t = (x + y) / span;
                var i = row + x * 4;
                dst[i] = (byte)(b0 + (b1 - b0) * t);
                dst[i + 1] = (byte)(g0 + (g1 - g0) * t);
                dst[i + 2] = (byte)(r0 + (r1 - r0) * t);
                dst[i + 3] = 255;
            }
        });
    }

    /// <summary>A sprinkle of soft stars, always the same pattern for the same size.</summary>
    private static void Stars(byte[] dst, int w, int h)
    {
        var random = new Random(2026);
        var count = Math.Clamp(w * h / 5000, 12, 1500);
        for (var n = 0; n < count; n++)
        {
            var x = random.Next(w);
            var y = random.Next(h);
            var big = random.NextDouble() < 0.12;
            var bright = 0.35 + random.NextDouble() * 0.65;
            var rad = big ? 1.6 : 0.9;
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var alpha = bright * Math.Clamp(1 - dist / (rad + 0.6), 0, 1);
                if (alpha <= 0) continue;
                var i = (yy * w + xx) * 4;
                for (var c = 0; c < 3; c++) dst[i + c] = (byte)(dst[i + c] + (255 - dst[i + c]) * alpha);
            }
        }
    }

    /// <summary>The capture itself, shrunk, blurred and stretched to cover the canvas, a little darker.</summary>
    private static void FillBlur(byte[] dst, int w, int h, byte[] src, int sw, int sh, int stride)
    {
        // 1. Shrink by averaging to about 64 pixels on the long side.
        var scale = Math.Max(sw, sh) / 64.0;
        var tw = Math.Max(1, (int)Math.Round(sw / scale));
        var th = Math.Max(1, (int)Math.Round(sh / scale));
        var small = new double[tw * th * 3];
        for (var ty = 0; ty < th; ty++)
        {
            int y0 = ty * sh / th, y1 = Math.Max(y0 + 1, (ty + 1) * sh / th);
            for (var tx = 0; tx < tw; tx++)
            {
                int x0 = tx * sw / tw, x1 = Math.Max(x0 + 1, (tx + 1) * sw / tw);
                double b = 0, g = 0, r = 0;
                var n = 0;
                // Sample at most 8×8 points per cell: enough for an average, fast on huge captures.
                var stepY = Math.Max(1, (y1 - y0) / 8);
                var stepX = Math.Max(1, (x1 - x0) / 8);
                for (var y = y0; y < y1; y += stepY)
                for (var x = x0; x < x1; x += stepX)
                {
                    var i = y * stride + x * 4;
                    b += src[i]; g += src[i + 1]; r += src[i + 2];
                    n++;
                }
                var o = (ty * tw + tx) * 3;
                small[o] = b / n; small[o + 1] = g / n; small[o + 2] = r / n;
            }
        }

        // 2. Blur: three box passes, close to a Gaussian.
        for (var pass = 0; pass < 3; pass++) BoxBlur(small, tw, th, radius: 3);

        // 3. Cover the canvas (keep the aspect ratio, crop the overflow), bilinear, 18% darker.
        var cover = Math.Max(w / (double)tw, h / (double)th);
        var offX = (tw * cover - w) / 2;
        var offY = (th * cover - h) / 2;
        Parallel.For(0, h, y =>
        {
            var fy = Math.Clamp((y + 0.5 + offY) / cover - 0.5, 0, th - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(y0 + 1, th - 1);
            var wy = fy - y0;
            var row = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var fx = Math.Clamp((x + 0.5 + offX) / cover - 0.5, 0, tw - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(x0 + 1, tw - 1);
                var wx = fx - x0;
                var i = row + x * 4;
                for (var c = 0; c < 3; c++)
                {
                    var top = small[(y0 * tw + x0) * 3 + c] * (1 - wx) + small[(y0 * tw + x1) * 3 + c] * wx;
                    var bot = small[(y1 * tw + x0) * 3 + c] * (1 - wx) + small[(y1 * tw + x1) * 3 + c] * wx;
                    dst[i + c] = ToByte((top * (1 - wy) + bot * wy) * 0.82 / 255.0);
                }
                dst[i + 3] = 255;
            }
        });
    }

    private static void BoxBlur(double[] px, int w, int h, int radius)
    {
        var tmp = new double[px.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var c = 0; c < 3; c++)
        {
            double sum = 0;
            var n = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var xx = Math.Clamp(x + k, 0, w - 1);
                sum += px[(y * w + xx) * 3 + c];
                n++;
            }
            tmp[(y * w + x) * 3 + c] = sum / n;
        }
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var c = 0; c < 3; c++)
        {
            double sum = 0;
            var n = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var yy = Math.Clamp(y + k, 0, h - 1);
                sum += tmp[(yy * w + x) * 3 + c];
                n++;
            }
            px[(y * w + x) * 3 + c] = sum / n;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void CopyOpaque(byte[] src, int srcIndex, byte[] dst, int dstIndex, int pixels)
    {
        Buffer.BlockCopy(src, srcIndex, dst, dstIndex, pixels * 4);
        var end = dstIndex + pixels * 4;
        for (var i = dstIndex + 3; i < end; i += 4) dst[i] = 255; // BGRX sources have junk alpha
    }

    /// <summary>Premultiplied "source over": paints color (b, g, r) with coverage <paramref name="alpha"/>.</summary>
    private static void Over(ref double b, ref double g, ref double r, ref double a, double sb, double sg, double sr, double alpha)
    {
        b = sb * alpha + b * (1 - alpha);
        g = sg * alpha + g * (1 - alpha);
        r = sr * alpha + r * (1 - alpha);
        a = alpha + a * (1 - alpha);
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    private static (byte R, byte G, byte B) Split(uint rgb) => ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Blends two 0xRRGGBB colors: t = 0 gives <paramref name="a"/>, 1 gives <paramref name="b"/>.</summary>
    internal static uint Mix(uint a, uint b, double t)
    {
        var (r0, g0, b0) = Split(a);
        var (r1, g1, b1) = Split(b);
        uint C(byte x, byte y) => (uint)Math.Round(x + (y - x) * t);
        return (C(r0, r1) << 16) | (C(g0, g1) << 8) | C(b0, b1);
    }
}
