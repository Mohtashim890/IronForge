using IronForge.Shared.Services;

namespace IronForge.Services
{
    /// <summary>
    /// The phone's own owner check: fingerprint or face where set up, else the
    /// screen lock PIN, pattern or password. The operating system does the
    /// check and only answers yes or no: no biometric data reaches the app.
    /// </summary>
    public sealed class MauiPhoneUnlock : IPhoneUnlock
    {
        public async Task<PhoneUnlockResult> UnlockAsync(string reason)
        {
            try
            {
                return await MainThread.InvokeOnMainThreadAsync(() => UnlockOnMainThreadAsync(reason));
            }
            catch (Exception)
            {
                return PhoneUnlockResult.NotSupported;
            }
        }

#if ANDROID
        private static Task<PhoneUnlockResult> UnlockOnMainThreadAsync(string reason)
        {
            // BiometricPrompt with the screen lock as fallback needs Android 10.
            if (!OperatingSystem.IsAndroidVersionAtLeast(29) || Platform.CurrentActivity is not { } activity)
            {
                return Task.FromResult(PhoneUnlockResult.NotSupported);
            }

            var keyguard = (Android.App.KeyguardManager?)activity.GetSystemService(Android.Content.Context.KeyguardService);

            if (keyguard?.IsDeviceSecure != true)
            {
                return Task.FromResult(PhoneUnlockResult.NotSetUp);
            }

            var result = new TaskCompletionSource<PhoneUnlockResult>();

            var builder = new Android.Hardware.Biometrics.BiometricPrompt.Builder(activity)
                .SetTitle("Confirm it's you")
                .SetSubtitle(reason);

            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                builder.SetAllowedAuthenticators(
                    (int)(Android.Hardware.Biometrics.BiometricManagerAuthenticators.BiometricWeak |
                          Android.Hardware.Biometrics.BiometricManagerAuthenticators.DeviceCredential));
            }
            else
            {
#pragma warning disable CA1422 // The replacement exists from Android 11 only.
                builder.SetDeviceCredentialAllowed(true);
#pragma warning restore CA1422
            }

            builder.Build().Authenticate(
                new Android.OS.CancellationSignal(),
                activity.MainExecutor!,
                new UnlockCallback(result));

            return result.Task;
        }

        private sealed class UnlockCallback : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
        {
            private readonly TaskCompletionSource<PhoneUnlockResult> _result;

            public UnlockCallback(TaskCompletionSource<PhoneUnlockResult> result) => _result = result;

            public override void OnAuthenticationSucceeded(Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult? result) =>
                _result.TrySetResult(PhoneUnlockResult.Unlocked);

            // Cancelled, too many attempts, or no hardware: the prompt is gone.
            public override void OnAuthenticationError(Android.Hardware.Biometrics.BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
                _result.TrySetResult(PhoneUnlockResult.Failed);

            // One finger not recognised: the prompt stays open for another try.
            public override void OnAuthenticationFailed()
            {
            }
        }
#elif IOS || MACCATALYST
        private static async Task<PhoneUnlockResult> UnlockOnMainThreadAsync(string reason)
        {
            using var context = new LocalAuthentication.LAContext();

            if (!context.CanEvaluatePolicy(LocalAuthentication.LAPolicy.DeviceOwnerAuthentication, out var error))
            {
                return error?.Code == (long)LocalAuthentication.LAStatus.PasscodeNotSet
                    ? PhoneUnlockResult.NotSetUp
                    : PhoneUnlockResult.NotSupported;
            }

            var result = await context.EvaluatePolicyAsync(LocalAuthentication.LAPolicy.DeviceOwnerAuthentication, reason);

            return result.Item1 ? PhoneUnlockResult.Unlocked : PhoneUnlockResult.Failed;
        }
#elif WINDOWS
        private static async Task<PhoneUnlockResult> UnlockOnMainThreadAsync(string reason)
        {
            var availability = await Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();

            if (availability == Windows.Security.Credentials.UI.UserConsentVerifierAvailability.NotConfiguredForUser)
            {
                return PhoneUnlockResult.NotSetUp;
            }

            if (availability != Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available)
            {
                return PhoneUnlockResult.NotSupported;
            }

            var result = await Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync(reason);

            return result == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified
                ? PhoneUnlockResult.Unlocked
                : PhoneUnlockResult.Failed;
        }
#else
        private static Task<PhoneUnlockResult> UnlockOnMainThreadAsync(string reason) =>
            Task.FromResult(PhoneUnlockResult.NotSupported);
#endif
    }
}
