using IronForge.Shared.Services;

namespace IronForge.Services
{
    /// <summary>
    /// Opens external pages (payment checkout) in the system browser, so the
    /// payment provider's page runs outside the app's web view.
    /// </summary>
    public sealed class MauiLinkOpener : IExternalLinkOpener
    {
        public async Task OpenAsync(string url)
        {
            await Browser.Default.OpenAsync(new Uri(url), BrowserLaunchMode.SystemPreferred);
        }
    }
}
