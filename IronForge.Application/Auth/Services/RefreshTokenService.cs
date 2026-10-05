using System.Security.Cryptography;
using System.Text;

namespace IronForge.Application.Auth.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        public string GenerateToken()
        {
            var randomBytes = new byte[64];

            RandomNumberGenerator.Fill(randomBytes);

            return Convert.ToBase64String(randomBytes);
        }

        public string HashToken(string token)
        {
            var tokenBytes = Encoding.UTF8.GetBytes(token);

            var hashBytes =
                SHA256.HashData(tokenBytes);

            return Convert.ToBase64String(hashBytes);
        }
    }
}
