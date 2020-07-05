using BotFramework.FreshDeskChannel.Models;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel.Shared
{
    public static class CustomChannelLogic
    {
        private static BotConversationState botConversationState;

        private static string preProcessingExtensibility;
        private static string postProcessingExtensibility;
        private static string maxDaysToWaitForBotResponses;

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

            preProcessingExtensibility = config["PreProcessingExtensibility"];
            postProcessingExtensibility = config["PostProcessingExtensibility"];

            maxDaysToWaitForBotResponses = config["MaxDaysToWaitForBotResponses"];


            // Set last run time for differentials
            DateTime lastRun = await CosmosDB.UpdateLastRun(log);   //TODO: this doesn't work well when bot crashes halfway (batch will not be reprocessed)
            log.LogInformation("* Last channel run time (GMT) was: " + lastRun);

            // Read all updated FreshDesk tickets
            log.LogInformation("* Checking for updated tickets in FreshDesk");
            List<FreshDeskTicket> listUpdatedFreshDeskTickets = await FreshDeskClient.GetUpdatedFreshDeskTicketsAsync(lastRun, log);
            log.LogInformation("  Processing " + listUpdatedFreshDeskTickets.Count + " updated tickets");

            foreach (FreshDeskTicket freshDeskTicket in listUpdatedFreshDeskTickets)
            {
                await ProcessTicket(freshDeskTicket, true, lastRun, log);
            }


            // Read conversations that had incoming messages less than x days ago, check for delayed bot responses, e.g. proactive bot messages. This allows for scenarios such as human intervention.
            log.LogInformation("* Checking for bot proactive messages or delayed responses");

            DateTime lastIncomingConversationDateAfter;
            if (Int32.Parse(maxDaysToWaitForBotResponses) > 0) {
                lastIncomingConversationDateAfter = DateTime.Now.ToUniversalTime().AddDays(-Int32.Parse(maxDaysToWaitForBotResponses));
            }
            else
            {
                //Set date to check for new conversations to epoch - this will always check all conversations for new bot messages
                lastIncomingConversationDateAfter = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            List <BotConversationState> listListeningConversationStates = await CosmosDB.ListConversationsSinceAsync(lastIncomingConversationDateAfter, log);

            log.LogInformation("  Tracking " + listListeningConversationStates.Count + " tickets for possible bot proactive messages or delayed responses");

            foreach (BotConversationState listeningConversationState in listListeningConversationStates)
            {
                // fake ticket FreshDeskTicket here to avoid calls to FreshDesk API to save hitting API limits - we only need the ticketId at this point
                FreshDeskTicket freshDeskTicket = new FreshDeskTicket();
                freshDeskTicket.Id = Convert.ToInt32(listeningConversationState.FreshDeskId);
                await ProcessTicket(freshDeskTicket, false, lastRun, log);
            }

        }

        public static async Task ProcessTicket(FreshDeskTicket freshDeskTicket, bool processInbound, DateTime lastRun, ILogger log)
        {

            log.LogInformation("\t- Processing ticket with ID #" + freshDeskTicket.Id);

            //TODO: Can we replace this separate CosmosDB container with Bot Framework conversation state? ex: context.ConversationData.SetValue("FreshDeskId", freshDeskId);

            //Start or continue conversation for ticketId
            botConversationState = await CosmosDB.ReadItemAsync(freshDeskTicket.Id.ToString(), log);
            if (botConversationState == null)
            {
                //start bot conversation
                Conversation conversation = await BotFrameworkDirectLine.StartBotConversation(log);
                log.LogInformation("\t- Starting new Bot conversation with ID: " + conversation.ConversationId);

                //record new Bot conversation in CosmosDB, keeping track of TicketId<>BotConversationId
                botConversationState = new BotConversationState
                {
                    FreshDeskId = freshDeskTicket.Id.ToString(),
                    BotConversationId = conversation.ConversationId,
                    BotWatermark = "0",
                    LastIncomingConversationDate = DateTime.Now.ToUniversalTime()
                };
                await CosmosDB.AddItemsToContainerAsync(botConversationState, log);
            }
            else
            {
                //continue bot conversation
                Conversation conversation = await BotFrameworkDirectLine.ContinueBotConveration(botConversationState.BotConversationId, log);
                log.LogInformation("\t- Continuing Bot conversation with ID: " + conversation.ConversationId);
            }


            //Read incoming messages from FreshDesk (FreshDesk -> BOT)

            // Don't call FreshDesk when not required, try to lower calls to FreshDesk API as they are metered (limits). Following will only be called for tickets that were recently updated.
            if (processInbound)     
            {
                // List new customer messages in FreshDesk
                List<FreshDeskChannelData> listCustomerMessagesToProcess = new List<FreshDeskChannelData>();

                //Original ticket description to process?
                if (freshDeskTicket.Created_at > lastRun)
                {
                    FreshDeskChannelData customerInitialMessage = new FreshDeskChannelData()
                    {
                        TicketId = freshDeskTicket.Id,
                        Subject = freshDeskTicket.Subject,
                        Message = freshDeskTicket.Description_text,
                        Group_id = freshDeskTicket.Group_id,
                        Responder_id = freshDeskTicket.Responder_id,
                        Source = freshDeskTicket.Source,
                        Company_id = freshDeskTicket.Company_id,
                        Status = (FreshDeskChannelData.FreshDeskTicketStatus)freshDeskTicket.Status,
                        Product_id = freshDeskTicket.Product_id,
                        Due_by = freshDeskTicket.Due_by,
                        MessageType = "initial_message",
                        Private = false,
                        FromEmail = freshDeskTicket.Requester.Email,
                        RequesterName = freshDeskTicket.Requester.Name,
                        Mobile = freshDeskTicket.Requester.Mobile,
                        Phone = freshDeskTicket.Requester.Phone
                    };
                    listCustomerMessagesToProcess.Add(customerInitialMessage);
                }

                //Any new incoming conversations in this ticket to process? 
                List<FreshDeskConversation> listTicketConversations = await FreshDeskClient.GetFreshDeskTicketConversationsAsync(freshDeskTicket.Id, log);
                List<FreshDeskConversation> listIncomingConversationsSinceLastRun = (from c in listTicketConversations
                                                                                     where c.Incoming == true && c.Updated_at > lastRun
                                                                                     orderby c.Updated_at ascending
                                                                                     select c).ToList();

                foreach (FreshDeskConversation incomingConversation in listIncomingConversationsSinceLastRun)
                {
                    FreshDeskChannelData customerConversationMessage = new FreshDeskChannelData()
                    {
                        TicketId = freshDeskTicket.Id,
                        Subject = freshDeskTicket.Subject,
                        Message = incomingConversation.Body_text,
                        Group_id = freshDeskTicket.Group_id,
                        Responder_id = freshDeskTicket.Responder_id,
                        Source = freshDeskTicket.Source,
                        Company_id = freshDeskTicket.Company_id,
                        Status = (FreshDeskChannelData.FreshDeskTicketStatus)freshDeskTicket.Status,
                        Product_id = freshDeskTicket.Product_id,
                        Due_by = freshDeskTicket.Due_by,
                        MessageType = "continued_conversation",
                        Private = incomingConversation.Private,
                        FromEmail = incomingConversation.From_email,
                        RequesterName = freshDeskTicket.Requester.Name,
                        Mobile = freshDeskTicket.Requester.Mobile,
                        Phone = freshDeskTicket.Requester.Phone
                    };

                    listCustomerMessagesToProcess.Add(customerConversationMessage);
                }

                log.LogDebug("\t- Retrieved " + listCustomerMessagesToProcess.Count + " incoming messages on FreshDesk Ticket #" + freshDeskTicket.Id);

                if (listCustomerMessagesToProcess.Any())
                {
                    //Log last timestamp that incoming messages from FreshDesk were processed for this conversation
                    botConversationState.LastIncomingConversationDate = DateTime.Now.ToUniversalTime();
                    await CosmosDB.ReplaceFreshDeskBotStateAsync(botConversationState, log);
                }

                // Send new customer messages to Bot Framework for processing
                foreach (FreshDeskChannelData freshDeskChannelData in listCustomerMessagesToProcess)
                {
                    // Run Pre-processing Extensibility
                    FreshDeskChannelData processedFreshDeskChannelData = freshDeskChannelData;
                    if (!String.IsNullOrEmpty(preProcessingExtensibility))
                    {
                        processedFreshDeskChannelData = await PreProcessingExtensibility(preProcessingExtensibility, freshDeskChannelData, log);
                    }

                    await BotFrameworkDirectLine.SendMessagesAsync(botConversationState.BotConversationId, processedFreshDeskChannelData, log);
                }


            }


            // Read any responses from the Bot Framework on this ticket (BOT -> FreshDesk)
            ActivitySet activitySet = await BotFrameworkDirectLine.ReadBotMessagesAsync(botConversationState.BotConversationId, botConversationState.BotWatermark, log);

            //Update the bot watermark in CosmosDB, to keep track which Bot Framework conversations we already processed
            botConversationState.BotWatermark = activitySet?.Watermark;
            await CosmosDB.ReplaceFreshDeskBotStateAsync(botConversationState, log);

            //Pass Bot Framework responses through to FreshDesk as ticket replies or ticket notes
            foreach (Activity activity in activitySet.Activities)
            {
                //If there is specific ChannelData add it to the message, otherwise default to standard ticket reply message
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
                    //Default to a standard reply message
                    botResponseChannelData = new BotResponseChannelData
                    {
                        Message = activity.Text,
                        MessageType = "reply",
                        Status = BotResponseChannelData.FreshDeskTicketStatus.Pending
                    };
                }
                
                // Run post-processing Extensibility
                BotResponseChannelData processedBotResponseChannelData = botResponseChannelData;
                if (!String.IsNullOrEmpty(postProcessingExtensibility))
                {
                    processedBotResponseChannelData = await PostProcessingExtensibility(postProcessingExtensibility, botResponseChannelData, log);
                }

                // Send the bot response to FreshDesk in the chosen messageType (current allowed values: note, reply)
                switch (processedBotResponseChannelData.MessageType)
                {
                    case "note":
                        await FreshDeskClient.SendFreshDeskNote(freshDeskTicket.Id.ToString(), processedBotResponseChannelData.Message, processedBotResponseChannelData.Private, processedBotResponseChannelData.NotifyEmails, log);
                        break;

                    case "reply":
                        await FreshDeskClient.SendFreshDeskTicketReply(freshDeskTicket.Id.ToString(), processedBotResponseChannelData.Message, log);
                        await FreshDeskClient.SetTicketStatus(freshDeskTicket.Id.ToString(), processedBotResponseChannelData.Status, log);

                        break;
                }

            }
        }


        public static async Task<FreshDeskChannelData> PreProcessingExtensibility(string preProcessingExtensibility, FreshDeskChannelData freshDeskChannelData, ILogger log)
        {
            try
            {
                log.LogInformation("\t- Sending Freshdesk message to pre-processing extensibility component");

                HttpClient client = new HttpClient();

                string stringData = JsonSerializer.Serialize(freshDeskChannelData);
                var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(preProcessingExtensibility, contentData);

                if (response.IsSuccessStatusCode)
                {
                    string responseData = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    return JsonSerializer.Deserialize<FreshDeskChannelData>(responseData, options);
                }
                else
                {
                    log.LogError("The configured processing extensibility returned an error");
                    return null;
                }
            }
            catch (Exception ex)
            {
                log.LogError("\tException occurred in PreProcessingExtensibility: {1}", ex);
                throw;
            }
        }

        public static async Task<BotResponseChannelData> PostProcessingExtensibility(string postProcessingExtensibility, BotResponseChannelData botResponseChannelData, ILogger log)
        {
            try
            {
                log.LogInformation("\t- Sending Bot response to post-processing extensibility component");

                HttpClient client = new HttpClient();

                string stringData = JsonSerializer.Serialize(botResponseChannelData);
                var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(postProcessingExtensibility, contentData);

                if (response.IsSuccessStatusCode)
                {
                    string responseData = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    return JsonSerializer.Deserialize<BotResponseChannelData>(responseData, options);
                }
                else
                {
                    log.LogError("The configured processing extensibility returned an error");
                    return null;
                }
            }
            catch (Exception ex)
            {
                log.LogError("\tException occurred in PreProcessingExtensibility: {1}", ex);
                throw;
            }
        }
    }
}
