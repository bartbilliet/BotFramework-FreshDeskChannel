using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    public class CustomerMessage
    {
        public string RequesterName { get; set; }
        public string FromEmail { get; set; }
        public string Message { get; set; }
    }
}
