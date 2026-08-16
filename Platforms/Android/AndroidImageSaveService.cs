using Android.Content;
using Android.Media;
using Android.OS;
using Android.Provider;
using CommunityFootballClubManager.Services;
using System.Runtime.Versioning;
using Application = Android.App.Application;
using Environment = Android.OS.Environment;
using File = System.IO.File;
using Path = System.IO.Path;
using Stream = System.IO.Stream;

namespace CommunityFootballClubManager.Platforms.Android;

public sealed class AndroidImageSaveService : IImageSaveService
{
    private const string AlbumName = "AWAKEN Community FCM";

    public async Task<string> SaveFileAsync(string sourcePath, string suggestedFileName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Không tìm thấy hình ảnh để lưu.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        var mimeType = extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" or ".heif" => "image/heic",
            _ => "image/jpeg"
        };
        var fileName = EnsureExtension(suggestedFileName, extension, ".jpg");
        await using var input = File.OpenRead(sourcePath);
        return await SaveStreamAsync(input, fileName, mimeType);
    }

    public async Task<string> SavePngAsync(byte[] pngBytes, string suggestedFileName)
    {
        if (pngBytes.Length == 0)
        {
            throw new InvalidOperationException("QR Code chưa sẵn sàng để lưu.");
        }

        var fileName = EnsureExtension(suggestedFileName, ".png", ".png");
        await using var input = new MemoryStream(pngBytes, writable: false);
        return await SaveStreamAsync(input, fileName, "image/png");
    }

    private static async Task<string> SaveStreamAsync(
        Stream input,
        string fileName,
        string mimeType)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return await SaveWithMediaStoreAsync(input, fileName, mimeType);
        }

        var permission = await Permissions.RequestAsync<Permissions.StorageWrite>();
        if (permission != PermissionStatus.Granted)
        {
            throw new UnauthorizedAccessException(
                "Cần cấp quyền lưu trữ để lưu hình ảnh vào thiết bị.");
        }

#pragma warning disable CA1422
        var pictures = Environment
            .GetExternalStoragePublicDirectory(Environment.DirectoryPictures)
            ?.AbsolutePath
            ?? throw new InvalidOperationException("Không tìm thấy thư mục Ảnh trên thiết bị.");
#pragma warning restore CA1422
        var album = Path.Combine(pictures, AlbumName);
        Directory.CreateDirectory(album);
        var destination = UniquePath(album, fileName);
        await using (var output = File.Create(destination))
        {
            await input.CopyToAsync(output);
        }

        MediaScannerConnection.ScanFile(
            Application.Context,
            [destination],
            [mimeType],
            null);
        return $"Ảnh/{AlbumName}/{Path.GetFileName(destination)}";
    }

    [SupportedOSPlatform("android29.0")]
    private static async Task<string> SaveWithMediaStoreAsync(
        Stream input,
        string fileName,
        string mimeType)
    {
        var context = Application.Context;
        var resolver = context.ContentResolver
                       ?? throw new InvalidOperationException("Không thể mở thư viện Ảnh.");
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, mimeType);
        values.Put(
            MediaStore.IMediaColumns.RelativePath,
            $"{Environment.DirectoryPictures}/{AlbumName}");
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        var collection = MediaStore.Images.Media.GetContentUri(
            MediaStore.VolumeExternalPrimary)
            ?? throw new InvalidOperationException("Không tìm thấy thư viện Ảnh.");
        var uri = resolver.Insert(collection, values)
                  ?? throw new InvalidOperationException("Không thể tạo hình ảnh trong thư viện.");
        try
        {
            await using (var output = resolver.OpenOutputStream(uri)
                                      ?? throw new InvalidOperationException(
                                          "Không thể ghi hình ảnh vào thư viện."))
            {
                await input.CopyToAsync(output);
                await output.FlushAsync();
            }

            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
            return $"Ảnh/{AlbumName}/{fileName}";
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }

    private static string EnsureExtension(
        string suggestedFileName,
        string preferredExtension,
        string fallbackExtension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string((suggestedFileName ?? string.Empty)
            .Where(character => !invalid.Contains(character))
            .ToArray())
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = $"image-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        var extension = string.IsNullOrWhiteSpace(preferredExtension)
            ? fallbackExtension
            : preferredExtension;
        return string.IsNullOrWhiteSpace(Path.GetExtension(safeName))
            ? safeName + extension
            : safeName;
    }

    private static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(
            directory,
            $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
    }
}
