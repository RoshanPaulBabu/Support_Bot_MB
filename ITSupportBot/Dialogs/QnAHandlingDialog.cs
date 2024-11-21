using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder;
using System;
using System.Threading.Tasks;
using System.Threading;
using ITSupportBot.Models;
using ITSupportBot.Services;

namespace ITSupportBot.Dialogs
{
    public class QnAHandlingDialog : ComponentDialog
    { 

        private readonly AzureOpenAIService _AzureOpenAIService;
        public QnAHandlingDialog(AzureOpenAIService AzureOpenAIService) : base(nameof(QnAHandlingDialog))
        {

            _AzureOpenAIService = AzureOpenAIService ?? throw new ArgumentNullException(nameof(AzureOpenAIService));
            // Define the dialog steps
            var waterfallSteps = new WaterfallStep[]
            {
                HandlePolicyMessageStepAsync
            };

            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> HandlePolicyMessageStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Retrieve the passed message
            var options = stepContext.Options as dynamic;
            string message = options?.Message ?? "No message provided.";

            string response = await _AzureOpenAIService.HandleQueryRefinement(message);
            // Send the message to the user
            await stepContext.Context.SendActivityAsync(MessageFactory.Text(response), cancellationToken);

            // End the dialog after sending the message
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }
    }
}
