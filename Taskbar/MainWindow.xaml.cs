
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

using KSE.LIB.WPF.CONTROLS;
using KSE.LIB.WPF.LAYOUT;
using Taskbar.THEME;
using Taskbar.MENUS;

namespace Taskbar;

/// <summary>
/// Main window for the MIRA floating taskbar.
/// Constructs the interface dynamically using KSE-LIB.
/// </summary>
public partial class MainWindow : Window
{
    #region Window Initialisation

    /// <summary>
    /// Initialises the MIRA floating taskbar.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        ConfigureWindow();
        CreateControls();

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
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));
        GridLayoutManager.AddColumn(layout, new GridLength(1, GridUnitType.Star));

        #endregion

        #region Navigation Controls

        var chatButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE8F2", "", "Open MIRA Chat"),
            0, 6);

        var tasksButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE73E", "", "Open Tasks"),
            0, 7);

        var emailButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE715", "", "Open Emails"),
            0, 9);

        var calendarButton = GridLayoutManager.Place(
            layout,
            CreateNavigationButton(
                "\uE787", "", "Open Calendar"),
            0, 10);

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
            0, 8);

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

        #endregion
    }

    /// <summary>
    /// Creates a standard navigation button using the MIRA colour palette.
    /// </summary>
    /// <param name="caption">The symbol displayed on the button.</param>
    /// <param name="tooltip">The text displayed when hovering over the button.</param>
    /// <returns>The configured navigation button.</returns>
    /// <example>
    /// Create a button for a Tasks module:
    /// <code>
    /// var button = CreateNavigationButton("✓", "Tasks");
    /// </code>
    /// </example>

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
            FontSize = 20,
            Foreground = new SolidColorBrush(glyphColor ?? MiraPalette.Accent),
        });

        if (!string.IsNullOrEmpty(text))
        {
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 18,
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
}
