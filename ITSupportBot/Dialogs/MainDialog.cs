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
            return await stepContext.BeginDialogAsync(nameof(ParameterCollectionDialog), null, cancellationToken);
        }

        private async Task<DialogTurnResult> AskForFurtherAssistanceStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {

            return await stepContext.PromptAsync(nameof(ChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Is there anything else I can assist you with?"),
                    Choices = ChoiceFactory.ToChoices(new List<string> { "Yes", "No" }),
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> HandleFurtherAssistanceStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the user's choice
            var choice = ((FoundChoice)stepContext.Result).Value;

            if (choice == "Yes")
            {
                // Restart the dialog
                return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
            }
            else
            {
                // Proceed to the thank-you message
                return await stepContext.NextAsync(null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> ThankYouStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync(MessageFactory.Text("Thank you for using IT Support Bot!"), cancellationToken);
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

    }

}