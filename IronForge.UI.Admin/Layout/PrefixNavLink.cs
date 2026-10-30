using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace IronForge.UI.Admin.Layout;

/// <summary>
/// A prefix-matching <see cref="NavLink"/> that is not active under the given sub-paths,
/// e.g. "Gym settings" (/settings) stays inactive on /settings/payments.
/// </summary>
public sealed class PrefixNavLink : NavLink
{
    [Parameter] public string[]? Except { get; set; }

    public PrefixNavLink() => Match = NavLinkMatch.Prefix;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override bool ShouldMatch(string uriAbsolute)
    {
        if (!base.ShouldMatch(uriAbsolute))
        {
            return false;
        }

        if (Except is not { Length: > 0 })
        {
            return true;
        }

        var path = "/" + Navigation.ToBaseRelativePath(uriAbsolute).Split('?', '#')[0].TrimEnd('/');
        return !Except.Any(x => path.Equals(x, StringComparison.OrdinalIgnoreCase)
                                || path.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase));
    }
}
