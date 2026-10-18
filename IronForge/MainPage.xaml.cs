namespace IronForge
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();

#if IOS || MACCATALYST
            // The camera preview plays inside the page, not in a full-screen player.
            blazorWebView.BlazorWebViewInitializing += (_, e) =>
            {
                e.Configuration.AllowsInlineMediaPlayback = true;
                e.Configuration.MediaTypesRequiringUserActionForPlayback = WebKit.WKAudiovisualMediaTypes.None;
            };
#endif

#if ANDROID
            // Scanning the gym's entrance code needs the camera in the WebView.
            blazorWebView.BlazorWebViewInitialized += (_, e) =>
            {
                e.WebView.SetWebChromeClient(new CameraWebChromeClient(e.WebView.WebChromeClient!));
            };
#endif
        }
    }
}
