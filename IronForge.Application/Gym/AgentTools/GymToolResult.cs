using IronForge.Application.Commons;

namespace IronForge.Application.Gym.AgentTools;

/// <summary>
/// The shape every gym tool returns to the model: success with data, or a
/// plain error it can explain to the user. Services enforce authorization;
/// their 403 messages are safe to show.
/// </summary>
public static class GymToolResult
{
    public static object From<T>(ServiceResult<T> result) =>
        result.Success
            ? new { Success = true, Data = result.Data, Error = (string?)null }
            : Failure(result.ErrorMessage);

    public static object Ok(object data) =>
        new { Success = true, Data = data, Error = (string?)null };

    public static object Failure(string error) =>
        new { Success = false, Data = (object?)null, Error = error };
}
