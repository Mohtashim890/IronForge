namespace IronForge.Application.Gym.Members;

/// <summary>The gender values a member profile accepts (any capitalisation in).</summary>
public static class MemberGender
{
    public static readonly string[] Values = ["Female", "Male", "Other"];

    public const string Invalid = "Gender must be Female, Male or Other.";

    /// <summary>
    /// The canonical value, or null for an empty input. False when the
    /// input is not one of <see cref="Values"/>.
    /// </summary>
    public static bool TryNormalize(string? input, out string? gender)
    {
        gender = null;

        if (string.IsNullOrWhiteSpace(input))
            return true;

        gender = Values.FirstOrDefault(x => string.Equals(x, input.Trim(), StringComparison.OrdinalIgnoreCase));
        return gender is not null;
    }
}
