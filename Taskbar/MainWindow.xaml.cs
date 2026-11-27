
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

using KSE.LIB.WPF.CONTROLS;
using KSE.LIB.WPF.LAYOUT;

using KSE.LIB.EMAIL.Models;
using KSE.LIB.EMAIL.Providers;
using KSE.LIB.EMAIL.Security;
using KSE.LIB.EMAIL.Services;

using Taskbar.THEME;
using Taskbar.MENUS;
using Taskbar.SERVICES;
using Taskbar.CONTROLS;

namespace Taskbar;

/// <summary>
/// Main window for the MIRA floating taskbar.
/// Constructs the interface dynamically using KSE-LIB.
/// </summary>
public partial class MainWindow : Window
{
    #region Fields

    private const string EmailAddress1 = "c.khalil137@gmail.com";
    private const string EmailAddress2 = "christopher.khalil137@gmail.com";
    private const string EmailAddress3 = "chris@khalilsystems.co.uk";
    private const string MicrosoftClientId = "e64a2575-759e-4695-8496-e481385944ce";
    private const string MicrosoftTenantId = "133fe072-5988-4e9b-82f4-fe55790acffd";

    private readonly Microsoft365Provider _microsoftProvider;
    private readonly IReadOnlyList<EmailAccount> _emailAccounts;
    private readonly MiraEmailManager _emailManager;
    private readonly Dictionary<string, EmailUnreadIndicator> _emailIndicators = [];

    private bool _emailShutdownStarted;
    private bool _emailShutdownFinished;

    #endregion

    #region Window Initialisation

    /// <summary>
    /// Initialises the MIRA floating taskbar.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        _emailAccounts =
        [
            new EmailAccount("personal", EmailAddress1, "Email 1", EmailProviderType.Gmail),
            new EmailAccount("backup", EmailAddress2, "Email 2", EmailProviderType.Gmail),
            new EmailAccount("email-3", EmailAddress3, "Email 3", EmailProviderType.Microsoft365)
        ];

        _microsoftProvider = new Microsoft365Provider(MicrosoftClientId, MicrosoftTenantId);
        _emailManager = new MiraEmailManager(_emailAccounts, [_microsoftProvider]);
        _emailManager.UnreadCountChanged += OnUnreadCountChanged;

        ConfigureWindow();
        CreateControls();

        Closing += async (_, e) =>
        {
            if (_emailShutdownFinished)
                return;

            e.Cancel = true;

            if (_emailShutdownStarted)
                return;

            _emailShutdownStarted = true;

            try
            {
                await Task.WhenAll(_emailManager.StopGmailNotificationsAsync(), _emailManager.StopMicrosoftNotificationsAsync());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Email shutdown failed: {ex}");
            }
            finally
            {
                _emailShutdownFinished = true;
                Close();
            }
        };

        Loaded += (_, _) => PositionWindow();
    }

    /// <summary>
    /// Configures the window as a borderless floating launcher.
    /// </summary>
    private void ConfigureWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;

        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;

        WindowStartupLocation = WindowStartupLocation.Manual;

        Height = 64;
    }

    /// <summary>
    /// Centres the launcher horizontally above the Windows work-area boundary.
    /// This initial implementation uses the primary display's work area.
    /// </summary>
    private void PositionWindow()
    {
        Rect workArea = SystemParameters.WorkArea;

        Width = Math.Min(
            workArea.Width - 24,
            Math.Max(420, workArea.Width * 0.5));

        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height;
    }

    #endregion

    #region Control Creation

    /// <summary>
    /// Dynamically creates the floating taskbar interface,
    /// including navigation controls and the MIRA menu popup.
    /// </summary>
    private void CreateControls()
    {
        #region Main Launcher Frame

        var frame = new Border
        {
            Background = new SolidColorBrush(MiraPalette.Surface),
            BorderBrush = new SolidColorBrush(MiraPalette.Accent),

            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(10, 6, 10, 6),

            Effect = new DropShadowEffect
            {
                Color = MiraPalette.Glow,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.55
            }
        };

        // Establish the window's root content.
        Content = frame;

        #endregion

        #region Grid Layout

        var layout = ControlManager.Add(frame, new Grid());

        GridLayoutManager.AddRow(
            layout,
            new GridLength(1, GridUnitType.Star));

        // 8 navigation buttons on each side of a flexible centre.
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));

        GridLayoutManager.AddColumn(
            layout,
            new GridLength(1, GridUnitType.Star));

        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));

        #endregion

        #region Navigation Controls

        var chatButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE8F2", "", "Open MIRA Chat"),
            0, 4);

        var tasksButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE73E", "", "Open Tasks"),
            0, 5);

        var emailButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE715", "", "Open Emails"),
            0, 7);

        var calendarButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE787", "", "Open Calendar"),
            0, 8);

        #endregion

        #region Email Notification Indicators

        // Preserve the existing Email icon.
        var emailIcon = (FrameworkElement)emailButton.Content;
        emailButton.Content = null;
        emailIcon.HorizontalAlignment = HorizontalAlignment.Center;

        // Create the horizontal row of account indicators.
        const int MaxEmailAccounts = 4;

        if (_emailAccounts.Count > MaxEmailAccounts)
            throw new InvalidOperationException("MIRA supports a maximum of four email accounts.");

        var indicatorRow = new Grid
        {
            Width = 50,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        for (int i = 0; i < MaxEmailAccounts; i++)
            indicatorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Temporary account colours until Settings is implemented.
        Color[] accountColors = [Colors.Coral, Colors.Gold, Colors.DeepSkyBlue];

        for (int i = 0; i < _emailAccounts.Count; i++)
        {
            var indicator = new EmailUnreadIndicator(accountColors[i % accountColors.Length]);

            Grid.SetColumn(indicator, i);

            _emailIndicators.Add(_emailAccounts[i].Id, indicator);
            indicatorRow.Children.Add(indicator);
        }

        // Stack the indicators above the existing Email icon.
        var emailContent = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        emailContent.Children.Add(indicatorRow);
        emailContent.Children.Add(emailIcon);

        emailButton.Content = emailContent;
        emailButton.Padding = new Thickness(12, 2, 12, 2);

        #endregion

        #region MIRA Logo

        var miraLogo = GridLayoutManager.Place(
            layout,
            new TextBlock
            {
                Text = "MIRA",

                Foreground = new SolidColorBrush(
                    MiraPalette.Logo),

                FontSize = 24,
                FontWeight = FontWeights.Bold,

                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,

                Cursor = Cursors.Hand,

                Effect = new DropShadowEffect
                {
                    Color = MiraPalette.Glow,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9
                }
            },
            0, 6);

        #endregion

        #region MIRA Menu Popup

        var menuPopup = new MiraMenuPopup(miraLogo);
        menuPopup.SettingsRequested += (_, _) => ShowPlaceholder("Settings");
        menuPopup.AboutRequested += (_, _) => ShowPlaceholder("About");
        menuPopup.ExitRequested += (_, _) => Close();

        #endregion

        #region Event Subscriptions

        // Toggle the MIRA popup when the logo is clicked.
        miraLogo.MouseLeftButtonUp += (_, _) => menuPopup.Toggle();
        tasksButton.Click += (_, _) => ShowPlaceholder("Tasks");
        emailButton.Click += (_, _) => ShowPlaceholder("Email");
        calendarButton.Click += (_, _) => ShowPlaceholder("Calendar");
        chatButton.Click += (_, _) => ShowPlaceholder("Chat");

        // Retrieve the first live email count when MIRA loads.
        Loaded += async (_, _) =>
        {
            try
            {
                string cacheDirectory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "KSE",
                    "MIRA",
                    "MicrosoftTokens");

                await _microsoftProvider.ConfigureTokenCacheAsync(
                    cacheDirectory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Microsoft token cache initialisation failed: {ex}");
            }

            await UpdateEmailTooltipAsync(emailButton);

            try
            {
                await _emailManager.StartGmailNotificationsAsync();
                System.Diagnostics.Debug.WriteLine("MIRA Gmail notifications started.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gmail notification startup failed: {ex}");
            }

            try
            {
                _emailManager.StartMicrosoftNotifications(_microsoftProvider);
                System.Diagnostics.Debug.WriteLine("MIRA Microsoft 365 notifications started.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Microsoft notification startup failed: {ex}");
            }

        };

        #endregion
    }

    /// <summary>
    /// Creates a MIRA navigation button with independently coloured
    /// icon and text, and a configurable hover tooltip.
    /// </summary>
    /// <param name="glyph">The Segoe MDL2 icon glyph.</param>
    /// <param name="text">The visible button caption.</param>
    /// <param name="hoverText">The tooltip shown when hovering.</param>
    /// <param name="glyphColor">Optional icon colour.</param>
    /// <param name="textColor">Optional caption colour.</param>
    /// <returns>The configured navigation button.</returns>
    /// <example>
    /// var button = CreateNavigationButton(
    ///     "\uE715", "Email", "Open Email",
    ///     MiraPalette.Accent, MiraPalette.TextPrimary);
    /// </example>
    private static Button CreateNavigationButton(
        string glyph,
        string text,
        string hoverText,
        Color? glyphColor = null,
        Color? textColor = null)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };

        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 32,
            Foreground = new SolidColorBrush(glyphColor ?? MiraPalette.Accent),
        });

        if (!string.IsNullOrEmpty(text))
        {
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 20,
                Foreground = new SolidColorBrush(
                    textColor ?? MiraPalette.TextPrimary),
                Margin = new Thickness(10, 0, 0, 0)
            });
        }

        return new Button
        {
            Content = content,
            Style = (Style)Application.Current.FindResource(
                "MiraMenuButtonStyle"),
            ToolTip = hoverText,
        };
    }

    #endregion

    #region Event Helpers

    /// <summary>
    /// Shows a temporary message for an unimplemented module.
    /// </summary>
    /// <param name="module">The name of the module.</param>
    private static void ShowPlaceholder(string module)
    {
        MessageBox.Show(
            $"{module} module is not implemented yet.",
            "MIRA",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    #region Email Integration

    /// <summary>
    /// Retrieves the latest unread email count through
    /// MiraEmailManager and updates the Email button tooltip.
    /// </summary>
    /// <param name="emailButton">
    /// The navigation button to update.
    /// </param>
    private async Task UpdateEmailTooltipAsync(Button emailButton)
    {
        emailButton.ToolTip = "Checking emails...";

        try
        {
            string[] accountStatuses = await Task.WhenAll(
                _emailAccounts.Select(async account =>
                {
                    try
                    {
                        EmailAccountSummary summary = await _emailManager.GetUnreadSummaryAsync(account.Id);

                        return $"{summary.DisplayName}: {summary.UnreadCount} unread";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Email account '{account.Id}' failed: {ex}");

                        _emailIndicators[account.Id].SetUnreadCount(0);

                        return $"{account.DisplayName}: unavailable";
                    }
                }));

            emailButton.ToolTip = string.Join(
                Environment.NewLine,
                accountStatuses);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Email refresh failed: {ex}");

            emailButton.ToolTip = "Email unavailable";
        }
    }

    /// <summary>
    /// Updates the appropriate email indicator when its
    /// unread count changes.
    /// </summary>
    /// <param name="summary">
    /// The updated unread summary for the account.
    /// </param>
    private void OnUnreadCountChanged(EmailAccountSummary summary)
    {
        System.Diagnostics.Debug.WriteLine($"UI EVENT: {summary.AccountId} = {summary.UnreadCount}");
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnUnreadCountChanged(summary)));
            return;
        }

        if (_emailIndicators.TryGetValue(summary.AccountId, out EmailUnreadIndicator? indicator))
            indicator.SetUnreadCount(summary.UnreadCount);
    }

    #endregion

}


