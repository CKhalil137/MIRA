
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Taskbar.THEME;

namespace Taskbar.MENUS;

/// <summary>
/// Provides the floating MIRA menu, including its appearance,
/// positioning and visibility.
/// </summary>
internal sealed class MiraMenuPopup : Popup
{
    #region Initialisation

    /// <summary>
    /// Creates a MIRA menu positioned above the specified control.
    /// </summary>
    /// <param name="anchor">
    /// The UI element used to position the menu.
    /// </param>
    /// <example>
    /// Create a menu anchored to the MIRA logo:
    /// <code>
    /// var menu = new MiraMenuPopup(miraLogo);
    /// menu.Toggle();
    /// </code>
    /// </example>
    public MiraMenuPopup(UIElement anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        PlacementTarget = anchor;
        Placement = PlacementMode.Custom;
        StaysOpen = true;

        CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        [
            new CustomPopupPlacement(
                new Point(
                    (targetSize.Width - popupSize.Width) / 2,
                    -popupSize.Height - 24),
                PopupPrimaryAxis.Horizontal)
        ];

        CreateControls();
    }

    #endregion

    #region Control Creation

    /// <summary>
    /// Creates the menu's visual content.
    /// </summary>
    private void CreateControls()
    {
        var settingsButton = CreateMenuButton("Settings", "\uE713");
        var aboutButton = CreateMenuButton("About MIRA", "\uE946");
        var exitButton = CreateMenuButton("Exit MIRA", "\uE7E8");

        settingsButton.Click += (_, _) =>
        {
            IsOpen = false;
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };

        aboutButton.Click += (_, _) =>
        {
            IsOpen = false;
            AboutRequested?.Invoke(this, EventArgs.Empty);
        };

        exitButton.Click += (_, _) =>
        {
            IsOpen = false;
            ExitRequested?.Invoke(this, EventArgs.Empty);
        };

        Child = new Border
        {
            Background = new SolidColorBrush(MiraPalette.Surface),
            BorderBrush = new SolidColorBrush(MiraPalette.Accent),

            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(15),

            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Main Menu",
                        Foreground = new SolidColorBrush(MiraPalette.Accent),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(5, 5, 5, 15)
                    },
                    settingsButton,
                    aboutButton,
                    exitButton
                }
            }
        };
    }

    /// <summary>
    /// Creates a consistently styled MIRA menu button
    /// containing an icon and descriptive text.
    /// </summary>
    /// <param name="caption">The menu label.</param>
    /// <param name="iconGlyph">The Windows icon glyph.</param>
    /// <returns>The configured menu button.</returns>
    /// <example>
    /// var settings = CreateMenuButton("Settings", "\uE713");
    /// </example>
    private static Button CreateMenuButton(
        string caption, string iconGlyph)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        content.Children.Add(new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = new SolidColorBrush(MiraPalette.Accent),
            FontSize = 20,
            Width = 30,
            VerticalAlignment = VerticalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        });

        return new Button
        {
            Content = content,
            Style = (Style)Application.Current.FindResource(
                "MiraMenuButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 2)
        };
    }

    #endregion
    /// <summary>
    /// Raised when the user requests MIRA settings information.
    /// </summary>
    public event EventHandler? SettingsRequested;
    /// <summary>
    /// Raised when the user requests about MIRA information.
    /// </summary>
    public event EventHandler? AboutRequested;
    /// <summary>
    /// Raised when the user requests to exit MIRA.
    /// </summary>
    public event EventHandler? ExitRequested;

    #region Menu Operations

    /// <summary>
    /// Opens the menu if closed, or closes it if open.
    /// </summary>
    /// <example>
    /// Toggle an existing MIRA menu:
    /// <code>
    /// menu.Toggle();
    /// </code>
    /// </example>
    public void Toggle()
    {
        IsOpen = !IsOpen;
    }

    #endregion
}
