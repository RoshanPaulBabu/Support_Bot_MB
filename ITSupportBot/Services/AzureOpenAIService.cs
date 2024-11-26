    using Azure;
    using Azure.AI.OpenAI;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using OpenAI.Chat;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using ITSupportBot.Models; // Assuming the updated ChatTransaction class is here

namespace ITSupportBot.Services
{
    public class AzureOpenAIService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AzureOpenAIService> _logger;
        private readonly TicketService _TicketService;
        private readonly LeaveService _LeaveService;

        public AzureOpenAIService(IConfiguration configuration, ILogger<AzureOpenAIService> logger, TicketService TicketService, LeaveService leaveService)
        {
            _configuration = configuration;
            _logger = logger;
            _TicketService = TicketService;
            _LeaveService = leaveService;
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



                // Define function tool parameters
                var ticketFunctionParameters = BinaryData.FromString(jsonSchemaTicket);

                var queryFunctionParameters = BinaryData.FromString(jsonSchemaQuery);

                var leaveFunctionParameters = BinaryData.FromString(jsonSchemaLeave);



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

                var chatOptions = new ChatCompletionOptions
                {
                    Tools = { ticketTool, queryTool, leaveTool },

                };


                // System message with clear role definition
                var currentDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                var chatMessages = new List<ChatMessage>
                {
                    new SystemChatMessage($@"
                    You are a precise and structured assistant. Your tasks are:
                    1. Collect detailed information to create support tickets without adding any data beyond what the user provides.
                    2. Refine user queries into optimized search queries for Azure Search AI.
                    3. Collect all required parameters step-by-step for leave requests or ticket creation.
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
                                return (leaveId, "Your leave request has been submitted successfully and is pending approval, you can check it using your leave id: ");
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
