using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Factory for creating cloud secrets provider instances
/// </summary>
public class CloudSecretsProviderFactory : ICloudSecretsProviderFactory
{
    private readonly ILoggingService _logger;

    public CloudSecretsProviderFactory(ILoggingService logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<CloudSecretsProviderType> SupportedProviders => new[]
    {
        CloudSecretsProviderType.Infisical,
        CloudSecretsProviderType.AzureKeyVault,
        CloudSecretsProviderType.AwsSecretsManager,
        CloudSecretsProviderType.GoogleSecretManager,
        CloudSecretsProviderType.OnePassword
    };

    public ICloudSecretsProvider CreateProvider(CloudSecretsProviderConfig config)
    {
        return config.ProviderType switch
        {
            CloudSecretsProviderType.Infisical => new InfisicalProvider(config, _logger),
            CloudSecretsProviderType.AzureKeyVault => new AzureKeyVaultProvider(config, _logger),
            CloudSecretsProviderType.AwsSecretsManager => new AwsSecretsManagerProvider(config, _logger),
            CloudSecretsProviderType.GoogleSecretManager => new GoogleSecretManagerProvider(config, _logger),
            CloudSecretsProviderType.OnePassword => new OnePasswordProvider(config, _logger),
            _ => throw new NotSupportedException($"Provider type {config.ProviderType} is not supported")
        };
    }

    public IReadOnlyList<CloudProviderSettingInfo> GetProviderSettings(CloudSecretsProviderType providerType)
    {
        return providerType switch
        {
            CloudSecretsProviderType.Infisical => GetInfisicalSettings(),
            CloudSecretsProviderType.AzureKeyVault => GetAzureKeyVaultSettings(),
            CloudSecretsProviderType.AwsSecretsManager => GetAwsSecretsManagerSettings(),
            CloudSecretsProviderType.GoogleSecretManager => GetGoogleSecretManagerSettings(),
            CloudSecretsProviderType.OnePassword => GetOnePasswordSettings(),
            _ => Array.Empty<CloudProviderSettingInfo>()
        };
    }

    public string GetProviderDisplayName(CloudSecretsProviderType providerType)
    {
        return providerType switch
        {
            CloudSecretsProviderType.Infisical => "Infisical",
            CloudSecretsProviderType.AzureKeyVault => "Azure Key Vault",
            CloudSecretsProviderType.AwsSecretsManager => "AWS Secrets Manager",
            CloudSecretsProviderType.GoogleSecretManager => "Google Secret Manager",
            CloudSecretsProviderType.OnePassword => "1Password",
            CloudSecretsProviderType.None => "None",
            _ => providerType.ToString()
        };
    }

    private static IReadOnlyList<CloudProviderSettingInfo> GetInfisicalSettings() => new[]
    {
        new CloudProviderSettingInfo(
            "SiteUrl",
            "Site URL",
            "The Infisical instance URL (use https://app.infisical.com for cloud)",
            IsRequired: true,
            IsSecret: false,
            DefaultValue: "https://app.infisical.com",
            Placeholder: "https://app.infisical.com"),
        new CloudProviderSettingInfo(
            "ClientId",
            "Client ID",
            "The Machine Identity Client ID",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"),
        new CloudProviderSettingInfo(
            "ClientSecret",
            "Client Secret",
            "The Machine Identity Client Secret",
            IsRequired: true,
            IsSecret: true,
            Placeholder: "xxxxxxxxxxxxxxxxxxxxxxxxxxxx"),
        new CloudProviderSettingInfo(
            "ProjectId",
            "Project ID",
            "The Infisical project ID to store secrets in",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"),
        new CloudProviderSettingInfo(
            "Environment",
            "Environment",
            "The environment slug (e.g., dev, staging, prod)",
            IsRequired: true,
            IsSecret: false,
            DefaultValue: "prod",
            Placeholder: "prod"),
        new CloudProviderSettingInfo(
            "SecretPath",
            "Secret Path",
            "The folder path for secrets (leave blank for root)",
            IsRequired: false,
            IsSecret: false,
            DefaultValue: "/maui-sherpa",
            Placeholder: "/maui-sherpa")
    };

    private static IReadOnlyList<CloudProviderSettingInfo> GetAzureKeyVaultSettings() => new[]
    {
        new CloudProviderSettingInfo(
            "VaultUrl",
            "Vault URL",
            "The Azure Key Vault URL",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "https://your-vault.vault.azure.net/"),
        new CloudProviderSettingInfo(
            "TenantId",
            "Tenant ID",
            "The Azure AD tenant ID",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"),
        new CloudProviderSettingInfo(
            "ClientId",
            "Client ID",
            "The Azure AD application (client) ID",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"),
        new CloudProviderSettingInfo(
            "ClientSecret",
            "Client Secret",
            "The Azure AD application client secret",
            IsRequired: true,
            IsSecret: true,
            Placeholder: "xxxxxxxxxxxxxxxxxxxxxxxxxxxx")
    };

    private static IReadOnlyList<CloudProviderSettingInfo> GetAwsSecretsManagerSettings() => new[]
    {
        new CloudProviderSettingInfo(
            "Region",
            "AWS Region",
            "The AWS region (e.g., us-east-1)",
            IsRequired: true,
            IsSecret: false,
            DefaultValue: "us-east-1",
            Placeholder: "us-east-1"),
        new CloudProviderSettingInfo(
            "AccessKeyId",
            "Access Key ID",
            "The AWS access key ID",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "AKIAXXXXXXXXXXXXXXXX"),
        new CloudProviderSettingInfo(
            "SecretAccessKey",
            "Secret Access Key",
            "The AWS secret access key",
            IsRequired: true,
            IsSecret: true,
            Placeholder: "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")
    };

    private static IReadOnlyList<CloudProviderSettingInfo> GetGoogleSecretManagerSettings() => new[]
    {
        new CloudProviderSettingInfo(
            "ProjectId",
            "Project ID",
            "The Google Cloud project ID",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "my-project-id"),
        new CloudProviderSettingInfo(
            "CredentialsJson",
            "Service Account JSON",
            "The service account credentials JSON (paste the entire JSON content)",
            IsRequired: true,
            IsSecret: true,
            Placeholder: "{\"type\": \"service_account\", ...}")
    };

    private static IReadOnlyList<CloudProviderSettingInfo> GetOnePasswordSettings() => new[]
    {
        new CloudProviderSettingInfo(
            "Vault",
            "Vault",
            "The 1Password vault to store secrets in",
            IsRequired: true,
            IsSecret: false,
            Placeholder: "Private"),
        new CloudProviderSettingInfo(
            "ItemTitle",
            "Item Title",
            "The name of the Secure Note item in your vault (defaults to MAUI.Sherpa)",
            IsRequired: false,
            IsSecret: false,
            DefaultValue: "MAUI.Sherpa",
            Placeholder: "MAUI.Sherpa"),
        new CloudProviderSettingInfo(
            "ServiceAccountToken",
            "Service Account Token",
            "Optional 1Password service account token for headless/CI use. Leave blank for interactive authentication via the 1Password desktop app.",
            IsRequired: false,
            IsSecret: true,
            Placeholder: "ops_...")
    };
}
