# StreamingAgent1 Sample

This is the sample of an Agent communicating with another Agent using DeliverMode.Stream. For this sample, `Agent1` is communicating with another Agent (`Agent2`) in a multi-turn conversation.

## Prerequisites

**To run the sample on a development workstation (local development), the following tools and SDK's are required:**

- [.NET SDK](https://dotnet.microsoft.com/download) version 8.0
- Visual Studio 2022+ with the .net workload installed.
- [Bot Framework Emulator](https://github.com/Microsoft/BotFramework-Emulator/releases) for Testing Web Chat.

**To run the sample connected to Azure Bot Service, the following additional tools are required:**

- Access to an Azure Subscription with access to preform the following tasks:
    - Create and configure Entra ID Application Identities
    - Create and configure an [Create an Azure Bot](https://aka.ms/AgentsSDK-CreateBot) for your Agent.
    - A tunneling tool to allow for local development and debugging should you wish to do local development whilst connected to a external client such as Microsoft Teams.

## Getting Started with StreamingAgent1 Sample

Read more about [Running an Agent](../../../docs/HowTo/running-an-agent.md)

### QuickStart using Bot Framework Emulator

1. Open the EchoAgent Sample in Visual Studio 2022
1. Run it in Debug Mode (F5)
1. Open the [BotFramework Emulator](https://github.com/Microsoft/BotFramework-Emulator/releases)
    1. Click **Open Bot**
    1. In the bot URL field input the URL you noted down from the web page and add /api/messages to it. It should appear similar to `https://localhost:3978/api/messages`
    1. Click **Connect**

If all is working correctly, the Bot Emulator should show you a Web Chat experience with the words **"Hello and Welcome!"**

If you type a message and hit enter, or the send arrow, your messages should be returned to you with **Echo:your message**

### QuickStart using WebChat

1. [Create an Azure Bot](https://aka.ms/AgentsSDK-CreateBot)
   - Record the Application ID, the Tenant ID, and the Client Secret for use below

1. Configuring the token connection in the Agent settings
   > The instructions for this sample are for a SingleTenant Azure Bot using ClientSecrets.  The token connection configuration will vary if a different type of Azure Bot was configured.  For more information see [DotNet MSAL Authentication provider](https://aka.ms/AgentsSDK-DotNetMSALAuth)

   1. Open the `appsettings.json` file in the root of the sample project.

   1. Find the section labeled `Connections`,  it should appear similar to this:

      ```json
      "Connections": {
        "ServiceConnection": {
          "Settings": {
            "AuthType": "ClientSecret", // this is the AuthType for the connection, valid values can be found in Microsoft.Agents.Authentication.Msal.Model.AuthTypes.  The default is ClientSecret.
            "AuthorityEndpoint": "https://login.microsoftonline.com/{{TenantId}}",
            "ClientId": "{{ClientId}}", // this is the Client ID used for the connection.
            "ClientSecret": "00000000-0000-0000-0000-000000000000", // this is the Client Secret used for the connection.
            "Scopes": [
              "https://api.botframework.com/.default"
            ]
          }
        }
      },
      ```

      1. Replace all **{{ClientId}}** with the AppId of the Azure Bot.
      1. Replace all **{{TenantId}}** with the Tenant Id where your application is registered.
      1. Set the **ClientSecret** to the Secret that was created for your identity.
      
      > Storing sensitive values in appsettings is not recommend.  Follow [AspNet Configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0) for best practices.

1. Update the ChannelHost configuration in `appsettings.json`
   ```json
   "Agent": {
     "ClientId": "{{ClientId}}", // this is the Client ID for Agent1
     "Description": null,
     "Publisher": null,
     "Copyright": null,
     "Host": {
       "DefaultEndpoint": "http://localhost:3978/api/agentresponse/", // Default host serviceUrl.  This is the Url to this Agent and AgentResponseController path.
       "Agents": {
         "Echo": {
           "ConnectionSettings": {
             "ClientId": "{{Agent2ClientId}}", // This is the Client ID of Agent2
             "Endpoint": "http://localhost:39783/api/messages", // The endpoint of Agent2
             "TokenProvider": "ServiceConnection"
           }
         }
       }
     }
   },

   ```
   1. Replace **{{Agent2ClientId}}** with the AppId of Agent2.
   1. Replace **{{ClientId}}** with the AppId of Agent1.

   > Storing sensitive values in appsettings is not recommend.  Follow [AspNet Configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0) for best practices.
    
1. Run `dev tunnels`. Please follow [Create and host a dev tunnel](https://learn.microsoft.com/en-us/azure/developer/dev-tunnels/get-started?tabs=windows) and host the tunnel with anonymous user access command as shown below:

   ```bash
   devtunnel host -p 3978 --allow-anonymous
   ```
 
1. On the Azure Bot, select **Settings**, then **Configuration**, and update the **Messaging endpoint** to `{tunnel-url}/api/messages`

1. Start the Agent in Visual Studio

1. Select **Test in WebChat** on the Azure Bot

## Further reading
To learn more about building Agents, see our [Microsoft 365 Agents SDK](https://github.com/microsoft/agents) repo.