using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    class BotResponseChannelData
    {
        public string MessageType { get; set; }
        public bool Private { get; set; }
        public string[] NotifyEmails { get; set; }
    }
}
