using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Taskbar.CONTROLS;

/// <summary>
/// Displays a compact, coloured unread-email counter
/// with a transparent background and circular outline.
/// Automatically hides when the unread count is zero.
/// </summary>
internal sealed class EmailUnreadIndicator : Border
{
    #region Fields

    private readonly TextBlock _countText;

    #endregion

    #region Initialisation

    /// <summary>
    /// Creates an email indicator using the specified account colour.
    /// </summary>
    /// <param name="accountColor">The account's indicator colour.</param>
    /// <example>
    /// <code>
    /// var indicator = new EmailUnreadIndicator(Colors.DeepSkyBlue);
    /// indicator.SetUnreadCount(5);
    /// </code>
    /// </example>
    public EmailUnreadIndicator(Color accountColor)
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Margin = new Thickness(2, 0, 2, 0);

        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Visibility = Visibility.Collapsed;

        _countText = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accountColor),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Child = _countText;
    }

    #endregion

    #region Indicator Operations

    /// <summary>
    /// Updates the displayed unread count.
    /// Automatically hides the indicator when the count is zero.
    /// </summary>
    /// <param name="unreadCount">The number of unread messages.</param>
    /// <example>
    /// <code>
    /// indicator.SetUnreadCount(12);
    /// </code>
    /// </example>
    public void SetUnreadCount(int unreadCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unreadCount);

        _countText.Text = unreadCount > 99 ? "99+" : unreadCount.ToString();
        Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}