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

        public async Task<(string, string)> HandleOpenAIResponseAsync(string userQuestion, List<ChatTransaction> chatHistory)
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


                string jsonSchemaQuery = @"
                    {
                        ""type"": ""object"",
                        ""properties"": {
                            ""query"": { ""type"": ""string"", ""description"": ""Refined query of user message optimized ofr Azure search AI"" }
                        },
                        ""required"": [""query""]
                    }";



                // Define function tool parameters
                var ticketFunctionParameters = BinaryData.FromString(jsonSchemaTicket);

                var queryFunctionParameters = BinaryData.FromString(jsonSchemaQuery);


                // Define the function tool
                var createSupportTicketTool = ChatTool.CreateFunctionTool(
                    "createSupportTicket",
                    "Creates a new support ticket based on user input by collectin a detailed description from user.",
                    ticketFunctionParameters
                );

                var QnATool = ChatTool.CreateFunctionTool(
                    "refine_query",
                    "This function refines the User message to a well defined query for Azure search AI.",
                    queryFunctionParameters
                );

                var chatOptions = new ChatCompletionOptions
                {
                    Tools = { createSupportTicketTool, QnATool }
                };

                // Prepare the chat history
                var chatMessages = new List<ChatMessage>
                    {
                        new SystemChatMessage("You are an  assistant that helps create support tickets by getting detailed information from the user and strictly include only the informations provided buy the user, and also optimize user queries about company policies into Azure Search queries."),
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
                                return (completion.Content[0]?.Text ?? "Please provide more details.", null);
                            }

                            var RowKey = Guid.NewGuid().ToString(); // Generate a unique RowKey

                            // All required parameters collected
                            await _ITSupportService.SaveTicketAsync(title, description, RowKey);

                            // Update chat history
                            chatHistory.Add(new ChatTransaction("Your support ticket has been created successfully!", userQuestion));
                            return (RowKey, toolCall.FunctionName);
                        }
                        else if (toolCall.FunctionName == "refine_query")
                        {
                            var inputData = toolCall.FunctionArguments.ToObjectFromJson<Dictionary<string, string>>();
                            string query = inputData.GetValueOrDefault("query");

                            // Parse tool call arguments
                            return (query,toolCall.FunctionName);
                        }

                    }
                }

                // Save the assistant's response for continuity
                string response = completion.Content[0]?.Text ?? "I'm unable to process your request at this time.";
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
