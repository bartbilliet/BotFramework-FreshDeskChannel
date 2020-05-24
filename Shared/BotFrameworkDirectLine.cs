using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel
{
    class BotFrameworkDirectLine
    {

        //DirectLine BotFramework connection parameters - currently filled in from main CustomChannelLogic class
        public static string directLineSecret;
        public static string botId;
        private static string fromUser = "FreshDeskChannel";
        private static DirectLineClient client;

        public static async Task<Conversation> StartBotConversation(ILogger log)
        {
            try
            {
                client = new DirectLineClient(directLineSecret);
                Conversation conversation = await client.Conversations.StartConversationAsync();

                return conversation;
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in StartBotConversation: {1}", ex);
                throw;
            }
        }

        public static async Task<Conversation> ContinueBotConveration(string conversationId, ILogger log)
        {
            try
            {
                client = new DirectLineClient(directLineSecret);
                Conversation conversation = await client.Conversations.ReconnectToConversationAsync(conversationId);

                return conversation;
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in ContinueBotConveration: {1}", ex);
                throw;
            }
        }

        public static async Task SendMessagesAsync(string conversationId, string input, ILogger log)
        {
            try
            {
                if (input.Length > 0)
                {
                    Activity userMessage = new Activity
                    {
                        From = new ChannelAccount(fromUser),
                        Text = input,
                        Type = ActivityTypes.Message
                    };

                    await client.Conversations.PostActivityAsync(conversationId, userMessage);

                    //TODO: handle the silent retry issue when bot receives a 15s timeout (and therefore processes the message twice)
                    //- https://github.com/microsoft/botframework-sdk/issues/3122
                    //- https://github.com/microsoft/botframework-sdk/issues/50687
                    //- https://github.com/microsoft/botframework-sdk/issues/4559
                }
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in SendMessagesAsync: {1}", ex);
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

                foreach (Activity activity in activitySet.Activities)
                {
                    log.LogInformation("BotMessage: " + activity.Text + "\n");
                }

                return activitySet;
            }
            catch (Exception ex)
            {
                log.LogError("Exception occurred in ReadBotMessagesAsync: {1}", ex);
                throw;
            }
        }
    }
}
