using Microsoft.AspNetCore.Identity;

namespace AnalystAI.Api.Models;

/// <summary>An account. The email address is the sign-in name; there are no roles.</summary>
public class AppUser : IdentityUser
{
    public DateTime CreatedAt { get; set; }
}
