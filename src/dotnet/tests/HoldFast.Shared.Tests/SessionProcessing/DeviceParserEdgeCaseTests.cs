using HoldFast.Shared.SessionProcessing;
using Xunit;

namespace HoldFast.Shared.Tests.SessionProcessing;

/// <summary>
/// Edge case tests for DeviceParser: malformed UAs, bots, version extraction,
/// browser priority ordering, and unusual but real-world User-Agent strings.
/// </summary>
public class DeviceParserEdgeCaseTests
{
    // ── iPad detection ──────────────────────────────────────────────────

    [Fact]
    public void Parse_iPad_DetectsIOS()
    {
        var ua = "Mozilla/5.0 (iPad; CPU OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("iOS", result.OSName);
        Assert.Equal("Safari", result.BrowserName);
        Assert.Equal("17.2", result.BrowserVersion);
    }

    [Fact]
    public void Parse_iPad_ExtractsOSVersion()
    {
        var ua = "Mozilla/5.0 (iPad; CPU OS 16_6_1 like Mac OS X) AppleWebKit/605.1.15";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("iOS", result.OSName);
        // iPad uses "CPU OS" pattern, extracted via second branch
        Assert.Equal("16.6.1", result.OSVersion);
    }

    // ── iPhone OS version extraction ────────────────────────────────────

    [Fact]
    public void Parse_iPhone_ExtractsOSVersionWithUnderscores()
    {
        var ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2_1 like Mac OS X) AppleWebKit/605.1.15";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("iOS", result.OSName);
        Assert.Equal("17.2.1", result.OSVersion);
    }

    [Fact]
    public void Parse_iPhone_ShortOSVersion()
    {
        var ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("iOS", result.OSName);
        Assert.Equal("16.0", result.OSVersion);
    }

    // ── Android version extraction ──────────────────────────────────────

    [Fact]
    public void Parse_Android_ExtractsVersion()
    {
        var ua = "Mozilla/5.0 (Linux; Android 14; Pixel 8 Pro) AppleWebKit/537.36 Chrome/120.0.6099.230 Mobile";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Android", result.OSName);
        Assert.Equal("14", result.OSVersion);
        Assert.Equal("Chrome", result.BrowserName);
    }

    [Fact]
    public void Parse_Android_OldVersion()
    {
        var ua = "Mozilla/5.0 (Linux; Android 8.1.0; SM-G950F) AppleWebKit/537.36 Chrome/99.0.4844.73 Mobile";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Android", result.OSName);
        Assert.Equal("8.1.0", result.OSVersion);
    }

    // ── Windows NT version extraction ───────────────────────────────────

    [Fact]
    public void Parse_Windows11_ExtractsVersion()
    {
        // Windows 11 reports as NT 10.0 but with build number > 22000
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Windows", result.OSName);
        Assert.Equal("10.0", result.OSVersion);
    }

    [Fact]
    public void Parse_WindowsOld_ExtractsVersion()
    {
        var ua = "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 Chrome/109.0.0.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Windows", result.OSName);
        Assert.Equal("6.1", result.OSVersion);
    }

    [Fact]
    public void Parse_Windows_VersionWithClosingParen()
    {
        // No semicolon after NT version — falls through to ")" delimiter
        var ua = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Windows", result.OSName);
        Assert.Equal("10.0", result.OSVersion);
    }

    // ── Mac OS X version extraction ─────────────────────────────────────

    [Fact]
    public void Parse_MacOSX_ConvertsUnderscoresToDots()
    {
        var ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Chrome/120.0.0.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Mac OS X", result.OSName);
        Assert.Equal("10.15.7", result.OSVersion);
    }

    [Fact]
    public void Parse_MacOSX_WithSemicolon()
    {
        var ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:121.0) Gecko/20100101 Firefox/121.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Mac OS X", result.OSName);
        // Semicolon delimiter should work here
        Assert.NotNull(result.OSVersion);
    }

    // ── Linux detection ─────────────────────────────────────────────────

    [Fact]
    public void Parse_Linux_NoVersion()
    {
        var ua = "Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Linux", result.OSName);
        Assert.Null(result.OSVersion);
    }

    [Fact]
    public void Parse_LinuxChrome()
    {
        var ua = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/120.0.6099.224 Safari/537.36";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Linux", result.OSName);
        Assert.Equal("Chrome", result.BrowserName);
    }

    // ── Browser priority ordering ───────────────────────────────────────

    [Fact]
    public void Parse_EdgeOverChrome()
    {
        // Edge UA contains both Chrome/ and Edg/ — Edge should win
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Edge", result.BrowserName);
        Assert.Equal("120.0.0.0", result.BrowserVersion);
    }

    [Fact]
    public void Parse_OperaOverChrome()
    {
        // Opera UA contains both Chrome/ and OPR/ — Opera should win
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36 OPR/106.0.0.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Opera", result.BrowserName);
        Assert.Equal("106.0.0.0", result.BrowserVersion);
    }

    [Fact]
    public void Parse_ChromiumExcluded()
    {
        // UA with "Chromium" should NOT match Chrome/
        var ua = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chromium/120.0.0.0 Chrome/120.0.0.0 Safari/537.36";
        var result = DeviceParser.Parse(ua);

        // Chrome detection excludes "Chromium" UAs
        Assert.Null(result.BrowserName);
    }

    [Fact]
    public void Parse_Safari_RequiresVersion()
    {
        // Safari detection requires both Safari/ AND Version/ tokens
        var ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Safari/605.1.15";
        var result = DeviceParser.Parse(ua);

        // No Version/ token, so Safari is NOT detected
        Assert.Null(result.BrowserName);
    }

    [Fact]
    public void Parse_Safari_WithVersion()
    {
        var ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2.1 Safari/605.1.15";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Safari", result.BrowserName);
        Assert.Equal("17.2.1", result.BrowserVersion);
    }

    // ── Edge version extraction ─────────────────────────────────────────

    [Fact]
    public void Parse_Edge_ExtractsVersionUntilSpace()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Edg/121.0.2277.128 Safari/537.36";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Edge", result.BrowserName);
        Assert.Equal("121.0.2277.128", result.BrowserVersion);
    }

    // ── Opera version extraction ────────────────────────────────────────

    [Fact]
    public void Parse_Opera_ExtractsVersion()
    {
        var ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Chrome/120.0.0.0 OPR/106.0.4998.70";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Opera", result.BrowserName);
        Assert.Equal("106.0.4998.70", result.BrowserVersion);
    }

    // ── Bot / crawler User-Agents ───────────────────────────────────────

    [Fact]
    public void Parse_Googlebot_NoMatch()
    {
        var ua = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";
        var result = DeviceParser.Parse(ua);

        Assert.Null(result.BrowserName);
        Assert.Null(result.OSName);
    }

    [Fact]
    public void Parse_CurlUserAgent_NoMatch()
    {
        var ua = "curl/8.4.0";
        var result = DeviceParser.Parse(ua);

        Assert.Null(result.BrowserName);
        Assert.Null(result.OSName);
    }

    [Fact]
    public void Parse_PythonRequests_NoMatch()
    {
        var ua = "python-requests/2.31.0";
        var result = DeviceParser.Parse(ua);

        Assert.Null(result.BrowserName);
        Assert.Null(result.OSName);
    }

    // ── Malformed / unusual UAs ─────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ReturnsAllNulls()
    {
        var result = DeviceParser.Parse("");
        Assert.Null(result.BrowserName);
        Assert.Null(result.BrowserVersion);
        Assert.Null(result.OSName);
        Assert.Null(result.OSVersion);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsAllNulls()
    {
        // Whitespace is not null/empty per IsNullOrEmpty, so it enters parsing
        var result = DeviceParser.Parse("   ");
        Assert.Null(result.BrowserName);
        Assert.Null(result.OSName);
    }

    [Fact]
    public void Parse_RandomGarbage_NoExceptions()
    {
        var result = DeviceParser.Parse("asd;flkja;sdf ////  ;;;; ()()()");
        Assert.Null(result.BrowserName);
        Assert.Null(result.OSName);
    }

    [Fact]
    public void Parse_VeryLongUA_NoExceptions()
    {
        var ua = new string('A', 10_000);
        var result = DeviceParser.Parse(ua);
        Assert.Null(result.BrowserName);
        Assert.Null(result.OSName);
    }

    // ── Firefox version extraction ──────────────────────────────────────

    [Fact]
    public void Parse_Firefox_AtEndOfString()
    {
        // Firefox/ at end of string — ExtractAfter should handle no terminator
        var ua = "Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Firefox", result.BrowserName);
        Assert.Equal("121.0", result.BrowserVersion);
    }

    // ── Chrome version extraction ───────────────────────────────────────

    [Fact]
    public void Parse_Chrome_VersionWithSemicolon()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0.0.0;extra stuff";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Chrome", result.BrowserName);
        Assert.Equal("120.0.0.0", result.BrowserVersion);
    }

    [Fact]
    public void Parse_Chrome_VersionWithParen()
    {
        var ua = "Something Chrome/100.0.4896.127)rest";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Chrome", result.BrowserName);
        Assert.Equal("100.0.4896.127", result.BrowserVersion);
    }

    // ── Combined OS + browser edge cases ────────────────────────────────

    [Fact]
    public void Parse_AndroidFirefox()
    {
        var ua = "Mozilla/5.0 (Android 13; Mobile; rv:121.0) Gecko/121.0 Firefox/121.0";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Android", result.OSName);
        Assert.Equal("Firefox", result.BrowserName);
    }

    [Fact]
    public void Parse_iPhoneChrome()
    {
        var ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 Chrome/120.0.6099.230 Mobile/15E148";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("iOS", result.OSName);
        Assert.Equal("Chrome", result.BrowserName);
    }

    [Fact]
    public void Parse_WindowsEdge_BothDetected()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36 Edg/120.0.2210.144";
        var result = DeviceParser.Parse(ua);

        Assert.Equal("Windows", result.OSName);
        Assert.Equal("10.0", result.OSVersion);
        Assert.Equal("Edge", result.BrowserName);
        Assert.Equal("120.0.2210.144", result.BrowserVersion);
    }
}
