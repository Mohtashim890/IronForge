using IronForge.Application.Configurations;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Agents.Memory.Summarization
{
    public class ConversationSummaryPolicy
     : IConversationSummaryPolicy
    {
        private readonly ConversationContextOptions _options;

        public ConversationSummaryPolicy(
            IOptions<ConversationContextOptions> options)
        {
            _options = options.Value;
        }

        public bool ShouldSummarize(
            ConversationSummaryPolicyContext context)
        {
            if (!_options.AutomaticSummarizationEnabled)
            {
                return false;
            }

            if (!context.IsSessionActive)
            {
                return false;
            }

            if (context.UnsummarizedMessageCount <
                _options.MinimumMessagesBeforeSummary)
            {
                return false;
            }

            if (context.EstimatedUnsummarizedTokens >=
                _options.SummaryTriggerTokens)
            {
                return true;
            }

            if (context.UnsummarizedMessageCount >=
                _options.SummaryTriggerMessageCount)
            {
                return true;
            }

            return false;
        }
    }
}
