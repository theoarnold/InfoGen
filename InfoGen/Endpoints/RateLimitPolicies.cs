namespace InfoGen.Endpoints;

/// <summary>Named rate-limiting policies, shared between registration in Program.cs and the
/// endpoints that apply them so the names can't drift apart silently.</summary>
public static class RateLimitPolicies
{
    /// <summary>Caps /api/generation/research, which runs a Gemini call without consuming a credit.</summary>
    public const string Research = "research";
}
