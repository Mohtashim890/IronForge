using Android.Webkit;
using WebView = Android.Webkit.WebView;

namespace IronForge
{
    /// <summary>
    /// Lets the app's own pages use the camera (scanning the gym's entrance
    /// code). Android's WebView refuses getUserMedia unless the chrome client
    /// grants it, and the app must also hold the CAMERA permission, which is
    /// asked for the first time it is needed.
    ///
    /// Wraps the BlazorWebView's own chrome client and forwards everything
    /// else to it (file picking for photo upload, windows, console).
    /// </summary>
    internal sealed class CameraWebChromeClient : WebChromeClient
    {
        private readonly WebChromeClient _inner;

        public CameraWebChromeClient(WebChromeClient inner)
        {
            _inner = inner;
        }

        public override void OnPermissionRequest(PermissionRequest? request)
        {
            if (request is null)
                return;

            var resources = request.GetResources() ?? [];

            // Only the camera, and only for the app's own pages (https://0.0.0.x).
            var cameraOnly = resources.Length == 1 && resources[0] == PermissionRequest.ResourceVideoCapture;
            var ownPage = request.Origin?.Host?.StartsWith("0.0.0.", StringComparison.Ordinal) == true;

            if (!cameraOnly || !ownPage)
            {
                request.Deny();
                return;
            }

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();

                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                }

                if (status == PermissionStatus.Granted)
                    request.Grant([PermissionRequest.ResourceVideoCapture]);
                else
                    request.Deny();
            });
        }

        public override void OnPermissionRequestCanceled(PermissionRequest? request) =>
            _inner.OnPermissionRequestCanceled(request);

        public override bool OnShowFileChooser(WebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams) =>
            _inner.OnShowFileChooser(webView, filePathCallback, fileChooserParams);

        public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Android.OS.Message? resultMsg) =>
            _inner.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);

        public override void OnCloseWindow(WebView? window) =>
            _inner.OnCloseWindow(window);

        public override bool OnConsoleMessage(ConsoleMessage? consoleMessage) =>
            _inner.OnConsoleMessage(consoleMessage);

        public override void OnProgressChanged(WebView? view, int newProgress) =>
            _inner.OnProgressChanged(view, newProgress);

        public override bool OnJsAlert(WebView? view, string? url, string? message, JsResult? result) =>
            _inner.OnJsAlert(view, url, message, result);

        public override bool OnJsConfirm(WebView? view, string? url, string? message, JsResult? result) =>
            _inner.OnJsConfirm(view, url, message, result);

        public override bool OnJsPrompt(WebView? view, string? url, string? message, string? defaultValue, JsPromptResult? result) =>
            _inner.OnJsPrompt(view, url, message, defaultValue, result);
    }
}
