using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;

namespace IronForge.Shared.Components.Gym;

/// <summary>
/// SVG elements Razor cannot write directly: &lt;text&gt; is reserved inside
/// Razor code blocks, so chart labels are emitted as encoded markup.
/// </summary>
public static class Svg
{
    public static MarkupString Text(string cssClass, double x, double y, string anchor, string content) =>
        new(
            $"<text class=\"{cssClass}\" x=\"{x.ToString("0.##", CultureInfo.InvariantCulture)}\" " +
            $"y=\"{y.ToString("0.##", CultureInfo.InvariantCulture)}\" text-anchor=\"{anchor}\">" +
            $"{WebUtility.HtmlEncode(content)}</text>");
}
