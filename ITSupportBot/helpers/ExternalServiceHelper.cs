﻿using ITSupportBot.Models;
using ITSupportBot.Services;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ITSupportBot.Helpers
{
    public class ExternalServiceHelper
    {
        private readonly AzureOpenAIService _azureOpenAIService;
        private readonly TicketService _ticketService;
        private readonly AzureSearchService _azureSearchService;
        private readonly TicketService _TicketService;
        private readonly LeaveService _LeaveService;
        private readonly HolidayService _HolidayService;

        public ExternalServiceHelper(AzureOpenAIService azureOpenAIService, TicketService ticketService, AzureSearchService azureSearchService, TicketService TicketService, LeaveService leaveService, HolidayService holidayService)
        {
            _azureOpenAIService = azureOpenAIService;
            _ticketService = ticketService;
            _azureSearchService = azureSearchService;
            _TicketService = TicketService;
            _LeaveService = leaveService;
            _HolidayService = holidayService;
        }

        public async Task<(string response, string functionName, Attachment)> HandleOpenAIResponseAsync(string input, List<ChatTransaction> chatHistory, ITurnContext turnContext)
        {
            // Call Azure OpenAI Service
            var (inputData, functionName, directResponse) = await _azureOpenAIService.HandleOpenAIResponseAsync(input, chatHistory);

            // If a direct response exists, return it
            if (!string.IsNullOrEmpty(directResponse))
            {
                return (directResponse, null, null);
            }

            var inputDictionary = inputData as Dictionary<string, string>;

            // Handle tool-based responses
            switch (functionName)
            {
                case "createSupportTicket":
                    var Name = turnContext.TurnState.Get<string>("TeamsUserName") ?? "Default User"; // User's display name
                    var Email = turnContext.TurnState.Get<string>("TeamsUserEmail") ?? "default@example.com"; // User's email
                    var title = inputDictionary.GetValueOrDefault("title");
                    var description = inputDictionary.GetValueOrDefault("description");
                    var ticketId = Guid.NewGuid().ToString();

                    await _ticketService.SaveTicketAsync(Name, Email, title, description, ticketId);
                    var ticketResponse = $"Your support ticket has been created successfully! Ticket ID: {ticketId}";
                    chatHistory.Add(new ChatTransaction(ticketResponse, input));
                    return (ticketId, functionName, null);

                case "refine_query":
                    var query = inputDictionary.GetValueOrDefault("query");
                    return (query, functionName, null);

                case "createLeave":
                    // Retrieve Teams user info from TurnState
                    //var empID = turnContext.TurnState.Get<string>("TeamsUserId"); // Use Azure AD Object ID as empID
                    var empName = turnContext.TurnState.Get<string>("TeamsUserName") ?? "Default User"; // User's display name
                    var email = turnContext.TurnState.Get<string>("TeamsUserEmail") ?? "default@example.com"; // User's email

                    // Extract leave details from inputDictionary
                    var leaveType = inputDictionary.GetValueOrDefault("leaveType");
                    var startDate = inputDictionary.GetValueOrDefault("startDate");
                    var endDate = inputDictionary.GetValueOrDefault("endDate");
                    var reason = inputDictionary.GetValueOrDefault("reason");
                    var leaveId = Guid.NewGuid().ToString();

                    // Save the leave request
                    await _LeaveService.SaveLeaveAsync(email, empName, leaveType, startDate, endDate, reason, leaveId);

                    // Return response
                    var leaveResponse = "Your leave request has been submitted successfully and is pending approval.";
                    chatHistory.Add(new ChatTransaction(leaveResponse, input));
                    return (email, functionName, null);


                case "GetLeaveStatus":
                    var empId = turnContext.TurnState.Get<string>("TeamsUserEmail") ?? "default@example.com";
                    var leaveStatus = await _LeaveService.GetLatestLeaveStatusAsync(empId);

                    if (leaveStatus != null)
                    {
                        chatHistory.Add(new ChatTransaction($"Latest Leave Status: {leaveStatus.Status}", input));

                        var adaptiveCardAttachment = CreateAdaptiveCardAttachment(
                        "leaveDetailsCard.json",
                        new Dictionary<string, string>
                        {
                                                                { "RowKey", leaveStatus.RowKey },
                                                                { "CreatedAt", leaveStatus.CreatedAt.ToString("f") },
                                                                { "Status", leaveStatus.Status },
                                                                { "LeaveType", leaveStatus.LeaveType },
                                                                { "StartDate", leaveStatus.StartDate.ToString("d") },
                                                                { "EndDate", leaveStatus.EndDate.ToString("d") },
                                                                { "Reason", leaveStatus.Reason }
                        });
                        return (null, functionName, adaptiveCardAttachment);
                    }

                    return ("No leave applications found.", functionName, null);

                case "GetHolidaysAfterDate":
                    var Date = inputDictionary.GetValueOrDefault("startDate");
                    var holidays = await _HolidayService.GetHolidaysAfterDateAsync(Date);

                    if (!holidays.Any())
                    {
                        var noHolidayMessage = $"No holidays found after {Date}.";
                        chatHistory.Add(new ChatTransaction(noHolidayMessage, input));
                        return (noHolidayMessage, functionName, null);
                    }

                    var holidayList = string.Join("\n", holidays.Select(h => $"{h.HolidayName} on {h.Date:yyyy-MM-dd}"));
                    return ($"Holidays:\n{holidayList}", functionName, null);

                default:
                    return ("Unknown operation requested.", functionName, null);
            }
        }


        public async Task<Ticket> GetTicketAsync(string rowKey)
        {
            return await _ticketService.GetTicketAsync(rowKey);
        }

        public async Task UpdateTicketAsync(string rowKey, string title, string description)
        {
            await _ticketService.UpdateTicketAsync(rowKey, title, description);
        }

        // Azure Search interaction
        public async Task<SearchResult> PerformSearchAsync(string query)
        {
            return await _azureSearchService.GetTopSearchResultAsync(query);
        }

        // Query refinement using OpenAI
        public async Task<string> RefineSearchResultAsync(string originalQuery, string searchResultContent)
        {
            return await _azureOpenAIService.HandleSearchResultRefinement(originalQuery, searchResultContent);
        }

        public Attachment CreateAdaptiveCardAttachment(string cardFileName, Dictionary<string, string> placeholders)
        {
            // Locate the resource path for the card file
            var resourcePath = GetType().Assembly.GetManifestResourceNames()
                                .FirstOrDefault(name => name.EndsWith(cardFileName, StringComparison.OrdinalIgnoreCase));

            if (resourcePath == null)
            {
                throw new FileNotFoundException($"The specified card file '{cardFileName}' was not found as an embedded resource.");
            }

            using (var stream = GetType().Assembly.GetManifestResourceStream(resourcePath))
            using (var reader = new StreamReader(stream))
            {
                // Read the card template
                var adaptiveCard = reader.ReadToEnd();

                // Replace placeholders dynamically
                foreach (var placeholder in placeholders)
                {
                    adaptiveCard = adaptiveCard.Replace($"${{{placeholder.Key}}}", placeholder.Value);
                }

                // Return the populated adaptive card as an attachment
                return new Attachment
                {
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Content = JsonConvert.DeserializeObject(adaptiveCard, new JsonSerializerSettings { MaxDepth = null }),
                };
            }
        }
    }
}