﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using AdaptiveCards.Templating;
using AdaptiveCards;
using Microsoft.Agents.Core.Models;
using BotAllCards.Dialogs;
using Microsoft.Agents.BotBuilder.State;
using Microsoft.Agents.BotBuilder;

namespace BotAllCards.Bots
{
    // RichCardsBot prompts a user to select a Rich Card and then returns the card
    // that matches the user's selection.


    public class TeamsBot : DialogBot<MainDialog>
    {
        public TeamsBot(ConversationState conversationState, UserState userState, MainDialog dialog, ILogger<DialogBot<MainDialog>> logger)
            : base(conversationState, userState, dialog, logger)
        {
        }

        /// <summary>
        /// Override this in a derived class to provide logic for when members other than the bot join the conversation, such as your bot's welcome logic.
        /// </summary>
        /// <param name="membersAdded">A list of all the members added to the conversation, as described by the conversation update activity.</param>
        /// <param name="turnContext">A strongly-typed context object for this turn.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                // Greet anyone that was not the target (recipient) of this message.
                // To learn more about Adaptive Cards, see https://aka.ms/msbot-adaptivecards for more details.
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    var reply = MessageFactory.Text("Welcome to Card Bot."
                        + " This bot will show you different types of Cards."
                        + " Please type anything to get started.");

                    await turnContext.SendActivityAsync(reply, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Media url submitted.
        /// Refreshes the adaptive card with the media file.
        /// </summary>
        /// <param name="url">Url of the media file</param>
        /// <param name="turnContext">The context for the current turn.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        private InvokeResponse createAdaptiveCardInvokeResponseAsync(string url)
        {
            string[] filepath = new[] { ".", "Resources", "adaptiveCardMedia.json" };
            var adaptiveCardJson = File.ReadAllText(Path.Combine(filepath));
            AdaptiveCardTemplate template = new AdaptiveCardTemplate(adaptiveCardJson);
            var payloadData = new
            {
                mediaUrl = url,
            };

            var cardJsonString = template.Expand(payloadData);
            var adaptiveCardResponse = new AdaptiveCardInvokeResponse()
            {
                StatusCode = 200,
                Type = AdaptiveCard.ContentType,
                Value = JsonSerializer.Deserialize<object>(cardJsonString)
            };

            return CreateInvokeResponse(adaptiveCardResponse);
        }

        /// <summary>
        /// Media url submitted.
        /// checks whether the media URL is present and sends invokeresponse with the media 
        /// </summary>
        /// <param name="turnContext">The context for the current turn.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        protected override async Task<InvokeResponse> OnInvokeActivityAsync(ITurnContext<IInvokeActivity> turnContext, CancellationToken cancellationToken)
        {
            if (turnContext.Activity.Name == "adaptiveCard/action")
            {
                if (turnContext.Activity.Value == null)
                    return null;

                // Convert the Activity Value to a JSON string
                var jsonString = turnContext.Activity.Value.ToString();

                using (JsonDocument document = JsonDocument.Parse(jsonString))
                {
                    JsonElement root = document.RootElement;

                    if (root.TryGetProperty("action", out JsonElement actionElement))
                    {
                        if (actionElement.TryGetProperty("data", out JsonElement dataElement))
                        {
                            if (dataElement.TryGetProperty("url", out JsonElement urlElement))
                            {
                                string url = urlElement.GetString();
                                return await Task.FromResult(createAdaptiveCardInvokeResponseAsync(url));
                            }
                        }
                    }
                }
            }
            return null;
        }
    }
}