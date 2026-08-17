namespace CommunityFootballClubManager.Services;

/// <summary>
/// Input used to render the Founder-facing player card.  Only the fields
/// intentionally shown on the exported card are included; account, guardian
/// and contact data never enter the generated image.
/// </summary>
public sealed record PlayerCardPngData(
    string PlayerName,
    string TeamName,
    string PhotoPath,
    DateTime? DateOfBirth,
    double HeightCm,
    double WeightKg);

public interface IPlayerCardPngService
{
    Task<byte[]> CreateAsync(
        PlayerCardPngData data,
        CancellationToken cancellationToken = default);
}
