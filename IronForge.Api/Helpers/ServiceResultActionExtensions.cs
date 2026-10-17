using IronForge.Application.Commons;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Helpers
{
    public static class ServiceResultActionExtensions
    {
        /// <summary>
        /// Maps a failed result to its status code with the message as body
        /// (the clients read plain-text error bodies). 403 keeps the message,
        /// unlike Forbid(), so the UI can explain why.
        /// </summary>
        public static IActionResult ToErrorResult<T>(
            this ControllerBase controller,
            ServiceResult<T> result)
        {
            var status = result.StatusCode ?? StatusCodes.Status400BadRequest;

            return controller.StatusCode(status, result.ErrorMessage);
        }

        public static IActionResult ToActionResult<T>(
            this ControllerBase controller,
            ServiceResult<T> result)
        {
            return result.Success
                ? controller.Ok(result.Data)
                : controller.ToErrorResult(result);
        }
    }
}
