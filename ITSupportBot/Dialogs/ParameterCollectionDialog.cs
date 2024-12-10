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
using ITSupportBot.Helpers;

namespace ITSupportBot.Dialogs
{
    public class ParameterCollectionDialog : ComponentDialog
    {
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;
        private readonly ExternalServiceHelper _externalServiceHelper;


        public ParameterCollectionDialog(ExternalServiceHelper ExternalServiceHelper, IStatePropertyAccessor<UserProfile> userProfileAccessor
            )
            : base(nameof(ParameterCollectionDialog))
        {
            _userProfileAccessor = userProfileAccessor;
            _externalServiceHelper = ExternalServiceHelper;

            var waterfallSteps = new WaterfallStep[]
            {
            AskHelpQueryStepAsync,
            BeginParameterCollectionStepAsync,
            HandleUserActionStepAsync,
            SaveEditedTicketStepAsync
            };
            AddDialog(new QnAHandlingDialog(_externalServiceHelper));
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> AskHelpQueryStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as dynamic;

            if (stepContext.Options is string textResponse && !string.IsNullOrEmpty(textResponse))

            {

                return await stepContext.PromptAsync(
                   nameof(TextPrompt),
                   new PromptOptions { Prompt = MessageFactory.Text(textResponse) },
                   cancellationToken
               );

            }

            else if (options != null && (options.GetType().GetProperty("Action") != null))
            {
                // Pass the action value as a string to the next dialog
                return await stepContext.NextAsync(options?.Action, cancellationToken);
            }
            else if (options != null && (options.GetType().GetProperty("Message") != null))
            {
                // Pass the action value as a string to the next dialog
                return await stepContext.NextAsync(options?.Message, cancellationToken);
            }

            else
        {
            // Send a message but then proceed with the dialog logic
            await stepContext.Context.SendActivityAsync(MessageFactory.Text("Hello! How can I help you?"), cancellationToken);

            return Dialog.EndOfTurn;
        }
        }

        private async Task<DialogTurnResult> BeginParameterCollectionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var card = stepContext.Context.Activity.Value;

            // Retrieve the user query (response from the previous step)
            string userMessage = (string)stepContext.Result;

            var userProfile = await _userProfileAccessor.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            userProfile.ChatHistory ??= new List<ChatTransaction>();

            // Determine what to pass to HandleOpenAIResponseAsync
            string inputToAIService = card != null ? card.ToString() : userMessage;

            var (response, functionName, attachment) = await _externalServiceHelper.HandleOpenAIResponseAsync(inputToAIService, userProfile.ChatHistory, stepContext.Context);

            if (functionName == "createSupportTicket")
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment), cancellationToken);
                stepContext.Values["rowKey"] = response;
                return Dialog.EndOfTurn;
            }
            else if (functionName == "refine_query")
            {
                // Begin the QnAHandlingDialog and pass the response as dialog options
                return await stepContext.BeginDialogAsync(nameof(QnAHandlingDialog), new { Message = response }, cancellationToken);
            }

            else if (functionName == "GetLeaveStatus")
            {
                 if (!string.IsNullOrEmpty(response))
                {
                    await stepContext.Context.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    // Begin the QnAHandlingDialog and pass the response as dialog options
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }
                else {
                    await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment), cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }
                 
            }

            else if (functionName == "GetHolidaysList")
            {
                if (!string.IsNullOrEmpty(response))
                {
                    await stepContext.Context.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    // Begin the QnAHandlingDialog and pass the response as dialog options
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }
                else
                {
                    await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment), cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

            }

            else if (functionName == "createLeave")
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Your leave request has been submitted successfully and is pending approval."), cancellationToken);
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
                var ticket = await _externalServiceHelper.GetTicketAsync(rowKey.ToString());  // Pass the rowKey here

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
                await _externalServiceHelper.UpdateTicketAsync(rowKey, updatedTitle, updatedDescription);

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
    }

}