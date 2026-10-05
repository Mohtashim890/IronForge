namespace IronForge.Application.Auth.Services
{
    public interface IRefreshTokenService
    {
        string GenerateToken();

        string HashToken(string token);
    }
}
