using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class VaultwardenProviderTests
{
    #region Test Helpers

    private static JsonElement ParseJson(string json)
        => JsonSerializer.Deserialize<JsonElement>(json);

    private static string SampleItem(params (string name, string value, int type)[] fields)
    {
        var fieldsJson = string.Join(",", fields.Select(f =>
            $"{{\"name\":\"{f.name}\",\"value\":\"{f.value}\",\"type\":{f.type}}}"));
        return $"{{\"id\":\"item-123\",\"type\":2,\"name\":\"MAUI.Sherpa\",\"secureNote\":{{\"type\":0}},\"fields\":[{fieldsJson}]}}";
    }

    #endregion

    #region FindFieldValue Tests

    [Fact]
    public void FindFieldValue_WithMatchingField_ReturnsValue()
    {
        var item = ParseJson(SampleItem(("my-secret", "dGVzdA==", 1)));
        var result = VaultwardenProvider.FindFieldValue(item, "my-secret");
        Assert.Equal("dGVzdA==", result);
    }

    [Fact]
    public void FindFieldValue_WithNoMatch_ReturnsNull()
    {
        var item = ParseJson(SampleItem(("other-key", "value", 1)));
        var result = VaultwardenProvider.FindFieldValue(item, "my-secret");
        Assert.Null(result);
    }

    [Fact]
    public void FindFieldValue_CaseInsensitive()
    {
        var item = ParseJson(SampleItem(("My-Secret", "dGVzdA==", 1)));
        var result = VaultwardenProvider.FindFieldValue(item, "my-secret");
        Assert.Equal("dGVzdA==", result);
    }

    [Fact]
    public void FindFieldValue_WithNoFieldsProperty_ReturnsNull()
    {
        var item = ParseJson("{\"id\":\"item-123\",\"type\":2,\"name\":\"MAUI.Sherpa\"}");
        var result = VaultwardenProvider.FindFieldValue(item, "my-secret");
        Assert.Null(result);
    }

    [Fact]
    public void FindFieldValue_WithEmptyFields_ReturnsNull()
    {
        var item = ParseJson(SampleItem());
        var result = VaultwardenProvider.FindFieldValue(item, "my-secret");
        Assert.Null(result);
    }

    #endregion

    #region GetCustomFieldNames Tests

    [Fact]
    public void GetCustomFieldNames_ReturnsAllFieldNames()
    {
        var item = ParseJson(SampleItem(
            ("key1", "val1", 1),
            ("key2", "val2", 1),
            ("key3", "val3", 0)));

        var names = VaultwardenProvider.GetCustomFieldNames(item);
        Assert.Equal(3, names.Count);
        Assert.Contains("key1", names);
        Assert.Contains("key2", names);
        Assert.Contains("key3", names);
    }

    [Fact]
    public void GetCustomFieldNames_WithNoFields_ReturnsEmpty()
    {
        var item = ParseJson("{\"id\":\"item-123\",\"type\":2}");
        var names = VaultwardenProvider.GetCustomFieldNames(item);
        Assert.Empty(names);
    }

    #endregion

    #region GetFieldsList / SetField / RemoveField Tests

    [Fact]
    public void GetFieldsList_ParsesCorrectly()
    {
        var item = ParseJson(SampleItem(("key1", "val1", 1), ("key2", "val2", 0)));
        var fields = VaultwardenProvider.GetFieldsList(item);

        Assert.Equal(2, fields.Count);
        Assert.Equal("key1", fields[0]["name"]);
        Assert.Equal("val1", fields[0]["value"]);
        Assert.Equal(1, fields[0]["type"]);
        Assert.Equal("key2", fields[1]["name"]);
    }

    [Fact]
    public void SetField_AddsNewField()
    {
        var fields = new List<Dictionary<string, object?>>();
        VaultwardenProvider.SetField(fields, "new-key", "new-value");

        Assert.Single(fields);
        Assert.Equal("new-key", fields[0]["name"]);
        Assert.Equal("new-value", fields[0]["value"]);
        Assert.Equal(1, fields[0]["type"]); // Hidden type
    }

    [Fact]
    public void SetField_UpdatesExistingField()
    {
        var fields = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "my-key", ["value"] = "old-value", ["type"] = 1 }
        };

        VaultwardenProvider.SetField(fields, "my-key", "new-value");

        Assert.Single(fields);
        Assert.Equal("new-value", fields[0]["value"]);
    }

    [Fact]
    public void SetField_UpdateIsCaseInsensitive()
    {
        var fields = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "My-Key", ["value"] = "old-value", ["type"] = 1 }
        };

        VaultwardenProvider.SetField(fields, "my-key", "new-value");

        Assert.Single(fields); // Should update, not add
        Assert.Equal("new-value", fields[0]["value"]);
    }

    [Fact]
    public void RemoveField_RemovesExistingField()
    {
        var fields = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "key1", ["value"] = "val1", ["type"] = 1 },
            new() { ["name"] = "key2", ["value"] = "val2", ["type"] = 1 }
        };

        var removed = VaultwardenProvider.RemoveField(fields, "key1");

        Assert.True(removed);
        Assert.Single(fields);
        Assert.Equal("key2", fields[0]["name"]);
    }

    [Fact]
    public void RemoveField_ReturnsFalseWhenNotFound()
    {
        var fields = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "key1", ["value"] = "val1", ["type"] = 1 }
        };

        var removed = VaultwardenProvider.RemoveField(fields, "nonexistent");

        Assert.False(removed);
        Assert.Single(fields);
    }

    [Fact]
    public void RemoveField_IsCaseInsensitive()
    {
        var fields = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "My-Key", ["value"] = "val1", ["type"] = 1 }
        };

        var removed = VaultwardenProvider.RemoveField(fields, "my-key");
        Assert.True(removed);
        Assert.Empty(fields);
    }

    #endregion

    #region RebuildItemWithFields Tests

    [Fact]
    public void RebuildItemWithFields_PreservesOtherProperties()
    {
        var original = ParseJson(SampleItem(("old-key", "old-val", 1)));
        var newFields = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "new-key", ["value"] = "new-val", ["type"] = 1 }
        };

        var encoded = VaultwardenProvider.RebuildItemWithFields(original, newFields);

        // Decode from base64
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var rebuilt = JsonSerializer.Deserialize<JsonElement>(json);

        // Original properties preserved
        Assert.Equal("item-123", rebuilt.GetProperty("id").GetString());
        Assert.Equal(2, rebuilt.GetProperty("type").GetInt32());
        Assert.Equal("MAUI.Sherpa", rebuilt.GetProperty("name").GetString());

        // Fields updated
        var fields = rebuilt.GetProperty("fields");
        Assert.Equal(1, fields.GetArrayLength());
        Assert.Equal("new-key", fields[0].GetProperty("name").GetString());
        Assert.Equal("new-val", fields[0].GetProperty("value").GetString());
    }

    [Fact]
    public void RebuildItemWithFields_OutputIsBase64Encoded()
    {
        var original = ParseJson(SampleItem());
        var fields = new List<Dictionary<string, object?>>();

        var encoded = VaultwardenProvider.RebuildItemWithFields(original, fields);

        // Should not throw — valid base64
        var decoded = Convert.FromBase64String(encoded);
        var json = Encoding.UTF8.GetString(decoded);

        // Should be valid JSON
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
    }

    #endregion

    #region Base64 Roundtrip Tests

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("")]
    [InlineData("Special chars: !@#$%^&*()_+-=[]{}|;':\",./<>?")]
    [InlineData("Unicode: 日本語 中文 한국어")]
    public void Base64Roundtrip_PreservesData(string original)
    {
        var bytes = Encoding.UTF8.GetBytes(original);
        var base64 = Convert.ToBase64String(bytes);
        var restored = Convert.FromBase64String(base64);
        Assert.Equal(bytes, restored);
    }

    [Fact]
    public void Base64Roundtrip_BinaryData()
    {
        var bytes = new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x80, 0x7F };
        var base64 = Convert.ToBase64String(bytes);
        var restored = Convert.FromBase64String(base64);
        Assert.Equal(bytes, restored);
    }

    #endregion

    #region Provider Properties Tests

    [Fact]
    public void ProviderType_ReturnsVaultwarden()
    {
        var config = new CloudSecretsProviderConfig(
            "test-id", "test", CloudSecretsProviderType.Vaultwarden, new Dictionary<string, string>());
        var provider = new VaultwardenProvider(config, new MockLoggingService());
        Assert.Equal(CloudSecretsProviderType.Vaultwarden, provider.ProviderType);
    }

    [Fact]
    public void DisplayName_ReturnsExpected()
    {
        var config = new CloudSecretsProviderConfig(
            "test-id", "test", CloudSecretsProviderType.Vaultwarden, new Dictionary<string, string>());
        var provider = new VaultwardenProvider(config, new MockLoggingService());
        Assert.Equal("Vaultwarden / Bitwarden", provider.DisplayName);
    }

    #endregion

    #region Factory Integration Tests

    [Fact]
    public void Factory_SupportsVaultwarden()
    {
        var factory = new CloudSecretsProviderFactory(new MockLoggingService());
        Assert.Contains(CloudSecretsProviderType.Vaultwarden, factory.SupportedProviders);
    }

    [Fact]
    public void Factory_GetProviderSettings_ReturnsVaultwardenSettings()
    {
        var factory = new CloudSecretsProviderFactory(new MockLoggingService());
        var settings = factory.GetProviderSettings(CloudSecretsProviderType.Vaultwarden);
        Assert.NotNull(settings);
        Assert.True(settings.Count >= 5);
        Assert.Contains(settings, s => s.Key == "ServerUrl");
        Assert.Contains(settings, s => s.Key == "Email");
        Assert.Contains(settings, s => s.Key == "ClientId");
        Assert.Contains(settings, s => s.Key == "ClientSecret" && s.IsSecret);
        Assert.Contains(settings, s => s.Key == "MasterPassword" && s.IsSecret);
        Assert.Contains(settings, s => s.Key == "ItemName");
    }

    [Fact]
    public void Factory_GetProviderDisplayName_ReturnsVaultwardenName()
    {
        var factory = new CloudSecretsProviderFactory(new MockLoggingService());
        var name = factory.GetProviderDisplayName(CloudSecretsProviderType.Vaultwarden);
        Assert.Equal("Vaultwarden / Bitwarden", name);
    }

    #endregion

    #region Mock

    private class MockLoggingService : ILoggingService
    {
        public string? LogFilePath => null;
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? exception = null) { }
        public void LogDebug(string message) { }
        public IReadOnlyList<LogEntry> GetRecentLogs(int maxCount = 500) => Array.Empty<LogEntry>();
        public void ClearLogs() { }
        public event Action? OnLogAdded;
    }

    #endregion
}
