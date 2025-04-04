// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Builder.UserAuth;
using Microsoft.Agents.Core.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AuthorizationAgent;

public class AuthAgent : AgentApplication
{
    public AuthAgent(AgentApplicationOptions options) : base(options)
    {
        // Perform an auto sign in if the user sends "auto"
        options.UserAuthorization.AutoSignIn = (turnContext, cancellationToken) => Task.FromResult(turnContext.Activity.Text == "auto");

        OnConversationUpdate(ConversationUpdateEvents.MembersAdded, WelcomeMessageAsync);

        // Manual sign in/out of 'graph'
        OnMessage("/signin", SignInAsync);
        OnMessage("/signout", SignOutAsync);

        // In this iteration this is only called for Auto SignIn
        // Could drop this in favor of AgentApplicationOptions.SignInFailedMessage
        Authorization.OnUserSignInFailure(OnUserSignInFailure);

        OnActivity(ActivityTypes.Message, OnMessageAsync, rank: RouteRank.Last);
    }

    private async Task WelcomeMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        foreach (ChannelAccount member in turnContext.Activity.MembersAdded)
        {
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                await turnContext.SendActivityAsync(MessageFactory.Text("Welcome to AuthorizationAgent. Type 'auto' to demonstrate Auto SignIn. Type '/signin' to sign in for graph.  Type '/signout' to sign-out.  Anything else will be repeated back."), cancellationToken);
            }
        }
    }

    private async Task SignInAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        var handlerName = "graph";
        var response = await Authorization.SignInUserAsync(turnContext, turnState, handlerName, cancellationToken: cancellationToken);
        if (response.Status == SignInStatus.Complete)
        {
            await turnContext.SendActivityAsync($"SignInAsync: Successfully logged in to '{handlerName}', token length: {response.Token.Length}", cancellationToken: cancellationToken);
        }
        else
        {
            await turnContext.SendActivityAsync($"SignInAsync: Failed logged in to '{handlerName}', error: {response.Cause}", cancellationToken: cancellationToken);
        }
    }

    private async Task SignOutAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        await Authorization.SignOutUserAsync(turnContext, turnState, "graph", cancellationToken);
        await turnContext.SendActivityAsync("You have signed out", cancellationToken: cancellationToken);
    }

    private async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        // After auto sign in is complete, the original Activity is processed.  In this case, "auto" is what the
        // user sent to start the sign in, and routed normally to this handler.
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

    private async Task OnUserSignInFailure(ITurnContext turnContext, ITurnState turnState, string handlerName, SignInResponse response, IActivity initiatingActivity, CancellationToken cancellationToken)
    {
        await turnContext.SendActivityAsync($"Auto Sign In: Failed to login to '{handlerName}': {response.Message}", cancellationToken: cancellationToken);
    }
}
