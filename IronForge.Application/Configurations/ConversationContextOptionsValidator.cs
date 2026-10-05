using Microsoft.Extensions.Options;

namespace IronForge.Application.Configurations
{
    public sealed class ConversationContextOptionsValidator
     : IValidateOptions<ConversationContextOptions>
    {
        public ValidateOptionsResult Validate(
            string? name,
            ConversationContextOptions options)
        {
            var failures = new List<string>();

            ValidateToolResultLimits(options, failures);
            ValidateContextBudget(options, failures);
            ValidateSummaryThresholds(options, failures);

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }

        private static void ValidateToolResultLimits(
            ConversationContextOptions options,
            List<string> failures)
        {
            if (options.ToolResultHeadTokens >
                options.MaximumToolResultTokens)
            {
                failures.Add(
                    $"{nameof(ConversationContextOptions.ToolResultHeadTokens)} " +
                    $"must be less than or equal to " +
                    $"{nameof(ConversationContextOptions.MaximumToolResultTokens)}.");
            }
        }

        private static void ValidateContextBudget(
            ConversationContextOptions options,
            List<string> failures)
        {
            var reservedTokens =
                options.ResponseReserveTokens
                + options.SystemPromptReserveTokens
                + options.ToolDefinitionReserveTokens
                + options.CurrentMessageReserveTokens;

            if (reservedTokens > options.MaximumContextTokens)
            {
                failures.Add(
                    "The combined response, system prompt, tool definition, " +
                    "and current message reservations must not exceed " +
                    $"{nameof(ConversationContextOptions.MaximumContextTokens)}.");
                            }
        }

        private static void ValidateSummaryThresholds(
            ConversationContextOptions options,
            List<string> failures)
        {
            if (options.SummaryTriggerTokens >=
                options.MaximumContextTokens)
            {
                failures.Add(
                    $"{nameof(ConversationContextOptions.SummaryTriggerTokens)} " +
                    $"must be less than " +
                    $"{nameof(ConversationContextOptions.MaximumContextTokens)}.");
            }

            if (options.MinimumMessagesBeforeSummary >
                options.SummaryTriggerMessageCount)
            {
                failures.Add(
                    $"{nameof(ConversationContextOptions.MinimumMessagesBeforeSummary)} " +
                    $"must be less than or equal to " +
                    $"{nameof(ConversationContextOptions.SummaryTriggerMessageCount)}.");
            }
        }
    }
}
