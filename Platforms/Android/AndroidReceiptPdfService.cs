using System.Globalization;
using Android.Graphics;
using Android.Graphics.Pdf;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using Color = Android.Graphics.Color;
using File = System.IO.File;
using Paint = Android.Graphics.Paint;
using Rect = Android.Graphics.Rect;
using Path = System.IO.Path;

namespace CommunityFootballClubManager.Platforms.Android;

public sealed class AndroidReceiptPdfService : IReceiptPdfService
{
    public Task<string> GenerateAsync(Receipt receipt, ClubProfile club)
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, "receipts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{receipt.ReceiptNumber}.pdf");

        using var document = new PdfDocument();
        var pageInfo = new PdfDocument.PageInfo.Builder(595, 842, 1).Create();
        var page = document.StartPage(pageInfo)
                   ?? throw new InvalidOperationException("Không thể tạo trang PDF.");
        var canvas = page.Canvas
                     ?? throw new InvalidOperationException("Không thể tạo canvas PDF.");

        DrawDocument(canvas, receipt, club);

        document.FinishPage(page);
        using var stream = File.Create(path);
        document.WriteTo(stream);
        document.Close();

        return Task.FromResult(path);
    }

    private static void DrawDocument(Canvas canvas, Receipt receipt, ClubProfile club)
    {
        canvas.DrawColor(Color.White);
        using var paint = new Paint(PaintFlags.AntiAlias);
        paint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal));

        var blue = Color.Rgb(11, 99, 206);
        var dark = Color.Rgb(17, 17, 19);
        var gray = Color.Rgb(97, 97, 106);
        var green = Color.Rgb(21, 115, 71);

        if (!string.IsNullOrWhiteSpace(club.LogoPath) && File.Exists(club.LogoPath))
        {
            using var logo = BitmapFactory.DecodeFile(club.LogoPath);
            if (logo is not null)
            {
                canvas.DrawBitmap(logo, null, new Rect(48, 42, 118, 112), paint);
            }
        }

        paint.Color = blue;
        paint.TextSize = 25;
        paint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold));
        canvas.DrawText("HÓA ĐƠN HỌC PHÍ", 48, 145, paint);

        paint.Color = dark;
        paint.TextSize = 16;
        canvas.DrawText(receipt.TeamNameSnapshot, 48, 175, paint);

        paint.Color = gray;
        paint.TextSize = 12;
        paint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal));
        canvas.DrawText($"Mã hóa đơn: {receipt.ReceiptNumber}", 48, 201, paint);
        canvas.DrawText(
            $"Ngày xác nhận: {receipt.ConfirmedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}",
            48,
            220,
            paint);

        paint.Color = Color.Rgb(216, 216, 222);
        paint.StrokeWidth = 1;
        canvas.DrawLine(48, 246, 547, 246, paint);

        var y = 285f;
        DrawRow(canvas, paint, "Học viên", receipt.TraineeNameSnapshot, ref y, dark, gray);
        DrawRow(canvas, paint, "Lớp học", receipt.ClassNameSnapshot, ref y, dark, gray);
        DrawRow(canvas, paint, "Kỳ học phí", DomainText.Period(receipt.PeriodSnapshot), ref y, dark, gray);
        DrawRow(
            canvas,
            paint,
            "Số tiền",
            receipt.AmountVndSnapshot.ToString("N0", CultureInfo.InvariantCulture) + " VNĐ",
            ref y,
            dark,
            gray);
        DrawRow(canvas, paint, "Người xác nhận", receipt.ConfirmedByNameSnapshot, ref y, dark, gray);

        paint.Color = green;
        paint.TextSize = 18;
        paint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold));
        canvas.DrawText("✓ ĐÃ THANH TOÁN", 48, y + 28, paint);

        paint.Color = gray;
        paint.TextSize = 11;
        paint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal));
        canvas.DrawText("Hóa đơn được tạo bởi AWAKEN Community FCM.", 48, 785, paint);
    }

    private static void DrawRow(
        Canvas canvas,
        Paint paint,
        string label,
        string value,
        ref float y,
        Color dark,
        Color gray)
    {
        paint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal));
        paint.Color = gray;
        paint.TextSize = 13;
        canvas.DrawText(label, 48, y, paint);

        paint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold));
        paint.Color = dark;
        paint.TextSize = 15;
        canvas.DrawText(string.IsNullOrWhiteSpace(value) ? "—" : value, 205, y, paint);
        y += 45;
    }
}
