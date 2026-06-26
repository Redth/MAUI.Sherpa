using System.Text.Json.Serialization;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Source-generated JSON metadata for the SQLCipher bundle document types and
/// their full model graph. Wiring this into the document store means bundle
/// read/write never depends on reflection-based serialization, so it keeps
/// working under trimming / Native AOT (iOS &amp; macOS Release) where reflection
/// serialization is disabled.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SqlCipherBundleStore.EnvironmentDocument))]
[JsonSerializable(typeof(SqlCipherBundleStore.BuildDocument))]
internal sealed partial class BundleJsonContext : JsonSerializerContext;
