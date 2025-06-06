﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.Client;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Client.Compat;
using DialogRootBot.Dialogs;
using DialogRootBot.Bots;
using Microsoft.AspNetCore.Http;
using System.Threading;
using Microsoft.Agents.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Logging.AddConsole();

// Add the Agent
builder.AddAgent<RootBot<MainDialog>>();

// Add the AgentHost, but using the back-compat handler for BF Skills
builder.AddAgentHost<SkillChannelApiHandler>();

// Register the MainDialog that will be run by the bot.
builder.Services.AddSingleton<MainDialog>();

// Register IStorage.  For development, MemoryStorage is suitable.
// For production Agents, persisted storage should be used so
// that state survives Agent restarts, and operate correctly
// in a cluster of Agent instances.
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Register Conversation state (used by the Dialog system itself).
builder.Services.AddSingleton<ConversationState>();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
app.UseRouting();
app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    await adapter.ProcessAsync(request, response, agent, cancellationToken);
})
    .AllowAnonymous();

// Hardcoded for brevity and ease of testing. 
// In production, this should be set in configuration.
app.Urls.Add($"http://localhost:3978");
app.MapGet("/", () => "Microsoft Agents SDK Sample");
app.MapControllers();

app.Run();
