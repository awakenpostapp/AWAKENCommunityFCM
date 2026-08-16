using CommunityFootballClubManager.Models;
using System.Text.Json.Serialization;

namespace CommunityFootballClubManager.Services.Online;

public sealed record CloudLoginRequest(
    string Username,
    string Password,
    string DeviceName);

public sealed record CloudFounderRegistrationRequest(
    string Username,
    string FullName,
    string Email,
    string? Password,
    string TeamName,
    string DeviceName);

public sealed record CloudRefreshRequest(string RefreshToken);

public sealed record CloudLogoutRequest(string? RefreshToken = null);

public sealed record CloudChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

public sealed record CloudAdminPasswordResetRequest(string Password);

public sealed record CloudUserStatusRequest(bool IsActive);

public sealed record CloudCreateUserRequest(
    string Username,
    string FullName,
    string Email,
    string Role,
    bool IsTuitionSupported,
    string Phone,
    string GuardianName,
    string GuardianPhone,
    string CoachPosition = "");

public sealed record CloudForgotPasswordRequest(
    string Username,
    string Email);

public sealed record CloudResetPasswordRequest(
    string ResetToken,
    string NewPassword);

public sealed record CloudOAuthExchangeRequest(
  ExternalAuthProvider Provider,
  string AuthorizationCode,
  string CodeVerifier,
  string RedirectUri,
  string DeviceName);

public sealed class CloudOAuthLinkResponse
{
    public string Id { get; init; } = string.Empty;

    public ExternalAuthProvider Provider { get; init; }

    public string ExternalSubject { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public DateTimeOffset? LinkedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

public sealed class CloudOAuthLinksResponse
{
    public List<CloudOAuthLinkResponse> Links { get; init; } = [];
}

public sealed class CloudTokenResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public string RefreshToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = "Bearer";

    public int ExpiresIn { get; init; }

    public DateTimeOffset? AccessTokenExpiresAtUtc { get; init; }

    public string SessionId { get; init; } = string.Empty;

    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; init; }
}

/// <summary>
/// Supports both the flattened token response currently emitted by the Worker
/// and a future nested <c>tokens</c> envelope without changing the client API.
/// </summary>
public sealed class CloudAuthResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public string RefreshToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = "Bearer";

    public int ExpiresIn { get; init; }

    public DateTimeOffset? AccessTokenExpiresAtUtc { get; init; }

    public string SessionId { get; init; } = string.Empty;

    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; init; }

    public CloudTokenResponse? Tokens { get; init; }

    public CloudUserSnapshot? User { get; init; }

    public CloudProfileSnapshot? Profile { get; init; }

    public CloudClubSnapshot? Club { get; init; }

    public CloudClubSnapshot? ActiveClub { get; init; }

    [JsonIgnore]
    public bool HasSessionTokens =>
        !string.IsNullOrWhiteSpace(EffectiveTokens.AccessToken)
        && !string.IsNullOrWhiteSpace(EffectiveTokens.RefreshToken);

    [JsonIgnore]
    public CloudTokenResponse EffectiveTokens
    {
        get
        {
            var source = Tokens;
            var accessToken = source?.AccessToken ?? AccessToken;
            var refreshToken = source?.RefreshToken ?? RefreshToken;
            var sessionId = source?.SessionId ?? SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = SessionIdFromRefreshToken(refreshToken);
            }

            return new CloudTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenType = source?.TokenType ?? TokenType,
                ExpiresIn = source?.ExpiresIn ?? ExpiresIn,
                AccessTokenExpiresAtUtc =
                    source?.AccessTokenExpiresAtUtc ?? AccessTokenExpiresAtUtc,
                SessionId = sessionId,
                RefreshTokenExpiresAtUtc =
                    source?.RefreshTokenExpiresAtUtc ?? RefreshTokenExpiresAtUtc
            };
        }
    }

    private static string SessionIdFromRefreshToken(string refreshToken)
    {
        var separator = refreshToken.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 ? refreshToken[..separator] : string.Empty;
    }
}

public sealed class CloudCurrentSessionResponse
{
    public CloudUserSnapshot? User { get; init; }

    public CloudProfileSnapshot? Profile { get; init; }

    public CloudClubSnapshot? Club { get; init; }

    public CloudClubSnapshot? ActiveClub { get; init; }
}

public sealed class CloudProfileResponse
{
    public CloudProfileSnapshot? Profile { get; init; }
}

public sealed class CloudClubResponse
{
    public CloudClubSnapshot? Club { get; init; }
}

public sealed class CloudFounderListResponse
{
    public IReadOnlyList<CloudFounderSummary> Founders { get; init; } = [];
}

public sealed class CloudFounderSummary
{
    public string Id { get; init; } = string.Empty;

    public string? TenantId { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public UserRole Role { get; init; } = UserRole.Founder;

    public bool IsActive { get; init; } = true;

    public bool IsTuitionSupported { get; init; }

    public bool MustChangePassword { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string TeamName { get; init; } = string.Empty;

    public string TenantStatus { get; init; } = "active";

    public string ApprovalStatus { get; init; } = "approved";

    public string FounderStatus { get; init; } = "approved";

    public CloudUserSnapshot ToUser() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Username = Username,
        Email = Email,
        Role = Role,
        IsActive = IsActive,
        IsTuitionSupported = IsTuitionSupported,
        MustChangePassword = MustChangePassword,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}

internal sealed class CloudApiErrorEnvelope
{
    public CloudApiErrorBody? Error { get; init; }

    public string? Code { get; init; }

    public string? Message { get; init; }

    public string? TraceId { get; init; }
}

internal sealed class CloudApiErrorBody
{
    public string? Code { get; init; }

    public string? Message { get; init; }

    public object? Details { get; init; }

    public string? TraceId { get; init; }
}
