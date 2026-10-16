using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations;

public sealed class InvitationOptions
{
    public const string SectionName = "Invitations";

    /// <summary>
    /// Web page that accepts invitations, e.g. https://app.example.com/accept-invite.
    /// The token is appended as ?token=... . It must point at the web app, not
    /// the API, because the link is opened by the invitee in a browser.
    /// </summary>
    [Required]
    [Url]
    public string AcceptUrl { get; set; } = "";

    [Range(1, 30)]
    public int LifetimeDays { get; set; } = 7;
}
