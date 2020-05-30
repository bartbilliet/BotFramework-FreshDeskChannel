using BotFramework.FreshDeskChannel.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel
{
    class CosmosDB
    {
        public static string cosmosDBEndpointUri;
        public static string cosmosDBPrimaryKey;
        public static string cosmosDBDatabaseId;
        public static string cosmosDBContainerId;

        private static CosmosClient cosmosClient;
        private static Database database;
        private static Microsoft.Azure.Cosmos.Container botStateContainer;
        private static Microsoft.Azure.Cosmos.Container lastRunContainer;

        private static async Task EnsureCosmosDBAsync(ILogger log)
        {
            try
            {
                //Create a new instance of the Cosmos Client
                cosmosClient = new CosmosClient(cosmosDBEndpointUri, cosmosDBPrimaryKey);

                //create database and container if not existing
                database = await cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDBDatabaseId);
                botStateContainer = await database.CreateContainerIfNotExistsAsync(cosmosDBContainerId, "/id");
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in EnsureCosmosDBAsync: {1}", ex);
                throw;
            }
        }

        public static async Task AddItemsToContainerAsync(BotConversationState botConversationState, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                // Read the item to see if it exists.  
                ItemResponse<BotConversationState> botConversationStateResponse = await botStateContainer.ReadItemAsync<BotConversationState>(botConversationState.FreshDeskId, new PartitionKey(botConversationState.FreshDeskId));
                log.LogInformation("Conversation in database corresponding to FreshDeskId: {0} already exists\n", botConversationStateResponse.Resource.BotConversationId);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    // Create an item in the container
                    ItemResponse<BotConversationState> botConversationStateResponse = await botStateContainer.CreateItemAsync(botConversationState, new PartitionKey(botConversationState.FreshDeskId)); 
                    log.LogInformation("Created item in database with ConversationId: {0} \n", botConversationStateResponse.Resource.BotConversationId);
                }
                catch (Exception ex2)
                {
                    log.LogError("Exception occurred in AddItemsToContainerAsync: {1}", ex2);
                    throw;
                }
            }

        }

        public static async Task<BotConversationState> ReadItemAsync(string ticketId, ILogger log)
        {
            //Connect to DB
            await EnsureCosmosDBAsync(log);

            ItemResponse<BotConversationState> botConversationStateResponse;
            try
            {
                // Read the item to see if it exists.  
                botConversationStateResponse = await botStateContainer.ReadItemAsync<BotConversationState>(ticketId, new PartitionKey(ticketId));
                log.LogInformation("Conversation in database corresponding to FreshDeskId: {0} found\n", botConversationStateResponse.Resource.BotConversationId);

                return botConversationStateResponse;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                log.LogInformation("Item does not exist");
                return null;
            }
            catch (Exception ex2)
            {
                log.LogError("Exception occurred in ReadItemAsync: {1}", ex2);
                throw;
            }

        }

        public static async Task ReplaceFreshDeskBotStateAsync(BotConversationState botConversationState, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                ItemResponse<BotConversationState> botConversationStateResponse = await botStateContainer.ReadItemAsync<BotConversationState>(botConversationState.FreshDeskId, new PartitionKey(botConversationState.FreshDeskId));
                var itemBody = botConversationStateResponse.Resource;

                // update bot watermark
                itemBody.BotWatermark = botConversationState.BotWatermark;

                // replace the item with the updated content
                botConversationStateResponse = await botStateContainer.ReplaceItemAsync<BotConversationState>(itemBody, botConversationState.FreshDeskId, new PartitionKey(botConversationState.FreshDeskId));
                log.LogInformation("Updated watermark to {0} for FreshDesk ID {1} with conversation ID {2}\n", itemBody.BotWatermark, itemBody.FreshDeskId, itemBody.BotConversationId);
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in ReplaceFreshDeskBotStateAsync: {1}", ex);
                throw;
            }
        }

        public static async Task<DateTime> UpdateLastRun(ILogger log)
        {
            // Set variable to keep lastRun time
            DateTime lastRun;

            // Bootstrap object to update DB with new date
            BotLastRun freshDeskBotLastRun = new BotLastRun()
            {
                Id = "0"
            };

            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                // Create LastRun container in DB if not existing
                lastRunContainer = await database.CreateContainerIfNotExistsAsync("LastRun", "/id");

                // Read the item to see if it exists.  
                ItemResponse<BotLastRun> freshDeskBotLastRunResponse;
                freshDeskBotLastRunResponse = await lastRunContainer.ReadItemAsync<BotLastRun>(freshDeskBotLastRun.Id, new PartitionKey(freshDeskBotLastRun.Id));
                log.LogInformation("Last run time (GMT) was: {0}\n", freshDeskBotLastRunResponse.Resource.LastRun);

                // Keep the last run time as return parameter of the function
                lastRun = freshDeskBotLastRunResponse.Resource.LastRun;

                // Update the lastrun time in DB
                freshDeskBotLastRun.LastRun = DateTime.Now.ToUniversalTime();
                freshDeskBotLastRunResponse = await lastRunContainer.ReplaceItemAsync<BotLastRun>(freshDeskBotLastRun, freshDeskBotLastRun.Id, new PartitionKey(freshDeskBotLastRun.Id));

                return lastRun;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    lastRun = DateTime.Now;

                    // Set current time as initial value in DB
                    log.LogInformation("Setting initial last run time to: {0}", freshDeskBotLastRun.LastRun);
                    freshDeskBotLastRun.LastRun = DateTime.Now.ToUniversalTime();
                    ItemResponse<BotLastRun> botConversationStateResponse = await lastRunContainer.CreateItemAsync(freshDeskBotLastRun, new PartitionKey(freshDeskBotLastRun.Id));

                    return lastRun;
                }
                catch (Exception ex2)
                {
                    log.LogError("Exception occurred in ReplaceFreshDeskBotStateAsync: {1}", ex2);
                    throw;
                }
            }
        }

        public static async Task<BotConversationState> QueryItemsAsync(string ticketId, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                var sqlQueryText = "SELECT * FROM c WHERE c.id = '" + ticketId + "'";   //TODO: add parameter
                QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
                FeedIterator<BotConversationState> queryResultSetIterator = botStateContainer.GetItemQueryIterator<BotConversationState>(queryDefinition);

                List<BotConversationState> botConversationStates = new List<BotConversationState>();

                while (queryResultSetIterator.HasMoreResults)
                {
                    FeedResponse<BotConversationState> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                    foreach (BotConversationState botConversationState in currentResultSet)
                    {
                        botConversationStates.Add(botConversationState);
                        log.LogInformation("\tRead conversation ID {0} with watermark {1}\n", botConversationState.BotConversationId, botConversationState.BotWatermark);
                    }
                }

                return botConversationStates[0];
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in QueryItemsAsync: {1}", ex);
                throw;
            }
        }
    }
}