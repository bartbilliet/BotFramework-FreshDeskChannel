using BotFramework.FreshDeskChannel.Models;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel.Shared
{
    public static class CustomChannelLogic
    {
        private static BotConversationState botConversationState;

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
                botConversationState = await CosmosDB.ReadItemAsync(FreshDeskTicket.Id.ToString(), log);
                if (botConversationState == null)
                {
                    //start bot conversation
                    Conversation conversation = await BotFrameworkDirectLine.StartBotConversation(log);
                    log.LogInformation("Starting conversation ID: " + conversation.ConversationId);

                    //record new Bot conversation in CosmosDB, keeping track of TicketId<>BotConversationId
                    botConversationState = new BotConversationState
                    {
                        FreshDeskId = FreshDeskTicket.Id.ToString(),
                        BotConversationId = conversation.ConversationId,
                        BotWatermark = "0",
                        Status = (BotConversationState.FreshDeskTicketStatus)FreshDeskTicket.Status
                    };
                    await CosmosDB.AddItemsToContainerAsync(botConversationState, log);
                }
                else
                {
                    //continue bot conversation
                    Conversation conversation = await BotFrameworkDirectLine.ContinueBotConveration(botConversationState.BotConversationId, log);
                    log.LogInformation("Continuing conversation ID: " + conversation.ConversationId);
                }


                // List new customer messages in FreshDesk
                List<FreshDeskChannelData> listCustomerMessagesToProcess = new List<FreshDeskChannelData>();

                //Original ticket description to process?
                if (FreshDeskTicket.Created_at > LastRun)
                {
                    FreshDeskChannelData customerInitialMessage = new FreshDeskChannelData()
                    {
                        TicketId = FreshDeskTicket.Id,
                        Subject = FreshDeskTicket.Subject,
                        Message = FreshDeskTicket.Description_text,
                        Group_id = FreshDeskTicket.Group_id,
                        Responder_id = FreshDeskTicket.Responder_id,
                        Source = FreshDeskTicket.Source,
                        Company_id = FreshDeskTicket.Company_id,
                        Status = (FreshDeskChannelData.FreshDeskTicketStatus) FreshDeskTicket.Status,
                        Product_id = FreshDeskTicket.Product_id,
                        Due_by = FreshDeskTicket.Due_by,
                        MessageType = "initial_message",
                        Private = false,
                        FromEmail = FreshDeskTicket.Requester.Email,
                        RequesterName = FreshDeskTicket.Requester.Name,
                        Mobile = FreshDeskTicket.Requester.Mobile,
                        Phone = FreshDeskTicket.Requester.Phone
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
                    FreshDeskChannelData customerConversationMessage = new FreshDeskChannelData()
                    {
                        TicketId = FreshDeskTicket.Id,
                        Subject = FreshDeskTicket.Subject,
                        Message = incomingConversation.Body_text,
                        Group_id = FreshDeskTicket.Group_id,
                        Responder_id = FreshDeskTicket.Responder_id,
                        Source = FreshDeskTicket.Source,
                        Company_id = FreshDeskTicket.Company_id,
                        Status = (FreshDeskChannelData.FreshDeskTicketStatus)FreshDeskTicket.Status,
                        Product_id = FreshDeskTicket.Product_id,
                        Due_by = FreshDeskTicket.Due_by,
                        MessageType = "continued_conversation",
                        Private = incomingConversation.Private,
                        FromEmail = incomingConversation.From_email,
                        RequesterName = FreshDeskTicket.Requester.Name,
                        Mobile = FreshDeskTicket.Requester.Mobile,
                        Phone = FreshDeskTicket.Requester.Phone
                    };

                    listCustomerMessagesToProcess.Add(customerConversationMessage);
                }


                // Send new customer messages to Bot Framework for processing
                foreach (FreshDeskChannelData freshDeskChannelData in listCustomerMessagesToProcess)
                {
                    await BotFrameworkDirectLine.SendMessagesAsync(botConversationState.BotConversationId, freshDeskChannelData, log);
                }


                // Read any new Bot Framework responses on this ticket
                ActivitySet activitySet = await BotFrameworkDirectLine.ReadBotMessagesAsync(botConversationState.BotConversationId, botConversationState.BotWatermark, log);

                //Update the bot watermark in CosmosDB, to keep track which Bot Framework conversations we have already read
                botConversationState.BotWatermark = activitySet?.Watermark;
                await CosmosDB.ReplaceFreshDeskBotStateAsync(botConversationState, log);

                //Send Bot Framework responses back to FreshDesk as ticket responses to the customer
                foreach (Activity activity in activitySet.Activities)
                {
                    //If there is specific ChannelData add it to the message, otherwise default to standard customer reply message
                    BotResponseChannelData botResponseChannelData;
                    if (activity.ChannelData != null)
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        botResponseChannelData = JsonSerializer.Deserialize<BotResponseChannelData>(activity.ChannelData.ToString(), options);
                    }
                    else
                    {
                        // Default to a standard reply message
                        botResponseChannelData = new BotResponseChannelData
                        {
                            MessageType = "reply"
                        };
                    }

                    // Send the bot response to FreshDesk in the chosen messageType (current allowed values: note, reply)
                    switch(botResponseChannelData.MessageType)
                    {
                        case "note":
                            await FreshDeskClient.SendFreshDeskNote(FreshDeskTicket.Id.ToString(), activity.Text, botResponseChannelData.Private, botResponseChannelData.NotifyEmails, log);
                            break;

                        case "reply":
                            await FreshDeskClient.SendFreshDeskTicketReply(FreshDeskTicket.Id.ToString(), activity.Text, log);
                            break;
                    }

                }

            }
        }
    }
}
