using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class OnePasswordProviderTests
{
    #region JSON Parsing Tests

    [Fact]
    public void FindFieldValue_ReturnsValue_ForMatchingLabel()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "fields": [
                { "id": "notesPlain", "type": "STRING", "purpose": "NOTES", "label": "notesPlain", "value": "" },
                { "id": "abc123", "type": "CONCEALED", "label": "my-secret", "value": "SGVsbG8gV29ybGQ=" }
            ]
        }
        """);

        var result = OnePasswordProvider.FindFieldValue(json, "my-secret");
        Assert.Equal("SGVsbG8gV29ybGQ=", result);
    }

    [Fact]
    public void FindFieldValue_SkipsBuiltInFields()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "fields": [
                { "id": "notesPlain", "type": "STRING", "purpose": "NOTES", "label": "notesPlain", "value": "some notes" }
            ]
        }
        """);

        var result = OnePasswordProvider.FindFieldValue(json, "notesPlain");
        Assert.Null(result);
    }

    [Fact]
    public void FindFieldValue_ReturnsNull_WhenFieldMissing()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "fields": [
                { "id": "abc", "type": "CONCEALED", "label": "other-key", "value": "data" }
            ]
        }
        """);

        var result = OnePasswordProvider.FindFieldValue(json, "nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void FindFieldValue_ReturnsNull_WhenNoFieldsProperty()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        var result = OnePasswordProvider.FindFieldValue(json, "any-key");
        Assert.Null(result);
    }

    [Fact]
    public void FindFieldValue_IsCaseInsensitive()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "fields": [
                { "id": "x", "type": "CONCEALED", "label": "My-Secret", "value": "data123" }
            ]
        }
        """);

        var result = OnePasswordProvider.FindFieldValue(json, "my-secret");
        Assert.Equal("data123", result);
    }

    [Fact]
    public void GetCustomFieldLabels_ReturnsOnlyCustomFields()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "fields": [
                { "id": "notesPlain", "type": "STRING", "purpose": "NOTES", "label": "notesPlain", "value": "" },
                { "id": "a", "type": "CONCEALED", "label": "secret-one", "value": "v1" },
                { "id": "b", "type": "CONCEALED", "label": "secret-two", "value": "v2" },
                { "id": "c", "type": "STRING", "purpose": "USERNAME", "label": "username", "value": "" }
            ]
        }
        """);

        var labels = OnePasswordProvider.GetCustomFieldLabels(json);
        Assert.Equal(2, labels.Count);
        Assert.Contains("secret-one", labels);
        Assert.Contains("secret-two", labels);
    }

    [Fact]
    public void GetCustomFieldLabels_ReturnsEmpty_WhenNoFields()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        var labels = OnePasswordProvider.GetCustomFieldLabels(json);
        Assert.Empty(labels);
    }

    [Fact]
    public void GetCustomFieldLabels_ReturnsEmpty_WhenOnlyBuiltInFields()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "fields": [
                { "id": "notesPlain", "type": "STRING", "purpose": "NOTES", "label": "notesPlain", "value": "" }
            ]
        }
        """);

        var labels = OnePasswordProvider.GetCustomFieldLabels(json);
        Assert.Empty(labels);
    }

    #endregion

    #region Sanitize Tests

    [Theory]
    [InlineData("simple-key", "simple-key")]
    [InlineData("key.with.dots", "key\\.with\\.dots")]
    [InlineData("key=value", "key\\=value")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("no-special", "no-special")]
    [InlineData("a.b=c\\d", "a\\.b\\=c\\\\d")]
    public void SanitizeFieldLabel_EscapesSpecialCharacters(string input, string expected)
    {
        var result = OnePasswordProvider.SanitizeFieldLabel(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Base64 Roundtrip Tests

    [Fact]
    public void Base64Roundtrip_PreservesData()
    {
        var original = Encoding.UTF8.GetBytes("Hello, 1Password! 🔐");
        var base64 = Convert.ToBase64String(original);
        var decoded = Convert.FromBase64String(base64);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Base64Roundtrip_PreservesBinaryData()
    {
        var original = new byte[] { 0x00, 0xFF, 0x01, 0xFE, 0x80, 0x7F };
        var base64 = Convert.ToBase64String(original);
        var decoded = Convert.FromBase64String(base64);
        Assert.Equal(original, decoded);
    }

    #endregion

    #region Full Item Parsing Tests

    [Fact]
    public void ParsesRealisticOpItemGetOutput()
    {
        // Realistic output from `op item get "MAUI.Sherpa" --vault "Private" --format json`
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "id": "abc123def456",
            "title": "MAUI.Sherpa",
            "version": 3,
            "vault": { "id": "vault123", "name": "Private" },
            "category": "SECURE_NOTE",
            "fields": [
                {
                    "id": "notesPlain",
                    "type": "STRING",
                    "purpose": "NOTES",
                    "label": "notesPlain",
                    "value": ""
                },
                {
                    "id": "f1",
                    "type": "CONCEALED",
                    "label": "sherpa-secrets/cert-key-ABC123",
                    "value": "TUlJRXZ..."
                },
                {
                    "id": "f2",
                    "type": "CONCEALED",
                    "label": "sherpa-secrets/apple-p8-key",
                    "value": "LS0tLS1C..."
                }
            ]
        }
        """);

        // FindFieldValue works
        Assert.Equal("TUlJRXZ...", OnePasswordProvider.FindFieldValue(json, "sherpa-secrets/cert-key-ABC123"));
        Assert.Equal("LS0tLS1C...", OnePasswordProvider.FindFieldValue(json, "sherpa-secrets/apple-p8-key"));
        Assert.Null(OnePasswordProvider.FindFieldValue(json, "nonexistent"));

        // GetCustomFieldLabels works
        var labels = OnePasswordProvider.GetCustomFieldLabels(json);
        Assert.Equal(2, labels.Count);
        Assert.Contains("sherpa-secrets/cert-key-ABC123", labels);
        Assert.Contains("sherpa-secrets/apple-p8-key", labels);
    }

    #endregion
}
