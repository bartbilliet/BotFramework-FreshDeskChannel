using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace BotFramework.FreshDeskChannel
{
    public static class PollFreshDeskTickets
    {
        [FunctionName("PollFreshDeskTickets")]
        public static void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            
        }
         
    }
}
