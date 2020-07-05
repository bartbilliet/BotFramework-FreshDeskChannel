using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;

namespace BotFramework.FreshDeskChannel
{
    class BotConversationState
    {
        [JsonProperty(PropertyName = "id")]
        public string FreshDeskId { get; set; }
        public string BotConversationId { get; set; }
        public string BotWatermark { get; set; }
        public DateTime LastIncomingConversationDate { get; set; }

    }
}
