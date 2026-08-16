namespace CommunityFootballClubManager.Services.Online;

/// <summary>
/// Non-secret client configuration for the Cloudflare Worker API.
/// Credentials and Cloudflare API tokens must never be placed in the mobile
/// application. This URL is public configuration, not a secret.
/// </summary>
public sealed class CloudBackendOptions
{
    public const string DefaultBaseUrl =
        "https://community-football-club-manager-api.old-mud-b712.workers.dev/";

    public Uri BaseAddress { get; init; } = new(DefaultBaseUrl);

    public string ApiVersionPath { get; init; } = "v1/";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsConfigured =>
        BaseAddress.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && !BaseAddress.Host.Contains("YOUR-CLOUDFLARE-WORKER", StringComparison.OrdinalIgnoreCase);

    public Uri ApiBaseAddress
    {
        get
        {
            var prefix = ApiVersionPath.Trim('/');
            return new Uri(BaseAddress, string.IsNullOrEmpty(prefix) ? string.Empty : $"{prefix}/");
        }
    }

    public void EnsureConfigured()
    {
        if (!BaseAddress.IsAbsoluteUri
            || !BaseAddress.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cloud backend phải sử dụng một HTTPS URL tuyệt đối.");
        }

        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Cloud backend chưa được cấu hình bằng URL Worker đã triển khai.");
        }
    }
}
