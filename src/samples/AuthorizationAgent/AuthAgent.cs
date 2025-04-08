// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Builder.UserAuth;
using Microsoft.Agents.Core.Models;
using Microsoft.Graph;
using Microsoft.VisualBasic;
using System;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AuthorizationAgent;

public class AuthAgent : AgentApplication
{
    public AuthAgent(AgentApplicationOptions options) : base(options)
    {
        OnConversationUpdate(ConversationUpdateEvents.MembersAdded, WelcomeMessageAsync);

        // OnMessage("/signin", SignInAsync);
        // OnMessage("/signout", SignOutAsync);
        // OnMessage("/reset", ResetAsync);
        OnMessage("/me", MeAsync);
        OnMessage("/chats", ChatAsync);
        // base.Authorization.OnUserSignInSuccess(OnUserSignInSuccess);
        base.Authorization.OnUserSignInFailure(OnUserSignInFailure);

       OnActivity(ActivityTypes.Message, OnMessageAsync);
    }

    private async Task MeAsync(ITurnContext turnContext, ITurnState state, CancellationToken ct)
    {
        //await Authorization.SignInUserAsync(turnContext, state, "graph", cancellationToken: ct);
        string token = Authorization.GetTurnToken(Authorization.DefaultHandlerName);
        // string token = state.User.GetValue<string>("token");
        //if (token == null)
        //{
        //    await turnContext.SendActivityAsync("Login first", cancellationToken: ct);
        //    await Authorization.SignInUserAsync(turnContext, state, "graph", cancellationToken: ct);
        //    return;
        //}

        var _userClient = new GraphServiceClient(new DelegateAuthenticationProvider(requestMessage =>
           {
               requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
               return Task.FromResult(0);
           }));
        
        var me = await _userClient.Me.Request().GetAsync();
        Console.WriteLine(me);
        await turnContext.SendActivityAsync($"Hello {me.GivenName} {me.Surname} ({me.DisplayName}) {me.JobTitle} {me.Mail}");
       
    }

    private async Task ChatAsync(ITurnContext turnContext, ITurnState state, CancellationToken ct)
    {
        //await Authorization.SignInUserAsync(turnContext, state, "graph", cancellationToken: ct);
        string token = Authorization.GetTurnToken(Authorization.DefaultHandlerName);
        // string token = state.User.GetValue<string>("token");
        //if (token == null)
        //{
        //    await turnContext.SendActivityAsync("Login first", cancellationToken: ct);
        //    await Authorization.SignInUserAsync(turnContext, state, "graph", cancellationToken: ct);
        //    return;
        //}

        var _userClient = new GraphServiceClient(new DelegateAuthenticationProvider(requestMessage =>
        {
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
            return Task.FromResult(0);
        }));

        var chats = await _userClient.Me.Chats.Request().GetAsync();
        await turnContext.SendActivityAsync($"found chats {chats.Count}");
        foreach (var chat in chats)
        {
            await turnContext.SendActivityAsync($"chat {chat.Id} {chat.ChatType} {chat.Messages?.Count} {chat.WebUrl}");
        }
    }

    private async Task WelcomeMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        foreach (ChannelAccount member in turnContext.Activity.MembersAdded)
        {
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                await turnContext.SendActivityAsync(MessageFactory.Text(
                    """
                    Welcome to AuthorizationAgent. Type :
                        '/me' to show your graph account. 
                        '/chats' to show your teams chats.
                    
                    Anything else will be repeated back.
                    
                    """), cancellationToken);
            }
        }
    }

    //private async Task SignInAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    //{
    //    await Authorization.SignInUserAsync(turnContext, turnState, "graph", cancellationToken: cancellationToken);
    //}

    //private async Task SignOutAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    //{
    //    await Authorization.SignOutUserAsync(turnContext, turnState, cancellationToken: cancellationToken);
    //    await turnContext.SendActivityAsync("You have signed out", cancellationToken: cancellationToken);
    //}

    //private async Task ResetAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    //{
    //    await turnState.Conversation.DeleteStateAsync(turnContext, cancellationToken);
    //    await turnState.User.DeleteStateAsync(turnContext, cancellationToken);
    //    await turnContext.SendActivityAsync("Ok I've deleted the current turn state", cancellationToken: cancellationToken);
    //}

    private async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        if (turnContext.Activity.Text == "auto")
        {
            await turnContext.SendActivityAsync($"Auto Sign In: Successfully logged in to '{Authorization.DefaultHandlerName}', token length: {Authorization.GetTurnToken(Authorization.DefaultHandlerName).Length}", cancellationToken: cancellationToken);
        }
        else
        {
            // Not one of the defined inputs.  Just repeat what user said.
            await turnContext.SendActivityAsync($"You said: {turnContext.Activity.Text}", cancellationToken: cancellationToken);
        }
    }

    //private async Task OnUserSignInSuccess(ITurnContext turnContext, ITurnState turnState, string handlerName, string token, IActivity initiatingActivity, CancellationToken cancellationToken)
    //{
    //    // turnState.User.SetValue<string>("token", token);
    //    await turnContext.SendActivityAsync($"Manual Sign In:Successfully logged in to '{Authorization.DefaultHandlerName}', token length: {Authorization.GetTurnToken(Authorization.DefaultHandlerName).Length}", cancellationToken: cancellationToken);
    //}

    private async Task OnUserSignInFailure(ITurnContext turnContext, ITurnState turnState, string handlerName, SignInResponse response, IActivity initiatingActivity, CancellationToken cancellationToken)
    {
        await turnContext.SendActivityAsync($"Manual Sign In: Failed to login to '{handlerName}': {response.Error.Message}", cancellationToken: cancellationToken);
    }
}
