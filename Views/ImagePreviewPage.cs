using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class ImagePreviewPage : ContentPage
{
    private readonly string _imagePath;
    private readonly string _suggestedFileName;
    private readonly IImageSaveService _imageSave;
    private bool _saving;

    public ImagePreviewPage(
        string title,
        string imagePath,
        string suggestedFileName,
        IImageSaveService imageSave)
    {
        _imagePath = imagePath;
        _suggestedFileName = suggestedFileName;
        _imageSave = imageSave;
        Title = title;
        BackgroundColor = Colors.Black;

        var image = new Image
        {
            Source = ImageSource.FromFile(imagePath),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        var initialScale = 1d;
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, args) =>
        {
            switch (args.Status)
            {
                case GestureStatus.Started:
                    initialScale = image.Scale;
                    break;
                case GestureStatus.Running:
                    image.Scale = Math.Clamp(initialScale * args.Scale, 1, 4);
                    break;
                case GestureStatus.Canceled:
                case GestureStatus.Completed:
                    image.Scale = Math.Clamp(image.Scale, 1, 4);
                    if (image.Scale <= 1)
                    {
                        image.TranslationX = 0;
                        image.TranslationY = 0;
                    }
                    break;
            }
        };
        image.GestureRecognizers.Add(pinch);
        var panStartX = 0d;
        var panStartY = 0d;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, args) =>
        {
            switch (args.StatusType)
            {
                case GestureStatus.Started:
                    panStartX = image.TranslationX;
                    panStartY = image.TranslationY;
                    break;
                case GestureStatus.Running when image.Scale > 1:
                    var maxX = Math.Max(0, image.Width * (image.Scale - 1) / 2);
                    var maxY = Math.Max(0, image.Height * (image.Scale - 1) / 2);
                    image.TranslationX = Math.Clamp(
                        panStartX + args.TotalX,
                        -maxX,
                        maxX);
                    image.TranslationY = Math.Clamp(
                        panStartY + args.TotalY,
                        -maxY,
                        maxY);
                    break;
            }
        };
        image.GestureRecognizers.Add(pan);

        var save = UiKit.PrimaryButton("Lưu hình ảnh");
        save.Margin = new Thickness(12, 8, 12, 14);
        save.Clicked += async (_, _) => await SaveAsync(save);

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        grid.Children.Add(image);
        Grid.SetRow(save, 1);
        grid.Children.Add(save);
        Content = grid;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Lưu",
            Command = new Command(async () => await SaveAsync(save))
        });
    }

    private async Task SaveAsync(Button source)
    {
        if (_saving)
        {
            return;
        }

        _saving = true;
        source.IsEnabled = false;
        try
        {
            var location = await _imageSave.SaveFileAsync(
                _imagePath,
                _suggestedFileName);
            await DisplayAlertAsync(
                "Đã lưu hình ảnh",
                $"Hình ảnh đã được lưu tại {location}.",
                "OK");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Chưa thể lưu hình ảnh",
                exception.Message,
                "Đóng");
        }
        finally
        {
            _saving = false;
            source.IsEnabled = true;
        }
    }
}
