﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Extensions.Teams.Models;
using Xunit;

namespace Microsoft.Agents.Extensions.Teams.Tests.Model
{
    public class NotificationInfoTests
    {
        [Fact]
        public void NotificationInfoInits()
        {
            var alert = true;
            var externalResourceUrl = "https://example.com";

            var notificationInfo = new NotificationInfo(alert)
            {
                AlertInMeeting = true,
                ExternalResourceUrl = externalResourceUrl,
            };

            Assert.NotNull(notificationInfo);
            Assert.IsType<NotificationInfo>(notificationInfo);
            Assert.True(notificationInfo.Alert);
            Assert.True(notificationInfo.AlertInMeeting);
            Assert.Equal(externalResourceUrl, notificationInfo.ExternalResourceUrl);
        }

        [Fact]
        public void NotificationInfoInitsWithNoArgs()
        {
            var notificationInfo = new NotificationInfo();

            Assert.NotNull(notificationInfo);
            Assert.IsType<NotificationInfo>(notificationInfo);
        }
    }
}
