using TorVps.Core.Config;
using Xunit;

namespace TorVps.Tests;

public class IniWriterTests
{
    [Fact]
    public void UpdatesExistingKeyInPlace_PreservingEverythingElse()
    {
        var text = "# header comment\r\n[ui]\r\n# key comment\r\ngraphics = true\r\n\r\n[tor]\r\nsocks_port = 9150\r\n";

        var result = IniWriter.SetValue(text, "ui", "graphics", "false");

        Assert.Equal("# header comment\r\n[ui]\r\n# key comment\r\ngraphics = false\r\n\r\n[tor]\r\nsocks_port = 9150\r\n", result);
    }

    [Fact]
    public void MatchesSectionAndKeyCaseInsensitively()
    {
        var text = "[UI]\r\nGraphics=1\r\n";

        var result = IniWriter.SetValue(text, "ui", "graphics", "false");

        Assert.Equal("[UI]\r\ngraphics = false\r\n", result);
    }

    [Fact]
    public void AppendsMissingKeyAtTheEndOfItsSection_KeepingTheBlankSeparator()
    {
        var text = "[ui]\r\ntheme = dark\r\n\r\n[tor]\r\nsocks_port = 9150\r\n";

        var result = IniWriter.SetValue(text, "ui", "graphics", "true");

        Assert.Equal("[ui]\r\ntheme = dark\r\ngraphics = true\r\n\r\n[tor]\r\nsocks_port = 9150\r\n", result);
    }

    [Fact]
    public void AppendsMissingSectionAtTheEndOfTheFile()
    {
        var text = "[tor]\r\nsocks_port = 9150\r\n";

        var result = IniWriter.SetValue(text, "ui", "graphics", "false");

        Assert.Equal("[tor]\r\nsocks_port = 9150\r\n\r\n[ui]\r\ngraphics = false\r\n", result);
    }

    [Fact]
    public void EmptyText_ProducesJustTheSectionAndKey()
    {
        var result = IniWriter.SetValue(string.Empty, "ui", "graphics", "true");

        Assert.Equal("[ui]\r\ngraphics = true\r\n", result);
    }

    [Fact]
    public void PreservesLfNewlineStyle()
    {
        var text = "[ui]\ngraphics = true\n";

        var result = IniWriter.SetValue(text, "ui", "graphics", "false");

        Assert.Equal("[ui]\ngraphics = false\n", result);
    }

    [Fact]
    public void DoesNotTouchASameNamedKeyInAnotherSection()
    {
        var text = "[watchdog]\r\ngraphics = whatever\r\n\r\n[ui]\r\ngraphics = true\r\n";

        var result = IniWriter.SetValue(text, "ui", "graphics", "false");

        Assert.Equal("[watchdog]\r\ngraphics = whatever\r\n\r\n[ui]\r\ngraphics = false\r\n", result);
    }

    [Fact]
    public void RoundTripsWithConfigParser()
    {
        var text = IniWriter.SetValue(string.Empty, "ui", "graphics", "false");

        Assert.Equal("false", ConfigParser.FirstIniValue(text, "ui", "graphics", "true"));

        text = IniWriter.SetValue(text, "ui", "graphics", "true");

        Assert.Equal("true", ConfigParser.FirstIniValue(text, "ui", "graphics", "false"));
    }
}
