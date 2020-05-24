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

        public static async Task AddItemsToContainerAsync(FreshDeskBotState freshDeskBotState, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                // Read the item to see if it exists.  
                ItemResponse<FreshDeskBotState> freshDeskBotStateResponse = await botStateContainer.ReadItemAsync<FreshDeskBotState>(freshDeskBotState.FreshDeskId, new PartitionKey(freshDeskBotState.FreshDeskId));
                log.LogInformation("Conversation in database corresponding to FreshDeskId: {0} already exists\n", freshDeskBotStateResponse.Resource.BotConversationId);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                try
                {
                    // Create an item in the container
                    ItemResponse<FreshDeskBotState> freshDeskBotStateResponse = await botStateContainer.CreateItemAsync(freshDeskBotState, new PartitionKey(freshDeskBotState.FreshDeskId)); 
                    log.LogInformation("Created item in database with ConversationId: {0} \n", freshDeskBotStateResponse.Resource.BotConversationId);
                }
                catch (Exception ex2)
                {
                    log.LogError("Exception occurred in AddItemsToContainerAsync: {1}", ex2);
                    throw;
                }
            }

        }

        public static async Task<FreshDeskBotState> ReadItemAsync(string ticketId, ILogger log)
        {
            //Connect to DB
            await EnsureCosmosDBAsync(log);

            ItemResponse<FreshDeskBotState> freshDeskBotStateResponse;
            try
            {
                // Read the item to see if it exists.  
                freshDeskBotStateResponse = await botStateContainer.ReadItemAsync<FreshDeskBotState>(ticketId, new PartitionKey(ticketId));
                log.LogInformation("Conversation in database corresponding to FreshDeskId: {0} found\n", freshDeskBotStateResponse.Resource.BotConversationId);

                return freshDeskBotStateResponse;
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

        public static async Task ReplaceFreshDeskBotStateAsync(FreshDeskBotState freshDeskBotState, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                ItemResponse<FreshDeskBotState> freshDeskBotStateResponse = await botStateContainer.ReadItemAsync<FreshDeskBotState>(freshDeskBotState.FreshDeskId, new PartitionKey(freshDeskBotState.FreshDeskId));
                var itemBody = freshDeskBotStateResponse.Resource;

                // update bot watermark
                itemBody.BotWatermark = freshDeskBotState.BotWatermark;

                // replace the item with the updated content
                freshDeskBotStateResponse = await botStateContainer.ReplaceItemAsync<FreshDeskBotState>(itemBody, freshDeskBotState.FreshDeskId, new PartitionKey(freshDeskBotState.FreshDeskId));
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
            FreshDeskBotLastRun freshDeskBotLastRun = new FreshDeskBotLastRun()
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
                ItemResponse<FreshDeskBotLastRun> freshDeskBotLastRunResponse;
                freshDeskBotLastRunResponse = await lastRunContainer.ReadItemAsync<FreshDeskBotLastRun>(freshDeskBotLastRun.Id, new PartitionKey(freshDeskBotLastRun.Id));
                log.LogInformation("Last run time (GMT) was: {0}\n", freshDeskBotLastRunResponse.Resource.LastRun);

                // Keep the last run time as return parameter of the function
                lastRun = freshDeskBotLastRunResponse.Resource.LastRun;

                // Update the lastrun time in DB
                freshDeskBotLastRun.LastRun = DateTime.Now.ToUniversalTime();
                freshDeskBotLastRunResponse = await lastRunContainer.ReplaceItemAsync<FreshDeskBotLastRun>(freshDeskBotLastRun, freshDeskBotLastRun.Id, new PartitionKey(freshDeskBotLastRun.Id));

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
                    ItemResponse<FreshDeskBotLastRun> freshDeskBotStateResponse = await lastRunContainer.CreateItemAsync(freshDeskBotLastRun, new PartitionKey(freshDeskBotLastRun.Id));

                    return lastRun;
                }
                catch (Exception ex2)
                {
                    log.LogError("Exception occurred in ReplaceFreshDeskBotStateAsync: {1}", ex2);
                    throw;
                }
            }
        }

        public static async Task<FreshDeskBotState> QueryItemsAsync(string ticketId, ILogger log)
        {
            try
            {
                //Connect to DB
                await EnsureCosmosDBAsync(log);

                var sqlQueryText = "SELECT * FROM c WHERE c.id = '" + ticketId + "'";   //TODO: add parameter
                QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
                FeedIterator<FreshDeskBotState> queryResultSetIterator = botStateContainer.GetItemQueryIterator<FreshDeskBotState>(queryDefinition);

                List<FreshDeskBotState> freshDeskBotStates = new List<FreshDeskBotState>();

                while (queryResultSetIterator.HasMoreResults)
                {
                    FeedResponse<FreshDeskBotState> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                    foreach (FreshDeskBotState freshDeskBotState in currentResultSet)
                    {
                        freshDeskBotStates.Add(freshDeskBotState);
                        log.LogInformation("\tRead conversation ID {0} with watermark {1}\n", freshDeskBotState.BotConversationId, freshDeskBotState.BotWatermark);
                    }
                }

                return freshDeskBotStates[0];
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in QueryItemsAsync: {1}", ex);
                throw;
            }
        }
    }
}