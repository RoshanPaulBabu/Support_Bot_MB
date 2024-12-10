using AdaptiveCards;
using ITSupportBot.Models;
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
        private readonly AdaptiveCardHelper _AdaptiveCardHelper;

        public ExternalServiceHelper(AzureOpenAIService azureOpenAIService, TicketService ticketService, AzureSearchService azureSearchService, TicketService TicketService, LeaveService leaveService, HolidayService holidayService, AdaptiveCardHelper adaptiveCardHelper)
        {
            _azureOpenAIService = azureOpenAIService;
            _ticketService = ticketService;
            _azureSearchService = azureSearchService;
            _TicketService = TicketService;
            _LeaveService = leaveService;
            _HolidayService = holidayService;
            _AdaptiveCardHelper = adaptiveCardHelper;
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
                    var createdAt = DateTime.UtcNow.ToString("MM/dd/yyyy HH:mm");
                    var ticketId = Guid.NewGuid().ToString();

                    await _ticketService.SaveTicketAsync(Name, Email, title, description, ticketId);

                    var adaptiveCardAttachment = _AdaptiveCardHelper.CreateAdaptiveCardAttachment(
                    "ticketCreationCard.json",
                    new Dictionary<string, string>
                    {
                                                            { "title", title },
                                                            { "description", description },
                                                            { "createdAt", createdAt },
                                                            { "ticketId", ticketId }
                    });

                    var ticketResponse = $"Your support ticket has been created successfully! Ticket ID: {ticketId}";
                    chatHistory.Add(new ChatTransaction(ticketResponse, input));
                    return (ticketId, functionName, adaptiveCardAttachment);





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

                    // Fetch leave records for the employee
                    var leaveRecords = await _LeaveService.GetLeaveRecordsAsync(empId);

                    // Generate the categorized leave status card with hidden sections
                    var categorizedCard = _AdaptiveCardHelper.GenerateCategorizedLeaveStatusCard(leaveRecords);

                    // Return the adaptive card
                    return (null, functionName, categorizedCard);





                //return ("No leave applications found.", functionName, null);

                case "GetHolidaysList":
                    var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    var holidays = await _HolidayService.GetHolidaysAfterDateAsync(date);

                    // Create the Adaptive Card
                    var holidaysCardAttachment = _AdaptiveCardHelper.CreateHolidaysAdaptiveCardAsync(holidays);

                    // Return the adaptive card
                    return (null, functionName, holidaysCardAttachment);

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

    }
}