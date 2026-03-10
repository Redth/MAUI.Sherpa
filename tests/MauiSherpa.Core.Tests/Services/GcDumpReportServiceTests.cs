using MauiSherpa.Core.Services;
using FluentAssertions;

namespace MauiSherpa.Core.Tests.Services;

public class GcDumpReportServiceTests
{
    [Fact]
    public void ParseHeapStatOutput_WithValidOutput_ReturnsReport()
    {
        var output = """
                                 MT    Count    TotalSize Class Name
            00007ffa12345678      100         4800 System.String
            00007ffa12345680       50         2400 System.Byte[]
            00007ffa12345690       25         1200 System.Int32[]
            00007ffa123456a0       10          480 System.Object
            Total 185 objects, 8880 bytes
            """;

        var report = GcDumpReportParser.ParseHeapStatOutput(output);

        report.Should().NotBeNull();
        report!.Types.Should().HaveCount(4);
        report.TotalCount.Should().Be(185);
        report.TotalSize.Should().Be(8880);

        // Should be sorted by size descending
        report.Types[0].TypeName.Should().Be("System.String");
        report.Types[0].Count.Should().Be(100);
        report.Types[0].Size.Should().Be(4800);

        report.Types[1].TypeName.Should().Be("System.Byte[]");
        report.Types[1].Count.Should().Be(50);
        report.Types[1].Size.Should().Be(2400);
    }

    [Fact]
    public void ParseHeapStatOutput_WithEmptyOutput_ReturnsNull()
    {
        var report = GcDumpReportParser.ParseHeapStatOutput("");
        report.Should().BeNull();
    }

    [Fact]
    public void ParseHeapStatOutput_WithOnlyHeaders_ReturnsNull()
    {
        var output = """
                                 MT    Count    TotalSize Class Name
            Total 0 objects, 0 bytes
            """;

        var report = GcDumpReportParser.ParseHeapStatOutput(output);
        report.Should().BeNull();
    }

    [Fact]
    public void ParseHeapStatOutput_WithLargeNumbers_ParsesCorrectly()
    {
        var output = """
                                 MT    Count    TotalSize Class Name
            00007ffaab8159c0   792355    110854724 System.String
            00007ffaab81aaa0   102205     60898249 System.Byte[]
            """;

        var report = GcDumpReportParser.ParseHeapStatOutput(output);

        report.Should().NotBeNull();
        report!.Types.Should().HaveCount(2);
        report.Types[0].TypeName.Should().Be("System.String");
        report.Types[0].Count.Should().Be(792355);
        report.Types[0].Size.Should().Be(110854724);
    }

    [Fact]
    public void ParseHeapStatOutput_PreservesRawOutput()
    {
        var output = """
                                 MT    Count    TotalSize Class Name
            00007ffa12345678        5          240 System.String
            """;

        var report = GcDumpReportParser.ParseHeapStatOutput(output);

        report.Should().NotBeNull();
        report!.RawOutput.Should().Be(output);
    }

    [Fact]
    public void ParseHeapStatOutput_WithGenericTypes_ParsesCorrectly()
    {
        var output = """
                                 MT    Count    TotalSize Class Name
            00007ffa12345678       10          480 System.Collections.Generic.Dictionary`2[[System.String],[System.Object]]
            00007ffa12345680        5          240 System.Collections.Generic.List`1[[System.Int32]]
            """;

        var report = GcDumpReportParser.ParseHeapStatOutput(output);

        report.Should().NotBeNull();
        report!.Types.Should().HaveCount(2);
        report.Types[0].TypeName.Should().Contain("Dictionary");
        report.Types[1].TypeName.Should().Contain("List");
    }
}
