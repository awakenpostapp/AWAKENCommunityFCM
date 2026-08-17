using Android.Graphics;
using CommunityFootballClubManager.Services;
using System.Globalization;
using System.Runtime.Versioning;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using ARect = Android.Graphics.Rect;
using ARectF = Android.Graphics.RectF;

namespace CommunityFootballClubManager.Platforms.Android;

/// <summary>
/// Renders a compact 590 x 1004 px player card using Android Canvas.  The
/// renderer deliberately keeps the implementation local to Android so the
/// PNG encoder is deterministic and does not add a second graphics package to
/// the MAUI app.
/// </summary>
[SupportedOSPlatform("android23.0")]
public sealed class AndroidPlayerCardPngService : IPlayerCardPngService
{
    private const int Width = 590;
    private const int Height = 1004;

    public Task<byte[]> CreateAsync(
        PlayerCardPngData data,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Render(data, cancellationToken), cancellationToken);
    }

    private static byte[] Render(
        PlayerCardPngData data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var target = Bitmap.CreateBitmap(Width, Height, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(target);
        using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);

        DrawBackground(canvas, paint);
        DrawPhoto(canvas, paint, data.PhotoPath, cancellationToken);
        DrawNameRibbon(canvas, paint, data.PlayerName);
        DrawInfoPanel(canvas, paint, data);

        cancellationToken.ThrowIfCancellationRequested();
        using var output = new MemoryStream();
        if (!target.Compress(Bitmap.CompressFormat.Png!, 100, output))
        {
            throw new InvalidOperationException("Không thể tạo ảnh PNG hồ sơ học viên.");
        }

        return output.ToArray();
    }

    private static void DrawBackground(Canvas canvas, Paint paint)
    {
        using var background = new LinearGradient(
            0,
            0,
            0,
            Height,
            [
                Color.ParseColor("#F33A9A"),
                Color.ParseColor("#F9A84A"),
                Color.ParseColor("#1B1D73")
            ],
            null,
            Shader.TileMode.Clamp!);
        paint.SetShader(background);
        paint.SetStyle(Paint.Style.Fill);
        canvas.DrawRect(0, 0, Width, Height, paint);
        paint.SetShader(null);

        paint.Color = Color.ParseColor("#FFF4D3");
        paint.Alpha = 225;
        canvas.DrawRect(12, 12, Width - 12, Height - 12, paint);
        paint.Alpha = 255;

        paint.Color = Color.ParseColor("#17206E");
        canvas.DrawRect(20, 20, Width - 20, Height - 20, paint);
        paint.Color = Color.ParseColor("#F47CAF");
        canvas.DrawRect(27, 27, Width - 27, Height - 27, paint);
        paint.Color = Color.ParseColor("#10236C");
        canvas.DrawRect(34, 34, Width - 34, Height - 34, paint);
    }

    private static void DrawPhoto(
        Canvas canvas,
        Paint paint,
        string photoPath,
        CancellationToken cancellationToken)
    {
        var photoFrame = new ARectF(48, 106, Width - 48, 558);
        paint.SetStyle(Paint.Style.Fill);
        paint.Color = Color.ParseColor("#081B61");
        canvas.DrawRect(photoFrame, paint);

        Bitmap? source = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
            {
                source = BitmapFactory.DecodeFile(photoPath);
            }

            if (source is null)
            {
                paint.Color = Color.ParseColor("#2543A3");
                canvas.DrawRect(photoFrame, paint);
                DrawCenteredText(
                    canvas,
                    paint,
                    "PLAYER",
                    photoFrame.CenterX(),
                    photoFrame.CenterY() - 8,
                    34,
                    Color.White,
                    bold: true);
                DrawCenteredText(
                    canvas,
                    paint,
                    "PHOTO",
                    photoFrame.CenterX(),
                    photoFrame.CenterY() + 34,
                    20,
                    Color.ParseColor("#B7C6FF"),
                    bold: false);
                return;
            }

            var sourceRect = CenterCrop(source, photoFrame.Width() / photoFrame.Height());
            canvas.Save();
            canvas.ClipRect(photoFrame);
            canvas.DrawBitmap(source, sourceRect, photoFrame, paint);
            canvas.Restore();
        }
        finally
        {
            source?.Dispose();
        }

        paint.SetStyle(Paint.Style.Stroke);
        paint.StrokeWidth = 4;
        paint.Color = Color.ParseColor("#F5E9D8");
        canvas.DrawRect(photoFrame, paint);
        paint.SetStyle(Paint.Style.Fill);

        DrawLeftText(canvas, paint, "FC", 66, 538, 28, Color.White, bold: true);
        paint.Color = Color.ParseColor("#F8C623");
        canvas.DrawCircle(514, 526, 15, paint);
        paint.Color = Color.ParseColor("#17206E");
        canvas.DrawCircle(514, 526, 7, paint);
    }

    private static void DrawNameRibbon(Canvas canvas, Paint paint, string playerName)
    {
        var ribbon = new ARectF(262, 32, Width - 22, 104);
        paint.SetStyle(Paint.Style.Fill);
        paint.Color = Color.ParseColor("#F2389A");
        canvas.DrawRoundRect(ribbon, 8, 8, paint);
        DrawCenteredFit(
            canvas,
            paint,
            string.IsNullOrWhiteSpace(playerName) ? "PLAYER NAME" : playerName,
            ribbon,
            30,
            17,
            Color.White,
            bold: true);
    }

    private static void DrawInfoPanel(Canvas canvas, Paint paint, PlayerCardPngData data)
    {
        var panel = new ARectF(45, 596, Width - 45, 930);
        paint.SetStyle(Paint.Style.Fill);
        paint.Color = Color.ParseColor("#FFFDF9");
        canvas.DrawRoundRect(panel, 5, 5, paint);

        paint.SetStyle(Paint.Style.Stroke);
        paint.StrokeWidth = 4;
        paint.Color = Color.ParseColor("#3C4A98");
        canvas.DrawRoundRect(panel, 5, 5, paint);
        paint.SetStyle(Paint.Style.Fill);

        var team = string.IsNullOrWhiteSpace(data.TeamName)
            ? "COMMUNITY FOOTBALL CLUB"
            : data.TeamName.Trim();
        DrawCenteredFit(
            canvas,
            paint,
            team,
            new ARectF(panel.Left + 26, panel.Top + 32, panel.Right - 26, panel.Top + 105),
            29,
            16,
            Color.ParseColor("#7E236A"),
            bold: true);

        paint.Color = Color.ParseColor("#E7DCE7");
        canvas.DrawRect(panel.Left + 24, panel.Top + 122, panel.Right - 24, panel.Top + 124, paint);

        var birth = data.DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    ?? "Chưa cập nhật";
        var height = data.HeightCm > 0
            ? $"{data.HeightCm:0.#} cm"
            : "Chưa cập nhật";
        var weight = data.WeightKg > 0
            ? $"{data.WeightKg:0.#} kg"
            : "Chưa cập nhật";
        var lines = new[]
        {
            $"Ngày sinh: {birth}",
            $"Chiều cao: {height}",
            $"Cân nặng: {weight}"
        };
        var y = panel.Top + 185;
        foreach (var line in lines)
        {
            DrawLeftFit(
                canvas,
                paint,
                line,
                panel.Left + 34,
                y,
                panel.Width() - 68,
                25,
                16,
                Color.ParseColor("#24324D"),
                bold: false);
            y += 58;
        }

        DrawCenteredText(
            canvas,
            paint,
            "AWAKEN COMMUNITY FCM",
            Width / 2f,
            970,
            16,
            Color.ParseColor("#E9E2F4"),
            bold: true);
    }

    private static ARect CenterCrop(Bitmap source, float targetAspect)
    {
        var sourceAspect = source.Width / (float)source.Height;
        if (sourceAspect > targetAspect)
        {
            var width = Math.Max(1, (int)Math.Round(source.Height * targetAspect));
            var left = Math.Max(0, (source.Width - width) / 2);
            return new ARect(left, 0, left + width, source.Height);
        }

        var height = Math.Max(1, (int)Math.Round(source.Width / targetAspect));
        var top = Math.Max(0, (source.Height - height) / 2);
        return new ARect(0, top, source.Width, top + height);
    }

    private static void DrawCenteredFit(
        Canvas canvas,
        Paint paint,
        string text,
        ARectF bounds,
        float initialSize,
        float minimumSize,
        Color color,
        bool bold)
    {
        paint.TextAlign = Paint.Align.Center;
        paint.Color = color;
        paint.SetTypeface(Typeface.Create("sans-serif", bold ? TypefaceStyle.Bold : TypefaceStyle.Normal));
        paint.TextSize = initialSize;
        while (paint.TextSize > minimumSize && paint.MeasureText(text) > bounds.Width() - 12)
        {
            paint.TextSize -= 1;
        }

        var baseline = bounds.CenterY() - (paint.Ascent() + paint.Descent()) / 2;
        canvas.DrawText(text, bounds.CenterX(), baseline, paint);
    }

    private static void DrawCenteredText(
        Canvas canvas,
        Paint paint,
        string text,
        float x,
        float baseline,
        float size,
        Color color,
        bool bold)
    {
        paint.TextAlign = Paint.Align.Center;
        paint.Color = color;
        paint.TextSize = size;
        paint.SetTypeface(Typeface.Create("sans-serif", bold ? TypefaceStyle.Bold : TypefaceStyle.Normal));
        canvas.DrawText(text, x, baseline, paint);
    }

    private static void DrawLeftText(
        Canvas canvas,
        Paint paint,
        string text,
        float x,
        float baseline,
        float size,
        Color color,
        bool bold)
    {
        paint.TextAlign = Paint.Align.Left;
        paint.Color = color;
        paint.TextSize = size;
        paint.SetTypeface(Typeface.Create("sans-serif", bold ? TypefaceStyle.Bold : TypefaceStyle.Normal));
        canvas.DrawText(text, x, baseline, paint);
    }

    private static void DrawLeftFit(
        Canvas canvas,
        Paint paint,
        string text,
        float x,
        float baseline,
        float maxWidth,
        float initialSize,
        float minimumSize,
        Color color,
        bool bold)
    {
        paint.TextAlign = Paint.Align.Left;
        paint.Color = color;
        paint.SetTypeface(Typeface.Create("sans-serif", bold ? TypefaceStyle.Bold : TypefaceStyle.Normal));
        paint.TextSize = initialSize;
        while (paint.TextSize > minimumSize && paint.MeasureText(text) > maxWidth)
        {
            paint.TextSize -= 1;
        }

        canvas.DrawText(text, x, baseline, paint);
    }
}
