using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Cometsnap.App.Services;

/// <summary>What text recognition found.</summary>
public sealed record OcrOutcome(string Text, int Lines, string? Language, bool EngineMissing)
{
    public bool Found => Text.Length > 0;
}

/// <summary>
/// Reads the text in an image with the OCR engine built into Windows (Windows.Media.Ocr).
/// Everything stays on the PC: no upload, no account. It uses the languages installed in
/// Windows (Settings &gt; Time &amp; language), and works best on screen text at its normal size.
/// </summary>
public static class OcrService
{
    /// <summary>Small screen text reads much better when it is scaled up first.</summary>
    private const int UpscaleBelow = 1600;

    private static OcrEngine? _engine;
    private static bool _triedEngine;

    /// <summary>The language the engine reads, like "en-US" (null when Windows has no OCR language).</summary>
    public static string? EngineLanguage => Engine()?.RecognizerLanguage?.LanguageTag;

    private static OcrEngine? Engine()
    {
        if (_triedEngine) return _engine;
        _triedEngine = true;
        try
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (_engine is null)
            {
                var any = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (any is not null) _engine = OcrEngine.TryCreateFromLanguage(any);
            }
            Log.Info(_engine is null ? "OCR: no recognizer language installed" : $"OCR engine: {_engine.RecognizerLanguage.LanguageTag}");
        }
        catch (Exception ex)
        {
            Log.Error("OCR engine is not available", ex);
        }
        return _engine;
    }

    public static async Task<OcrOutcome> RecognizeAsync(BitmapSource image)
    {
        var engine = Engine();
        if (engine is null) return new OcrOutcome("", 0, null, EngineMissing: true);

        var source = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        var max = (int)OcrEngine.MaxImageDimension;
        var scale = Math.Max(width, height) < UpscaleBelow ? 2 : 1;
        if (width * scale > max) scale = 1;
        var bandRows = Math.Max(64, max / scale - 8);

        var lines = new List<RecognizedLine>();
        foreach (var (start, length) in OcrText.Bands(EmptyRows(pixels, width, height), bandRows))
        {
            var band = new byte[stride * length];
            Buffer.BlockCopy(pixels, start * stride, band, 0, band.Length);
            var bandWidth = width;
            if (width > max)
            {
                // Wider than the engine takes: shrink this band to fit.
                var shrink = BitmapSource.Create(width, length, 96, 96, PixelFormats.Bgra32, null, band, stride);
                var factor = (double)max / width;
                var small = new TransformedBitmap(shrink, new ScaleTransform(factor, factor));
                bandWidth = small.PixelWidth;
                var smallHeight = small.PixelHeight;
                band = new byte[bandWidth * 4 * smallHeight];
                small.CopyPixels(band, bandWidth * 4, 0);
                await AddLines(engine, band, bandWidth, smallHeight, 1 / factor, start, lines);
                continue;
            }
            if (scale > 1)
            {
                var bmp = BitmapSource.Create(width, length, 96, 96, PixelFormats.Bgra32, null, band, stride);
                var big = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
                var w2 = big.PixelWidth;
                var h2 = big.PixelHeight;
                var scaled = new byte[w2 * 4 * h2];
                big.CopyPixels(scaled, w2 * 4, 0);
                await AddLines(engine, scaled, w2, h2, 1.0 / scale, start, lines);
            }
            else
            {
                await AddLines(engine, band, width, length, 1, start, lines);
            }
        }

        var text = OcrText.Join(lines);
        return new OcrOutcome(text, lines.Count, engine.RecognizerLanguage?.LanguageTag, EngineMissing: false);
    }

    private static async Task AddLines(OcrEngine engine, byte[] bgra, int width, int height, double toImage, int offsetY, List<RecognizedLine> into)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            double left = double.MaxValue, top = double.MaxValue, bottom = 0;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                bottom = Math.Max(bottom, r.Y + r.Height);
            }
            into.Add(new RecognizedLine(line.Text, left * toImage, offsetY + top * toImage, (bottom - top) * toImage));
        }
    }

    /// <summary>Rows of a single color: safe places to cut a tall image.</summary>
    private static bool[] EmptyRows(byte[] bgra, int width, int height)
    {
        var empty = new bool[height];
        var stride = width * 4;
        for (var y = 0; y < height; y++)
        {
            var row = bgra.AsSpan(y * stride, stride);
            var first = row[0] | row[1] << 8 | row[2] << 16;
            var same = true;
            for (var i = 4; i < row.Length && same; i += 4)
                same = (row[i] | row[i + 1] << 8 | row[i + 2] << 16) == first;
            empty[y] = same;
        }
        return empty;
    }
}
