using BotFramework.FreshDeskChannel.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
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
        private static Container botStateContainer;
        private static Container lastRunContainer;

        static CosmosDB()
        {
            cosmosDBEndpointUri = Environment.GetEnvironmentVariable("CosmosDBEndpointUri");
            cosmosDBPrimaryKey = Environment.GetEnvironmentVariable("CosmosDBPrimaryKey");
            cosmosDBDatabaseId = Environment.GetEnvironmentVariable("CosmosDBDatabaseId");
            cosmosDBContainerId = Environment.GetEnvironmentVariable("CosmosDBContainerId");
        }

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
                log.LogError("\t  Exception occurred in EnsureCosmosDBAsync: {1}", ex);
                throw;
            }
        }

        public static async Task AddItemsToContainerAsync(BotConversationState botConversationState, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                // Read the item to see if it already exists before creating
                ItemResponse<BotConversationState> botConversationStateResponse = await botStateContainer.ReadItemAsync<BotConversationState>(botConversationState.FreshDeskId, new PartitionKey(botConversationState.FreshDeskId));
                log.LogDebug("Conversation in database corresponding to FreshDeskId: {0} already exists", botConversationStateResponse.Resource.BotConversationId);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    // Create an item in the container
                    ItemResponse<BotConversationState> botConversationStateResponse = await botStateContainer.CreateItemAsync(botConversationState, new PartitionKey(botConversationState.FreshDeskId)); 
                    log.LogDebug("\t  Created item in database with Conversation ID: {0} ", botConversationStateResponse.Resource.BotConversationId);
                }
                catch (Exception ex2)
                {
                    log.LogError("\t  Exception occurred in AddItemsToContainerAsync: {1}", ex2);
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
                log.LogDebug("\t  Conversation {0} found, corresponding to FreshDeskId: {1}", botConversationStateResponse.Resource.BotConversationId, ticketId);

                return botConversationStateResponse;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                log.LogDebug("\t  Cannot find an existing conversationstate for ticket ID #" + ticketId);
                return null;
            }
            catch (Exception ex2)
            {
                log.LogError("\t  Exception occurred in ReadItemAsync: {1}", ex2);
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
                log.LogDebug("\t  Updated watermark to {0} for FreshDesk ID {1} with conversation ID {2}", itemBody.BotWatermark, itemBody.FreshDeskId, itemBody.BotConversationId);
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in ReplaceFreshDeskBotStateAsync: {1}", ex);
                throw;
            }
        }

        public static async Task<BotConversationState> QueryItemsAsync(string ticketId, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                var sqlQueryText = "SELECT * FROM c WHERE c.id = '" + ticketId + "'";   //TODO: add variable in SQL parameter?
                QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
                FeedIterator<BotConversationState> queryResultSetIterator = botStateContainer.GetItemQueryIterator<BotConversationState>(queryDefinition);

                List<BotConversationState> botConversationStates = new List<BotConversationState>();

                while (queryResultSetIterator.HasMoreResults)
                {
                    FeedResponse<BotConversationState> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                    foreach (BotConversationState botConversationState in currentResultSet)
                    {
                        botConversationStates.Add(botConversationState);
                        log.LogDebug("\t  Read conversation ID {0} with watermark {1}", botConversationState.BotConversationId, botConversationState.BotWatermark);
                    }
                }

                return botConversationStates[0];
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in QueryItemsAsync: {1}", ex);
                throw;
            }
        }

        public static async Task<List<BotConversationState>> ListConversationsSinceAsync(DateTime lastIncomingConversationDateAfter, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                //TODO: Final query should be: all tickets UNRESOLVED since <configurable> days
                var sqlQueryText = "SELECT * FROM c WHERE c.LastIncomingConversationDate > '" + JsonConvert.SerializeObject(lastIncomingConversationDateAfter.ToUniversalTime()) + "'";
                QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
                FeedIterator<BotConversationState> queryResultSetIterator = botStateContainer.GetItemQueryIterator<BotConversationState>(queryDefinition);

                List<BotConversationState> botConversationStates = new List<BotConversationState>();

                while (queryResultSetIterator.HasMoreResults)
                {
                    FeedResponse<BotConversationState> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                    foreach (BotConversationState botConversationState in currentResultSet)
                    {
                        botConversationStates.Add(botConversationState);
                    }
                }

                return botConversationStates;
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in QueryItemsAsync: {1}", ex);
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
                Id = "0",
                LastRun = DateTime.Now.ToUniversalTime()
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

                // Keep the previous last run time as return parameter of the function
                lastRun = freshDeskBotLastRunResponse.Resource.LastRun;
                log.LogDebug("\t  Previous last run time (GMT) was: {0}", lastRun);

                // Update the lastrun time in DB
                freshDeskBotLastRunResponse = await lastRunContainer.ReplaceItemAsync<BotLastRun>(freshDeskBotLastRun, freshDeskBotLastRun.Id, new PartitionKey(freshDeskBotLastRun.Id));
                log.LogDebug("\t  Updating new last run time (GMT) to: {0}", freshDeskBotLastRun.LastRun);

                return lastRun;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    lastRun = DateTime.Now;

                    // Set current time as initial value in DB
                    log.LogDebug("\t  Setting initial last channel run time to: {0}", freshDeskBotLastRun.LastRun);
                    freshDeskBotLastRun.LastRun = DateTime.Now.ToUniversalTime();
                    ItemResponse<BotLastRun> botConversationStateResponse = await lastRunContainer.CreateItemAsync(freshDeskBotLastRun, new PartitionKey(freshDeskBotLastRun.Id));

                    return lastRun;
                }
                catch (Exception ex2)
                {
                    log.LogError("\t  Exception occurred in ReplaceFreshDeskBotStateAsync: {1}", ex2);
                    throw;
                }
            }
        }
    }
}