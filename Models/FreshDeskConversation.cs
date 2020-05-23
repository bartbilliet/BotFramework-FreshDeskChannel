using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    public class FreshDeskConversation
    {
        public string from_email { get; set; }
        public string body_text { get; set; }
        public bool incoming { get; set; }
        public bool @private { get; set; }
        public DateTime updated_at { get; set; }
    }
}
