﻿using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ITSupportBot.Services;
using ITSupportBot.Models;
using System.IO;
using System.Net.Sockets;
using Microsoft.Bot.Builder.Dialogs.Choices;
using ITSupportBot.Helpers;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
namespace ITSupportBot.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;
        private readonly ExternalServiceHelper _externalServiceHelper;

        public MainDialog(UserState userState, ExternalServiceHelper ExternalServiceHelper)
        : base(nameof(MainDialog))
        {
            _userProfileAccessor = userState.CreateProperty<UserProfile>("UserProfile");
            _externalServiceHelper = ExternalServiceHelper;

            var waterfallSteps = new WaterfallStep[]
        {   WelcomeStepAsync,
            AskForFurtherAssistanceStepAsync,
            HandleFurtherAssistanceStepAsync,
            ThankYouStepAsync
        };

            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            AddDialog(new QnAHandlingDialog(_externalServiceHelper));
            AddDialog(new ParameterCollectionDialog( _externalServiceHelper, _userProfileAccessor));
            AddDialog(new ChoicePrompt(nameof(ChoicePrompt)));
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> WelcomeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as dynamic;
            if (options != null && (options.GetType().GetProperty("Message") != null))
            {
                return await stepContext.BeginDialogAsync(nameof(ParameterCollectionDialog), new { options?.Message }, cancellationToken);
            }
            else if (options != null && (options.GetType().GetProperty("Action") != null))
            {
                return await stepContext.BeginDialogAsync(nameof(ParameterCollectionDialog), new { Action = options?.Action }, cancellationToken);
            }
            return await stepContext.BeginDialogAsync(nameof(ParameterCollectionDialog), null, cancellationToken);
        }

        private async Task<DialogTurnResult> AskForFurtherAssistanceStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Load the Adaptive Card
            var adaptiveCard = File.ReadAllText("Cards/GreetingCard.json");

            // Send the Adaptive Card to the user
            var response = new Attachment
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCard)
            };

            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(response), cancellationToken);

            return Dialog.EndOfTurn; // Wait for the user's response to the card actions
        }

        private async Task<DialogTurnResult> HandleFurtherAssistanceStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the user profile
            var userProfile = await _userProfileAccessor.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

            // Ensure ChatHistory is initialized if it's null
            userProfile.ChatHistory ??= new List<ChatTransaction>();

            // Perform dialog steps here...

            // Clear chat history after dialog set finishes
            userProfile.ChatHistory.Clear();
            // Get the user's response
            var value = stepContext.Context.Activity.Value as JObject;
            string message = (string)stepContext.Result;

            if (value != null && value.ContainsKey("action"))
            {
                var action = value["action"].ToString();

                if (action == "No")
                {
                    return await stepContext.NextAsync(null, cancellationToken);
                }
                else
                {
                    // Restart the dialog for any other action
                    return await stepContext.ReplaceDialogAsync(InitialDialogId, new { Action = action }, cancellationToken);
                }
            }
            else if (!string.IsNullOrEmpty(message))
            {
                return await stepContext.ReplaceDialogAsync(InitialDialogId, new { Message = message }, cancellationToken);

            }

                // Handle unexpected scenarios
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("I couldn't understand your choice. Please try again."), cancellationToken);
            return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
        }


        private async Task<DialogTurnResult> ThankYouStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync(MessageFactory.Text("Thank you for using IT Support Bot!"), cancellationToken);
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

    }

}