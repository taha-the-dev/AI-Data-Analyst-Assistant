using System.Security.Claims;

namespace AnalystAI.Api.Services;

/// <summary>The account the current request is signed in as, or null when it is not.</summary>
public interface ICurrentUser
{
    string? Id { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? Id => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
