using IronForge.Shared.Models;
using IronForge.Shared.Services.Gym;

namespace IronForge.Shared.Services;

/// <summary>The signed-in person's own two-factor sign-in (api/account/two-factor).</summary>
public sealed class AccountApiClient : ApiClientBase
{
    public AccountApiClient(HttpClient http) : base(http)
    {
    }

    public Task<ServiceResult<TwoFactorStatusDto>> GetTwoFactorAsync(CancellationToken ct = default) =>
        GetAsync<TwoFactorStatusDto>("api/account/two-factor", "Unable to load two-factor sign-in.", ct);

    public Task<ServiceResult<TwoFactorSetupDto>> BeginTwoFactorSetupAsync(CancellationToken ct = default) =>
        PostAsync<TwoFactorSetupDto>("api/account/two-factor/setup", null, "Unable to start the setup.", ct);

    public Task<ServiceResult<TwoFactorRecoveryCodesDto>> EnableTwoFactorAsync(string code, CancellationToken ct = default) =>
        PostAsync<TwoFactorRecoveryCodesDto>("api/account/two-factor/enable", new TwoFactorCodeRequest { Code = code }, "Unable to turn on two-factor sign-in.", ct);

    public Task<ServiceResult<TwoFactorRecoveryCodesDto>> RegenerateRecoveryCodesAsync(string code, CancellationToken ct = default) =>
        PostAsync<TwoFactorRecoveryCodesDto>("api/account/two-factor/recovery-codes", new TwoFactorCodeRequest { Code = code }, "Unable to create new recovery codes.", ct);

    public Task<ServiceResult<bool>> DisableTwoFactorAsync(string code, CancellationToken ct = default) =>
        PostAsync<bool>("api/account/two-factor/disable", new TwoFactorCodeRequest { Code = code }, "Unable to turn off two-factor sign-in.", ct);
}
