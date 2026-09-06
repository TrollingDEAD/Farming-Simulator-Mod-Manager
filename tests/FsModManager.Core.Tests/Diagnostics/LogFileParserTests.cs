using FsModManager.Core.Diagnostics;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public sealed class LogFileParserTests
{
    [Fact]
    public void ParseLine_BracketedErrorTag_ClassifiesAsError()
    {
        var entry = LogFileParser.ParseLine("2026-08-24 22:15:21.029 8346.328 [--- 74] [ERROR] Island 3: First headland intersects field boundary!");

        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(new DateTime(2026, 8, 24, 22, 15, 21, 29), entry.Timestamp);
    }

    [Fact]
    public void ParseLine_WarningPrefixWithPath_ClassifiesAsWarning()
    {
        var entry = LogFileParser.ParseLine("2026-08-24 20:13:39.494   Warning (C:/Users/Test/mods/FS25_Foo/foo.xml): Unable to insert parameters into text.");

        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public void ParseLine_InfoPrefix_ClassifiesAsInfo()
    {
        var entry = LogFileParser.ParseLine("2026-08-24 20:13:25.524   Info: Starting singleplayer game...");

        Assert.Equal(LogLevel.Info, entry.Level);
    }

    [Fact]
    public void ParseLine_NoTimestampNoMarker_ClassifiesAsUnknownWithNullTimestamp()
    {
        var entry = LogFileParser.ParseLine("  CPU: AMD Ryzen 5 5600X 6-Core Processor");

        Assert.Equal(LogLevel.Unknown, entry.Level);
        Assert.Null(entry.Timestamp);
    }

    [Fact]
    public void Parse_SkipsBlankLines()
    {
        var entries = LogFileParser.Parse(new[] { "Info: line one", "", "   ", "Info: line two" });

        Assert.Equal(2, entries.Count);
    }
}
