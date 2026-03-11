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

    [Fact]
    public void ParseHeapStatOutput_WithModernFormat_ParsesCorrectly()
    {
        // Modern dotnet-gcdump output uses "Object Bytes  Count  Type" header
        // with comma-formatted numbers and no hex MT prefix
        var output = """
            2,530,744  GC Heap bytes
                38,658  GC Heap objects

               Object Bytes     Count  Type
                     46,376         1  System.Collections.Generic.Dictionary.Entry<System.IntPtr,Android.Runtime.IdentityHashTargets>[] (Bytes > 10K)  [Module(0xb400006fe0fb8390)]
                     22,096         1  System.Collections.Generic.Dictionary.Entry<System.Int32,System.Diagnostics.Tracing.EventSource.EventMetadata>[] (Bytes > 10K)  [Module(0xb400006fe0fb8390)]
                      8,224         1  System.Char[] (Bytes > 1K)  [Module(0xb400006fe0fb8390)]
                      4,016         3  System.String (Bytes > 1K)  [Module(0xb400006fe0fb8390)]
                      1,072        22  System.Collections.Generic.Dictionary.Entry<Microsoft.Maui.Controls.BindableProperty,Microsoft.Maui.Controls.BindableObject.BindablePropertyContext>[] (Bytes > 1K)  [Module(0xb400006fe0fb8390)]
            """;

        var report = GcDumpReportParser.ParseHeapStatOutput(output);

        report.Should().NotBeNull();
        report!.Types.Should().HaveCount(5);

        // Should be sorted by size descending
        report.Types[0].TypeName.Should().StartWith("System.Collections.Generic.Dictionary.Entry<System.IntPtr");
        report.Types[0].Size.Should().Be(46376);
        report.Types[0].Count.Should().Be(1);

        report.Types[1].Size.Should().Be(22096);
        report.Types[2].Size.Should().Be(8224);

        // Verify comma-formatted numbers are parsed correctly
        report.Types[3].TypeName.Should().Be("System.String");
        report.Types[3].Count.Should().Be(3);
        report.Types[3].Size.Should().Be(4016);

        // Annotations like "(Bytes > 1K)" and "[Module(...)]" should be stripped
        report.Types[3].TypeName.Should().NotContain("Bytes");
        report.Types[3].TypeName.Should().NotContain("Module");

        report.TotalSize.Should().Be(46376 + 22096 + 8224 + 4016 + 1072);
        report.TotalCount.Should().Be(1 + 1 + 1 + 3 + 22);
    }
}
