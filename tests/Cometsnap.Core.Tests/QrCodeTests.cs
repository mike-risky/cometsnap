using Cometsnap.Core.Text;
using ZXing;
using ZXing.Common;

namespace Cometsnap.Core.Tests;

public class QrCodeTests
{
    /// <summary>Draws a code into a BGRA "screen" (gray page, some fake text around it).</summary>
    private static (byte[] Pixels, int Width, int Height) Screen(int width, int height, params (string Text, int X, int Y, int Module, bool Quiet, bool Dark)[] codes)
    {
        var px = new byte[width * height * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = 235; px[i + 1] = 235; px[i + 2] = 235; px[i + 3] = 255; }
        var rng = new Random(7);
        for (var k = 0; k < 40; k++)
        {
            int x0 = rng.Next(width - 60), y0 = rng.Next(height - 10);
            for (var y = y0; y < y0 + 6; y++)
                for (var x = x0; x < x0 + 50; x++)
                    if (rng.Next(3) == 0) Set(px, width, x, y, 40);
        }
        foreach (var (text, cx, cy, module, quiet, dark) in codes)
        {
            var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = quiet ? 4 : 0, [EncodeHintType.CHARACTER_SET] = "UTF-8" };
            var matrix = new ZXing.QrCode.QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
            for (var my = 0; my < matrix.Height; my++)
                for (var mx = 0; mx < matrix.Width; mx++)
                {
                    var on = matrix[mx, my];
                    byte v = dark ? (byte)(on ? 230 : 25) : (byte)(on ? 0 : 255);
                    for (var y = 0; y < module; y++)
                        for (var x = 0; x < module; x++)
                            Set(px, width, cx + mx * module + x, cy + my * module + y, v);
                }
        }
        return (px, width, height);
    }

    private static void Set(byte[] px, int width, int x, int y, byte v)
    {
        var i = (y * width + x) * 4;
        if (i < 0 || i + 3 >= px.Length) return;
        px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
    }

    [Fact]
    public void Reads_a_qr_code_on_a_busy_screen()
    {
        var (px, w, h) = Screen(900, 600, ("https://cometsnap.com/changelog", 300, 150, 4, true, false));
        var codes = QrCodes.Scan(px, w, h, w * 4);
        var code = Assert.Single(codes);
        Assert.Equal("https://cometsnap.com/changelog", code.Text);
        Assert.Equal("QR code", code.Format);
        Assert.Equal(CodeKind.Link, code.Kind);
        Assert.Equal("https://cometsnap.com/changelog", code.Link);
    }

    [Fact]
    public void Reads_a_tightly_cropped_code_without_quiet_zone()
    {
        // The user dragged the box exactly on the code's edge.
        var (px, w, h) = Screen(29 * 5, 29 * 5, ("Hello from the comet ☄", 0, 0, 5, false, false));
        var codes = QrCodes.Scan(px, w, h, w * 4);
        Assert.Equal("Hello from the comet ☄", Assert.Single(codes).Text);
    }

    [Fact]
    public void Reads_a_light_code_on_a_dark_page()
    {
        var (px, w, h) = Screen(400, 400, ("dark mode", 60, 60, 6, true, true));
        Assert.Equal("dark mode", Assert.Single(QrCodes.Scan(px, w, h, w * 4)).Text);
    }

    [Fact]
    public void Reads_a_tiny_code()
    {
        var (px, w, h) = Screen(200, 160, ("tiny", 40, 30, 2, true, false));
        Assert.Equal("tiny", Assert.Single(QrCodes.Scan(px, w, h, w * 4)).Text);
    }

    [Fact]
    public void Reads_several_codes()
    {
        var (px, w, h) = Screen(1000, 500, ("first", 80, 100, 5, true, false), ("second", 600, 120, 5, true, false));
        var texts = QrCodes.Scan(px, w, h, w * 4).Select(c => c.Text).OrderBy(t => t).ToArray();
        Assert.Equal(new[] { "first", "second" }, texts);
    }

    [Fact]
    public void Quick_scan_finds_qr_codes_in_a_capture()
    {
        var (px, w, h) = Screen(1280, 720, ("WIFI:T:WPA;S:Comet Base;P:to the moon;;", 900, 300, 4, true, false));
        Assert.Single(QrCodes.Scan(px, w, h, w * 4, quick: true));
    }

    [Fact]
    public void Finds_nothing_on_a_page_without_codes()
    {
        var (px, w, h) = Screen(640, 400);
        Assert.Empty(QrCodes.Scan(px, w, h, w * 4));
    }

    [Fact]
    public void Honors_row_padding()
    {
        var (px, w, h) = Screen(300, 300, ("stride", 50, 50, 5, true, false));
        var stride = w * 4 + 16;
        var padded = new byte[stride * h];
        for (var y = 0; y < h; y++) Buffer.BlockCopy(px, y * w * 4, padded, y * stride, w * 4);
        Assert.Equal("stride", Assert.Single(QrCodes.Scan(padded, w, h, stride)).Text);
    }

    [Theory]
    [InlineData("https://example.com/a?b=c", "https://example.com/a?b=c")]
    [InlineData("  http://Example.com  ", "http://example.com/")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows/System32/calc.exe", null)]
    [InlineData("ms-settings:privacy", null)]
    [InlineData("example.com", null)]
    [InlineData("https://exa mple.com", null)]
    [InlineData("just some words", null)]
    public void Only_web_addresses_can_be_opened(string text, string? expected)
    {
        Assert.Equal(expected, QrCodes.OpenableLink(text));
    }

    [Fact]
    public void Link_host_is_shown()
    {
        Assert.Equal("cometsnap.com", QrCodes.LinkHost("https://cometsnap.com/fr"));
        Assert.Null(QrCodes.LinkHost("hello"));
    }

    [Fact]
    public void Reads_a_wifi_code()
    {
        var wifi = QrCodes.ParseWifi(@"WIFI:T:WPA;S:My\;Net;P:pa\:ss\\word;H:true;;");
        Assert.NotNull(wifi);
        Assert.Equal("My;Net", wifi!.Name);
        Assert.Equal(@"pa:ss\word", wifi.Password);
        Assert.Equal("WPA", wifi.Security);
        Assert.True(wifi.Hidden);

        var code = new ScannedCode("WIFI:S:Cafe;T:WPA;P:latte42;;", "QR code");
        Assert.Equal(CodeKind.WiFi, code.Kind);
        Assert.Equal("latte42", code.ClipboardText);
    }

    [Fact]
    public void Open_wifi_copies_the_whole_text()
    {
        var code = new ScannedCode("WIFI:S:Lobby;T:nopass;;", "QR code");
        Assert.Null(code.Wifi!.Password);
        Assert.Equal(code.Text, code.ClipboardText);
        Assert.Null(QrCodes.ParseWifi("WIFI:T:WPA;;"));
    }

    [Theory]
    [InlineData("mailto:hi@example.com", CodeKind.Email)]
    [InlineData("tel:+41790000000", CodeKind.Phone)]
    [InlineData("4006381333931", CodeKind.Text)]
    public void Knows_what_a_code_holds(string text, CodeKind kind)
    {
        Assert.Equal(kind, QrCodes.KindOf(text));
    }
}
