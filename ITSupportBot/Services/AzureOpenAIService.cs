    using Azure;
    using Azure.AI.OpenAI;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using OpenAI.Chat;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using ITSupportBot.Models; // Assuming the updated ChatTransaction class is here
    using System.Linq;


namespace ITSupportBot.Services
{
    public class AzureOpenAIService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AzureOpenAIService> _logger;
        private readonly TicketService _TicketService;
        private readonly LeaveService _LeaveService;
        private readonly HolidayService _HolidayService;

        public AzureOpenAIService(IConfiguration configuration, ILogger<AzureOpenAIService> logger, TicketService TicketService, LeaveService leaveService, HolidayService holidayService)
        {
            _configuration = configuration;
            _logger = logger;
            _TicketService = TicketService;
            _LeaveService = leaveService;
            _HolidayService = holidayService;
        }

        public async Task<(string, string)> HandleOpenAIResponseAsync(string userQuestion, List<ChatTransaction> chatHistory)
        {
            try
            {
                // Initialize OpenAI client
                var apiKeyCredential = new System.ClientModel.ApiKeyCredential(_configuration["AzureOpenAIKey"]);
                var client = new AzureOpenAIClient(new Uri(_configuration["AzureOpenAIEndpoint"]), apiKeyCredential);
                var chatClient = client.GetChatClient("gpt-35-turbo-16k");

                // Define JSON schemas for various tools
                string jsonSchemaTicket = @"
                {
                    ""type"": ""object"",
                    ""properties"": {
                        ""title"": { ""type"": ""string"", ""description"": ""Title of the issue provided by the user."" },
                        ""description"": { ""type"": ""string"", ""description"": ""Detailed issue description provided by the user."" }
                    },
                    ""required"": [""title"", ""description""]
                }";

                string jsonSchemaQuery = @"
                {
                    ""type"": ""object"",
                    ""properties"": {
                        ""query"": { ""type"": ""string"", ""description"": ""Refined query derived from the user's input, optimized for Azure Search AI."" }
                    },
                    ""required"": [""query""]
                }";

                string jsonSchemaLeave = @"
                {
                    ""type"": ""object"",
                    ""properties"": {
                        ""empID"": { ""type"": ""string"", ""description"": ""Employee ID of the leave requester."" },
                        ""empName"": { ""type"": ""string"", ""description"": ""Name of the employee."" },
                        ""leaveType"": { ""type"": ""string"", ""description"": ""Type of leave (earned, sick, Casual)."" },
                        ""startDate"": { ""type"": ""string"", ""format"": ""date"", ""description"": ""Leave start date in yyyy-MM-dd format."" },
                        ""endDate"": { ""type"": ""string"", ""format"": ""date"", ""description"": ""Leave end date in yyyy-MM-dd format."" },
                        ""reason"": { ""type"": ""string"", ""description"": ""Reason for the leave."" }
                    },
                    ""required"": [""empID"", ""empName"", ""leaveType"", ""startDate"", ""endDate"", ""reason""]
                }";


                string jsonSchemaLeaveStatus = @"
                {
                    ""type"": ""object"",
                    ""properties"": {
                        ""empID"": { ""type"": ""string"", ""description"": ""Employee ID of the leave requester."" }
                    },
                    ""required"": [""empID""]
                }";

                string jsonSchemaHolidayQuery = @"
                {
                    ""type"": ""object"",
                    ""properties"": {
                        ""startDate"": { 
                            ""type"": ""string"", 
                            ""format"": ""date"", 
                            ""description"": ""The start date to fetch holidays from (YYYY-MM-DD)."" 
                        }
                    },
                    ""required"": [""startDate""]
                }";


                // Define function tools
                var ticketTool = ChatTool.CreateFunctionTool(
                    "createSupportTicket",
                    "Collects a detailed issue description from the user and creates a support ticket.",
                    BinaryData.FromString(jsonSchemaTicket)
                );

                var queryTool = ChatTool.CreateFunctionTool(
                    "refine_query",
                    "Refines user input into a clear query optimized for Azure Search AI.",
                    BinaryData.FromString(jsonSchemaQuery)
                );

                var leaveTool = ChatTool.CreateFunctionTool(
                    "createLeave",
                    "Collects leave request details from the user and creates a leave record.",
                    BinaryData.FromString(jsonSchemaLeave)
                );

                var leaveStatusTool = ChatTool.CreateFunctionTool(
                    "GetLeaveStatus",
                    "Get latest leave status.",
                    BinaryData.FromString(jsonSchemaLeaveStatus)
                );

                var holidayQueryTool = ChatTool.CreateFunctionTool(
                    "GetHolidaysAfterDate",
                    "Fetch a list of holidays after a specific date.",
                    BinaryData.FromString(jsonSchemaHolidayQuery)
                );

                var chatOptions = new ChatCompletionOptions
                {
                    Tools = { ticketTool, queryTool, leaveTool, leaveStatusTool, holidayQueryTool },

                };


                // System message with clear role definition
                var currentDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                var chatMessages = new List<ChatMessage>
                {
                    new SystemChatMessage($@"
                    You are a precise and structured assistant. Your tasks are:
                    1. Collect detailed information to create support tickets without adding any data beyond what the user provides.
                    2. Refine user queries into optimized search queries for Azure Search AI.
                    3. Collect all required parameters step-by-step for leave requests, ticket creation, or holiday queries.
                    4. Get leave status by collecting employee ID without adding any data beyond what the user provides.
                    5. Retrieve holiday details by collecting a start date and ensuring the date is provided in the correct format (YYYY-MM-DD).
                    Today's date and time is {currentDateTime}. Adhere strictly to user-provided information and validate missing fields by asking directly.
                    ")

                };


                // Add chat history
                foreach (var transaction in chatHistory)
                {
                    if (!string.IsNullOrEmpty(transaction.UserMessage))
                        chatMessages.Add(new UserChatMessage(transaction.UserMessage));
                    if (!string.IsNullOrEmpty(transaction.BotMessage))
                        chatMessages.Add(new AssistantChatMessage(transaction.BotMessage));
                }

                // Add the current user question
                chatMessages.Add(new UserChatMessage(userQuestion));

                // Perform chat completion
                ChatCompletion completion = await chatClient.CompleteChatAsync(chatMessages.ToArray(), chatOptions);

                // Process tool calls
                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    foreach (var toolCall in completion.ToolCalls)
                    {
                        var inputData = toolCall.FunctionArguments.ToObjectFromJson<Dictionary<string, string>>();

                        switch (toolCall.FunctionName)
                        {
                            case "createSupportTicket":
                                var title = inputData.GetValueOrDefault("title");
                                var description = inputData.GetValueOrDefault("description");
                                var ticketId = Guid.NewGuid().ToString();

                                await _TicketService.SaveTicketAsync(title, description, ticketId);
                                chatHistory.Add(new ChatTransaction("Your support ticket has been created successfully!", userQuestion));
                                return (ticketId, toolCall.FunctionName);

                            case "refine_query":
                                var query = inputData.GetValueOrDefault("query");
                                return (query, toolCall.FunctionName);

                            case "createLeave":
                                var empID = inputData.GetValueOrDefault("empID");
                                var empName = inputData.GetValueOrDefault("empName");
                                var leaveType = inputData.GetValueOrDefault("leaveType");
                                var startDate = inputData.GetValueOrDefault("startDate");
                                var endDate = inputData.GetValueOrDefault("endDate");
                                var reason = inputData.GetValueOrDefault("reason");
                                var leaveId = Guid.NewGuid().ToString();

                                await _LeaveService.SaveLeaveAsync(empID, empName, leaveType, startDate, endDate, reason, leaveId);
                                chatHistory.Add(new ChatTransaction("Your leave request has been successfully submitted!", userQuestion));
                                return (empID, "Your leave request has been submitted successfully and is pending approval, you can check it using your employee id: ");


                            case "GetLeaveStatus":
                                var emplID = inputData.GetValueOrDefault("empID");

                                var empstatus = await _LeaveService.GetLatestLeaveStatusAsync(emplID);

                                if (empstatus != null)
                                {
                                    chatHistory.Add(new ChatTransaction($"Succefully got the latest leave status, status {empstatus.Status.ToString()}", userQuestion));
                                    return (empstatus.Status.ToString(), toolCall.FunctionName);
                                }
                                else
                                {
                                    return ("You doesnt have any leave applications or the emp id is incorrect", toolCall.FunctionName);
                                }


                            case "GetHolidaysAfterDate":
                                var Date = inputData.GetValueOrDefault("startDate");

                                var holidays = await _HolidayService.GetHolidaysAfterDateAsync(Date);

                                if (holidays.Count == 0)
                                {
                                    chatHistory.Add(new ChatTransaction($"No holidays found after {Date:yyyy-MM-dd}.", userQuestion));
                                    return ($"No holidays found after {Date:yyyy-MM-dd}.", toolCall.FunctionName);
                                }
                                // Format the list of holidays as a string
                                var holidayList = string.Join("\r\n", holidays.Select(h => $"{h.HolidayName} on {h.Date:yyyy-MM-dd}"));
                                return ($"Holidays after {Date:yyyy-MM-dd}:\r\n{holidayList}", toolCall.FunctionName);


                        }
                    }
                }

                // Default response
                var response = completion.Content[0]?.Text ?? "I'm unable to process your request at this time.";
                chatHistory.Add(new ChatTransaction(response, userQuestion));
                return (response, null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleOpenAIResponseAsync: {ex.Message}");
                return ("An error occurred while processing your request. Please try again.", null);
            }
        }

public async Task<string> HandleQueryRefinement(string userQuery, string result)
        {
            try
            {
                // Initialize OpenAI client
                var apiKeyCredential = new System.ClientModel.ApiKeyCredential(_configuration["AzureOpenAIKey"]);
                var client = new AzureOpenAIClient(new Uri(_configuration["AzureOpenAIEndpoint"]), apiKeyCredential);
                var chatClient = client.GetChatClient("gpt-35-turbo-16k");

                var chatMessages = new List<ChatMessage>
                {
                    new SystemChatMessage("You are a search refinement AI. Your task is to analyze user queries and refine search results to deliver precise and actionable insights.")
                };


                chatMessages.Add(new UserChatMessage(userQuery, result));

                ChatCompletion completion = await chatClient.CompleteChatAsync(chatMessages.ToArray());

                string response = completion.Content[0]?.Text ?? "I'm unable to process your request at this time.";

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleQueryRefinement: {ex.Message}");
                return "An error occurred while processing your request. Please try again.";
            }

        }
    }
}
