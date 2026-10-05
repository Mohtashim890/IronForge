namespace IronForge.Application.Agents.Memory.ConversationSession
{
    public class ConversationAccessResult
    {
        public bool Allowed { get; init; }

        public string? Reason { get; init; }

        public static ConversationAccessResult Allow()
        {
            return new ConversationAccessResult
            {
                Allowed = true
            };
        }

        public static ConversationAccessResult Deny(
            string reason)
        {
            return new ConversationAccessResult
            {
                Allowed = false,
                Reason = reason
            };
        }
    }
}