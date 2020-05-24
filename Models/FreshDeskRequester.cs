using System;
using System.Collections.Generic;
using System.Text;

namespace BotFramework.FreshDeskChannel.Models
{
    public class FreshDeskRequester
    {
        public string Name { get; set; }
        public string Email { get; set; }

#nullable enable
        public string? Mobile { get; set; }
        public string? Phone { get; set; }
    }
}
