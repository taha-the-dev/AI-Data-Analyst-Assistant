namespace AnalystAI.Api.Security;

/// <summary>The numbers the account rules are built from, in one place.</summary>
public static class AccountRules
{
    /// <summary>
    /// Length is what makes a password hard to guess, so it is the only rule:
    /// no forced mix of symbols and capitals, which people satisfy predictably.
    /// </summary>
    public const int MinPasswordLength = 12;

    /// <summary>Long enough for any passphrase; short enough that hashing one is not a way to burn CPU.</summary>
    public const int MaxPasswordLength = 256;

    public const int MaxEmailLength = 254;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
}

/// <summary>Names of the rate-limiting policies endpoints opt into.</summary>
public static class RateLimits
{
    /// <summary>Sign-in, sign-up and account deletion, per client address.</summary>
    public const string Auth = "auth";

    /// <summary>Questions to the assistant, which may call a paid model, per account.</summary>
    public const string Assistant = "assistant";
}

/// <summary>Upper bounds on what a request may store.</summary>
public static class InputLimits
{
    public const long UploadBytes = 25 * 1024 * 1024;
    public const int QuestionLength = 1_000;
    public const int TitleLength = 120;
    public const int SubtitleLength = 200;
    public const int DatasetNameLength = 200;

    public static string Clip(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd();
}
