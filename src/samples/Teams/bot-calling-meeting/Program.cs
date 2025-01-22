// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CallingBotSample.Bots;
using CallingBotSample.Cache;
using CallingBotSample.Services.BotFramework;
using CallingBotSample.Services.CognitiveServices;
using CallingBotSample.Services.MicrosoftGraph;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.BotBuilder;
using CallingBotSample.AdaptiveCards;
using CallingBotSample.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Agents.Samples;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add AspNet token validation
builder.Services.AddBotAspNetAuthentication(builder.Configuration);

// Add basic bot functionality
builder.Services.AddTransient<IBot, MessageBot>();
builder.Services.AddTransient<CallingBot>();

builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection("AzureAd"));
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Bot"));
builder.Services.Configure<CognitiveServicesOptions>(builder.Configuration.GetSection("CognitiveServices"));
builder.Services.Configure<UsersOptions>(builder.Configuration.GetSection("Users"));

builder.Services.AddSingleton<IAdaptiveCardFactory, AdaptiveCardFactory>();
builder.Services.AddMicrosoftGraphServices(options => builder.Configuration.Bind("AzureAd", options));

builder.Services.AddSingleton<IConnectorClientFactory, ConnectorClientFactory>();
builder.Services.AddScoped<ISpeechService, SpeechService>();

builder.Services.AddTransient<IBotService, BotService>();
builder.Services.AddCaches();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => "Microsoft Copilot SDK Sample");
    app.UseDeveloperExceptionPage();
    app.MapControllers().AllowAnonymous();
}
else
{
    app.MapControllers();
}

app.Run();
