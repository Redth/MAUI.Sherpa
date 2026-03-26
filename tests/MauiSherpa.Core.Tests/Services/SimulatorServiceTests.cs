using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class SimulatorServiceTests
{
    [Fact]
    public void ParseRuntimeStorageJson_WhenSimctlReturnsDictionaryShape_ParsesAllEntries()
    {
        const string json = """
        {
          "66CF85B1-19BF-4A4C-AC70-A53632BCC013": {
            "build": "23A343",
            "deletable": true,
            "identifier": "66CF85B1-19BF-4A4C-AC70-A53632BCC013",
            "lastUsedAt": "2026-03-07T22:06:27Z",
            "mountPath": "/Library/Developer/CoreSimulator/Volumes/iOS_23A343",
            "platformIdentifier": "com.apple.platform.iphonesimulator",
            "runtimeIdentifier": "com.apple.CoreSimulator.SimRuntime.iOS-26-0",
            "sizeBytes": 8014773567,
            "state": "Ready"
          },
          "D0363ADF-F887-4360-BD28-FE883451FF4A": {
            "build": "23J352",
            "deletable": true,
            "identifier": "D0363ADF-F887-4360-BD28-FE883451FF4A",
            "platformIdentifier": "com.apple.platform.tvsimulator",
            "runtimeIdentifier": "com.apple.CoreSimulator.SimRuntime.tvOS-26-0",
            "sizeBytes": 6123456789,
            "state": "Ready"
          }
        }
        """;

        var runtimes = SimulatorService.ParseRuntimeStorageJson(json);

        runtimes.Should().HaveCount(2);
        runtimes[0].Identifier.Should().Be("com.apple.CoreSimulator.SimRuntime.iOS-26-0");
        runtimes[0].DeleteIdentifier.Should().Be("66CF85B1-19BF-4A4C-AC70-A53632BCC013");
        runtimes[0].Build.Should().Be("23A343");
        runtimes[0].PlatformIdentifier.Should().Be("com.apple.platform.iphonesimulator");
        runtimes[0].Deletable.Should().BeTrue();
        runtimes[0].SizeBytes.Should().Be(8014773567);
        runtimes[0].LastUsedAt.Should().Be(DateTime.Parse("2026-03-07T22:06:27Z").ToUniversalTime());

        runtimes[1].Identifier.Should().Be("com.apple.CoreSimulator.SimRuntime.tvOS-26-0");
        runtimes[1].DeleteIdentifier.Should().Be("D0363ADF-F887-4360-BD28-FE883451FF4A");
        runtimes[1].PlatformIdentifier.Should().Be("com.apple.platform.tvsimulator");
    }

    [Fact]
    public void ParseRuntimeStorageJson_WhenSimctlReturnsLegacyResultArray_ParsesEntries()
    {
        const string json = """
        {
          "result": [
            {
              "runtimeIdentifier": "com.apple.CoreSimulator.SimRuntime.watchOS-26-0",
              "build": "23R123",
              "platformIdentifier": "com.apple.platform.watchsimulator",
              "state": "Ready",
              "sizeBytes": 123456789,
              "deletable": false
            }
          ]
        }
        """;

        var runtimes = SimulatorService.ParseRuntimeStorageJson(json);

        runtimes.Should().ContainSingle();
        runtimes[0].Identifier.Should().Be("com.apple.CoreSimulator.SimRuntime.watchOS-26-0");
        runtimes[0].DeleteIdentifier.Should().Be("com.apple.CoreSimulator.SimRuntime.watchOS-26-0");
        runtimes[0].Build.Should().Be("23R123");
        runtimes[0].PlatformIdentifier.Should().Be("com.apple.platform.watchsimulator");
        runtimes[0].State.Should().Be("Ready");
        runtimes[0].Deletable.Should().BeFalse();
    }

    [Fact]
    public void ParseDownloadableRuntimesPlist_WhenFeedContainsDuplicateArchitectures_MergesRows()
    {
        const string plist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
          <dict>
            <key>downloadables</key>
            <array>
              <dict>
                <key>identifier</key>
                <string>com.apple.MobileAsset.iOSSimulatorRuntime.22F77-arm64</string>
                <key>name</key>
                <string>iOS 18.5 Simulator Runtime</string>
                <key>platform</key>
                <string>com.apple.platform.iphoneos</string>
                <key>contentType</key>
                <string>cryptexDiskImage</string>
                <key>fileSize</key>
                <integer>123456</integer>
                <key>architectures</key>
                <array>
                  <string>arm64</string>
                </array>
                <key>simulatorVersion</key>
                <dict>
                  <key>buildUpdate</key>
                  <string>22F77</string>
                  <key>version</key>
                  <string>18.5</string>
                </dict>
              </dict>
              <dict>
                <key>identifier</key>
                <string>com.apple.MobileAsset.iOSSimulatorRuntime.22F77-universal</string>
                <key>name</key>
                <string>iOS 18.5 Simulator Runtime</string>
                <key>platform</key>
                <string>com.apple.platform.iphoneos</string>
                <key>contentType</key>
                <string>cryptexDiskImage</string>
                <key>fileSize</key>
                <integer>234567</integer>
                <key>architectures</key>
                <array>
                  <string>arm64</string>
                  <string>x86_64</string>
                </array>
                <key>simulatorVersion</key>
                <dict>
                  <key>buildUpdate</key>
                  <string>22F77</string>
                  <key>version</key>
                  <string>18.5</string>
                </dict>
              </dict>
              <dict>
                <key>identifier</key>
                <string>com.apple.MobileAsset.iOSSimulatorRuntime.23C52-rc</string>
                <key>name</key>
                <string>iOS 26.2 Release Candidate Simulator Runtime</string>
                <key>platform</key>
                <string>com.apple.platform.iphoneos</string>
                <key>contentType</key>
                <string>cryptexDiskImage</string>
                <key>fileSize</key>
                <integer>345678</integer>
                <key>simulatorVersion</key>
                <dict>
                  <key>buildUpdate</key>
                  <string>23C52</string>
                  <key>version</key>
                  <string>26.2</string>
                </dict>
              </dict>
            </array>
          </dict>
        </plist>
        """;

        var runtimes = SimulatorService.ParseDownloadableRuntimesPlist(plist);

        runtimes.Should().HaveCount(2);

        var ios18_5 = runtimes.Single(runtime => runtime.Build == "22F77");
        ios18_5.PlatformIdentifier.Should().Be("com.apple.platform.iphoneos");
        ios18_5.Version.Should().Be("18.5");
        ios18_5.ContentType.Should().Be("cryptexDiskImage");
        ios18_5.FileSizeBytes.Should().Be(234567);
        ios18_5.Architectures.Should().Equal("arm64", "x86_64");
        ios18_5.IsPrerelease.Should().BeFalse();

        var ios26_2Rc = runtimes.Single(runtime => runtime.Build == "23C52");
        ios26_2Rc.IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public void ParseDownloadableRuntimesPlist_WhenDownloadablesArrayIsMissing_ReturnsEmpty()
    {
        const string plist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0">
          <dict>
            <key>version</key>
            <string>1</string>
          </dict>
        </plist>
        """;

        var runtimes = SimulatorService.ParseDownloadableRuntimesPlist(plist);

        runtimes.Should().BeEmpty();
    }
}
