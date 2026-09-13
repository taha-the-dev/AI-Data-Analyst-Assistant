using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using AnalystAI.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

/// <summary>
/// Sign-up, sign-in, sign-out and account deletion.
///
/// The session is an HttpOnly, SameSite=Strict cookie issued by ASP.NET Core
/// Identity, which also hashes passwords and locks an account after repeated
/// failures. Nothing a script on the page can read ever carries the session.
/// </summary>
public static class AuthEndpoints
{
    private static readonly EmailAddressAttribute EmailFormat = new();

    /// <summary>
    /// Hash of a password nobody knows, checked when an address has no account,
    /// so that refusal takes as long as a wrong password does. Without it the
    /// response time says which addresses are registered.
    /// </summary>
    private static string? decoyHash;

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Accounts");

        group.MapPost("/signup", async (
            UserManager<AppUser> users, SignInManager<AppUser> signIn, CredentialsRequest? body) =>
        {
            if (Invalid(body, forSignUp: true) is { } problem) return problem;

            var email = body!.Email.Trim();
            if (string.Equals(body.Password.Trim(), email, StringComparison.OrdinalIgnoreCase))
                return Problems.BadRequest("Choose a different password", "The password cannot be your email address.");

            var user = new AppUser { UserName = email, Email = email, CreatedAt = DateTime.UtcNow };
            var created = await users.CreateAsync(user, body.Password);

            if (!created.Succeeded)
            {
                // This does tell a caller the address is registered. With no email
                // service there is no way to sign up that avoids it, so the
                // endpoint is rate limited instead: a few attempts a minute.
                if (created.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
                    return Problems.Conflict("An account with this email already exists", "Sign in with it instead.");

                return Problems.BadRequest(
                    "The account could not be created",
                    string.Join(" ", created.Errors.Select(e => e.Description)));
            }

            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Created("/api/auth/me", new AccountDto(user.Email!));
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimits.Auth)
        .WithName("SignUp")
        .WithSummary("Create an account and start a session for it.");

        group.MapPost("/login", async (
            UserManager<AppUser> users, SignInManager<AppUser> signIn, IPasswordHasher<AppUser> hasher,
            CredentialsRequest? body) =>
        {
            if (Invalid(body, forSignUp: false) is { } problem) return problem;

            var user = await users.FindByEmailAsync(body!.Email.Trim());
            if (user is null)
            {
                var decoy = new AppUser();
                decoyHash ??= hasher.HashPassword(decoy, Guid.NewGuid().ToString("N"));
                hasher.VerifyHashedPassword(decoy, decoyHash, body.Password);
                return WrongCredentials();
            }

            // Lockout is counted even for a locked account, and a locked account
            // gets the same answer as a wrong password, so the response never
            // reveals which of the two it was.
            var result = await signIn.PasswordSignInAsync(user, body.Password, isPersistent: false, lockoutOnFailure: true);
            return result.Succeeded ? Results.Ok(new AccountDto(user.Email!)) : WrongCredentials();
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimits.Auth)
        .WithName("SignIn")
        .WithSummary("Start a session with an email address and password.");

        group.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        })
        .AllowAnonymous()
        .WithName("SignOut")
        .WithSummary("End the current session.");

        group.MapGet("/me", async (
            UserManager<AppUser> users, SignInManager<AppUser> signIn, ClaimsPrincipal principal) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null)
            {
                // The cookie outlived its account.
                await signIn.SignOutAsync();
                return SignInRequired();
            }

            return Results.Ok(new AccountDto(user.Email!));
        })
        .RequireAuthorization()
        .WithName("CurrentAccount")
        .WithSummary("The signed-in account, or 401.");

        // DELETE carries a body here, so it is bound explicitly: ASP.NET Core
        // does not infer one for DELETE and refused to start without this.
        group.MapDelete("/account", async (
            AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            ClaimsPrincipal principal, [FromBody] DeleteAccountRequest? body, CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return SignInRequired();

            if (string.IsNullOrEmpty(body?.Password) || body.Password.Length > AccountRules.MaxPasswordLength)
                return Problems.BadRequest(
                    "Enter your password",
                    "Deleting an account needs its password, sent as { \"password\": \"...\" }.");

            // Counted towards lockout, so this is not a way around the sign-in limits.
            var check = await signIn.CheckPasswordSignInAsync(user, body.Password, lockoutOnFailure: true);
            if (!check.Succeeded)
                return Results.Problem(
                    title: "The password is incorrect",
                    detail: "The account was not deleted.",
                    statusCode: StatusCodes.Status403Forbidden);

            // Each delete names the account explicitly as well as passing through
            // the query filters: a statement that removes rows should not depend
            // on a single safeguard.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await db.ChatMessages.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.ChatSessions.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.Reports.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.SalesRows.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.DatasetColumns.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.Datasets.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.UserSettings.Where(e => e.UserId == user.Id).ExecuteDeleteAsync(ct);

            var deleted = await users.DeleteAsync(user);
            if (!deleted.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return Results.Problem(
                    title: "The account could not be deleted",
                    detail: "Nothing was removed. Try again in a moment.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            await transaction.CommitAsync(ct);
            await signIn.SignOutAsync();
            return Results.NoContent();
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimits.Auth)
        .WithName("DeleteAccount")
        .WithSummary("Delete the signed-in account and everything it owns. Needs its password.");

        return api;
    }

    private static IResult? Invalid(CredentialsRequest? body, bool forSignUp)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrEmpty(body.Password))
            return Problems.BadRequest(
                "Email and password are both required",
                "Send { \"email\": \"...\", \"password\": \"...\" } in the body.");

        var email = body.Email.Trim();
        if (email.Length > AccountRules.MaxEmailLength || !EmailFormat.IsValid(email))
            return Problems.BadRequest(
                "Enter a valid email address",
                $"Use an address such as name@example.com, up to {AccountRules.MaxEmailLength} characters.");

        if (body.Password.Length > AccountRules.MaxPasswordLength)
            return Problems.BadRequest(
                "That password is too long",
                $"Use at most {AccountRules.MaxPasswordLength} characters.");

        if (forSignUp && body.Password.Length < AccountRules.MinPasswordLength)
            return Problems.BadRequest(
                "That password is too short",
                $"Use at least {AccountRules.MinPasswordLength} characters. A few unrelated words make a long password easy to remember.");

        return null;
    }

    private static IResult WrongCredentials() => Results.Problem(
        title: "Email or password is incorrect",
        detail: $"Check both and try again. After {AccountRules.MaxFailedAttempts} failed attempts in a row, " +
                $"an account is locked for {AccountRules.LockoutDuration.TotalMinutes:0} minutes.",
        statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>What every protected endpoint answers when there is no session.</summary>
    internal static IResult SignInRequired() => Results.Problem(
        title: "Sign in to continue",
        detail: "There is no active session. It may have expired, or been ended in another tab.",
        statusCode: StatusCodes.Status401Unauthorized);
}
