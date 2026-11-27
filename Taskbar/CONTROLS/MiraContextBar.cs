using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

using KSE.LIB.EMAIL.Models;

namespace Taskbar.CONTROLS;

/// <summary>
/// Selects the content presented in MIRA's shared upper context bar.
/// </summary>
public enum MiraContextMode
{
    Ticker,
    Chat,
    Tasks,
    Email,
    Calendar
}

/// <summary>
/// A reusable upper bar that displays either the live ticker or one module menu.
/// Only a single mode is visible at any given time.
/// </summary>
public sealed class MiraContextBar : Grid
{
    #region Constants and Fields

    private const int MaximumEmailAccounts = 4;

    // Match the existing notification ticker's typography and palette.
    private static readonly Color TickerTextColor = Color.FromRgb(255, 213, 239);
    private static readonly Color TickerIconColor = Color.FromRgb(255, 122, 206);
    private static readonly Color TickerGlowColor = Color.FromRgb(239, 61, 177);

    private readonly IReadOnlyList<EmailAccount> _accounts;
    private readonly IReadOnlyList<Color> _accountColors;
    private readonly Dictionary<string, int> _unreadCounts = [];
    private readonly Dictionary<string, TextBlock> _emailCountLabels = [];
    private readonly Dictionary<string, Border> _emailBadgeBorders = [];

    private readonly MiraNotificationTicker _ticker;
    private readonly Border _menuFrame;
    private readonly Grid _menuContent;
    private readonly Style _entryButtonStyle;

    #endregion

    #region Events and Properties

    /// <summary>
    /// Raised when a menu action is selected. The host decides how to handle it.
    /// </summary>
    public event Action<string>? MenuActionRequested;

    /// <summary>
    /// Raised when a configured email account is selected in the Email menu.
    /// </summary>
    public event Action<EmailAccount>? EmailAccountSelected;

    /// <summary>
    /// Gets the currently displayed mode.
    /// </summary>
    public MiraContextMode CurrentMode { get; private set; } = MiraContextMode.Ticker;

    #endregion

    #region Initialisation

    /// <summary>
    /// Creates MIRA's shared context bar using the configured email accounts.
    /// The four-account limit belongs to MIRA, not to KSE-LIB.EMAIL.
    /// </summary>
    /// <param name="accounts">MIRA's configured email accounts.</param>
    /// <param name="accountColors">Optional per-account display colours.</param>
    /// <example>
    /// <code>
    /// var bar = new MiraContextBar(accounts, [Colors.Coral, Colors.Gold]);
    /// bar.Toggle(MiraContextMode.Email);
    /// </code>
    /// </example>
    public MiraContextBar(IReadOnlyList<EmailAccount> accounts, IReadOnlyList<Color>? accountColors = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        if (accounts.Count > MaximumEmailAccounts)
            throw new ArgumentException("MIRA supports up to four email accounts.", nameof(accounts));

        _accounts = accounts;
        _accountColors = accountColors ?? [Colors.Coral, Colors.Gold, Colors.DeepSkyBlue, Colors.MediumOrchid];
        _entryButtonStyle = CreateEntryButtonStyle();

        Background = Brushes.Transparent;
        ClipToBounds = false;
        IsHitTestVisible = false; // The transparent ticker must not block the desktop.

        _ticker = new MiraNotificationTicker();

        _menuContent = new Grid { ClipToBounds = true };
        _menuFrame = new Border
        {
            // The upper surface has no visible border, fill or frame glow.
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 0, 2),
            Child = _menuContent,
            Visibility = Visibility.Collapsed
        };

        Children.Add(_ticker);
        Children.Add(_menuFrame);
    }

    #endregion

    #region Mode Selection

    /// <summary>
    /// Switches to a module menu, or returns to the ticker if the same module
    /// button is selected again.
    /// </summary>
    /// <param name="mode">The selected module.</param>
    /// <example>
    /// <code>
    /// bar.Toggle(MiraContextMode.Calendar);
    /// </code>
    /// </example>
    public void Toggle(MiraContextMode mode) => Show(CurrentMode == mode ? MiraContextMode.Ticker : mode);

    /// <summary>
    /// Displays exactly one module menu or the default ticker.
    /// </summary>
    /// <param name="mode">The view to display.</param>
    /// <example>
    /// <code>
    /// bar.Show(MiraContextMode.Ticker);
    /// </code>
    /// </example>
    public void Show(MiraContextMode mode)
    {
        CurrentMode = mode;
        bool showTicker = mode == MiraContextMode.Ticker;

        _ticker.Visibility = showTicker ? Visibility.Visible : Visibility.Collapsed;
        _ticker.SetPaused(!showTicker);
        _menuFrame.Visibility = showTicker ? Visibility.Collapsed : Visibility.Visible;
        IsHitTestVisible = !showTicker;

        if (showTicker)
            return;

        if (mode == MiraContextMode.Email)
            BuildEmailMenu();
        else
            BuildModuleMenu(mode);
    }

    /// <summary>
    /// Queues a notification for the default, right-to-left ticker.
    /// Messages are retained while another menu is open.
    /// </summary>
    /// <param name="text">Notification text.</param>
    /// <param name="glyph">Optional Segoe MDL2 icon glyph.</param>
    /// <example>
    /// <code>
    /// bar.EnqueueNotification("Backup Gmail: 2 unread");
    /// </code>
    /// </example>
    public void EnqueueNotification(string text, string glyph = "\uE715") => _ticker.Enqueue(text, glyph);

    #endregion

    #region Unread Counts

    /// <summary>
    /// Updates a configured email account's count, including while the menu is hidden.
    /// </summary>
    /// <param name="accountId">Stable email account identifier.</param>
    /// <param name="unreadCount">Non-negative unread count.</param>
    /// <example>
    /// <code>
    /// bar.SetUnreadCount("personal", 3);
    /// </code>
    /// </example>
    public void SetUnreadCount(string accountId, int unreadCount)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => SetUnreadCount(accountId, unreadCount)));
            return;
        }

        int count = Math.Max(0, unreadCount);
        _unreadCounts[accountId] = count;

        if (_emailCountLabels.TryGetValue(accountId, out TextBlock? label))
            UpdateBadge(label, count, GetAccountColor(accountId));

        if (_emailBadgeBorders.TryGetValue(accountId, out Border? border))
            UpdateBadgeBorder(border, count, GetAccountColor(accountId));
    }

    private Color GetAccountColor(string accountId)
    {
        int index = -1;

        for (int i = 0; i < _accounts.Count; i++)
        {
            if (_accounts[i].Id == accountId)
            {
                index = i;
                break;
            }
        }

        return index < 0 ? Colors.LightPink : _accountColors[index % _accountColors.Count];
    }

    private static void UpdateBadge(TextBlock label, int count, Color accountColor)
    {
        label.Text = count > 99 ? "99+" : count.ToString();
        label.Foreground = new SolidColorBrush(count == 0 ? Color.FromRgb(130, 123, 139) : accountColor);
    }

    private static void UpdateBadgeBorder(Border border, int count, Color accountColor)
    {
        border.BorderBrush = count == 0
            ? new SolidColorBrush(Color.FromRgb(100, 94, 111))
            : new SolidColorBrush(Color.FromArgb(150, accountColor.R, accountColor.G, accountColor.B));
    }

    #endregion

    #region Email Menu

    /// <summary>
    /// Builds an email menu with one evenly sized position per configured
    /// account. The unread badge stays immediately after its email address.
    /// </summary>
    private void BuildEmailMenu()
    {
        ClearMenu();

        if (_accounts.Count == 0)
        {
            _menuContent.ColumnDefinitions.Add(new ColumnDefinition());
            _menuContent.Children.Add(new TextBlock
            {
                Text = "No email accounts connected",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TickerTextColor),
                Effect = CreateTickerTextGlow(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        // Use only the configured accounts. There is no empty fourth slot.
        for (int i = 0; i < _accounts.Count; i++)
            _menuContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < _accounts.Count; i++)
        {
            EmailAccount account = _accounts[i];
            Color accountColor = GetAccountColor(account.Id);

            var icon = new TextBlock
            {
                Text = "\uE715",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = new SolidColorBrush(TickerIconColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            };

            var address = new TextBlock
            {
                Text = account.EmailAddress,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TickerTextColor),
                Effect = CreateTickerTextGlow(),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            var countText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Effect = CreateTickerTextGlow(),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            int unreadCount = _unreadCounts.GetValueOrDefault(account.Id);
            UpdateBadge(countText, unreadCount, accountColor);
            _emailCountLabels[account.Id] = countText;

            var badge = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                MinWidth = 21,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(5, 0, 0, 0),
                Child = countText,
                VerticalAlignment = VerticalAlignment.Center
            };

            UpdateBadgeBorder(badge, unreadCount, accountColor);
            _emailBadgeBorders[account.Id] = badge;

            // Keep all three elements together, rather than docking the badge
            // at the far edge of an account's column.
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            line.Children.Add(icon);
            line.Children.Add(address);
            line.Children.Add(badge);

            var button = new Button
            {
                Style = _entryButtonStyle,
                Content = new Viewbox
                {
                    Child = line,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = account.EmailAddress,
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand
            };

            button.Click += (_, _) => EmailAccountSelected?.Invoke(account);
            Grid.SetColumn(button, i);
            _menuContent.Children.Add(button);

            // Draw dividers only between configured accounts.
            if (i < _accounts.Count - 1)
                AddSeparator(i);
        }
    }

    #endregion

    #region Chat, Tasks and Calendar Menus

    private void BuildModuleMenu(MiraContextMode mode)
    {
        ClearMenu();

        (string Title, string Action)[] items = mode switch
        {
            MiraContextMode.Chat =>
            [
                ("Recent chats", "Chat: Recent chats"),
                ("New chat", "Chat: New chat"),
                ("Search chats", "Chat: Search chats")
            ],
            MiraContextMode.Tasks =>
            [
                ("My tasks", "Tasks: My tasks"),
                ("Add task", "Tasks: Add task"),
                ("Completed", "Tasks: Completed")
            ],
            MiraContextMode.Calendar =>
            [
                ("Today", "Calendar: Today"),
                ("This week", "Calendar: This week"),
                ("New event", "Calendar: New event")
            ],
            _ => []
        };

        for (int i = 0; i < items.Length; i++)
            _menuContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];

            var button = new Button
            {
                Style = _entryButtonStyle,
                Content = new TextBlock
                {
                    Text = item.Title,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(TickerTextColor),
                    Effect = CreateTickerTextGlow(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0)
            };

            button.Click += (_, _) => MenuActionRequested?.Invoke(item.Action);
            Grid.SetColumn(button, i);
            _menuContent.Children.Add(button);

            if (i < items.Length - 1)
                AddSeparator(i);
        }
    }

    private void ClearMenu()
    {
        _menuContent.Children.Clear();
        _menuContent.ColumnDefinitions.Clear();
        _emailCountLabels.Clear();
        _emailBadgeBorders.Clear();
    }

    private void AddSeparator(int column)
    {
        var separator = new Border
        {
            Width = 1,
            Height = 13,
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 119, 195)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        Grid.SetColumn(separator, column);
        _menuContent.Children.Add(separator);
    }

    /// <summary>
    /// Applies the same soft magenta text glow as MiraNotificationTicker.
    /// </summary>
    private static DropShadowEffect CreateTickerTextGlow() => new()
    {
        Color = TickerGlowColor,
        BlurRadius = 7,
        ShadowDepth = 0,
        Opacity = 0.6
    };

    private static Style CreateEntryButtonStyle()
    {
        var root = new FrameworkElementFactory(typeof(Border));
        root.Name = "Chrome";
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        // Keep the menu fully transparent even on hover; use opacity as feedback.
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78));
        template.Triggers.Add(hover);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        return style;
    }

    #endregion
}
