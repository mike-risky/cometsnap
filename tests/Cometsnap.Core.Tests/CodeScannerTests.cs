using Cometsnap.Core.Scanning;
using ZXing;
using ZXing.Common;

namespace Cometsnap.Core.Tests;

public class CodeScannerTests
{
    /// <summary>A code drawn like on a screen: dark modules on a light page, with a margin around it.</summary>
    private static (byte[] Px, int W, int H) Draw(string text, BarcodeFormat format, int width, int height, int pageW, int pageH, int atX, int atY, bool dark = false, int margin = 1)
    {
        var matrix = new MultiFormatWriter().encode(text, format, width, height, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = margin, [EncodeHintType.CHARACTER_SET] = "UTF-8" });
        var px = new byte[pageW * pageH * 4];
        byte bg = dark ? (byte)0x12 : (byte)0xF7, fg = dark ? (byte)0xF0 : (byte)0x10;
        for (var i = 0; i < px.Length; i += 4) { px[i] = bg; px[i + 1] = bg; px[i + 2] = bg; px[i + 3] = 255; }
        for (var y = 0; y < matrix.Height; y++)
        for (var x = 0; x < matrix.Width; x++)
        {
            var i = ((atY + y) * pageW + atX + x) * 4;
            var v = matrix[x, y] ? fg : bg;
            px[i] = v; px[i + 1] = v; px[i + 2] = v;
        }
        return (px, pageW, pageH);
    }

    [Fact]
    public void Reads_a_qr_code_in_a_bigger_capture()
    {
        var (px, w, h) = Draw("https://cometsnap.com/?q=1", BarcodeFormat.QR_CODE, 180, 180, 640, 400, 300, 120);
        var codes = CodeScanner.Scan(px, w, h, w * 4);
        var code = Assert.Single(codes);
        Assert.Equal("https://cometsnap.com/?q=1", code.Text);
        Assert.Equal("QR code", code.Format);
    }

    [Fact]
    public void Spots_a_qr_code_in_a_full_screenshot_quickly()
    {
        var (px, w, h) = Draw("https://example.org/menu", BarcodeFormat.QR_CODE, 220, 220, 2560, 1440, 1900, 900);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var code = CodeScanner.FindQr(px, w, h, w * 4);
        Console.WriteLine($"FindQr 2560x1440: {sw.ElapsedMilliseconds} ms");
        Assert.Equal("https://example.org/menu", code?.Text);
        var (bar, bw, bh) = Draw("Cometsnap-2026", BarcodeFormat.CODE_128, 300, 80, 400, 140, 50, 30);
        Assert.Null(CodeScanner.FindQr(bar, bw, bh, bw * 4)); // barcodes are only for Scan a QR code
        Assert.Null(CodeScanner.FindQr(new byte[800 * 600 * 4], 800, 600, 3200));
    }

    [Fact]
    public void Reads_a_code_boxed_right_up_to_its_edges()
    {
        // No quiet zone at all: the scanner adds one before decoding.
        var (px, w, h) = Draw("tight box", BarcodeFormat.QR_CODE, 150, 150, 150, 150, 0, 0, margin: 0);
        Assert.Equal("tight box", Assert.Single(CodeScanner.Scan(px, w, h, w * 4)).Text);
    }

    [Fact]
    public void Reads_small_and_inverted_codes_and_unicode()
    {
        var (px, w, h) = Draw("Grüezi 彗星 ✨", BarcodeFormat.QR_CODE, 58, 58, 120, 90, 30, 16);
        Assert.Equal("Grüezi 彗星 ✨", Assert.Single(CodeScanner.Scan(px, w, h, w * 4)).Text);
        var (dark, dw, dh) = Draw("dark mode", BarcodeFormat.QR_CODE, 150, 150, 300, 220, 60, 30, dark: true);
        Assert.Equal("dark mode", Assert.Single(CodeScanner.Scan(dark, dw, dh, dw * 4)).Text);
    }

    [Fact]
    public void Reads_other_codes_and_finds_nothing_in_a_plain_capture()
    {
        var (px, w, h) = Draw("Cometsnap-2026", BarcodeFormat.CODE_128, 300, 80, 400, 140, 50, 30);
        Assert.Equal("Cometsnap-2026", Assert.Single(CodeScanner.Scan(px, w, h, w * 4)).Text);
        var blank = new byte[200 * 100 * 4];
        Array.Fill(blank, (byte)0xEE);
        Assert.Empty(CodeScanner.Scan(blank, 200, 100, 800));
    }

    [Theory]
    [InlineData("https://cometsnap.com", "https://cometsnap.com/")]
    [InlineData("  http://example.org/a?b=c ", "http://example.org/a?b=c")]
    [InlineData("www.cometsnap.com", "https://www.cometsnap.com/")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows", null)]
    [InlineData("WIFI:S:home;T:WPA;P:secret;;", null)]
    [InlineData("hello there", null)]
    [InlineData("", null)]
    public void Only_web_links_get_an_open_button(string text, string? link) => Assert.Equal(link, CodeScanner.LinkOf(text));
}
