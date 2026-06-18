using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class PublishProfileServiceTests
{
    readonly Mock<ICloudSecretsService> _cloudService = new();
    readonly Mock<ICertificateSyncService> _certSync = new();
    readonly Mock<IKeystoreService> _keystoreService = new();
    readonly Mock<ISecureStorageService> _secureStorage = new();
    readonly Mock<IManagedSecretsService> _managedSecrets = new();
    readonly Mock<IAppleConnectService> _appleConnect = new();
    readonly Mock<IAppleIdentityService> _appleIdentity = new();
    readonly Mock<IAppleIdentityStateService> _identityState = new();
    readonly Mock<IGoogleIdentityService> _googleIdentity = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly PublishProfileService _sut;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PublishProfileServiceTests()
    {
        _sut = new PublishProfileService(
            _cloudService.Object,
            _certSync.Object,
            _keystoreService.Object,
            _secureStorage.Object,
            _managedSecrets.Object,
            _appleConnect.Object,
            _appleIdentity.Object,
            _identityState.Object,
            _googleIdentity.Object,
            _logger.Object);
    }

    [Fact]
    public async Task ResolveSecretsAsync_WithAppleIdentity_MapsIdentityCredentials()
    {
        _appleIdentity.Setup(x => x.GetIdentityAsync("identity-1"))
            .ReturnsAsync(new AppleIdentity(
                Id: "identity-1",
                Name: "Production Team",
                KeyId: "ABC123XYZ0",
                IssuerId: "11111111-2222-3333-4444-555555555555",
                P8KeyPath: null,
                P8KeyContent: "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----"));

        var profile = CreateProfile() with
        {
            AppleIdentities = new List<PublishProfileAppleIdentity>
            {
                new(
                    Label: "Production Team",
                    IdentityId: "identity-1",
                    KeyMappings: new Dictionary<string, List<string>>
                    {
                        ["APPLE_PRODUCTION_TEAM_KEY_ID"] = new() { "ASC_KEY_ID" },
                        ["APPLE_PRODUCTION_TEAM_ISSUER_ID"] = new() { "ASC_ISSUER_ID" },
                        ["APPLE_PRODUCTION_TEAM_P8_KEY"] = new() { "ASC_PRIVATE_KEY", "ASC_PRIVATE_KEY_COPY" }
                    })
            }
        };

        var secrets = await _sut.ResolveSecretsAsync(profile);

        secrets.Should().Contain("ASC_KEY_ID", "ABC123XYZ0");
        secrets.Should().Contain("ASC_ISSUER_ID", "11111111-2222-3333-4444-555555555555");
        secrets.Should().Contain("ASC_PRIVATE_KEY", "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----");
        secrets.Should().Contain("ASC_PRIVATE_KEY_COPY", "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----");
        _appleIdentity.Verify(x => x.GetIdentityAsync("identity-1"), Times.Once);
    }

    [Fact]
    public async Task ResolveSecretsAsync_WithGoogleIdentity_MapsIdentityCredentials()
    {
        const string serviceAccountJson = """
            {"type":"service_account","project_id":"firebase-prod","private_key":"secret","client_email":"firebase-adminsdk@example.iam.gserviceaccount.com"}
            """;
        _googleIdentity.Setup(x => x.GetIdentityAsync("google-1"))
            .ReturnsAsync(new GoogleIdentity(
                Id: "google-1",
                Name: "Firebase Prod",
                ProjectId: "firebase-prod",
                ClientEmail: "firebase-adminsdk@example.iam.gserviceaccount.com",
                ServiceAccountJsonPath: null,
                ServiceAccountJson: serviceAccountJson));

        var profile = CreateProfile() with
        {
            GoogleIdentities = new List<PublishProfileGoogleIdentity>
            {
                new(
                    Label: "Firebase Prod",
                    IdentityId: "google-1",
                    KeyMappings: new Dictionary<string, List<string>>
                    {
                        ["GOOGLE_FIREBASE_PROD_PROJECT_ID"] = new() { "FIREBASE_PROJECT_ID" },
                        ["GOOGLE_FIREBASE_PROD_CLIENT_EMAIL"] = new() { "FIREBASE_CLIENT_EMAIL" },
                        ["GOOGLE_FIREBASE_PROD_SERVICE_ACCOUNT_JSON"] = new() { "FIREBASE_SERVICE_ACCOUNT_JSON" }
                    })
            }
        };

        var secrets = await _sut.ResolveSecretsAsync(profile);

        secrets.Should().Contain("FIREBASE_PROJECT_ID", "firebase-prod");
        secrets.Should().Contain("FIREBASE_CLIENT_EMAIL", "firebase-adminsdk@example.iam.gserviceaccount.com");
        secrets.Should().Contain("FIREBASE_SERVICE_ACCOUNT_JSON", serviceAccountJson);
        _googleIdentity.Verify(x => x.GetIdentityAsync("google-1"), Times.Once);
    }

    [Fact]
    public void DeserializeProfile_WhenIdentityPropertiesAreMissing_UsesEmptyLists()
    {
        var original = CreateProfile();
        var json = JsonSerializer.Serialize(original, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var legacyProperties = root.EnumerateObject()
            .Where(property => property.Name is not "appleIdentities" and not "googleIdentities")
            .Select(property => $"\"{property.Name}\":{property.Value.GetRawText()}");
        var legacyJson = "{" + string.Join(",", legacyProperties) + "}";

        var profile = JsonSerializer.Deserialize<PublishProfile>(legacyJson, JsonOptions);

        profile.Should().NotBeNull();
        profile!.AppleIdentities.Should().BeEmpty();
        profile.GoogleIdentities.Should().BeEmpty();
    }

    static PublishProfile CreateProfile() => new(
        Id: "profile-1",
        Name: "Profile",
        Description: null,
        PublisherId: null,
        RepositoryId: null,
        RepositoryFullName: null,
        AppleConfigs: new List<PublishProfileAppleConfig>(),
        AndroidConfigs: new List<PublishProfileAndroidConfig>(),
        SecretMappings: new List<PublishProfileSecretMapping>(),
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: DateTime.UtcNow);
}
