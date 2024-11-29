using ITSupportBot.Services;
using Microsoft.Bot.Builder.Dialogs;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using ITSupportBot.Models;
using Microsoft.Bot.Builder;
using System;
using Microsoft.Bot.Schema;
using System.IO;
using Newtonsoft.Json;
using System.Linq;

namespace ITSupportBot.Dialogs
{
    public class ParameterCollectionDialog : ComponentDialog
    {
        private readonly AzureOpenAIService _AzureOpenAIService;
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;
        private readonly TicketService _ITSupportService;
        private readonly AzureSearchService _AzureSearchService;

        public ParameterCollectionDialog(AzureOpenAIService AzureOpenAIService, IStatePropertyAccessor<UserProfile> userProfileAccessor, TicketService iTSupportService, AzureSearchService AzureSearchService
            )
            : base(nameof(ParameterCollectionDialog))
        {
            _AzureOpenAIService = AzureOpenAIService;
            _AzureSearchService = AzureSearchService;
            _userProfileAccessor = userProfileAccessor;
            _ITSupportService = iTSupportService;

            var waterfallSteps = new WaterfallStep[]
            {
            AskHelpQueryStepAsync,
            BeginParameterCollectionStepAsync,
            HandleUserActionStepAsync,
            SaveEditedTicketStepAsync
            };
            AddDialog(new QnAHandlingDialog(_AzureSearchService, _AzureOpenAIService));
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> AskHelpQueryStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {

            if (stepContext.Options is string textResponse && !string.IsNullOrEmpty(textResponse))
            {
                return await stepContext.PromptAsync(
                   nameof(TextPrompt),
                   new PromptOptions { Prompt = MessageFactory.Text(textResponse) },
                   cancellationToken
               );

            }
        else
        {
            // Send a message but then proceed with the dialog logic
            await stepContext.Context.SendActivityAsync(MessageFactory.Text("Hello! How can I help you today?"), cancellationToken);

            // Explicitly return an EndOfTurn result, or continue the dialog
            return Dialog.EndOfTurn;
        }
        }

        private async Task<DialogTurnResult> BeginParameterCollectionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            
            var card = stepContext.Context.Activity.Value;

            // Retrieve the user query (response from the previous step)
            string userMessage = (string)stepContext.Result;

            // Get user profile and initialize chat history if necessary
            var userProfile = await _userProfileAccessor.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            userProfile.ChatHistory ??= new List<ChatTransaction>();

            // Determine what to pass to HandleOpenAIResponseAsync
            string inputToAIService = card != null ? card.ToString() : userMessage;

            // Get the response from the AI service
            var (response, functionName) = await _AzureOpenAIService.HandleOpenAIResponseAsync(inputToAIService, userProfile.ChatHistory);
            var FinalResponse = $"{functionName}{response}";

            if (functionName == "createSupportTicket")
            {
                string rowKey = response;

                // Fetch the ticket from the service
                var ticket = await _ITSupportService.GetTicketAsync(rowKey);

                // Create the adaptive card using the helper method
                var adaptiveCardAttachment = CreateAdaptiveCardAttachment("ticketCreationCard.json", new Dictionary<string, string>
        {
            { "title", ticket.Title },
            { "description", ticket.Description },
            { "createdAt", ticket.CreatedAt.ToString("MM/dd/yyyy HH:mm") },
            { "ticketId", ticket.RowKey }
        });

                // Send the adaptive card
                var reply = MessageFactory.Attachment(adaptiveCardAttachment);
                await stepContext.Context.SendActivityAsync(reply, cancellationToken);

                stepContext.Values["rowKey"] = rowKey;

                // End the dialog after processing the ticket
                return Dialog.EndOfTurn;
            }
            else if (functionName == "refine_query")
            {
                // Begin the QnAHandlingDialog and pass the response as dialog options
                return await stepContext.BeginDialogAsync(nameof(QnAHandlingDialog), new { Message = response }, cancellationToken);
            }

            else if (functionName == "GetLeaveStatus")
            {
                 if (!string.IsNullOrEmpty(response) && response.Contains("incorrect"))
                {
                    return await stepContext.ReplaceDialogAsync(InitialDialogId, response, cancellationToken);
                }
                else {
                    await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Your leave request is {response}"), cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }
                 
            }

            else if (functionName == "GetHolidaysAfterDate")
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"{response}"), cancellationToken);
                return await stepContext.EndDialogAsync(null, cancellationToken);

            }

            else if (!string.IsNullOrEmpty(functionName) && functionName.Contains("employee id"))
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"{FinalResponse}"), cancellationToken);
                // Begin the QnAHandlingDialog and pass the response as dialog options
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }


            // If the response indicates the process is not complete, replace the dialog to collect more parameters
            return await stepContext.ReplaceDialogAsync(InitialDialogId, response, cancellationToken);
        }


        private async Task<DialogTurnResult> HandleUserActionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userAction = (string)((stepContext.Context.Activity.Value as dynamic)?.action);

            if (userAction == "edit")
            {
                var rowKey =  stepContext.Values["rowKey"];

                // Fetch ticket using rowKey
                var ticket = await _ITSupportService.GetTicketAsync(rowKey.ToString());  // Pass the rowKey here

                // Proceed with the rest of the logic after ticket is fetched
                string editCardJson = File.ReadAllText("Cards/editTicketCard.json");

                editCardJson = editCardJson.Replace("${title}", ticket.Title)
                   .Replace("${description}", ticket.Description)
                   .Replace("${ticketId}", ticket.RowKey);  // Use RowKey here

                var editCardAttachment = new Attachment
                {
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Content = Newtonsoft.Json.JsonConvert.DeserializeObject(editCardJson)
                };

                await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(editCardAttachment), cancellationToken);
                return Dialog.EndOfTurn;
            }
            else if (userAction == "confirm")
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Your ticket has been successfully confirmed."), cancellationToken);
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }


        private async Task<DialogTurnResult> SaveEditedTicketStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Extract ticket details from the adaptive card data
            dynamic activityValue = stepContext.Context.Activity.Value;
            string rowKey = activityValue.ticketId?.ToString();  // Convert ticketId to string
            string updatedTitle = activityValue.title?.ToString();
            string updatedDescription = activityValue.description?.ToString();

            if (string.IsNullOrEmpty(updatedTitle) || string.IsNullOrEmpty(updatedDescription) || string.IsNullOrEmpty(rowKey))
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Invalid input. Please provide valid ticket details."), cancellationToken);
                return Dialog.EndOfTurn;
            }

            try
            {
                // Update the existing ticket in the database
                await _ITSupportService.UpdateTicketAsync(rowKey, updatedTitle, updatedDescription);

                // Send a success message
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Your ticket has been updated successfully."), cancellationToken);
            }
            catch (Exception ex)
            {
                // Handle errors (e.g., ticket not found)
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Error updating ticket: {ex.Message}"), cancellationToken);
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        private Attachment CreateAdaptiveCardAttachment(string cardFileName, Dictionary<string, string> placeholders)
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
                    Content = Newtonsoft.Json.JsonConvert.DeserializeObject(adaptiveCard, new JsonSerializerSettings { MaxDepth = null }),
                };
            }
        }


    }

}