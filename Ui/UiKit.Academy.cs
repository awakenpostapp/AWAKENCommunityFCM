using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace CommunityFootballClubManager.Ui;

public static partial class UiKit
{
    public static Image Icon(string source, double size = 24) => new()
    {
        Source = source, HeightRequest = size, WidthRequest = size,
        Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center
    };

    public static ImageButton IconButton(string source, string description, EventHandler clicked)
    {
        var button = new ImageButton
        {
            Source = source, BackgroundColor = Colors.Transparent,
            WidthRequest = 48, HeightRequest = 48, Padding = 11,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += clicked;
        return button;
    }

    public static Button TextButton(string text, EventHandler? clicked = null)
    {
        var button = new Button
        {
            Text = text, TextColor = Primary, BackgroundColor = Colors.Transparent,
            FontFamily = "OpenSansSemibold", FontSize = 14, MinimumHeightRequest = 44,
            Padding = new Thickness(6, 4), CornerRadius = 10
        };
        if (clicked is not null) button.Clicked += clicked;
        SemanticProperties.SetDescription(button, text);
        return button;
    }

    public static BoxView DividerLine() => new() { HeightRequest = 1, Color = Divider };

    public static View IdentityAvatar(string path, string name, double size = 44)
    {
        if (File.Exists(path)) return Avatar(path, size);
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(2).Select(part => StringInfo.GetNextTextElement(part))).ToUpperInvariant();
        var label = Headline(initials);
        label.FontSize = Math.Max(14, size * .31);
        label.HorizontalTextAlignment = TextAlignment.Center;
        label.VerticalTextAlignment = TextAlignment.Center;
        var circle = new Border
        {
            WidthRequest = size, HeightRequest = size, BackgroundColor = TealSoft,
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
            Content = label, VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(circle, name);
        return circle;
    }

    public static Grid BrandHeader(string logoPath, string teamName, string role, EventHandler notifications)
    {
        var logo = new Image
        {
            Source = File.Exists(logoPath) ? logoPath : "awaken_brand_mark.png",
            WidthRequest = 48, HeightRequest = 48, Aspect = Aspect.AspectFit
        };
        var text = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center,
            Children = { Headline(teamName), Caption(role) } };
        var bell = IconButton("icon_bell.svg", "Thông báo", notifications);
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(48), new ColumnDefinition(GridLength.Star), new ColumnDefinition(48) },
            ColumnSpacing = 12, Children = { logo, text, bell }
        };
        Grid.SetColumn(text, 1); Grid.SetColumn(bell, 2);
        return grid;
    }

    public static View SectionHeading(string title, string actionText, EventHandler clicked)
    {
        var label = Title(title);
        label.VerticalTextAlignment = TextAlignment.Center;
        var action = TextButton(actionText, clicked);
        action.TextColor = Accent;
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8, Children = { label, action }
        };
        Grid.SetColumn(action, 1);
        return grid;
    }

    public static View NavigationRow(string title, string subtitle, string icon, EventHandler clicked)
    {
        var image = Icon(icon, 25);
        var text = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        text.Children.Add(Headline(title));
        if (!string.IsNullOrWhiteSpace(subtitle)) text.Children.Add(Caption(subtitle));
        var arrow = Icon("icon_chevron_right.svg", 20);
        var grid = new Grid
        {
            Padding = new Thickness(0, 14), ColumnSpacing = 12, MinimumHeightRequest = 62,
            ColumnDefinitions = { new ColumnDefinition(32), new ColumnDefinition(GridLength.Star), new ColumnDefinition(24) },
            Children = { image, text, arrow }
        };
        Grid.SetColumn(text, 1); Grid.SetColumn(arrow, 2);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => clicked(grid, EventArgs.Empty);
        grid.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(grid, string.IsNullOrWhiteSpace(subtitle) ? title : $"{title}. {subtitle}");
        return new VerticalStackLayout { Spacing = 0, Children = { grid, DividerLine() } };
    }

    public static Grid WithStickyFooter(View scrollContent, View actions)
    {
        var footer = new VerticalStackLayout
        {
            BackgroundColor = Background, Spacing = 0,
            Children = { DividerLine(), new Border
            {
                StrokeThickness = 0, BackgroundColor = Background,
                Padding = new Thickness(20, 10, 20, 12), Content = actions
            } }
        };
        var layout = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
            Children = { scrollContent, footer }
        };
        Grid.SetRow(footer, 1);
        return layout;
    }
}
