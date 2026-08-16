using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Services;

public interface IReceiptPdfService
{
    Task<string> GenerateAsync(Receipt receipt, ClubProfile club);
}
