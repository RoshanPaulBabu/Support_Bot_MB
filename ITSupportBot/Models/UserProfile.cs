// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Bot.Schema;
using System.Collections.Generic;

namespace ITSupportBot.Models
{
    /// <summary>
    /// This is our application state. Just a regular serializable .NET class.
    /// </summary>
    public class UserProfile
    {

        public string Name { get; set; }

        public long Number { get; set; }

        public string Id { get; set; }
        public string ConversationId { get; set; }
        public string UserType { get; set; }
        public List<ChatTransaction> ChatHistory { get; set; } = new List<ChatTransaction>();

    }
}
