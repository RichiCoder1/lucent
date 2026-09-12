using System.Security.Cryptography;
using System.Text;

namespace Lucent.Core;

/// <summary>Communicates the meaning of a durable inline notice without relying on color.</summary>
public enum NoticeSeverity
{
    /// <summary>Supporting information.</summary>
    Information,

    /// <summary>A completed operation.</summary>
    Success,

    /// <summary>A condition requiring attention.</summary>
    Warning,

    /// <summary>A failure with persistent recovery information.</summary>
    Error,
}

public static partial class Components
{
    /// <summary>Displays a persistent inline message with a severity icon and optional explicitly named action.</summary>
    /// <remarks>Visibility belongs to application state. The notice never auto-dismisses or creates an assertive announcement loop.</remarks>
    [LucentComponent]
    public static ComponentRecipe InlineNotice(
        Func<string> message,
        NoticeSeverity severity = NoticeSeverity.Information,
        Action? onAction = null,
        string? actionLabel = null,
        Style? style = null,
        SemanticAnnouncement announcement = SemanticAnnouncement.None
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));
        if (!Enum.IsDefined(announcement))
            throw new ArgumentOutOfRangeException(nameof(announcement));
        if ((onAction is null) != (actionLabel is null))
            throw new ArgumentException(
                "A notice action requires both its callback and accessible label."
            );
        if (actionLabel is not null)
            _ = Required(actionLabel, nameof(actionLabel));
        return InlineNoticeContent(message, severity, onAction, actionLabel, style, announcement);
    }

    internal static string NoticeLabel(NoticeSeverity severity) =>
        severity switch
        {
            NoticeSeverity.Information => "Information",
            NoticeSeverity.Success => "Success",
            NoticeSeverity.Warning => "Warning",
            NoticeSeverity.Error => "Error",
            _ => throw new ArgumentOutOfRangeException(nameof(severity)),
        };

    internal static ImageSource NoticeIcon(NoticeSeverity severity) =>
        NoticeArtwork.Sources[(int)severity];

    [LucentComponent]
    internal static ComponentRecipe InlineNoticeFrame(
        [DefaultContent] ComponentContent content,
        Style? style = null
    ) =>
        ComponentRecipe.Create(
            "inline-notice",
            (context, root) =>
            {
                var theme = context.Theme;
                var presentation = Style.Empty.Bind(
                    VisualProperties.Border,
                    () =>
                        theme.PresentationMode == ControlPresentationMode.Minimal
                            ? Border.None
                            : Border.Hairline(theme.Token(ControlThemes.Border))
                );
                root.Present(theme, presentation, style);
                context.Mount(root, content);
            }
        );

    private static class NoticeArtwork
    {
        internal static readonly ImageSource[] Sources =
        [
            Source("information", "<circle cx='12' cy='12' r='9'/><path d='M12 11v6M12 7v1'/>"),
            Source("success", "<circle cx='12' cy='12' r='9'/><path d='m7 12 3 3 7-7'/>"),
            Source("warning", "<path d='M12 3 2 21h20ZM12 9v5M12 17v1'/>"),
            Source("error", "<circle cx='12' cy='12' r='9'/><path d='m8 8 8 8M16 8l-8 8'/>"),
        ];

        // Small original vector geometry uses the ordinary bounded packaged-image pipeline.
        // No native object, alternate decoder or independently scheduled loader is retained here.
        private static ImageSource Source(string name, string geometry)
        {
            var bytes = Encoding.UTF8.GetBytes(
                "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' stroke='black' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>"
                    + geometry
                    + "</svg>"
            );
            return ImageSource.FromAsset(
                new AssetReference(
                    new AssetId("Lucent.Core", "notices/" + name + ".svg"),
                    Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    bytes.Length,
                    AssetFormat.Svg,
                    () => new MemoryStream(bytes, writable: false),
                    new AssetImageMetadata(24, 24)
                )
            );
        }
    }
}
