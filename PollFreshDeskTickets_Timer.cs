using System;
using System.Threading.Tasks;
using BotFramework.FreshDeskChannel.Shared;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BotFramework.FreshDeskChannel
{
    public static class PollFreshDeskTickets_Timer
    {
        
        [FunctionName("PollFreshDeskTickets_Timer")]
        public static async Task RunAsync([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ExecutionContext context, ILogger log)
        {

            try
            {
                //Read config values
                var config = new ConfigurationBuilder()
                    .SetBasePath(context.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true) // <- This gives you access to your application settings in your local development environment
                    .AddEnvironmentVariables() // <- This is what actually gets you the application settings in Azure
                    .Build();

                await CustomChannelLogic.ProcessChannel(config, log);
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in processing Azure Function: {1}", ex);
            }

        }
    }
}
