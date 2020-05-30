using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    class BotLastRun
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }
        public DateTime LastRun { get; set; }
    }
}
