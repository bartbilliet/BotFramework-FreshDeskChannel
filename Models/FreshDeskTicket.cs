using System;
using System.Collections.Generic;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    public class FreshDeskTicket
    {
        public int id { get; set; }
        public string subject { get; set; }
        public string description_text { get; set; }
        public int status { get; set; }
        public DateTime created_at { get; set; }
        public FreshDeskRequester Requester { get; set; }
    }
}
