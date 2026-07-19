using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class DotnetUpDownloaderTests
{
    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("ABC123\n", "ABC123")]
    [InlineData("abc123  dotnetup-osx-arm64", "abc123")]
    [InlineData("  abc123\t name\n", "abc123")]
    public void NormalizeChecksum_ExtractsFirstToken(string raw, string expected)
    {
        DotnetUpDownloader.NormalizeChecksum(raw).Should().Be(expected);
    }

    [Fact]
    public void HashesEqual_IsCaseInsensitive()
    {
        DotnetUpDownloader.HashesEqual("ABCDEF", "abcdef").Should().BeTrue();
        DotnetUpDownloader.HashesEqual("abc", "abd").Should().BeFalse();
        DotnetUpDownloader.HashesEqual("", "abc").Should().BeFalse();
    }

    [Fact]
    public async Task ComputeSha512Async_MatchesKnownDigest()
    {
        var bytes = Encoding.UTF8.GetBytes("hello dotnetup");
        var expected = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();

        using var stream = new MemoryStream(bytes);
        var actual = await DotnetUpDownloader.ComputeSha512Async(stream);

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ValidChecksum_WritesExecutable()
    {
        var payload = Encoding.UTF8.GetBytes("fake-dotnetup-binary");
        var checksum = Convert.ToHexString(SHA512.HashData(payload)).ToLowerInvariant();
        var handler = new FakeHandler(payload, checksum + "  dotnetup-osx-arm64");
        using var client = new HttpClient(handler);
        var downloader = new DotnetUpDownloader(client);

        var dest = Path.Combine(Path.GetTempPath(), $"dotnetup-test-{Guid.NewGuid():N}", "dotnetup");
        try
        {
            await downloader.DownloadAndVerifyAsync("osx-arm64", dest);

            File.Exists(dest).Should().BeTrue();
            File.ReadAllBytes(dest).Should().Equal(payload);
        }
        finally
        {
            var dir = Path.GetDirectoryName(dest);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_BadChecksum_ThrowsAndLeavesNoFile()
    {
        var payload = Encoding.UTF8.GetBytes("fake-dotnetup-binary");
        var handler = new FakeHandler(payload, new string('0', 128));
        using var client = new HttpClient(handler);
        var downloader = new DotnetUpDownloader(client);

        var dest = Path.Combine(Path.GetTempPath(), $"dotnetup-test-{Guid.NewGuid():N}", "dotnetup");
        try
        {
            var act = async () => await downloader.DownloadAndVerifyAsync("osx-arm64", dest);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*checksum*");
            File.Exists(dest).Should().BeFalse();
        }
        finally
        {
            var dir = Path.GetDirectoryName(dest);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_UnsupportedRid_Throws()
    {
        using var client = new HttpClient(new FakeHandler(Array.Empty<byte>(), "00"));
        var downloader = new DotnetUpDownloader(client);

        var act = async () => await downloader.DownloadAndVerifyAsync(
            "freebsd-x64", Path.Combine(Path.GetTempPath(), "x"));

        await act.Should().ThrowAsync<PlatformNotSupportedException>();
    }

    [Theory]
    [InlineData(
        "https://ci.dot.net/public/dotnetup/0.1.4-preview.6.26358.1/dotnetup-osx-arm64.sha512",
        "0.1.4-preview.6.26358.1")]
    [InlineData("https://example.test/file", null)]
    public void ExtractPublishedVersion_UsesParentPathSegment(string url, string? expected)
    {
        DotnetUpDownloader.ExtractPublishedVersion(new Uri(url)).Should().Be(expected);
    }

    [Fact]
    public async Task GetPublishedArtifactAsync_ReturnsVersionAndChecksum()
    {
        var checksum = new string('a', 128);
        var effectiveUri = new Uri(
            "https://ci.dot.net/public/dotnetup/0.1.4-preview.6.26358.1/dotnetup-osx-arm64.sha512");
        var handler = new FakeHandler(Array.Empty<byte>(), checksum, effectiveUri);
        using var client = new HttpClient(handler);
        var downloader = new DotnetUpDownloader(client);

        var artifact = await downloader.GetPublishedArtifactAsync("osx-arm64");

        artifact.Version.Should().Be("0.1.4-preview.6.26358.1");
        artifact.Sha512.Should().Be(checksum);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly byte[] _binary;
        private readonly string _checksumBody;
        private readonly Uri? _effectiveChecksumUri;

        public FakeHandler(byte[] binary, string checksumBody, Uri? effectiveChecksumUri = null)
        {
            _binary = binary;
            _checksumBody = checksumBody;
            _effectiveChecksumUri = effectiveChecksumUri;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            HttpResponseMessage response;
            if (url.EndsWith(".sha512", StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_checksumBody)
                };
                if (_effectiveChecksumUri is not null)
                    response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, _effectiveChecksumUri);
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_binary)
                };
            }
            return Task.FromResult(response);
        }
    }
}
