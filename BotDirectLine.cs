using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel
{
    class BotDirectLine
    {

        //DirectLine BotFramework
        public static string directLineSecret;
        public static string botId;
        private static string fromUser = "DirectLineSampleClientUser";
        private static DirectLineClient client;

        public static async Task<Conversation> StartBotConversation()
        {
            client = new DirectLineClient(directLineSecret);
            Conversation conversation = await client.Conversations.StartConversationAsync();

            return conversation;
        }

        public static async Task<Conversation> ContinueBotConveration(string conversationId)
        {
            client = new DirectLineClient(directLineSecret);
            Conversation conversation = await client.Conversations.ReconnectToConversationAsync(conversationId);

            return conversation;
        }

        public static async Task SendMessagesAsync(string conversationId, string input)
        {
            //Send input
            if (input.Length > 0)
            {
                Activity userMessage = new Activity
                {
                    From = new ChannelAccount(fromUser),
                    Text = input,
                    Type = ActivityTypes.Message
                };

                await client.Conversations.PostActivityAsync(conversationId, userMessage);
            }
        }

        public static async Task<ActivitySet> ReadBotMessagesAsync(string conversationId, string watermark, ILogger log)
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
    }
}
