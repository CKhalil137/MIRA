using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Taskbar.CONTROLS;

/// <summary>
/// Displays a queued, single-line notification feed that scrolls from right to left.
/// The ticker is presentation-only and never queries email providers directly.
/// </summary>
public sealed class MiraNotificationTicker : Grid
{
    #region Fields

    private const int MaximumQueuedMessages = 25;
    private const double PixelsPerSecond = 90;

    private readonly Queue<(string Text, string Glyph)> _queue = new();
    private readonly StackPanel _messageLine;
    private readonly TextBlock _glyphText;
    private readonly TextBlock _messageText;
    private readonly TranslateTransform _translation = new();

    private (string Text, string Glyph)? _activeMessage;
    private bool _isAnimating;
    private bool _isPaused;

    #endregion

    #region Initialisation

    /// <summary>
    /// Creates the transparent, non-interactive ticker surface.
    /// </summary>
    /// <example>
    /// <code>
    /// var ticker = new MiraNotificationTicker();
    /// ticker.Enqueue("Email activity: Personal Gmail, 3 unread");
    /// </code>
    /// </example>
    public MiraNotificationTicker()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        IsHitTestVisible = false;

        var viewport = new Canvas { ClipToBounds = true };
        Children.Add(viewport);

        _glyphText = new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 122, 206)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };

        _messageText = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 213, 239)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(239, 61, 177),
                BlurRadius = 7,
                ShadowDepth = 0,
                Opacity = 0.6
            }
        };

        _messageLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            RenderTransform = _translation
        };

        _messageLine.Children.Add(_glyphText);
        _messageLine.Children.Add(_messageText);
        Canvas.SetTop(_messageLine, 5);
        viewport.Children.Add(_messageLine);

        Loaded += (_, _) => TryStartNext();
        SizeChanged += (_, _) => TryStartNext();
    }

    #endregion

    #region Queue Operations

    /// <summary>
    /// Adds a message to the scrolling queue. Thread-safe for WPF callers.
    /// </summary>
    /// <param name="text">Text to display.</param>
    /// <param name="glyph">Optional Segoe MDL2 icon glyph.</param>
    /// <example>
    /// <code>
    /// ticker.Enqueue("Personal Gmail: 4 unread", "\uE715");
    /// </code>
    /// </example>
    public void Enqueue(string text, string glyph = "\uE715")
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Enqueue(text, glyph)));
            return;
        }

        if (_queue.Count >= MaximumQueuedMessages)
            _queue.Dequeue();

        _queue.Enqueue((text.Trim(), glyph));
        TryStartNext();
    }

    /// <summary>
    /// Pauses the ticker while a module menu is visible, then resumes the queue.
    /// </summary>
    /// <param name="isPaused">True to pause playback.</param>
    /// <example>
    /// <code>
    /// ticker.SetPaused(true);
    /// ticker.SetPaused(false);
    /// </code>
    /// </example>
    public void SetPaused(bool isPaused)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => SetPaused(isPaused)));
            return;
        }

        if (_isPaused == isPaused)
            return;

        _isPaused = isPaused;

        if (_isPaused && _isAnimating)
        {
            // Put the interrupted message back at the head of the queue.
            var pending = _queue.ToArray();
            _queue.Clear();

            if (_activeMessage is { } current)
                _queue.Enqueue(current);

            foreach (var message in pending)
                _queue.Enqueue(message);

            _isAnimating = false;
            _activeMessage = null;
            _messageLine.Visibility = Visibility.Collapsed;
            _translation.BeginAnimation(TranslateTransform.XProperty, null);
            _translation.X = 0;
        }

        if (!_isPaused)
            TryStartNext();
    }

    #endregion

    #region Animation

    private void TryStartNext()
    {
        if (_isPaused || _isAnimating || !IsLoaded || ActualWidth <= 0 || _queue.Count == 0)
            return;

        _activeMessage = _queue.Dequeue();
        _glyphText.Text = _activeMessage.Value.Glyph;
        _messageText.Text = _activeMessage.Value.Text;
        _messageLine.Visibility = Visibility.Visible;

        _messageLine.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double startX = ActualWidth;
        double endX = -Math.Max(1, _messageLine.DesiredSize.Width);
        double travelSeconds = Math.Max(3, (startX - endX) / PixelsPerSecond);

        _translation.X = startX;
        _isAnimating = true;

        var animation = new DoubleAnimation(startX, endX, TimeSpan.FromSeconds(travelSeconds))
        {
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            if (!_isAnimating)
                return;

            _isAnimating = false;
            _activeMessage = null;
            _messageLine.Visibility = Visibility.Collapsed;
            _translation.BeginAnimation(TranslateTransform.XProperty, null);
            _translation.X = 0;

            Dispatcher.BeginInvoke(new Action(TryStartNext), DispatcherPriority.Background);
        };

        _translation.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    #endregion
}
