using BotFramework.FreshDeskChannel.Models;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel.Shared
{
    public static class CustomChannelLogic
    {
        private static FreshDeskBotState freshDeskBotState;

        public static async Task ProcessChannel(IConfigurationRoot config, ILogger log)
        {

            //Read config values
            BotFrameworkDirectLine.directLineSecret = config["DirectLineSecret"];
            BotFrameworkDirectLine.botId = config["BotId"];

            CosmosDB.cosmosDBEndpointUri = config["CosmosDBEndpointUri"];
            CosmosDB.cosmosDBPrimaryKey = config["CosmosDBPrimaryKey"];
            CosmosDB.cosmosDBDatabaseId = config["CosmosDBDatabaseId"];
            CosmosDB.cosmosDBContainerId = config["CosmosDBContainerId"];

            FreshDeskClient.freshDeskClientUrl = config["FreshDeskClientUrl"];
            FreshDeskClient.freshDeskAPIKey = config["FreshDeskAPIKey"];

            
            // Set last run time for differentials
            DateTime LastRun = await CosmosDB.UpdateLastRun(log);   //TODO: this doesn't work well when bot crashes halfway (batch will not be reprocessed)


            // Read all updated FreshDesk tickets
            List<FreshDeskTicket> listUpdatedFreshDeskTickets = await FreshDeskClient.GetUpdatedFreshDeskTicketsAsync(LastRun, log);

            foreach (FreshDeskTicket FreshDeskTicket in listUpdatedFreshDeskTickets)
            {
                //Start or continue conversation for ticketId
                freshDeskBotState = await CosmosDB.ReadItemAsync(FreshDeskTicket.Id.ToString(), log);
                if (freshDeskBotState == null)
                {
                    //start bot conversation
                    Conversation conversation = await BotFrameworkDirectLine.StartBotConversation(log);
                    log.LogInformation("Starting conversation ID: " + conversation.ConversationId);

                    //record new Bot conversation in CosmosDB, keeping track of TicketId<>BotConversationId
                    freshDeskBotState = new FreshDeskBotState
                    {
                        FreshDeskId = FreshDeskTicket.Id.ToString(),
                        BotConversationId = conversation.ConversationId,
                        BotWatermark = "0",
                        Status = (FreshDeskBotState.FreshDeskTicketStatus)FreshDeskTicket.Status
                    };
                    await CosmosDB.AddItemsToContainerAsync(freshDeskBotState, log);
                }
                else
                {
                    //continue bot conversation
                    Conversation conversation = await BotFrameworkDirectLine.ContinueBotConveration(freshDeskBotState.BotConversationId, log);
                    log.LogInformation("Continuing conversation ID: " + conversation.ConversationId);
                }


                // List new customer messages in FreshDesk
                List<CustomerMessage> listCustomerMessagesToProcess = new List<CustomerMessage>();

                //Original ticket description to process?
                if (FreshDeskTicket.Created_at > LastRun)
                {
                    CustomerMessage customerInitialMessage = new CustomerMessage()
                    {
                        FromEmail = FreshDeskTicket.Requester.Email,
                        RequesterName = FreshDeskTicket.Requester.Name,
                        Message = FreshDeskTicket.Description_text
                    };
                    listCustomerMessagesToProcess.Add(customerInitialMessage);
                }

                //Any new incoming conversations in this ticket to process? 
                //TODO: add paging to this (>30 conversations)
                List<FreshDeskConversation> listTicketConversations = await FreshDeskClient.GetFreshDeskTicketConversationsAsync(FreshDeskTicket.Id, log);
                List<FreshDeskConversation> listIncomingConversationsSinceLastRun = (from c in listTicketConversations
                                                                                         where c.Incoming == true && c.Updated_at > LastRun
                                                                                         orderby c.Updated_at ascending
                                                                                         select c).ToList();
                foreach (FreshDeskConversation incomingConversation in listIncomingConversationsSinceLastRun)
                {
                    CustomerMessage customerConversationMessage = new CustomerMessage()
                    {
                        FromEmail = incomingConversation.From_email,
                        Message = incomingConversation.Body_text,
                        RequesterName = FreshDeskTicket.Requester.Name                        
                    };
                    listCustomerMessagesToProcess.Add(customerConversationMessage);
                }


                // Send new customer messages to Bot Framework for processing
                foreach (CustomerMessage customerMessage in listCustomerMessagesToProcess)
                {
                    //Send ticket updates to Bot
                    log.LogInformation("Send user message to bot: " + customerMessage.Message);
                    await BotFrameworkDirectLine.SendMessagesAsync(freshDeskBotState.BotConversationId, customerMessage.Message, log);
                }


                // Read any new Bot Framework responses on this ticket
                ActivitySet activitySet = await BotFrameworkDirectLine.ReadBotMessagesAsync(freshDeskBotState.BotConversationId, freshDeskBotState.BotWatermark, log);

                //Send Bot Frameowrk responses back to FreshDesk as ticket responses to the customer
                foreach (Activity activity in activitySet.Activities)
                {
                    await FreshDeskClient.SendFreshDeskTicketReply(FreshDeskTicket.Id.ToString(), activity.Text, log);
                }

                //Update the bot watermark in CosmosDB, to keep track which Bot Framework conversations we already processed
                freshDeskBotState.BotWatermark = activitySet?.Watermark;
                await CosmosDB.ReplaceFreshDeskBotStateAsync(freshDeskBotState, log);
            }
        }
    }
}
