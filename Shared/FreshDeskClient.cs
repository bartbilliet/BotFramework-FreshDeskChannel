﻿using BotFramework.FreshDeskChannel.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static BotFramework.FreshDeskChannel.Models.BotResponseChannelData;

namespace BotFramework.FreshDeskChannel
{
    public class FreshDeskClient
    {
        public static string freshDeskClientUrl;
        public static string freshDeskAPIKey; 
        private static HttpClient client;

        static FreshDeskClient()
        {
            freshDeskClientUrl = Environment.GetEnvironmentVariable("FreshDeskClientUrl");
            freshDeskAPIKey = Environment.GetEnvironmentVariable("FreshDeskAPIKey");

            client = new HttpClient();
            var byteArray = Encoding.ASCII.GetBytes(freshDeskAPIKey + ":dummypassword");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        public static async Task<List<FreshDeskTicket>> GetUpdatedFreshDeskTicketsAsync(DateTime lastRun, ILogger log)
        {
            try
            {
                log.LogDebug("\t  Reading tickets updated since " + lastRun + " (GMT)");
                
                HttpResponseMessage response = await client.GetAsync(freshDeskClientUrl + "tickets?updated_since=" + lastRun.ToString("yyyy-MM-ddTHH:mm:ssZ") + "&include=requester,description");
                string stringData = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                List<FreshDeskTicket> listTickets = JsonSerializer.Deserialize<List<FreshDeskTicket>>(stringData, options);

                return listTickets;
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in GetUpdatedFreshDeskTicketsAsync: {1}", ex);
                throw;
            }
        }

        public static async Task<List<FreshDeskConversation>> GetFreshDeskTicketConversationsAsync(long ticketId, ILogger log)
        {
            try
            {
                log.LogDebug("\t  Getting all conversation messages on ticket #" + ticketId);
                
                List<FreshDeskConversation> listConversations = new List<FreshDeskConversation>();

                string nextUrl = freshDeskClientUrl + "tickets/" + ticketId + "/conversations"; //Base URL
                do
                {
                    HttpResponseMessage response = await client.GetAsync(nextUrl);
                    string stringData = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    listConversations.AddRange(JsonSerializer.Deserialize<List<FreshDeskConversation>>(stringData, options));

                    // Get the URL for the next page
                    if (response.Headers.Contains("link"))
                    {
                        nextUrl = ParseLinkHeader(response.Headers.GetValues("link").FirstOrDefault())["next"];
                    }
                    else {
                        nextUrl = string.Empty;
                    }

                }
                while (!string.IsNullOrEmpty(nextUrl)); 


                return listConversations;
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in GetFreshDeskTicketConversationsAsync: {1}", ex);
                throw;
            }
        }

        public static async Task SendFreshDeskTicketReply(string ticketId, string responseMessage, ILogger log)
        {
            try
            {
                log.LogDebug("\t  Sending ticket reply to FreshDesk ticket ID " + ticketId);

                string stringData = JsonSerializer.Serialize(new { body = responseMessage });
                var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(freshDeskClientUrl + "tickets/" + ticketId + "/reply", contentData);

                if (!response.IsSuccessStatusCode)
                { 
                    log.LogError("\t  Error sending Bot resonse to FreshDesk");
                }
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in SendFreshDeskTicketReply: {1}", ex);
                throw;
            }
        }

        public static async Task SendFreshDeskNote(string ticketId, string noteMessage, bool @private, string[] notifyEmails, ILogger log)
        {
            try
            {
                log.LogDebug("\t  Sending ticket note to FreshDesk ticket ID " + ticketId);

                // Only add when notification email addresses were added
                string stringData;
                if (notifyEmails != null) {
                    stringData = JsonSerializer.Serialize(new { body = noteMessage, @private = @private, notify_emails = notifyEmails });
                }
                else
                {
                    stringData = JsonSerializer.Serialize(new { body = noteMessage, @private = @private });
                }
                
                var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(freshDeskClientUrl + "tickets/" + ticketId + "/notes", contentData);

                if (!response.IsSuccessStatusCode)
                {
                    log.LogError("\t  Error sending note from Bot to FreshDesk");
                }
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in SendFreshDeskNote: {1}", ex);
                throw;
            }
        }

        public static async Task SetTicketStatus(string ticketId, FreshDeskTicketStatus freshDeskTicketStatus, ILogger log)
        {
            try
            {
                log.LogDebug("\t  Updating ticket status for ticket ID #" + ticketId);

                string stringData = JsonSerializer.Serialize(new { status = freshDeskTicketStatus });
                var contentData = new StringContent(stringData, System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PutAsync(freshDeskClientUrl + "tickets/" + ticketId, contentData);

                if (!response.IsSuccessStatusCode)
                {
                    log.LogError("\t  Error updating the ticket status in FreshDesk");
                }
            }
            catch (Exception ex)
            {
                log.LogError("\t  Exception occurred in SetTicketStatus: {1}", ex);
                throw;
            }
        }

        private static Dictionary<string, string> ParseLinkHeader(string linkHeader)
        {
            //<https://<acccount>.freshdesk.com/api/v2/tickets/90/conversations?page=2>; rel="next", <https://<acccount>.freshdesk.com/api/v2/tickets/90/conversations?page=5>; rel="last"
            var dictionary = new Dictionary<string, string>();

            string[] links = linkHeader.Split(',');
            foreach (string linkEntry in links)
            {
                var searcher = new Regex("<(.*)>; rel=\"(.*)\"");
                Match match = searcher.Match(linkEntry);
                if (match.Groups.Count == 3)
                {
                    dictionary.Add(match.Groups[2].Value, match.Groups[1].Value);
                }
            }

            return dictionary;
        }
    }
}

