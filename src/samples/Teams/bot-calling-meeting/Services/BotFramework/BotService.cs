// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using Microsoft.Agents.Connector;
using Microsoft.Agents.BotBuilder.Dialogs;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.Core.Interfaces;
using Microsoft.Agents.Connector.Types;

namespace CallingBotSample.Services.BotFramework
{
    /// <inheritdoc/>
    public class BotService : IBotService
    {
        private readonly IConnectorClientFactory connectorClientFactory;
        private readonly ILogger<BotService> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="BotService"/> class.
        /// </summary>
        /// <param name="connectorClientFactory">Connector client factory</param>
        /// <param name="logger">Logger.</param>
        public BotService(IConnectorClientFactory connectorClientFactory, ILogger<BotService> logger)
        {
            this.connectorClientFactory = connectorClientFactory;
            this.logger = logger;
        }

        /// <inheritdoc/>
        public Task<ResourceResponse> SendToConversation(string message, string conversationId)
        {
            return SendToConversation(MessageFactory.Text(message), conversationId);
        }

        /// <inheritdoc/>
        public Task<ResourceResponse> SendToConversation(Attachment attachment, string conversationId)
        {
            return SendToConversation(MessageFactory.Attachment(attachment), conversationId);
        }

        private async Task<ResourceResponse> SendToConversation(IActivity activity, string conversationId)
        {
            ConnectorClient client = connectorClientFactory.CreateConnectorClient();

            return await client.Conversations.SendToConversationAsync(conversationId, (Activity)activity);
        }
    }
}
