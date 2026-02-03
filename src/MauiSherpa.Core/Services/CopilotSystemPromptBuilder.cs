using System.Reflection;
using System.Text;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Builds the system prompt for Copilot sessions in MAUI Sherpa.
/// Loads the prompt from an external markdown file and performs token replacements.
/// </summary>
public static class CopilotSystemPromptBuilder
{
    /// <summary>
    /// Token replacements that can be used in the system prompt template.
    /// Use {{TOKEN_NAME}} syntax in the markdown file.
    /// </summary>
    private static readonly Dictionary<string, Func<string>> TokenReplacements = new()
    {
        { "APP_VERSION", () => GetAppVersion() },
        { "CURRENT_DATE", () => DateTime.Now.ToString("yyyy-MM-dd") },
        { "PLATFORM", () => GetPlatform() }
    };

    /// <summary>
    /// Builds the system prompt, loading from embedded resource if available,
    /// otherwise falling back to the hardcoded default.
    /// </summary>
    public static string Build(string? promptContent = null)
    {
        var content = promptContent ?? GetDefaultPrompt();
        return ApplyTokenReplacements(content);
    }

    /// <summary>
    /// Apply token replacements to the prompt content.
    /// Tokens are in the format {{TOKEN_NAME}}.
    /// </summary>
    private static string ApplyTokenReplacements(string content)
    {
        foreach (var (token, valueFunc) in TokenReplacements)
        {
            var placeholder = $"{{{{{token}}}}}";
            if (content.Contains(placeholder))
            {
                content = content.Replace(placeholder, valueFunc());
            }
        }
        return content;
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private static string GetPlatform()
    {
#if MACCATALYST
        return "macOS (Mac Catalyst)";
#elif WINDOWS
        return "Windows";
#else
        return "Unknown";
#endif
    }

    /// <summary>
    /// Returns a hardcoded fallback prompt in case the external file cannot be loaded.
    /// </summary>
    private static string GetDefaultPrompt()
    {
        return """
            # MAUI Sherpa Assistant

            You are MAUI Sherpa, an expert assistant for .NET MAUI mobile app development.

            ## CRITICAL RULES

            1. **Identity First**: ALWAYS call `get_current_apple_identity` before ANY Apple Developer operation.
            2. **Use SDK Path Tools**: ALWAYS use `get_android_sdk_path` for Android SDK location - never assume ANDROID_HOME.
            3. **Security**: Never execute arbitrary code, never run destructive commands (rm -rf, format, etc.).
            4. **Confirmation Required**: Always confirm before revoking certificates, deleting profiles/bundle IDs, or uninstalling packages.

            ## Available Tools

            Use the available tools proactively to help users. Don't just describe what's possible - actually do it.

            ### Apple Developer
            - `get_current_apple_identity` - Check this FIRST
            - `list_apple_identities`, `select_apple_identity`
            - `list_bundle_ids`, `create_bundle_id`, `delete_bundle_id`
            - `list_devices`, `register_device`, `enable_device`, `disable_device`
            - `list_certificates`, `create_certificate`, `revoke_certificate`
            - `list_provisioning_profiles`, `download_provisioning_profile`, `install_provisioning_profile`

            ### Android SDK
            - `get_android_sdk_path` - Use this instead of ANDROID_HOME
            - `list_android_packages`, `install_android_package`, `uninstall_android_package`
            - `list_emulators`, `create_emulator`, `delete_emulator`, `start_emulator`, `stop_emulator`
            - `list_android_devices`, `list_system_images`, `list_device_definitions`
            """;
    }
}

