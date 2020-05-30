using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;

namespace BotFramework.FreshDeskChannel
{
    class BotConversationState
    {
        public enum FreshDeskTicketStatus
        {
            Open = 2,
            Pending = 3,
            Resolved = 4,
            Closed = 5
        }

        [JsonProperty(PropertyName = "id")]
        public string FreshDeskId { get; set; }
        public string BotConversationId { get; set; }
        public string BotWatermark { get; set; }
        public FreshDeskTicketStatus Status { get; set; }

    }
}
