using IronForge.Shared.Models.Agents;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace IronForge.Shared.Services
{

    public static class AgentToolActivityMapper
    {
        public static ToolActivityDto? FromMessage(
            ConversationMessageDto message)
        {
            if (message is null)
                return null;

            if (string.IsNullOrWhiteSpace(message.ToolName))
                return null;

            if (string.IsNullOrWhiteSpace(message.ToolCallId))
                return null;

            var contentType =
                GetContentType(message.MetadataJson);

            if (contentType is not
                ("functionCall" or "functionResult"))
            {
                return null;
            }

            var activity = new ToolActivityDto
            {
                ToolCallId = message.ToolCallId,
                ToolName = message.ToolName,
                CreatedAtUtc = message.CreatedAtUtc
            };

            if (contentType == "functionCall")
            {
                activity.ArgumentsJson =
                    message.Content;

                activity.Status =
                    ToolActivityStatus.Started;
            }
            else
            {
                activity.ResultJson =
                    message.Content;

                activity.Status =
                    ToolActivityStatus.Completed;
            }

            return activity;
        }

        private static string? GetContentType(
            string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
                return null;

            try
            {
                using var document =
                    JsonDocument.Parse(metadataJson);

                if (document.RootElement.TryGetProperty(
                        "contentType",
                        out var contentType))
                {
                    return contentType.GetString();
                }
            }
            catch (JsonException)
            {
                // Invalid metadata is treated as
                // ordinary conversation content.
            }

            return null;
        }
    }
}
