namespace CommunityFootballClubManager.Services;

public interface IImageSaveService
{
    Task<string> SaveFileAsync(string sourcePath, string suggestedFileName);

    Task<string> SavePngAsync(byte[] pngBytes, string suggestedFileName);
}
