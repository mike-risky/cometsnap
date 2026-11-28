using ZXing;
using ZXing.Common;

namespace Cometsnap.Core.Scanning;

/// <summary>A code found in an image: its text and its kind ("QR code", "Data Matrix", "EAN-13"...).</summary>
public sealed record ScannedCode(string Text, string Format);

/// <summary>
/// Finds QR codes (and other common 2D codes and barcodes) in a capture, offline, with ZXing.
/// Plain pixel code with no UI framework, shared by Windows and Linux (the Mac uses its built-in Vision).
/// </summary>
public static class CodeScanner
{
    private static readonly BarcodeFormat[] Formats =
    [
        BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417,
        BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.ITF,
    ];

    /// <summary>
    /// Every code in a 32-bit BGRA/BGRX image (alpha ignored), best first. Empty when there is none.
    /// </summary>
    public static IReadOnlyList<ScannedCode> Scan(byte[] bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return [];
        var found = Decode(Luminance(bgra, width, height, stride, 1), width, height);
        // Small codes on a small capture: a 2x enlargement helps the finder patterns stand out.
        if (found.Count == 0 && Math.Max(width, height) < 600)
            found = Decode(Luminance(bgra, width, height, stride, 2), width * 2, height * 2);
        return found;
    }

    /// <summary>
    /// The QR code in a normal capture, if there is one: QR codes only, so it stays quick on a big
    /// screenshot. Runs off the UI thread after the capture is already copied and saved.
    /// </summary>
    public static ScannedCode? FindQr(byte[] bgra, int width, int height, int stride)
    {
        if (width < 21 || height < 21) return null;
        return Decode(Luminance(bgra, width, height, stride, 1), width, height, [BarcodeFormat.QR_CODE]).FirstOrDefault();
    }

    private static List<ScannedCode> Decode(byte[] luminance, int w, int h, IList<BarcodeFormat>? formats = null)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = formats ?? Formats, CharacterSet = "UTF-8" },
        };
        // A white quiet zone around the picked area: a tightly boxed code still reads.
        // (Light-on-dark codes get a dark one too, as TryInverted flips the whole image.)
        var pad = Math.Max(16, Math.Max(w, h) / 8);
        (luminance, w, h) = (Pad(luminance, w, h, pad), w + 2 * pad, h + 2 * pad);
        var source = new PlanarYUVLuminanceSource(luminance, w, h, 0, 0, w, h, false);
        var results = reader.DecodeMultiple(source) ?? (reader.Decode(source) is { } one ? [one] : []);
        var list = new List<ScannedCode>();
        foreach (var r in results)
        {
            if (string.IsNullOrEmpty(r.Text) || list.Any(c => c.Text == r.Text)) continue;
            list.Add(new ScannedCode(r.Text, FormatName(r.BarcodeFormat)));
        }
        // QR codes first: that is what people scan screens for.
        return list.OrderBy(c => c.Format == "QR code" ? 0 : 1).ToList();
    }

    private static byte[] Pad(byte[] lum, int w, int h, int pad)
    {
        var ow = w + 2 * pad;
        var outp = new byte[ow * (h + 2 * pad)];
        Array.Fill(outp, (byte)255);
        for (var y = 0; y < h; y++) Buffer.BlockCopy(lum, y * w, outp, (y + pad) * ow + pad, w);
        return outp;
    }

    /// <summary>Gray levels (0.299 R + 0.587 G + 0.114 B), optionally enlarged by an integer factor.</summary>
    private static byte[] Luminance(byte[] bgra, int w, int h, int stride, int factor)
    {
        var ow = w * factor;
        var lum = new byte[ow * h * factor];
        for (var y = 0; y < h * factor; y++)
        {
            var row = (y / factor) * stride;
            for (var x = 0; x < ow; x++)
            {
                var i = row + (x / factor) * 4;
                lum[y * ow + x] = (byte)((bgra[i + 2] * 77 + bgra[i + 1] * 150 + bgra[i] * 29) >> 8);
            }
        }
        return lum;
    }

    private static string FormatName(BarcodeFormat f) => f switch
    {
        BarcodeFormat.QR_CODE => "QR code",
        BarcodeFormat.DATA_MATRIX => "Data Matrix",
        BarcodeFormat.AZTEC => "Aztec",
        BarcodeFormat.PDF_417 => "PDF417",
        BarcodeFormat.EAN_13 => "EAN-13",
        BarcodeFormat.EAN_8 => "EAN-8",
        BarcodeFormat.UPC_A => "UPC-A",
        BarcodeFormat.UPC_E => "UPC-E",
        BarcodeFormat.CODE_128 => "Code 128",
        BarcodeFormat.CODE_39 => "Code 39",
        BarcodeFormat.ITF => "ITF",
        _ => f.ToString(),
    };

    /// <summary>
    /// The web link in a code's text, if it is one (http or https only), else null. Cometsnap never
    /// opens it by itself: the card shows an Open button and the person decides.
    /// </summary>
    public static string? LinkOf(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t) || t.Any(char.IsWhiteSpace)) return null;
        if (!t.Contains("://") && t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
        return Uri.TryCreate(t, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(uri.Host)
            ? uri.AbsoluteUri
            : null;
    }
}
