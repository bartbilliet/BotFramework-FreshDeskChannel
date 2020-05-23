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
            // Create a new instance of the Cosmos Client
            cosmosClient = new CosmosClient(cosmosDBEndpointUri, cosmosDBPrimaryKey);

            //create database and container if not existing
            database = await cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDBDatabaseId);
            botStateContainer = await database.CreateContainerIfNotExistsAsync(cosmosDBContainerId, "/id");
        }

        public static async Task AddItemsToContainerAsync(FreshDeskBotState freshDeskBotState, ILogger log)
        {
            await EnsureCosmosDBAsync(log);

            try
            {
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

                    // Note that after creating the item, we can access the body of the item with the Resource property off the ItemResponse. 
                    log.LogInformation("Created item in database with ConversationId: {0} \n", freshDeskBotStateResponse.Resource.BotConversationId);
                }
                catch (Exception ex2)
                {
                    log.LogInformation(ex2.Message);
                }
            }

        }

        public static async Task<FreshDeskBotState> ReadItemAsync(string ticketId, ILogger log)
        {
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

        }

        public static async Task<FreshDeskBotState> QueryItemsAsync(string ticketId, ILogger log)
        {
            await EnsureCosmosDBAsync(log);

            var sqlQueryText = "SELECT * FROM c WHERE c.id = '" + ticketId + "'";

            log.LogInformation("Running query: {0}\n", sqlQueryText);

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

        public static async Task ReplaceItemAsync(FreshDeskBotState freshDeskBotState, ILogger log)
        {
            await EnsureCosmosDBAsync(log);

            ItemResponse<FreshDeskBotState> freshDeskBotStateResponse = await botStateContainer.ReadItemAsync<FreshDeskBotState>(freshDeskBotState.FreshDeskId, new PartitionKey(freshDeskBotState.FreshDeskId));
            var itemBody = freshDeskBotStateResponse.Resource;

            // update bot watermark
            itemBody.BotWatermark = freshDeskBotState.BotWatermark;

            // replace the item with the updated content
            freshDeskBotStateResponse = await botStateContainer.ReplaceItemAsync<FreshDeskBotState>(itemBody, freshDeskBotState.FreshDeskId, new PartitionKey(freshDeskBotState.FreshDeskId));
            Console.WriteLine("Updated watermark to {0} for FreshDesk ID {1} with conversation ID {2}\n", itemBody.BotWatermark, itemBody.FreshDeskId, itemBody.BotConversationId);
        }

        public static async Task<DateTime> UpdateLastRun(ILogger log)
        {
            await EnsureCosmosDBAsync(log);

            // Create LastRun container in DB if not existing
            lastRunContainer = await database.CreateContainerIfNotExistsAsync("LastRun", "/id");
            DateTime lastRun;

            // Bootstrap object to update DB with new date
            FreshDeskBotLastRun freshDeskBotLastRun = new FreshDeskBotLastRun()
            {
                Id = "0"
            };

            ItemResponse<FreshDeskBotLastRun> freshDeskBotLastRunResponse;
            try
            {
                // Read the item to see if it exists.  
                freshDeskBotLastRunResponse = await lastRunContainer.ReadItemAsync<FreshDeskBotLastRun>(freshDeskBotLastRun.Id, new PartitionKey(freshDeskBotLastRun.Id));
                log.LogInformation("Last run time: {0}\n", freshDeskBotLastRunResponse.Resource.LastRun);

                // Keep the last run time as return parameter of the function
                lastRun = freshDeskBotLastRunResponse.Resource.LastRun;

                // Update the lastrun time in DB
                freshDeskBotLastRun.LastRun = DateTime.Now.ToUniversalTime();
                freshDeskBotLastRunResponse = await lastRunContainer.ReplaceItemAsync<FreshDeskBotLastRun>(freshDeskBotLastRun, freshDeskBotLastRun.Id, new PartitionKey(freshDeskBotLastRun.Id));

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
                }
                catch (Exception ex2)
                {
                    lastRun = DateTime.Now.ToUniversalTime();

                    log.LogInformation(ex2.Message);
                }

            }

            return lastRun;
        }
    }
}