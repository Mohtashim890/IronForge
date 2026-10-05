using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Configurations
{
    public class ConversationContextOptions
    {
        public const string SectionName =
        "Agent:ConversationContext";

        [Range(1, int.MaxValue)]
        public int MaximumHistoryMessages { get; set; } = 100;

        [Range(1, int.MaxValue)]
        public int MaximumContextTokens { get; set; } = 16000;

        [Range(1, int.MaxValue)]
        public int ResponseReserveTokens { get; set; } = 2000;

        [Range(1, int.MaxValue)]
        public int SystemPromptReserveTokens { get; set; } = 1500;

        [Range(1, int.MaxValue)]
        public int ToolDefinitionReserveTokens { get; set; } = 1500;

        [Range(1, int.MaxValue)]
        public int CurrentMessageReserveTokens { get; set; } = 1000;

        [Range(1, int.MaxValue)]
        public int ApproximateCharactersPerToken { get; set; } = 4;

        [Range(1, int.MaxValue)]
        public int MaximumToolResultTokens { get; set; } = 2000;

        [Range(1, int.MaxValue)]
        public int ToolResultHeadTokens { get; set; } = 1800;

        public bool UseConversationSummary { get; set; } = true;

        public bool AutomaticSummarizationEnabled { get; set; } = true;

        [Range(1, int.MaxValue)]
        public int SummaryTriggerTokens { get; set; } = 12000;

        [Range(1, int.MaxValue)]
        public int SummaryTriggerMessageCount { get; set; } = 30;

        [Range(1, int.MaxValue)]
        public int MinimumMessagesBeforeSummary { get; set; } = 10;
    }
}
