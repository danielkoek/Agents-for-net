// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Builder.UserAuth;
using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.Agents.Builder.Errors;
using System.Collections.Generic;
using Microsoft.Agents.Core.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.Builder.App.UserAuth
{
    public delegate Task AuthorizationFailure(ITurnContext turnContext, ITurnState turnState, string handlerName, SignInResponse response, IActivity initiatingActivity, CancellationToken cancellationToken);

    /// <summary>
    /// UserAuthorization supports and extensible number of OAuth flows.
    /// 
    /// Auto Sign In:
    /// If enabled in <see cref="UserAuthorizationOptions"/>, sign in starts automatically after the first Message the user sends.  When
    /// the sign in is complete, the turn continues with the original message. On failure, <see cref="OnUserSignInFailure(Func{ITurnContext, ITurnState, string, SignInResponse, CancellationToken, Task})"/>
    /// is called.
    /// 
    /// Manual Sign In:
    /// <see cref="SignInUserAsync"/> is used to get a cached token or start the sign in.  In either case, the
    /// <see cref="OnUserSignInSuccess(Func{ITurnContext, ITurnState, string, string, CancellationToken, Task})"/> and
    /// <see cref="OnUserSignInFailure(Func{ITurnContext, ITurnState, string, SignInResponse, CancellationToken, Task})"/> should
    /// be set to handle continuation.  That is, after calling SignInUserAsync, the turn should be considered complete,
    /// and performing actions after that could be confusing.  i.e., Perform additional turn activity in OnUserSignInSuccess.
    /// </summary>
    /// <remarks>
    /// This is always executed in the context of a turn for the user in <see cref="ITurnContext.Activity.From"/>.
    /// </remarks>
    public class UserAuthorization
    {
        private readonly AutoSignInSelectorAsync? _startSignIn;
        private const string SIGN_IN_STATE_KEY = "__SignInState__";
        private readonly IUserAuthorizationDispatcher _dispatcher;
        private readonly UserAuthorizationOptions _options;
        private readonly AgentApplication _app;
        private readonly Dictionary<string, string> _authTokens = [];
        private readonly ILogger<UserAuthorization> _logger;

        /// <summary>
        /// Callback when user sign in fail
        /// </summary>
        private AuthorizationFailure _userSignInFailureHandler;

        public string DefaultHandlerName { get; private set; }

        public UserAuthorization(AgentApplication app, UserAuthorizationOptions options, ILogger<UserAuthorization> logger = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _dispatcher = options.Dispatcher;

            if (_app.Options.Adapter == null)
            {
                throw Core.Errors.ExceptionHelper.GenerateException<ArgumentNullException>(ErrorHelper.UserAuthorizationRequiresAdapter, null);
            }

            if (_options.AutoSignIn != null)
            {
                _startSignIn = _options.AutoSignIn;
            }
            else
            {
                // If AutoSignIn wasn't specified, default to true. 
                _startSignIn = (context, cancellationToken) => Task.FromResult(true);
            }

            DefaultHandlerName = _options.DefaultHandlerName ?? _dispatcher.Default.Name;

            if (!_dispatcher.TryGet(DefaultHandlerName, out _))
            {
                throw ExceptionHelper.GenerateException<IndexOutOfRangeException>(ErrorHelper.UserAuthorizationDefaultHandlerNotFound, null, DefaultHandlerName);
            }

            _logger = logger;
            _logger ??= app.Options.LoggerFactory != null ? _logger = app.Options.LoggerFactory.CreateLogger<UserAuthorization>() : NullLogger<UserAuthorization>.Instance;
        }

        /// <summary>
        /// Return a previously acquired token.
        /// </summary>
        /// <param name="handlerName"></param>
        /// <returns></returns>
        public string GetTurnToken(string handlerName)
        {
            return _authTokens.TryGetValue(handlerName, out var token) ? token : default;
        }

        /// <summary>
        /// Acquire a token with OAuth.  <see cref="OnUserSignInSuccess(Func{ITurnContext, ITurnState, string, string, CancellationToken, Task})"/> and
        /// <see cref="OnUserSignInFailure(Func{ITurnContext, ITurnState, string, SignInResponse, CancellationToken, Task})"/> should
        /// be set to handle continuation.  Those handlers will be called with a token is acquired.
        /// </summary>
        /// <param name="turnContext"> The turn context.</param>
        /// <param name="turnState"></param>
        /// <param name="handlerName">The name of the authorization setting.</param>
        /// <param name="exchangeConnection"></param>
        /// <param name="exchangeScopes"></param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="InvalidOperationException">If a flow is already active.</exception>
        public async Task<SignInResponse> SignInUserAsync(ITurnContext turnContext, ITurnState turnState, string handlerName, string exchangeConnection = null, IList<string> exchangeScopes = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(turnContext);
            ArgumentNullException.ThrowIfNull(turnState);
            ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);

            // Only one active flow allowed
            var signInState = GetSignInState(turnState);
            if (!string.IsNullOrEmpty(signInState.ActiveHandler))
            {
                throw ExceptionHelper.GenerateException<InvalidOperationException>(ErrorHelper.UserAuthorizationAlreadyActive, null, signInState.ActiveHandler);
            }

            // Handle the case where we already have a token for this handler and the Agent is calling this again.
            var existingCachedToken = GetTurnToken(handlerName);
            if (existingCachedToken != null)
            {
                return new SignInResponse(SignInStatus.Complete) { Token = existingCachedToken };
            }

            if (turnContext.Activity.IsType(ActivityTypes.Invoke))
            {
                _logger.LogWarning("SignInUserAsync with '{HandlerName}' within an Invoke request.", handlerName);
            }

            SignInResponse response = await _dispatcher.SignUserInAsync(turnContext, handlerName, true, exchangeConnection, exchangeScopes, cancellationToken).ConfigureAwait(false);

            if (response.Status == SignInStatus.Complete)
            {
                CacheToken(handlerName, response.Token);
                return response;
            }

            if (response.Status == SignInStatus.Pending)
            {
                signInState.ActiveHandler = handlerName;
                signInState.ManualContext = new ManualContext() { PassedOBOConnectionName = exchangeConnection, PassedOBOScopes = exchangeScopes };
                signInState.InitiatingActivity = turnContext.Activity;
                await turnState.User.SaveChangesAsync(turnContext, true, cancellationToken).ConfigureAwait(false);

                // Poll for token
                string token = null;
                int delay = 5000;
                var stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();
                _dispatcher.TryGet(handlerName, out var handler);
                do
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = 1000;

                    await turnState.User.LoadAsync(turnContext, true, cancellationToken: cancellationToken).ConfigureAwait(false);

                    signInState = GetSignInState(turnState);
                    if (signInState.ManualContext.SignInResponses.TryGetValue(handlerName, out response))
                    {
                        if (response.Status == SignInStatus.Complete)
                        {
                            // we need to get the token again since we're not storing in State.
                            token = await handler.GetUserToken(turnContext, exchangeConnection, exchangeScopes, cancellationToken).ConfigureAwait(false);
                        }
                    }
                } while (token == null && response == null && stopwatch.Elapsed.TotalMilliseconds < handler.Timeout);

                DeleteSignInState(turnState);

                if (token == null)
                {
                    await handler.ResetStateAsync(turnContext, cancellationToken).ConfigureAwait(false);
                    return response ?? new SignInResponse(SignInStatus.Error) { Cause = AuthExceptionReason.Timeout };
                }

                CacheToken(handlerName, token);
                return new SignInResponse(SignInStatus.Complete) { Token = token };
            }

            return response;
        }

        public async Task SignOutUserAsync(ITurnContext turnContext, ITurnState turnState, string? flowName = null, CancellationToken cancellationToken = default)
        {
            var flow = flowName ?? DefaultHandlerName;
            await _dispatcher.SignOutUserAsync(turnContext, flow, cancellationToken).ConfigureAwait(false);
            DeleteCachedToken(flow);
        }

        /// <summary>
        /// Clears all UserAuth state for the user.  This includes cached tokens, and flow related state.
        /// </summary>
        /// <param name="turnContext"></param>
        /// <param name="turnState"></param>
        /// <param name="handlerName"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ResetStateAsync(ITurnContext turnContext, ITurnState turnState, string handlerName = null, CancellationToken cancellationToken = default)
        {
            handlerName ??= DefaultHandlerName;
            
            await SignOutUserAsync(turnContext, turnState, handlerName, cancellationToken).ConfigureAwait(false);
            await _dispatcher.ResetStateAsync(turnContext, handlerName, cancellationToken).ConfigureAwait(false);
            DeleteSignInState(turnState);            
        }

        /// <summary>
        /// The handler function is called when the user sign in flow fails
        /// </summary>
        /// <remarks>
        /// This is called for either Manual or Auto SignIn flows.  However, normally expected AgentApplication
        /// Turn process has not been performed during an Auto Sign In.  This handler should be used to send failure message to the user
        /// and the turn ended.
        /// </remarks>
        /// <param name="handler">The handler function to call when the user failed to signed in</param>
        /// <returns>The class itself for chaining purpose</returns>
        public void OnUserSignInFailure(AuthorizationFailure handler)
        {
            _userSignInFailureHandler = handler;
        }

        /// <summary>
        /// This starts/continues the sign in flow.
        /// </summary>
        /// <remarks>
        /// This should be called to start or continue the user auth until true is returned, which indicates sign in is complete.
        /// When complete, the token is cached and can be access via <see cref="GetTurnToken"/>.  For manual sign in, the <see cref="OnUserSignInSuccess"/> or 
        /// <see cref="OnUserSignInFailure"/> are called at completion.
        /// </remarks>
        /// <param name="turnContext"></param>
        /// <param name="turnState"></param>
        /// <param name="handlerName">The name of the handler defined in <see cref="UserAuthorizationOptions"/></param>
        /// <param name="cancellationToken"></param>
        /// <returns>false indicates the sign in is not complete.</returns>
        internal async Task<bool> StartOrContinueSignInUserAsync(ITurnContext turnContext, ITurnState turnState, string handlerName = null, CancellationToken cancellationToken = default)
        {
            // If a flow is active, continue that.
            var signInState = GetSignInState(turnState);
            string? activeFlowName = signInState.ActiveHandler;
            bool flowContinuation = activeFlowName != null;
            bool autoSignIn = _startSignIn != null && await _startSignIn(turnContext, cancellationToken);

            if (autoSignIn || flowContinuation)
            {
                // Auth flow hasn't start yet.
                activeFlowName ??= handlerName ?? DefaultHandlerName;

                // Get token or start flow for specified flow.
                SignInResponse response = await _dispatcher.SignUserInAsync(
                    turnContext, 
                    activeFlowName, 
                    forceSignIn: !flowContinuation,
                    exchangeConnection: signInState.ManualContext?.PassedOBOConnectionName, 
                    exchangeScopes: signInState.ManualContext?.PassedOBOScopes, 
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.Status == SignInStatus.Pending)
                {
                    if (!flowContinuation)
                    {
                        // Bank the incoming Activity so it can be executed after sign in is complete.
                        signInState.InitiatingActivity = turnContext.Activity;
                        signInState.ActiveHandler = activeFlowName;
                        await turnState.SaveStateAsync(turnContext, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }

                    // Flow started, pending user input
                    return false;
                }

                // An InvalidActivity is expected, but anything else is a hard error and the flow is cancelled.
                if (response.Status == SignInStatus.Error)
                {
                    // Clear user auth state
                    await _dispatcher.ResetStateAsync(turnContext, activeFlowName, cancellationToken).ConfigureAwait(false);
                    DeleteSignInState(turnState);
                    await turnState.User.SaveChangesAsync(turnContext, cancellationToken: cancellationToken).ConfigureAwait(false);

                    // Handle manual signin completion
                    if (signInState.ManualContext != null)
                    {
                        // Save the response for the blocking SignInUser to pick up.
                        signInState.ManualContext.SignInResponses.Add(activeFlowName, response);
                        await turnState.User.SaveChangesAsync(turnContext, cancellationToken: cancellationToken).ConfigureAwait (false);

                        return false;
                    }

                    if (_userSignInFailureHandler != null)
                    {
                        await _userSignInFailureHandler(turnContext, turnState, activeFlowName, response, signInState.InitiatingActivity, cancellationToken).ConfigureAwait(false);
                        return false;
                    }

                    await turnContext.SendActivitiesAsync(
                        _options.SignInFailedMessage == null ? [MessageFactory.Text("SignIn Failed")] : _options.SignInFailedMessage(activeFlowName, response), 
                        cancellationToken).ConfigureAwait(false);
                    return false;
                }

                if (response.Status == SignInStatus.Complete)
                {
                    CacheToken(activeFlowName, response.Token);

                    if (signInState.ManualContext != null)
                    {
                        // Save the response for the blocking SignInUser to pick up.
                        response.Token = null;
                        signInState.ManualContext.SignInResponses.Add(activeFlowName, response);
                        await turnState.User.SaveChangesAsync(turnContext, cancellationToken: cancellationToken).ConfigureAwait(false);

                        return false;
                    }

                    DeleteSignInState(turnState);

                    if (signInState.InitiatingActivity != null)
                    {
                        // If the current activity matches the one used to trigger sign in, then
                        // this is because the user received a token that didn't involve a multi-turn
                        // flow.  No further action needed.
                        if (!ProtocolJsonSerializer.Equals(signInState.InitiatingActivity, turnContext.Activity))
                        {
                            // Since we could be handling an Invoke in this turn, and Teams has expectation for Invoke response times,
                            // we need to continue the conversation in a different turn with the original Activity that triggered sign in.
                            await turnState.SaveStateAsync(turnContext, cancellationToken: cancellationToken).ConfigureAwait(false);
                            await _app.Options.Adapter.ProcessProactiveAsync(turnContext.Identity, signInState.InitiatingActivity, _app, cancellationToken).ConfigureAwait(false);
                            return false;
                        }
                    }
                }
            }

            // Sign in is complete (or never started if Auto Sign in is false)
            // AgentApplication will perform normal ITurnContext.Activity routing to Agent.
            return true;
        }

        /// <summary>
        /// Set token in state
        /// </summary>
        /// <param name="name">The name of token</param>
        /// <param name="token">The value of token</param>
        private void CacheToken(string name, string token)
        {
            _authTokens[name] = token;
        }

        /// <summary>
        /// Delete token from turn state
        /// </summary>
        /// <param name="name">The name of token</param>
        private void DeleteCachedToken(string name)
        {
            _authTokens.Remove(name);
        }

        private static SignInState GetSignInState(ITurnState turnState)
        {
            return turnState.User.GetValue<SignInState>(SIGN_IN_STATE_KEY, () => new());
        }

        private static void DeleteSignInState(ITurnState turnState)
        {
            turnState.User.DeleteValue(SIGN_IN_STATE_KEY);
        }
    }

    class ManualContext
    {
        public Dictionary<string, SignInResponse> SignInResponses { get; set; } = [];
        public string PassedOBOConnectionName { get; set; }
        public IList<string> PassedOBOScopes { get; set; }
    }

    class SignInState
    {
        public bool IsActive() => !string.IsNullOrEmpty(ActiveHandler);
        public string ActiveHandler { get; set; }
        public IActivity InitiatingActivity { get; set; }
        public ManualContext ManualContext { get; set; }
    }
}
