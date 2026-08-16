using System.Security.Cryptography;
using Microsoft.Maui.Authentication;

namespace CommunityFootballClubManager.Services.Online;

public sealed class OAuthService
{
    public const string CallbackUri = "communityfootballclubmanager://oauth/callback";
    private readonly CloudBackendOptions _options;

    public OAuthService(CloudBackendOptions options) => _options = options;

    public async Task<string> AuthenticateAsync(
        CommunityFootballClubManager.Models.ExternalAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (provider != CommunityFootballClubManager.Models.ExternalAuthProvider.Google)
        {
            throw new NotSupportedException("Ứng dụng hiện chỉ hỗ trợ đăng nhập và Bind Account bằng Google.");
        }

        _options.EnsureConfigured();
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(verifier)));
        const string providerName = "google";
        var start = new UriBuilder(new Uri(_options.ApiBaseAddress, "auth/oauth/start"));
        start.Query = string.Join("&", new[]
        {
            $"provider={Uri.EscapeDataString(providerName)}",
            $"redirect_uri={Uri.EscapeDataString(CallbackUri)}",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
            $"code_verifier={Uri.EscapeDataString(verifier)}",
        });
        var result = await WebAuthenticator.Default.AuthenticateAsync(
            start.Uri,
            new Uri(CallbackUri));
        if (!result.Properties.TryGetValue("ticket", out var ticket)
            || string.IsNullOrWhiteSpace(ticket))
        {
            throw new InvalidOperationException("OAuth không trả về mã xác thực.");
        }

        return ticket;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
}
