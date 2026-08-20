using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Ui;

public static class UiKit
{
    public static readonly Thickness PagePadding = new(18, 16, 18, 26);
    public const double SectionSpacing = 14;

    // Lavender canvas + navy surfaces + teal actions follow the supplied
    // reference while preserving the existing native MAUI navigation/RBAC.
    public static readonly Color Primary = Color.FromArgb("#159A8A");
    public static readonly Color PrimaryDark = Color.FromArgb("#0E746A");
    public static readonly Color TealSoft = Color.FromArgb("#E0F4F0");
    public static readonly Color Accent = Color.FromArgb("#EA6A5A");
    public static readonly Color Background = Color.FromArgb("#F5F4FA");
    public static readonly Color HeroNavy = Color.FromArgb("#111025");
    public static readonly Color Surface = Colors.White;
    public static readonly Color SurfaceSecondary = Color.FromArgb("#EAF4FF");
    public static readonly Color TextPrimary = Color.FromArgb("#16152B");
    public static readonly Color TextSecondary = Color.FromArgb("#74738A");
    public static readonly Color Divider = Color.FromArgb("#E7E5F0");
    public static readonly Color Success = Color.FromArgb("#159A8A");
    public static readonly Color Warning = Color.FromArgb("#D99A2B");
    public static readonly Color Danger = Color.FromArgb("#D85A67");

    public static Label LargeTitle(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 26,
        TextColor = TextPrimary,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label Title(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 19,
        TextColor = TextPrimary,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label Headline(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 16,
        TextColor = TextPrimary,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label Body(string text, Color? color = null) => new()
    {
        Text = text,
        FontSize = 13,
        TextColor = color ?? TextPrimary,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label Caption(string text, Color? color = null) => new()
    {
        Text = text,
        FontSize = 12,
        TextColor = color ?? TextSecondary,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Border Card(View content, Thickness? padding = null)
    {
        return new Border
        {
            BackgroundColor = Surface,
            Stroke = Divider,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = padding ?? new Thickness(15),
            Content = content,
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Opacity = 0.055f,
                Offset = new Point(0, 3),
                Radius = 9
            }
        };
    }

    public static Border OfflineBanner()
    {
        return new Border
        {
            IsVisible = false,
            HeightRequest = 0
        };
    }

    public static ScrollView KeyboardAwareScroll(View content)
    {
        var attached = new HashSet<VisualElement>();
        var keyboardClearance = new BoxView
        {
            HeightRequest = 24,
            Opacity = 0,
            InputTransparent = true
        };
        var wrapper = new VerticalStackLayout { Spacing = 0 };
        wrapper.Children.Add(content);
        wrapper.Children.Add(keyboardClearance);
        var scroll = new ScrollView { Content = wrapper };

        List<VisualElement> GetInputsInDisplayOrder()
        {
            var elements = new List<Element> { content };
            elements.AddRange(content.GetVisualTreeDescendants().OfType<Element>());
            return elements
                .Where(element => element is Entry or Editor or SearchBar)
                .Cast<VisualElement>()
                .Distinct()
                .ToList();
        }

        bool IsOneOfLastTwoInputs(VisualElement input)
        {
            var inputs = GetInputsInDisplayOrder();
            var index = inputs.IndexOf(input);
            return index >= 0 && index >= Math.Max(0, inputs.Count - 2);
        }

        void Attach(Element element)
        {
            if (element is not Entry
                && element is not Editor
                && element is not SearchBar)
            {
                return;
            }

            var input = (VisualElement)element;
            if (!attached.Add(input))
            {
                return;
            }

            input.Focused += async (_, _) =>
            {
                if (!IsOneOfLastTwoInputs(input))
                {
                    return;
                }

                try
                {
                    keyboardClearance.HeightRequest = 240;
                    await Task.Delay(300);
                    if (input.IsFocused)
                    {
                        await scroll.ScrollToAsync(
                            input,
                            ScrollToPosition.Center,
                            animated: true);
                    }
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Không thể cuộn đến ô nhập liệu: {exception.Message}");
                }
            };
            input.Unfocused += async (_, _) =>
            {
                await Task.Delay(180);
                if (attached.All(item => !item.IsFocused))
                {
                    keyboardClearance.HeightRequest = 24;
                }
            };
        }

        void AttachInputs()
        {
            Attach(content);
            foreach (var element in content
                         .GetVisualTreeDescendants()
                         .OfType<Element>())
            {
                Attach(element);
            }
        }

        AttachInputs();
        scroll.Loaded += (_, _) => AttachInputs();
        scroll.DescendantAdded += (_, args) => Attach(args.Element);
        return scroll;
    }

    public static ScrollView ScrollBody(params View[] children)
    {
        var stack = new VerticalStackLayout
        {
            Padding = PagePadding,
            Spacing = SectionSpacing
        };
        stack.Children.Add(OfflineBanner());
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }

        return KeyboardAwareScroll(stack);
    }

    public static Button PrimaryButton(string text, EventHandler? clicked = null)
    {
        var icon = IconForAction(text);
        var button = new Button
        {
            Text = text,
            BackgroundColor = Primary,
            TextColor = Colors.White,
            CornerRadius = 22,
            MinimumHeightRequest = 46,
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            Padding = new Thickness(16, 8),
            ImageSource = icon is null ? null : ImageSource.FromFile(icon)
        };
        if (clicked is not null)
        {
            button.Clicked += clicked;
        }

        SemanticProperties.SetDescription(button, text);
        return button;
    }

    public static Button SecondaryButton(string text, EventHandler? clicked = null)
    {
        var button = PrimaryButton(text, clicked);
        button.BackgroundColor = TealSoft;
        button.TextColor = PrimaryDark;
        button.BorderColor = Primary.WithAlpha(0.24f);
        button.BorderWidth = 1;
        return button;
    }

    public static Button DestructiveButton(string text, EventHandler? clicked = null)
    {
        var button = SecondaryButton(text, clicked);
        button.BackgroundColor = Danger.WithAlpha(0.12f);
        button.TextColor = Danger;
        button.BorderColor = Danger.WithAlpha(0.5f);
        return button;
    }

    private static string? IconForAction(string text)
    {
        var value = text.ToLowerInvariant();
        if (value.Contains("thêm") || value.Contains("tạo") || value.Contains("add"))
        {
            return "icon_plus.svg";
        }

        if (value.Contains("xóa") || value.Contains("xoá") || value.Contains("delete"))
        {
            return "icon_trash.svg";
        }

        if (value.Contains("sửa") || value.Contains("chỉnh") || value.Contains("edit"))
        {
            return "icon_edit.svg";
        }

        if (value.Contains("gửi") || value.Contains("send"))
        {
            return "icon_send.svg";
        }

        if (value.Contains("trợ giúp") || value.Contains("hỗ trợ") || value.Contains("help"))
        {
            return "icon_help.svg";
        }

        if (value.Contains("đánh giá") || value.Contains("trophy") || value.Contains("giải"))
        {
            return "icon_trophy.svg";
        }

        if (value.Contains("điểm danh") || value.Contains("check-in") || value.Contains("check-out")
            || value.Contains("vắng") || value.Contains("có mặt") || value.Contains("đi trễ"))
        {
            return "tab_attendance.svg";
        }

        if (value.Contains("học phí") || value.Contains("bill") || value.Contains("lương")
            || value.Contains("thanh toán") || value.Contains("qr") || value.Contains("hóa đơn"))
        {
            return "tab_finance.svg";
        }

        if (value.Contains("lớp") || value.Contains("lịch") || value.Contains("sân"))
        {
            return "tab_classes.svg";
        }

        if (value.Contains("thành viên") || value.Contains("account") || value.Contains("coach")
            || value.Contains("cầu thủ") || value.Contains("founder"))
        {
            return "tab_people.svg";
        }

        if (value.Contains("thông báo") || value.Contains("notification"))
        {
            return "icon_bell.svg";
        }

        if (value.Contains("hồ sơ") || value.Contains("mật khẩu") || value.Contains("bind")
            || value.Contains("google") || value.Contains("đăng nhập") || value.Contains("đăng xuất"))
        {
            return value.Contains("đăng nhập") || value.Contains("đăng xuất")
                ? "icon_login.svg"
                : "tab_profile.svg";
        }

        return "tab_more.svg";
    }

    public static Grid PasswordField(Entry entry)
    {
        entry.IsPassword = true;

        var visibilityButton = new ImageButton
        {
            Source = "password_eye.svg",
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8),
            WidthRequest = 48,
            HeightRequest = 44,
            MinimumHeightRequest = 44
        };

        void ApplyVisibility(bool showPassword)
        {
            entry.IsPassword = !showPassword;
            visibilityButton.Source = showPassword
                ? "password_eye_off.svg"
                : "password_eye.svg";
            SemanticProperties.SetDescription(
                visibilityButton,
                showPassword ? "Ẩn mật khẩu" : "Hiện mật khẩu");
        }

        visibilityButton.Clicked += (_, _) => ApplyVisibility(entry.IsPassword);
        ApplyVisibility(showPassword: false);

        var field = new Grid
        {
            ColumnSpacing = 4,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        field.Children.Add(entry);
        Grid.SetColumn(visibilityButton, 1);
        field.Children.Add(visibilityButton);
        return field;
    }

    public static View LabeledField(string label, View input, string? helper = null)
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Children.Add(Caption(label, TextSecondary));
        stack.Children.Add(new Border
        {
            BackgroundColor = Surface,
            Stroke = Divider,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            Padding = new Thickness(12, 0),
            Content = input
        });
        if (!string.IsNullOrWhiteSpace(helper))
        {
            stack.Children.Add(Caption(helper));
        }

        return stack;
    }

    public static Border StatusBadge(string text, Color color)
    {
        return new Border
        {
            BackgroundColor = color == Primary ? TealSoft : color.WithAlpha(0.11f),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            Padding = new Thickness(10, 5),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                TextColor = color,
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                CharacterSpacing = 0.1
            }
        };
    }

    /// <summary>
    /// High-emphasis success badge for a completed check-in. The solid
    /// background keeps the state immediately visible while the white label
    /// maintains contrast on the green surface.
    /// </summary>
    public static Border SuccessStatusBadge(string text)
    {
        return new Border
        {
            BackgroundColor = Success,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            Padding = new Thickness(10, 5),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                TextColor = Colors.White,
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                CharacterSpacing = 0.1
            }
        };
    }

    /// <summary>
    /// A centered, modal-style progress notice used while an authentication
    /// operation is in flight.  The overlay intentionally lives above the
    /// page content so it cannot be mistaken for a regular inline status
    /// badge or be hidden below the keyboard/scroll view.
    /// </summary>
    public static Grid LoadingOverlay(string text)
    {
        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Primary.WithAlpha(0.35f),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(24, 16),
            MinimumWidthRequest = 190,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                TextColor = TextPrimary,
                FontFamily = "OpenSansSemibold",
                FontSize = 16,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        var overlay = new Grid
        {
            IsVisible = false,
            InputTransparent = false,
            BackgroundColor = Colors.Black.WithAlpha(0.16f),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = 100
        };
        overlay.Children.Add(card);
        return overlay;
    }

    public static View EmptyState(string title, string message, Button? action = null)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 7,
            HorizontalOptions = LayoutOptions.Fill,
            Padding = new Thickness(16, 24),
            Children =
            {
                new Image
                {
                    Source = "icon_soccer_ball.svg",
                    HeightRequest = 38,
                    WidthRequest = 38,
                    HorizontalOptions = LayoutOptions.Center
                },
                Headline(title),
                Body(message, TextSecondary)
            }
        };
        ((Label)stack.Children[1]).HorizontalTextAlignment = TextAlignment.Center;
        ((Label)stack.Children[2]).HorizontalTextAlignment = TextAlignment.Center;
        if (action is not null)
        {
            action.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(action);
        }

        return Card(stack);
    }

    public static Grid MetricGrid(params (string Value, string Label, Color Color)[] items)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var index = 0; index < items.Length; index++)
        {
            var row = index / 2;
            while (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var item = items[index];
            var inner = new VerticalStackLayout
            {
                Spacing = 2,
                Padding = new Thickness(13, 10, 13, 13),
                Children =
                {
                    new Label
                    {
                        Text = item.Value,
                        TextColor = item.Color,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 22
                    },
                    Caption(item.Label)
                }
            };
            var content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new BoxView
                    {
                        HeightRequest = 4,
                        BackgroundColor = item.Color,
                        CornerRadius = 2
                    },
                    inner
                }
            };
            var card = Card(content, new Thickness(0));
            Grid.SetColumn(card, index % 2);
            Grid.SetRow(card, row);
            grid.Children.Add(card);
        }

        return grid;
    }

    public static Image Avatar(string path, double size = 46)
    {
        return new Image
        {
            Source = File.Exists(path) ? ImageSource.FromFile(path) : "tab_profile.svg",
            HeightRequest = size,
            WidthRequest = size,
            Aspect = Aspect.AspectFill,
            Clip = new EllipseGeometry
            {
                Center = new Point(size / 2, size / 2),
                RadiusX = size / 2,
                RadiusY = size / 2
            }
        };
    }

    public static Border ClubLogo(string path, double size = 88, bool fillFrame = false)
    {
        View content = File.Exists(path)
            ? new Image
            {
                Source = ImageSource.FromFile(path),
                Aspect = fillFrame ? Aspect.AspectFill : Aspect.AspectFit,
                Margin = fillFrame ? new Thickness(0) : new Thickness(8),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            }
            : new Image
            {
                Source = "icon_soccer_ball.svg",
                HeightRequest = size * 0.5,
                WidthRequest = size * 0.5,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        return new Border
        {
            HeightRequest = size,
            WidthRequest = size,
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = Surface,
            Stroke = Primary.WithAlpha(0.32f),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = content,
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Opacity = 0.07f,
                Offset = new Point(0, 2),
                Radius = 7
            }
        };
    }

    public static Border SportsHero(
        string logoPath,
        string eyebrow,
        string title,
        string subtitle,
        string? secondarySubtitle = null,
        double subtitleFontSize = 12)
    {
        var logo = ClubLogo(logoPath, 74, fillFrame: true);
        logo.VerticalOptions = LayoutOptions.Center;
        logo.HorizontalOptions = LayoutOptions.Center;
        var content = new VerticalStackLayout
        {
            Spacing = 7,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                new Label
                {
                    Text = eyebrow.ToUpperInvariant(),
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    CharacterSpacing = 1.4,
                    TextColor = Colors.White.WithAlpha(0.72f),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                logo,
                new Label
                {
                    Text = title,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 22,
                    LineHeight = 1.05,
                    LineBreakMode = LineBreakMode.WordWrap,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = subtitle,
                    FontSize = subtitleFontSize,
                    LineBreakMode = LineBreakMode.WordWrap,
                    TextColor = Colors.White.WithAlpha(0.82f),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
        if (!string.IsNullOrWhiteSpace(secondarySubtitle))
        {
            content.Children.Add(new Label
            {
                Text = secondarySubtitle,
                FontSize = 12,
                LineBreakMode = LineBreakMode.WordWrap,
                TextColor = Colors.White.WithAlpha(0.76f),
                HorizontalTextAlignment = TextAlignment.Center
            });
        }

        return new Border
        {
            MinimumHeightRequest = 220,
            BackgroundColor = HeroNavy,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            Padding = new Thickness(18),
            Content = content,
            Shadow = new Shadow
            {
                Brush = HeroNavy,
                Opacity = 0.18f,
                Offset = new Point(0, 7),
                Radius = 16
            }
        };
    }

    public static Entry MoneyEntry(string placeholder, long initialAmount = 0)
    {
        var entry = new Entry
        {
            Placeholder = placeholder,
            Keyboard = Keyboard.Numeric,
            Text = initialAmount > 0 ? Money(initialAmount) : string.Empty
        };

        void FormatAmount()
        {
            var amount = ParseMoney(entry.Text);
            entry.Text = amount > 0 ? Money(amount) : string.Empty;
        }

        entry.Focused += (_, _) =>
        {
            var amount = ParseMoney(entry.Text);
            entry.Text = amount > 0
                ? amount.ToString("N0", CultureInfo.InvariantCulture)
                : string.Empty;
            entry.CursorPosition = entry.Text?.Length ?? 0;
        };
        entry.Unfocused += (_, _) => FormatAmount();
        entry.Completed += (_, _) =>
        {
            FormatAmount();
            entry.Unfocus();
        };
        return entry;
    }

    public static long ParseMoney(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0;
    }

    public static Color InvoiceColor(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => Success,
        InvoiceStatus.ProofSubmitted => Primary,
        InvoiceStatus.Rejected => Danger,
        InvoiceStatus.Overdue => Danger,
        _ => Warning
    };

    public static Color AttendanceColor(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => Success,
        AttendanceStatus.Late => Warning,
        AttendanceStatus.Absent => Danger,
        AttendanceStatus.Excused => Primary,
        _ => TextSecondary
    };

    public static string Money(long amount) =>
        amount.ToString("N0", CultureInfo.InvariantCulture) + " VNĐ";
}
