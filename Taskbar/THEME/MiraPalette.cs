
using System.Windows.Media;

namespace Taskbar.THEME;

/// <summary>
/// Defines the shared colour palette for the MIRA interface.
/// All application branding colours should be maintained here.
/// </summary>
public static class MiraPalette
{
    #region Background Colours

    /// <summary>
    /// Primary dark purple surface used by the launcher and menus.
    /// </summary>
    public static readonly Color Surface =
        Color.FromArgb(245, 20, 17, 32);

    #endregion

    #region Accent Colours

    /// <summary>
    /// Primary pink accent used for borders and highlights.
    /// </summary>
    public static readonly Color Accent =
        Color.FromRgb(245, 93, 196);

    /// <summary>
    /// Pink colour used for the MIRA logo.
    /// </summary>
    public static readonly Color Logo =
        Color.FromRgb(255, 116, 209);

    /// <summary>
    /// Neon pink used for glow and shadow effects.
    /// </summary>
    public static readonly Color Glow =
        Color.FromRgb(255, 77, 187);

    #endregion

    #region Text Colours

    /// <summary>
    /// Primary foreground colour for text and navigation controls.
    /// </summary>
    public static readonly Color TextPrimary = Colors.White;

    #endregion
}
