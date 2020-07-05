using BotFramework.FreshDeskChannel.Models;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel
{
    class BotFrameworkDirectLine
    {
        public static string directLineSecret;
        public static string botId;
        private static string fromUser = "FreshDeskChannel";
        private static DirectLineClient client;

        static BotFrameworkDirectLine()
        {
            directLineSecret = Environment.GetEnvironmentVariable("DirectLineSecret");
            botId = Environment.GetEnvironmentVariable("BotId");

            client = new DirectLineClient(directLineSecret);

            //Fix silent retry issue which produces duplicate messages
            client.SetRetryPolicy(new Microsoft.Rest.TransientFaultHandling.RetryPolicy(new Microsoft.Rest.TransientFaultHandling.HttpStatusCodeErrorDetectionStrategy(), 0));
        }

        public static async Task<Conversation> StartBotConversation(ILogger log)
        {
            try
            {
                //client = new DirectLineClient(directLineSecret);
                
                //Fix silent retry issue which produces duplicate messages
                //client.SetRetryPolicy(new Microsoft.Rest.TransientFaultHandling.RetryPolicy(new Microsoft.Rest.TransientFaultHandling.HttpStatusCodeErrorDetectionStrategy(), 0));

                Conversation conversation = await client.Conversations.StartConversationAsync();

                return conversation;
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in StartBotConversation: {1}", ex);
                throw;
            }
        }

        public static async Task<Conversation> ContinueBotConveration(string conversationId, ILogger log)
        {
            try
            {
                //client = new DirectLineClient(directLineSecret);

                //Fix silent retry issue which produces duplicate messages
                //client.SetRetryPolicy(new Microsoft.Rest.TransientFaultHandling.RetryPolicy(new Microsoft.Rest.TransientFaultHandling.HttpStatusCodeErrorDetectionStrategy(), 0));

                Conversation conversation = await client.Conversations.ReconnectToConversationAsync(conversationId);

                return conversation;
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in ContinueBotConveration: {1}", ex);
                throw;
            }
        }

        public static async Task SendMessagesAsync(string conversationId, FreshDeskChannelData freshDeskChannelData, ILogger log)
        {
            try
            {
                log.LogTrace("\t  Sending FreshDesk message to Bot: " + freshDeskChannelData.Message);

                Activity customerMessageActivity = new Activity
                {
                    From = new ChannelAccount(fromUser),
                    Text = freshDeskChannelData.Message,
                    Type = ActivityTypes.Message,
                    ChannelData = freshDeskChannelData
                };

                //TODO: Handle the silent retry issue when bot receives a 15s timeout (and therefore processes the message twice)
                //This issue produces when Bot Framework code is hosted on App Service and the App Service goes to sleep. 
                //Host the bot on App Service using AlwaysOn or Azure Function to avoid at this time.
                //UPDATE: Possibly solved by the SetRetryPolicy in constructor.
                //- https://github.com/microsoft/botframework-sdk/issues/3122
                //- https://github.com/microsoft/botframework-sdk/issues/5068
                //- https://github.com/microsoft/botframework-sdk/issues/4559

                // No need to wait for the response, we will poll later (leave possibility for back-end responses)
                await client.Conversations.PostActivityAsync(conversationId, customerMessageActivity);

            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in SendMessagesAsync: {1}", ex);
                throw;
            }
        }

        public static async Task<ActivitySet> ReadBotMessagesAsync(string conversationId, string watermark, ILogger log)
        {
            try
            {
                ActivitySet activitySet = await client.Conversations.GetActivitiesAsync(conversationId, watermark);

                activitySet.Activities = (from x in activitySet.Activities
                                          where x.From.Id == botId
                                          select x).ToList();

                log.LogDebug("\t  Retrieved " + activitySet.Activities.Count + " Bot responses for conversation with ID #" + conversationId);

                foreach (Activity activity in activitySet.Activities)
                {
                    log.LogTrace("\t  Bot response: " + activity.Text);
                }

                return activitySet;
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in ReadBotMessagesAsync: {1}", ex);
                throw;
            }
        }
    }
}
