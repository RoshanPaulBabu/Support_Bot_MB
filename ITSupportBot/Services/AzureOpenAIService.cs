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
        private readonly ITSupportService _ITSupportService;

        public AzureOpenAIService(IConfiguration configuration, ILogger<AzureOpenAIService> logger, ITSupportService ITSupportService)
        {
            _configuration = configuration;
            _logger = logger;
            _ITSupportService = ITSupportService;
        }

        public async Task<string> HandleOpenAIResponseAsync(string userQuestion, List<ChatTransaction> chatHistory)
        {
            try
            {
                // Initialize OpenAI client
                var apiKeyCredential = new System.ClientModel.ApiKeyCredential(_configuration["AzureOpenAIKey"]);
                var client = new AzureOpenAIClient(new Uri(_configuration["AzureOpenAIEndpoint"]), apiKeyCredential);
                var chatClient = client.GetChatClient("gpt-35-turbo-16k");

                // Define JSON schema for support ticket creation
                string jsonSchemaTicket = @"
                    {
                        ""type"": ""object"",
                        ""properties"": {
                            ""title"": { ""type"": ""string"", ""description"": ""Title of the issue."" },
                            ""description"": { ""type"": ""string"", ""description"": ""Get a ndetailed description of the issue from the user."" }
                        },
                        ""required"": [""title"", ""description""]
                    }";




                // Define function tool parameters
                var ticketFunctionParameters = BinaryData.FromString(jsonSchemaTicket);


                // Define the function tool
                var createSupportTicketTool = ChatTool.CreateFunctionTool(
                    "createSupportTicket",
                    "Creates a new support ticket based on user input by collectin a detailed description from user.",
                    ticketFunctionParameters
                );

                var QnATool = ChatTool.CreateFunctionTool(
                    "get_policy_information",
                    "This function just replies with 'Q&A Question' to the user."
                );

                var chatOptions = new ChatCompletionOptions
                {
                    Tools = { createSupportTicketTool, QnATool }
                };

                // Prepare the chat history
                var chatMessages = new List<ChatMessage>
                    {
                        new SystemChatMessage("You are an  assistant that helps create support tickets by getting detailed information from the user and strictly include only the informations provided buy the user, and also replies with 'Q&A Question' to the user if its Q&A related, dont ask any further questions."),
                    };




                // Add previous conversation history to chat messages
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

                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    foreach (var toolCall in completion.ToolCalls)
                    {
                        if (toolCall.FunctionName == "createSupportTicket")
                        {
                            // Parse tool call arguments
                            var inputData = toolCall.FunctionArguments.ToObjectFromJson<Dictionary<string, string>>();
                            _logger.LogInformation($"Title: {inputData.GetValueOrDefault("title")}, Description: {inputData.GetValueOrDefault("description")}");

                            // Extract required parameters
                            string title = inputData.GetValueOrDefault("title");
                            string description = inputData.GetValueOrDefault("description");

                            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(description))
                            {
                                // Save the assistant's message for continuity
                                chatHistory.Add(new ChatTransaction(completion.Content[0]?.Text ?? "Please provide more details.", userQuestion));
                                return completion.Content[0]?.Text ?? "Please provide more details.";
                            }

                            var RowKey = Guid.NewGuid().ToString(); // Generate a unique RowKey

                            // All required parameters collected
                            await _ITSupportService.SaveTicketAsync(title, description, RowKey);

                            // Update chat history
                            chatHistory.Add(new ChatTransaction("Your support ticket has been created successfully!", userQuestion));
                            return $"RowKey: {RowKey}";
                        }
                        else if (toolCall.FunctionName == "get_policy_information")
                        {
                            // Parse tool call arguments
                            return userQuestion;
                        }

                    }
                }

                // Save the assistant's response for continuity
                string response = completion.Content[0]?.Text ?? "I'm unable to process your request at this time.";
                chatHistory.Add(new ChatTransaction(response, userQuestion));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HandleOpenAIResponseAsync: {ex.Message}");
                return "An error occurred while processing your request. Please try again.";
            }
        }

        public async Task<string> HandleQueryRefinement(string userQuery)
        {
            try
            {
                // Initialize OpenAI client
                var apiKeyCredential = new System.ClientModel.ApiKeyCredential(_configuration["AzureOpenAIKey"]);
                var client = new AzureOpenAIClient(new Uri(_configuration["AzureOpenAIEndpoint"]), apiKeyCredential);
                var chatClient = client.GetChatClient("gpt-35-turbo-16k");

                var chatMessages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an AI assistant designed to optimize user queries about company policies into structured Azure Search queries. Your task is to analyze natural language questions from users, understand the key intent, and translate them into precise search queries for efficient retrieval from the company's policy database indexed in Azure Search. Ensure that:\n\n1. The generated query captures the user's intent and includes relevant keywords or filters.\n2. The query structure is compatible with Azure Search syntax.\n3. Any ambiguous or missing details are clarified or generalized for better search results.\n4. You prioritize clarity, precision, and relevance while maintaining broad coverage when necessary.\n5. For unsupported questions, indicate the inability to generate a query.\n\nOutput the result in JSON format with the following structure:\n- `searchText`: The core keywords or phrase for the query.\n- `filters`: Any specific filters or conditions (e.g., department, category).\n- `suggestions`: Additional suggestions for improving search results (optional).\n\nIf additional information is required to create an effective query, include a note to the user for clarification."),
                new UserChatMessage("Example input: Can I carry forward unused leave to the next year?"),
                new AssistantChatMessage("Example reply: {\n  \"searchText\": \"carry forward unused leave policy\",\n  \"filters\": {\n    \"category\": \"HR\",\n    \"documentType\": \"Employee Handbook\"\n  },\n  \"suggestions\": \"Consider specifying the year or type of leave for more accurate results.\"\n}")
            };


                chatMessages.Add(new UserChatMessage(userQuery));

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
