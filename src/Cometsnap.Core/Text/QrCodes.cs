using System.Text;
using ZXing;
using ZXing.Common;

namespace Cometsnap.Core.Text;

/// <summary>What a scanned code holds, so the result can offer the right button.</summary>
public enum CodeKind { Text, Link, WiFi, Email, Phone }

/// <summary>A Wi-Fi network from a "WIFI:S:name;T:WPA;P:secret;;" code.</summary>
public sealed record WifiNetwork(string Name, string? Password, string Security, bool Hidden);

/// <summary>One QR code (or barcode) read from the screen.</summary>
public sealed record ScannedCode(string Text, string Format)
{
    public CodeKind Kind => QrCodes.KindOf(Text);

    /// <summary>The web address to offer behind "Open link" (http/https only), or null.</summary>
    public string? Link => QrCodes.OpenableLink(Text);

    public WifiNetwork? Wifi => QrCodes.ParseWifi(Text);

    /// <summary>What goes on the clipboard: the Wi-Fi password for a Wi-Fi code, else the whole text.</summary>
    public string ClipboardText => Wifi is { Password: { Length: > 0 } password } ? password : Text;
}

/// <summary>
/// Reads QR codes and barcodes in screen pixels, offline (ZXing.Net). Screen codes are sharp, but
/// people drag tight boxes around them, so the image gets a white margin (codes need a quiet zone),
/// and light-on-dark codes and tiny ones get a second try.
/// </summary>
public static class QrCodes
{
    /// <summary>2D codes plus the common barcodes. 1D barcodes are only looked for on purpose (the hotkey).</summary>
    private static readonly List<BarcodeFormat> AllFormats =
    [
        BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417,
        BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.CODE_93, BarcodeFormat.ITF, BarcodeFormat.CODABAR,
    ];

    private static readonly List<BarcodeFormat> QrOnly = [BarcodeFormat.QR_CODE];

    /// <summary>Above this, the quick check after a normal capture skips the image (it must stay cheap).</summary>
    public const long QuickScanMaxPixels = 12_000_000;

    /// <summary>
    /// Finds the codes in 32-bit BGRA pixels (the layout of a WPF/Avalonia screen grab).
    /// <paramref name="quick"/> = only QR codes, no extra tries: the silent check after each capture.
    /// </summary>
    public static IReadOnlyList<ScannedCode> Scan(byte[] bgra, int width, int height, int stride, bool quick = false)
    {
        if (width <= 0 || height <= 0) return [];
        if (quick && (long)width * height > QuickScanMaxPixels) return [];
        var gray = ToGray(bgra, width, height, stride);
        return ScanGray(gray, width, height, quick);
    }

    /// <summary>Same as <see cref="Scan"/>, for 8-bit gray pixels (one byte per pixel, no padding).</summary>
    public static IReadOnlyList<ScannedCode> ScanGray(byte[] gray, int width, int height, bool quick = false)
    {
        if (width <= 0 || height <= 0) return [];
        var margin = Math.Clamp(Math.Min(width, height) / 8, 16, 64);
        var (padded, pw, ph) = Pad(gray, width, height, margin, 255);
        var found = Decode(padded, pw, ph, quick);
        if (found.Count > 0 || quick) return found;

        // Light code on a dark page (dark mode): flip it.
        var inverted = Invert(gray);
        (padded, pw, ph) = Pad(inverted, width, height, margin, 255);
        found = Decode(padded, pw, ph, quick: false);
        if (found.Count > 0) return found;

        // Tiny codes (1-2 screen pixels per module): look again at twice the size.
        if (Math.Max(width, height) <= 1200)
        {
            var (big, bw, bh) = Upscale2x(gray, width, height);
            (padded, pw, ph) = Pad(big, bw, bh, margin * 2, 255);
            found = Decode(padded, pw, ph, quick: false);
        }
        return found;
    }

    private static List<ScannedCode> Decode(byte[] gray, int width, int height, bool quick)
    {
        var source = new RGBLuminanceSource(gray, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = !quick,
            Options = new DecodingOptions
            {
                TryHarder = !quick,
                PossibleFormats = quick ? QrOnly : AllFormats,
                CharacterSet = null,
            },
        };
        Result[]? results;
        try
        {
            results = reader.DecodeMultiple(source);
        }
        catch (Exception)
        {
            // A broken code can make the decoder throw: treat it as "nothing found".
            results = null;
        }

        var list = new List<ScannedCode>();
        if (results is null) return list;
        foreach (var r in results)
        {
            if (string.IsNullOrEmpty(r?.Text)) continue;
            if (list.Any(c => c.Text == r.Text)) continue;
            list.Add(new ScannedCode(r.Text, FormatName(r.BarcodeFormat)));
        }
        return list;
    }

    /// <summary>"QR code", "EAN-13"... (the format names people know).</summary>
    public static string FormatName(BarcodeFormat format) => format switch
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
        BarcodeFormat.CODE_93 => "Code 93",
        BarcodeFormat.ITF => "ITF",
        BarcodeFormat.CODABAR => "Codabar",
        _ => format.ToString(),
    };

    // ------------------------------------------------------------------ what the code holds

    public static CodeKind KindOf(string text)
    {
        if (OpenableLink(text) is not null) return CodeKind.Link;
        if (ParseWifi(text) is not null) return CodeKind.WiFi;
        var t = text.Trim();
        if (t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("MATMSG:", StringComparison.OrdinalIgnoreCase)) return CodeKind.Email;
        if (t.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return CodeKind.Phone;
        return CodeKind.Text;
    }

    /// <summary>
    /// The address behind "Open link": only plain http/https web addresses, never other schemes
    /// (file:, javascript:, app links...). Nothing is ever opened without a click.
    /// </summary>
    public static string? OpenableLink(string text)
    {
        var t = text.Trim();
        if (t.Length == 0 || t.Length > 4096 || t.Any(char.IsWhiteSpace)) return null;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>The web site's name, shown next to "Open link" so you see where it goes.</summary>
    public static string? LinkHost(string text) =>
        OpenableLink(text) is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri.IdnHost : null;

    /// <summary>Reads a Wi-Fi code ("WIFI:T:WPA;S:name;P:secret;H:false;;"), or null.</summary>
    public static WifiNetwork? ParseWifi(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase)) return null;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var key = new StringBuilder();
        var value = new StringBuilder();
        var inValue = false;
        for (var i = 5; i < t.Length; i++)
        {
            var c = t[i];
            if (c == '\\' && i + 1 < t.Length)
            {
                (inValue ? value : key).Append(t[++i]);
                continue;
            }
            if (!inValue && c == ':') { inValue = true; continue; }
            if (c == ';')
            {
                if (key.Length > 0) fields[key.ToString().Trim()] = value.ToString();
                key.Clear();
                value.Clear();
                inValue = false;
                continue;
            }
            (inValue ? value : key).Append(c);
        }
        if (key.Length > 0 && inValue) fields[key.ToString().Trim()] = value.ToString();
        if (!fields.TryGetValue("S", out var name) || name.Length == 0) return null;
        fields.TryGetValue("P", out var password);
        fields.TryGetValue("T", out var security);
        fields.TryGetValue("H", out var hidden);
        return new WifiNetwork(name, string.IsNullOrEmpty(password) ? null : password,
            string.IsNullOrEmpty(security) ? "nopass" : security, string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ pixels

    internal static byte[] ToGray(byte[] bgra, int width, int height, int stride)
    {
        var gray = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            var o = y * width;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                // Transparent pixels count as white (a code on a see-through PNG).
                var a = bgra[i + 3];
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                var l = (r * 77 + g * 150 + b * 29) >> 8;
                gray[o + x] = (byte)((l * a + 255 * (255 - a)) / 255);
            }
        }
        return gray;
    }

    private static byte[] Invert(byte[] gray)
    {
        var o = new byte[gray.Length];
        for (var i = 0; i < gray.Length; i++) o[i] = (byte)(255 - gray[i]);
        return o;
    }

    private static (byte[] Pixels, int Width, int Height) Pad(byte[] gray, int width, int height, int margin, byte fill)
    {
        var pw = width + margin * 2;
        var ph = height + margin * 2;
        var o = new byte[pw * ph];
        Array.Fill(o, fill);
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(gray, y * width, o, (y + margin) * pw + margin, width);
        return (o, pw, ph);
    }

    private static (byte[] Pixels, int Width, int Height) Upscale2x(byte[] gray, int width, int height)
    {
        var w = width * 2;
        var h = height * 2;
        var o = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var src = (y / 2) * width;
            var dst = y * w;
            for (var x = 0; x < w; x++) o[dst + x] = gray[src + x / 2];
        }
        return (o, w, h);
    }
}
