using BotFramework.FreshDeskChannel.Models;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BotFramework.FreshDeskChannel
{
    public class FreshDesk
    {
        private static HttpClient client = new HttpClient();
        public static string freshDeskClientUrl;
        public static string freshDeskAPIKey;

        private static void SetFreshDeskAuthHeaders()
        {
            var byteArray = Encoding.ASCII.GetBytes(freshDeskAPIKey + ":dummypassword");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        public static async Task<List<FreshDeskTicket>> GetUpdatedFreshDeskTicketsAsync(DateTime lastRun)
        {
            SetFreshDeskAuthHeaders();

            HttpResponseMessage response = await client.GetAsync(freshDeskClientUrl + "tickets?updated_since=" + lastRun.ToString("yyyy-MM-ddTHH:mm:ssZ") + "&include=requester,description");
            string stringData = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            List<FreshDeskTicket> listTickets = JsonSerializer.Deserialize<List<FreshDeskTicket>>(stringData, options);

            return listTickets;
        }

        public static async Task<List<FreshDeskConversation>> GetFreshDeskTicketConversationsAsync(long ticketId)
        {
            SetFreshDeskAuthHeaders();

            HttpResponseMessage response = await client.GetAsync(freshDeskClientUrl + "tickets/" + ticketId + "/conversations");
            string stringData = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            List<FreshDeskConversation> listConversations = JsonSerializer.Deserialize<List<FreshDeskConversation>>(stringData, options);

            return listConversations;
        }

        public static async Task SendFreshDeskTicketReply(string ticketId, string ResponseMessage, ILogger log)
        {
            SetFreshDeskAuthHeaders();

            string stringData = JsonSerializer.Serialize(new { body = ResponseMessage });
            var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(freshDeskClientUrl + "tickets/" + ticketId + "/reply", contentData);

            if (response.IsSuccessStatusCode)
            {
                log.LogInformation("Reply succeeded");
            }
            else
            {
                log.LogError("Reply failed");
            }
        }

    }
}
