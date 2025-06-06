﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Storage;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.Connector;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core;

namespace Microsoft.Agents.Extensions.Teams.Compat
{
    /// <summary>
    /// If the activity name is signin/tokenExchange, this middleware will attempt to
    /// exchange the token, and deduplicate the incoming call, ensuring only one
    /// exchange request is processed.
    /// </summary>
    /// <remarks>
    /// If a user is signed into multiple Teams clients, the Agent could receive a
    /// "signin/tokenExchange" from each client. Each token exchange request for a
    /// specific user login will have an identical Activity.Value.Id.
    /// 
    /// Only one of these token exchange requests should be processed by the Agent.
    /// The others return <see cref="System.Net.HttpStatusCode.PreconditionFailed"/>.
    /// For a distributed Agent in production, this requires a distributed storage
    /// ensuring only one token exchange is processed. This middleware supports
    /// CosmosDb storage found in Microsoft.Agents.Storage.CosmosDb, or MemoryStorage for
    /// local development. IStorage's ETag implementation for token exchange activity
    /// deduplication.
    /// </remarks>
    public class TeamsSSOTokenExchangeMiddleware : IMiddleware
    {
        private readonly IStorage _storage;
        private readonly string _oAuthConnectionName;

        /// <summary>
        /// Initializes a new instance of the <see cref="TeamsSSOTokenExchangeMiddleware"/> class.
        /// </summary>
        /// <param name="storage">The <see cref="IStorage"/> to use for deduplication.</param>
        /// <param name="connectionName">The connection name to use for the single
        /// sign on token exchange.</param>
        public TeamsSSOTokenExchangeMiddleware(IStorage storage, string connectionName)
        {
            AssertionHelpers.ThrowIfNull(storage, nameof(storage));
            AssertionHelpers.ThrowIfNullOrEmpty(connectionName, nameof(connectionName));

            _oAuthConnectionName = connectionName;
            _storage = storage;
        }

        /// <inheritdoc/>
        public async Task OnTurnAsync(ITurnContext turnContext, NextDelegate next, CancellationToken cancellationToken = default)
        {
            if (string.Equals(Channels.Msteams, turnContext.Activity.ChannelId, StringComparison.OrdinalIgnoreCase) 
                && string.Equals(SignInConstants.TokenExchangeOperationName, turnContext.Activity.Name, StringComparison.OrdinalIgnoreCase))
            {
                // If the TokenExchange is NOT successful, the response will have already been sent by ExchangedTokenAsync
                if (!await this.ExchangedTokenAsync(turnContext, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                // Only one token exchange should proceed from here. Deduplication is performed second because in the case
                // of failure due to consent required, every caller needs to receive the 
                if (!await DeduplicatedTokenExchangeIdAsync(turnContext, cancellationToken).ConfigureAwait(false))
                {
                    // If the token is not exchangeable, do not process this activity further.
                    return;
                }
            }

            await next(cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> DeduplicatedTokenExchangeIdAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            // Create a StoreItem with Etag of the unique 'signin/tokenExchange' request
            var storeItem = new TokenStoreItem
            {
                ETag = ProtocolJsonSerializer.ToJsonElements(turnContext.Activity.Value)["id"].ToString(),
            };

            var storeItems = new Dictionary<string, object> { { TokenStoreItem.GetStorageKey(turnContext), storeItem } };
            try
            {
                // Writing the IStoreItem with ETag of unique id will succeed only once
                await _storage.WriteAsync(storeItems, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)

                // Memory storage throws a generic exception with a Message of 'Etag conflict. [other error info]'
                // CosmosDbPartitionedStorage throws: ex.Message.Contains("pre-condition is not met")
                when (ex.Message.StartsWith("Etag conflict", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("pre-condition is not met"))
            {
                // Do NOT proceed processing this message, some other thread or machine already has processed it.

                // Send 200 invoke response.
                await SendInvokeResponseAsync(turnContext, cancellationToken: cancellationToken).ConfigureAwait(false);
                return false;
            }

            return true;
        }

        private static async Task SendInvokeResponseAsync(ITurnContext turnContext, object body = null, HttpStatusCode httpStatusCode = HttpStatusCode.OK, CancellationToken cancellationToken = default)
        {
            await turnContext.SendActivityAsync(
                new Activity
                {
                    Type = ActivityTypes.InvokeResponse,
                    Value = new InvokeResponse
                    {
                        Status = (int)httpStatusCode,
                        Body = body,
                    },
                }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> ExchangedTokenAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            TokenResponse tokenExchangeResponse = null;
            var tokenExchangeRequest = ProtocolJsonSerializer.ToObject<TokenExchangeInvokeRequest>(turnContext.Activity.Value);

            try
            {
                var userTokenClient = turnContext.Services.Get<IUserTokenClient>();
                if (userTokenClient != null)
                {
                    tokenExchangeResponse = await userTokenClient.ExchangeTokenAsync(
                        turnContext.Activity.From.Id,
                        _oAuthConnectionName,
                        turnContext.Activity.ChannelId,
                        new TokenExchangeRequest { Token = tokenExchangeRequest.Token },
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw new NotSupportedException("Token Exchange is not supported by the current adapter.");
                }
            }
            catch
            {
                // Ignore Exceptions
                // If token exchange failed for any reason, tokenExchangeResponse above stays null,
                // and hence we send back a failure invoke response to the caller.
            }

            if (string.IsNullOrEmpty(tokenExchangeResponse?.Token))
            {
                // The token could not be exchanged (which could be due to a consent requirement)
                // Notify the sender that PreconditionFailed so they can respond accordingly.

                var invokeResponse = new TokenExchangeInvokeResponse
                {
                    Id = tokenExchangeRequest.Id,
                    ConnectionName = _oAuthConnectionName,
                    FailureDetail = "The Agent is unable to exchange token. Proceed with regular login.",
                };

                await SendInvokeResponseAsync(turnContext, invokeResponse, HttpStatusCode.PreconditionFailed, cancellationToken).ConfigureAwait(false);

                return false;
            }

            return true;
        }

        private class TokenStoreItem : IStoreItem
        {
            public string ETag { get; set; }

            public static string GetStorageKey(ITurnContext turnContext)
            {
                var activity = turnContext.Activity;
                var channelId = activity.ChannelId ?? throw new InvalidOperationException("invalid activity-missing channelId");
                var conversationId = activity.Conversation?.Id ?? throw new InvalidOperationException("invalid activity-missing Conversation.Id");

                var value = activity.Value.ToJsonElements();
                if (value == null || !value.TryGetValue("id", out System.Text.Json.JsonElement idValue))
                {
                    throw new InvalidOperationException("Invalid signin/tokenExchange. Missing activity.Value.Id.");
                }

                return $"{channelId}/{conversationId}/{idValue}";
            }
        }
    }
}
