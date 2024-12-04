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
using Microsoft.Recognizers.Text;


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

        public async Task<(object, string, string)> HandleOpenAIResponseAsync(string userQuestion, List<ChatTransaction> chatHistory)
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
                        ""description"": { ""type"": ""string"", ""description"": ""A comprehensive description of the issue or problem the user is facing."" }
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
                        ""leaveType"": { ""type"": ""string"", ""description"": ""Type of leave (earned, sick, Casual)."" },
                        ""startDate"": { ""type"": ""string"", ""format"": ""date"", ""description"": ""Leave start date in yyyy-MM-dd format."" },
                        ""endDate"": { ""type"": ""string"", ""format"": ""date"", ""description"": ""Leave end date in yyyy-MM-dd format."" },
                        ""reason"": { ""type"": ""string"", ""description"": ""Reason for the leave."" }
                    },
                    ""required"": [""leaveType"", ""startDate"", ""endDate""]
                }";



                // Define function tools with specific behavior
                var ticketTool = ChatTool.CreateFunctionTool(
                    "createSupportTicket",
                    "Collects a detailed issue description from the user and creates a support ticket. " +
                    "Ensure to confirm with the user before creating the ticket. Example: 'You are about to create a ticket with the following details. Is this correct?'",
                    BinaryData.FromString(jsonSchemaTicket)
                );

                var queryTool = ChatTool.CreateFunctionTool(
                    "refine_query",
                    "Refines user input related to company policies into a clear query optimized for Azure Search AI.",
                    BinaryData.FromString(jsonSchemaQuery)
                );

                var leaveTool = ChatTool.CreateFunctionTool(
                    "createLeave",
                    "Apply for leave by collecting leave details from the user. Ensure to handle natural language date inputs like 'today', 'tomorrow', or 'next Monday' based on the current date. " +
                    "After collecting all required details ('leaveType', 'startDate', 'endDate', and 'reason'), confirm with the user: " +
                    "'You are about to apply for leave with the following details. Is this correct?' before invoking the tool.",
                    BinaryData.FromString(jsonSchemaLeave)
                );

                var leaveStatusTool = ChatTool.CreateFunctionTool(
                    "GetLeaveStatus",
                    "Retrieves the status of leave requests. This tool can be invoked without requiring additional parameters."
                );

                var holidayQueryTool = ChatTool.CreateFunctionTool(
                    "GetHolidaysAfterDate",
                    "Fetches a list of holidays . This tool can be invoked without requiring additional parameters.."
                );

                var chatOptions = new ChatCompletionOptions
                {
                    Tools = { ticketTool, queryTool, leaveTool, leaveStatusTool, holidayQueryTool }
                };


                // System message with clear role definition





                var currentDateTimeWithDay = $"{DateTime.Now:yyyy-MM-dd HH:mm} ({DateTime.Now.DayOfWeek})";


                var chatMessages = new List<ChatMessage>
                {
                    new SystemChatMessage($@"
                        You are an intelligent assistant equipped with tools to handle user requests. Your primary task is to assist users by invoking the appropriate tools. Follow these guidelines to ensure correct and precise tool usage:

                        1. Understand User Intent:
                           - Determine the user's specific request and match it with the most suitable tool.
                           - Avoid assumptions; ask clarifying questions to collect all required details.

                        2. Parameter Validation:
                           - Gather all required parameters for the selected tool.
                           - Do not invoke a tool unless every required parameter is explicitly provided.
                           - Always validate the user-provided information against the tool's schema.

                        3. Natural Language Date Handling:
                           - The current date and day are: {currentDateTimeWithDay}.
                           - When users specify dates such as 'today', 'tomorrow', 'this Friday', or 'next Monday', calculate the exact dates based on the current date and day. Use this to populate date-related parameters accurately.

                        4. Reprompt for Missing Information:
                           - If any required parameter is missing or unclear, ask the user for that specific information.
                           - Use clear, concise language to request missing data.

                        5. Confirm Before Invoking Tools:
                           - For 'createSupportTicket', after collecting 'title' and 'description', confirm with the user: 'You are about to create a support ticket with the following details. Is this correct?' 
                             - If the user confirms, invoke the tool. If not, allow corrections.
                           - For 'createLeave', after collecting 'leaveType', 'startDate', 'endDate', and 'reason', confirm with the user: 'You are about to apply for leave with the following details. Is this correct?' 
                             - If the user confirms, invoke the tool. If not, allow corrections.

                        6. Invoke Tools Once Ready:
                           - Only invoke the tool when all required parameters are present.
                           - Do not use placeholders or null values.

                        7. Schema-Specific Behavior:
                           - For 'refine_query', ensure the 'query' is clear and precise.
                           - For 'GetHolidaysAfterDate', only invoke if the user explicitly requests holiday information, and ensure 'startDate' is provided.
                           - For 'GetLeaveStatus', directly invoke the tool without requiring additional input.

                        8. Handle Edge Cases:
                           - If a user provides incomplete or ambiguous details, clarify before proceeding.
                           - If the user switches topics mid-conversation, prioritize the most recent intent.

                        Ensure precise tool invocations, adhering to the tool's schema and user needs.
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
                var chat = chatMessages.ToArray();

                // Perform chat completion
                ChatCompletion completion = await chatClient.CompleteChatAsync(chatMessages.ToArray(), chatOptions);

                // Process tool calls
                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    foreach (var toolCall in completion.ToolCalls)
                    {
                        var inputData = toolCall.FunctionArguments.ToObjectFromJson<Dictionary<string, string>>();

                        return (inputData, toolCall.FunctionName, null);
                    }
                }

                // Default response
                var response = completion.Content[0]?.Text ?? "I'm unable to process your request at this time.";
                chatHistory.Add(new ChatTransaction(response, userQuestion));
                return (null, null, response);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleOpenAIResponseAsync: {ex.Message}");
                return (null, null, "An error occurred while processing your request. Please try again.");
            }
        }

public async Task<string> HandleSearchResultRefinement(string userQuery, string result)
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
