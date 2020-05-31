using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    public class BotResponseChannelData
    {
        public enum FreshDeskTicketStatus
        {
            Open = 2,
            Pending = 3,
            Resolved = 4,
            Closed = 5
        }

        public string MessageType { get; set; }
        public string Message { get; set; }
        public bool Private { get; set; }
        public string[] NotifyEmails { get; set; }
        public FreshDeskTicketStatus Status { get; set; }
    }
}
