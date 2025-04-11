﻿# Agent2 Sample

This is a sample of a simple Agent that represents remote Agent, or secondary Agent, of a multi-agent configuration.  This Agent is configured to accept a request and echo the text of the request back to the caller.

## Prerequisites

**To run the sample on a development workstation (local development), the following tools and SDK's are required:**

- [.NET SDK](https://dotnet.microsoft.com/download) version 8.0
- Visual Studio 2022+ with the .net workload installed.
- [Bot Framework Emulator](https://github.com/Microsoft/BotFramework-Emulator/releases) for Testing Web Chat.

**To run the sample connected to Azure Bot Service, the following additional tools are required:**

- Access to an Azure Subscription with access to preform the following tasks:
    - Create and configure Entra ID Application Identities
    - Create and configure an [Azure Bot Service](https://aka.ms/AgentsSDK-CreateBot) for your Agent
    - A tunneling tool to allow for local development and debugging should you wish to do local development whilst connected to a external client such as Microsoft Teams.

## Getting Started with Agent2 Sample

### Local development

Local development means running the Sample on 'your' workstation for development and debugging purposes.

Local development begins with utilizing the Bot Framework Emulator and Visual Studio on your workstation to build and run your Agent while debugging from Visual Studio.

If you do not wish to configure authentication at this time, Skip to "Running the Agent for the first time".

### Authentication and Local Development

There are two ways to support local development, depending on what your working with.

**- Anonymous or No-Authentication**

While this is the simplest way to get started and run your Agent, there are important limitations to consider.

When running in Anonymous mode, your Agent will not be able to create authentication tokens to access other services, Nor can it interact with Azure Bot Services. Therefor, Anonymous Mode is there to support testing basic operational features of the Agent and to work with and test various events that your Agent can process. It should be used only during initial development.

> [!IMPORTANT]
> This sample is configured, by default, for Anonymous Authentication when running in Development mode. Before using this sample with Azure Bot Service, it is necessary to configure authentication.

**- Configured Authentication with Entra ID.**

Configuring authentication for your Agent will allow it to communicate with Azure Bot Services and create access tokens for other services.

However there are a few key items to consider when configuring authentication for your Agent.

1. Both Azure Bot Service's Bot registration and your Agent Must use the same ClientID for creating an authentication token.
    1. By default Azure Bot Service will create a Managed Identity when you initially configure the bot registration.  **This type of identity cannot currently be used when working with Local Development**.
    1. To successfully use **Local Development** with an Azure bot Service Identity, you must utilize either **Client Secret** or **Client Certificate** based authentication.
1. Once you are ready to deploy to Azure App Services, you can use all types of Identity supported.
    1. Its often more efficient to have an Azure Bot Service Registration for Local Development and a separate one configured for your App Services Deployment.

#### Configuring authentication in the Agent2 Sample Project

To configure authentication into the Agent2 Sample Project you will need the following information:

1. Follow the instructions for [Register a Single Tenant agent with Azure](https://aka.ms/AgentsSDK-CreateBot).
1. Record the Client ID (App ID), Client Secret, and Tenant ID of the Application just created. 
   1. Keep the Client ID handy as it will also be needed when configuring `Agent1`.

1. Once you have that information, to configure authentication, Open the `appsettings.json` file in the root of the sample project.

   ```json
   "Agent": {
     "ClientId": "{{Agent2ClientId}}",
   },

   "TokenValidation": {
     "Audiences": [
       "{{Agent2ClientId}}"
     ],
     "TenantId": "{{TenantId}}"
   },

   "Connections": {
     "ServiceConnection": {
       "Settings": {
         "AuthType": "ClientSecret", 
         "AuthorityEndpoint": "https://login.microsoftonline.com/{{TenantId}}",
         "ClientId": "{{Agent2ClientId}}",
         "ClientSecret": "{{ClientSecret}}", 
         "Scopes": [
           "https://api.botframework.com/.default"
         ]
      }
   }
   ```
    
   1. Replace all **{{Agent2ClientId}}** with the AppId of Agent2.
   1. Replace all **{{TenantId}}** to the Tenant Id where your application is registered.
   1. Set the **{{ClientSecret}}** to the Secret that was created for your identity.

> Storing sensitive values in appsettings is not recommend.  Follow [AspNet Configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0) for best practices.

## Running the Agent for the first time

To run the Agent2 Sample for the first time:

1. Open the Agent2 Sample in Visual Studio 2022
1. Run it in Debug Mode (F5)
1. A blank web page will open, note down the URL which should be similar too `https://localhost:39783/`
1. Open the [BotFramework Emulator](https://github.com/Microsoft/BotFramework-Emulator/releases)
    1. Click **Open Bot**
    1. In the bot URL field input the URL you noted down from the web page and add /api/messages to it. It should appear similar to `https://localhost:39783/api/messages`
    1. Click **Connect**

If you type a message and hit enter, or the send arrow, your messages should be echoed along with **Say “end” and I’ll end the conversation.**

If this happens, `Agent2` is working.

## Further reading
To learn more about building Agents, see our [Microsoft 365 Agents SDK](https://github.com/microsoft/agents) repo.