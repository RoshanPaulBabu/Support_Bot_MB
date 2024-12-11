using Microsoft.Bot.Builder;
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
        private readonly AdaptiveCardHelper _AdaptiveCardHelper;

        public MainDialog(UserState userState, ExternalServiceHelper ExternalServiceHelper, AdaptiveCardHelper AdaptiveCardHelper)
        : base(nameof(MainDialog))
        {
            _userProfileAccessor = userState.CreateProperty<UserProfile>("UserProfile");
            _externalServiceHelper = ExternalServiceHelper;
            _AdaptiveCardHelper = AdaptiveCardHelper;

            var waterfallSteps = new WaterfallStep[]
        {   WelcomeStepAsync,
            FurtherIndentStepAsync,
            AskForFurtherAssistanceStepAsync,
            HandleFurtherAssistanceStepAsync,
            ThankYouStepAsync
        };

            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            AddDialog(new QnAHandlingDialog(_externalServiceHelper));
            AddDialog(new ParameterCollectionDialog( _externalServiceHelper, _userProfileAccessor, _AdaptiveCardHelper));
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

        private async Task<DialogTurnResult> FurtherIndentStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {

            await stepContext.Context.SendActivityAsync(MessageFactory.Text(" "), cancellationToken);

            return Dialog.EndOfTurn; 
        }

        private async Task<DialogTurnResult> AskForFurtherAssistanceStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the user query (response from the previous step)
            string userMessage = (string)stepContext.Result;

            var indent = await _externalServiceHelper.IndentHandlingAsync(userMessage);

            var jsonObject = JObject.Parse(indent);

            // Extract the response value
            string responseValue = jsonObject["response"].ToString();

            if (responseValue == "YES")
            {
                var adaptiveCard = File.ReadAllText("Cards/GreetingCard.json");

                // Send the Adaptive Card to the user
                var response = new Attachment
                {
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Content = JsonConvert.DeserializeObject(adaptiveCard)
                };

                await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(response), cancellationToken);
                return Dialog.EndOfTurn; // Wait for the user's response to the card actions

            };
            // Load the Adaptive Card

            return await stepContext.NextAsync(null, cancellationToken);

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
            string message = stepContext.Context.Activity.AsMessageActivity().Text;

            userProfile.ChatHistory.Add(new ChatTransaction("Is there anything I can assist you with? ", null));


            if (value != null && value.ContainsKey("action"))
            {
                var action = value["action"].ToString();
                    // Restart the dialog for any other action
                return await stepContext.ReplaceDialogAsync(InitialDialogId, new { Action = action }, cancellationToken);
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