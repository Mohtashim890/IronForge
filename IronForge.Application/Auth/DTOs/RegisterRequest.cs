using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Auth.DTOs
{
    public class RegisterRequest
    {
        [Required]
        [StringLength(
            50,
            MinimumLength = 3,
            ErrorMessage =
                "Username must be between 3 and 50 characters.")]
        public string Username { get; set; } = "";

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = "";

        [Required]
        [StringLength(
            100,
            MinimumLength = 8,
            ErrorMessage =
                "Password must be at least 8 characters.")]
        public string Password { get; set; } = "";
    }
}
