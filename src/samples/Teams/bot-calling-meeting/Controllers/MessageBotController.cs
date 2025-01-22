// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.BotBuilder;
using Microsoft.AspNetCore.Authorization;
using System.Threading;

namespace CallingBotSample.Controllers
{
    // ASP.Net Controller that receives incoming HTTP requests from the Azure Bot Service or other configured event activity protocol sources.
    // When called, the request has already been authorized and credentials and tokens validated.
    [Authorize]
    [ApiController]
    [Route("api/messages")]
    public class MessageBotController(IBotHttpAdapter adapter, IBot bot) : ControllerBase
    {
        [HttpPost]
        public Task PostAsync(CancellationToken cancellationToken)
            => adapter.ProcessAsync(Request, Response, bot, cancellationToken);

    }
}

