using System;
using System.Collections.Generic;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    public class FreshDeskTicket
    {
        public enum FreshDeskTicketStatus
        {
            Open = 2,
            Pending = 3,
            Resolved = 4,
            Closed = 5
        }

        public int Id { get; set; }
        public string Subject { get; set; }
        public string Description_text { get; set; }
        public long? Group_id { get; set; }
        public long? Responder_id { get; set; }
        public int Source { get; set; }
        public int? Company_id { get; set; }
        public FreshDeskTicketStatus Status { get; set; }
        public int? Product_id { get; set; }
        public DateTime Due_by { get; set; }
        public DateTime Created_at { get; set; }
        public FreshDeskRequester Requester { get; set; }
    }
}
