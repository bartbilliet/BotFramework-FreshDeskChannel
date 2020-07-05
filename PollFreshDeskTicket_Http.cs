using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using BotFramework.FreshDeskChannel.Shared;
using System;

namespace BotFramework.FreshDeskChannel
{
    public static class PollFreshDeskTicket_Http
    {

        [FunctionName("PollFreshDeskTicket_Http")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ExecutionContext context,
            ILogger log)
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
                
                return new OkObjectResult("Ok");
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in processing Azure Function: {1}", ex);
                return new StatusCodeResult(500);
            }
 
        }

    }

}
