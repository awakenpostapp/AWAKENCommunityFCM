namespace CommunityFootballClubManager.Services;

public sealed class MediaService
{
    public async Task<string?> CapturePhotoAsync(string category)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            throw new NotSupportedException("Thiết bị này không hỗ trợ chụp ảnh từ ứng dụng.");
        }

        var result = await MediaPicker.Default.CapturePhotoAsync();
        return result is null ? null : await CopyIntoAppAsync(result, category);
    }

    public async Task<string?> PickPhotoAsync(string category)
    {
        var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
        {
            Title = "Chọn hình ảnh"
        });
        var result = results.FirstOrDefault();

        return result is null ? null : await CopyIntoAppAsync(result, category);
    }

    private static async Task<string> CopyIntoAppAsync(FileResult result, string category)
    {
        var safeCategory = string.Concat(category.Where(char.IsLetterOrDigit));
        var directory = Path.Combine(FileSystem.AppDataDirectory, "media", safeCategory);
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(result.FileName);
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif"
        };
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Chỉ chấp nhận ảnh JPG, PNG, WEBP hoặc HEIC.");
        }

        var destination = Path.Combine(
            directory,
            $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}");

        await using var source = await result.OpenReadAsync();
        if (source.CanSeek && source.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException("Hình ảnh phải nhỏ hơn 10 MB.");
        }

        await using var output = File.Create(destination);
        await source.CopyToAsync(output);
        return destination;
    }
}
