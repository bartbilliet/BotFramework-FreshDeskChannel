using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Bot.Connector.DirectLine;
using System.Linq;
using Microsoft.Azure.Cosmos;
using BotFramework.FreshDeskChannel.Models;
using System.Collections.Generic;
using System;

namespace BotFramework.FreshDeskChannel
{
    public static class PollFreshDeskTicket_Http
    {

        private static FreshDeskBotState freshDeskBotState = new FreshDeskBotState();

        [FunctionName("PollFreshDeskTicket_Http")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ExecutionContext context,
            ILogger log)
        {

            //Read config values
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true) // <- This gives you access to your application settings in your local development environment
                .AddEnvironmentVariables() // <- This is what actually gets you the application settings in Azure
                .Build();

            BotDirectLine.directLineSecret = config["DirectLineSecret"];
            BotDirectLine.botId = config["BotId"];

            CosmosDB.cosmosDBEndpointUri = config["CosmosDBEndpointUri"];
            CosmosDB.cosmosDBPrimaryKey = config["CosmosDBPrimaryKey"];
            CosmosDB.cosmosDBDatabaseId = config["CosmosDBDatabaseId"];
            CosmosDB.cosmosDBContainerId = config["CosmosDBContainerId"];

            FreshDesk.freshDeskClientUrl = config["FreshDeskClientUrl"];
            FreshDesk.freshDeskAPIKey = config["FreshDeskAPIKey"];

            // Set last run time for differentials
            DateTime LastRun = await CosmosDB.UpdateLastRun(log);   //TODO: this doesn't work well when bot crashes (batch will not be reprocessed)

            // Read all updated FreshDesk tickets
            List<FreshDeskTicket> listTickets = await FreshDesk.GetUpdatedFreshDeskTicketsAsync(LastRun);

            foreach(FreshDeskTicket ticket in listTickets)
            {
                //Start or continue conversation for ticketId
                freshDeskBotState = await CosmosDB.ReadItemAsync(ticket.id.ToString(), log);
                if (freshDeskBotState == null)
                {
                    //start bot conversation
                    Conversation conversation = await BotDirectLine.StartBotConversation();
                    log.LogInformation("Starting conversation ID: " + conversation.ConversationId);

                    //record new conversation in DB
                    freshDeskBotState = new FreshDeskBotState
                    {
                        FreshDeskId = ticket.id.ToString(),
                        BotConversationId = conversation.ConversationId,
                        BotWatermark = "0",
                        FreshDeskTicketStatus = ticket.status
                    };

                    await CosmosDB.AddItemsToContainerAsync(freshDeskBotState, log);
                }
                else
                {
                    //continue bot conversation
                    Conversation conversation = await BotDirectLine.ContinueBotConveration(freshDeskBotState.BotConversationId);
                    log.LogInformation("Continuing conversation ID: " + conversation.ConversationId);
                }

                List<UserMessage> listUserMessages = new List<UserMessage>();

                if(ticket.created_at > LastRun)
                {
                    UserMessage initialMessage = new UserMessage() 
                    {
                        FromEmail = ticket.Requester.email,
                        RequesterName = ticket.Requester.name,
                        Message = ticket.description_text
                    };
                    listUserMessages.Add(initialMessage);
                }

                //Get incoming conversations if any
                List<FreshDeskConversation> listConversations = await FreshDesk.GetFreshDeskTicketConversationsAsync(ticket.id);
                listConversations = (from c in listConversations 
                                     where c.incoming == true && c.updated_at > LastRun
                                     orderby c.updated_at ascending 
                                     select c).ToList(); //TODO: Add filter on date
                foreach(FreshDeskConversation freshDeskConversation in listConversations)
                {         
                    UserMessage conversationMessage = new UserMessage()
                    {
                        FromEmail = freshDeskConversation.from_email,
                        RequesterName = ticket.Requester.name,
                        Message = freshDeskConversation.body_text
                    };
                    listUserMessages.Add(conversationMessage);
                }

                foreach(UserMessage userMessage in listUserMessages) 
                {
                    //Send ticket updates to Bot
                    await BotDirectLine.SendMessagesAsync(freshDeskBotState.BotConversationId, userMessage.Message);
                }

                //Check for bot responses
                ActivitySet activitySet = await BotDirectLine.ReadBotMessagesAsync(freshDeskBotState.BotConversationId, freshDeskBotState.BotWatermark, log);

                foreach(Activity activity in activitySet.Activities)
                {
                    await FreshDesk.SendFreshDeskTicketReply(ticket.id.ToString(), activity.Text, log);
                }

                //record new watermark to DB
                freshDeskBotState.BotWatermark = activitySet?.Watermark;
                await CosmosDB.ReplaceItemAsync(freshDeskBotState, log);
            }           

            return new OkObjectResult("ok");
        }

    }

}
