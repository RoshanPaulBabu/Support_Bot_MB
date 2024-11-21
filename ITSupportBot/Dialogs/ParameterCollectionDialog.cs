﻿using ITSupportBot.Services;
using Microsoft.Bot.Builder.Dialogs;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using ITSupportBot.Models;
using Microsoft.Bot.Builder;
using System;
using Microsoft.Bot.Schema;
using System.IO;

namespace ITSupportBot.Dialogs
{
    public class ParameterCollectionDialog : ComponentDialog
    {
        private readonly AzureOpenAIService _AzureOpenAIService;
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;
        private readonly ITSupportService _ITSupportService;

        public ParameterCollectionDialog(AzureOpenAIService AzureOpenAIService, IStatePropertyAccessor<UserProfile> userProfileAccessor, ITSupportService iTSupportService)
            : base(nameof(ParameterCollectionDialog))
        {
            _AzureOpenAIService = AzureOpenAIService ?? throw new ArgumentNullException(nameof(AzureOpenAIService));
            _userProfileAccessor = userProfileAccessor ?? throw new ArgumentNullException(nameof(userProfileAccessor));
            _ITSupportService = iTSupportService;

            var waterfallSteps = new WaterfallStep[]
            {
            AskHelpQueryStepAsync,
            BeginParameterCollectionStepAsync,
            HandleUserActionStepAsync,
            SaveEditedTicketStepAsync
            };
            AddDialog(new QnAHandlingDialog(_AzureOpenAIService));
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
                return await stepContext.PromptAsync(
                    nameof(TextPrompt),
                    new PromptOptions { Prompt = MessageFactory.Text("Hello! How can I help you today?") },
                    cancellationToken
                );

            }
        }

        private async Task<DialogTurnResult> BeginParameterCollectionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the user query (response from the previous step)
            string userMessage = (string)stepContext.Result;

            // Get user profile and initialize chat history if necessary
            var userProfile = await _userProfileAccessor.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            userProfile.ChatHistory ??= new List<ChatTransaction>();

            // Get the response from the AI service
            string response = await _AzureOpenAIService.HandleOpenAIResponseAsync(userMessage, userProfile.ChatHistory);

            // Check if the process is complete (e.g., user query has been successfully processed)
            if (response.Contains("RowKey"))
            {
                string rowKey = response.Substring(response.IndexOf("RowKey:") + 7).Trim();
                // Simulate fetching a ticket from the database
                var ticket = await _ITSupportService.GetTicketAsync(rowKey);

                // Load and customize the adaptive card template
                string cardJson = File.ReadAllText("Cards/ticketCreationCard.json");
                cardJson = cardJson.Replace("${title}", ticket.Title)
                                   .Replace("${description}", ticket.Description)
                                   .Replace("${createdAt}", ticket.CreatedAt.ToString("MM/dd/yyyy HH:mm"))
                                   .Replace("${ticketId}", ticket.RowKey);

                // Create and send the adaptive card
                var adaptiveCardAttachment = new Attachment
                {
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Content = Newtonsoft.Json.JsonConvert.DeserializeObject(cardJson)
                };

                var reply = MessageFactory.Attachment(adaptiveCardAttachment);
                await stepContext.Context.SendActivityAsync(reply, cancellationToken);

                stepContext.Values["rowKey"] = rowKey;

                // End the dialog after successfully processing the ticket
                return Dialog.EndOfTurn;
            }
            else if (response == userMessage)
            {
                // Begin the QnAHandlingDialog and pass the response as dialog options
                return await stepContext.BeginDialogAsync(nameof(QnAHandlingDialog), new { Message = response }, cancellationToken);
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

            await stepContext.Context.SendActivityAsync(MessageFactory.Text("Unexpected action. Please try again."), cancellationToken);
            return Dialog.EndOfTurn;
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

    }

}