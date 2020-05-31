using System;

namespace BotFramework.FreshDeskChannel.Models
{
    public class FreshDeskChannelData
    {
        public enum FreshDeskTicketStatus
        {
            Open = 2,
            Pending = 3,
            Resolved = 4,
            Closed = 5
        }

        public int TicketId { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }

        public long? Group_id { get; set; }
        public long? Responder_id { get; set; }
        public int Source { get; set; }
        public int? Company_id { get; set; }
        public FreshDeskTicketStatus Status { get; set; }
        public int? Product_id { get; set; }
        public DateTime Due_by { get; set; }

        public string MessageType { get; set; }
        public bool? Private { get; set; }

        public string RequesterName { get; set; }
        public string FromEmail { get; set; }

#nullable enable
        public string? Mobile { get; set; }
        public string? Phone { get; set; }

    }
}
