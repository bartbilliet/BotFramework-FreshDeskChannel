using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    public class FreshDeskConversation
    {
        public string From_email { get; set; }
        public string Body_text { get; set; }
        public bool Incoming { get; set; }
        public bool Private { get; set; }
        public DateTime Updated_at { get; set; }
    }
}
