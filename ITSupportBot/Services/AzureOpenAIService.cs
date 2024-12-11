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
                var chatClient = client.GetChatClient("gpt-4o");

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
                    "Collects a detailed issue description from the user and creates a support ticket. ",
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
                    "'You are about to apply for leave with the following details. Is this correct?' before invoking the tool."+
                    "Example: If the user says, 'I'm not feeling well, apply for a leave,' respond with: 'It seems you need a sick leave, and the reason is that you're not feeling well. Could you please specify the start and end dates for your leave?' Once the user provides dates, confirm the details before submission.",
                    BinaryData.FromString(jsonSchemaLeave)
                );

                var leaveStatusTool = ChatTool.CreateFunctionTool(
                    "GetLeaveStatus",
                    "Retrieves the status of leave requests. This tool can be invoked without requiring additional parameters."
                );

                var holidayQueryTool = ChatTool.CreateFunctionTool(
                    "GetHolidaysList",
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
        You are an intelligent assistant equipped with tools to handle user requests. The current date and day are: {currentDateTimeWithDay}.

        Follow these essential guidelines:
        1. **Understand User Intent:** Match the user's request with the appropriate tool and ask clarifying questions if required.
        2. **Validate Parameters:** Ensure all required parameters are provided. Do not invoke tools with placeholders or null values.
        3. **Confirm Actions:** Before invoking 'createLeave' tool, confirm details with the user: 'You are about to [action]. Is this correct?'
        4. **Handle Edge Cases:** Address ambiguous or incomplete details by reprompting for clarity. Prioritize the most recent user intent if topics shift.
        5. **Schema Adherence:** Follow the schema requirements strictly for each tool.

        Use precise and concise language for all interactions, ensuring seamless user experience and accurate tool invocations."
    )
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

public async Task<string> HandleSearchResultRefinement(string queryresult, string sysMessage)
        {
            try
            {
                // Initialize OpenAI client
                var apiKeyCredential = new System.ClientModel.ApiKeyCredential(_configuration["AzureOpenAIKey"]);
                var client = new AzureOpenAIClient(new Uri(_configuration["AzureOpenAIEndpoint"]), apiKeyCredential);
                var chatClient = client.GetChatClient("gpt-35-turbo-16k");

                var chatMessages = new List<ChatMessage>
                {
                    new SystemChatMessage(sysMessage),
                    new UserChatMessage(queryresult)
                };



                ChatCompletion completion = await chatClient.CompleteChatAsync(chatMessages.ToArray());
                var result = completion;

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
