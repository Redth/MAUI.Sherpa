using System.Net.Http.Headers;
using System.Text.Json;

namespace MauiSherpa.Core.Services;

/// <summary>
/// JSON request content with an explicit Content-Length. The DevFlow agent's HTTP server reads a
/// request body by Content-Length and does not decode chunked transfer encoding, which is what
/// <c>JsonContent</c> (and therefore <c>PostAsJsonAsync</c>/<c>PutAsJsonAsync</c>) sends.
/// </summary>
internal static class DevFlowJsonContent
{
    public static HttpContent Create<T>(T value, JsonSerializerOptions? options = null)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, options));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }
}
