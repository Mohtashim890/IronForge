using Microsoft.AspNetCore.Components;

namespace IronForge.Shared.Services;

/// <summary>
/// Opens pages outside the app, such as a payment checkout. The mobile app
/// opens the system browser; the web apps navigate in the browser.
/// </summary>
public interface IExternalLinkOpener
{
    Task OpenAsync(string url);
}

public sealed class NavigationLinkOpener : IExternalLinkOpener
{
    private readonly NavigationManager _navigation;

    public NavigationLinkOpener(NavigationManager navigation)
    {
        _navigation = navigation;
    }

    public Task OpenAsync(string url)
    {
        _navigation.NavigateTo(url, forceLoad: true);
        return Task.CompletedTask;
    }
}
