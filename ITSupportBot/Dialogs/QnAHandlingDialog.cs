using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder;
using System;
using System.Threading.Tasks;
using System.Threading;
using ITSupportBot.Models;
using ITSupportBot.Services;
using ITSupportBot.Helpers;

namespace ITSupportBot.Dialogs
{
    public class QnAHandlingDialog : ComponentDialog
    {
        private readonly ExternalServiceHelper _externalServiceHelper;
        public QnAHandlingDialog(ExternalServiceHelper ExternalServiceHelper) : base(nameof(QnAHandlingDialog))
        {

            _externalServiceHelper = ExternalServiceHelper;
            // Define the dialog steps
            var waterfallSteps = new WaterfallStep[]
            {
                PerformSearchAsync
            };

            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> PerformSearchAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as dynamic;
            var query = options?.Message ?? "No message provided.";

            if (string.IsNullOrEmpty(query))
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Query cannot be empty or null."), cancellationToken);
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            // Perform the search
            var result = await _externalServiceHelper.PerformSearchAsync(query);

            if (result != null)
            {
                var refinedQuery = await _externalServiceHelper.RefineSearchResultAsync(query, result.Content);
                await stepContext.Context.SendActivityAsync(refinedQuery);
            }

            else
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("No results found for your query."), cancellationToken);
            }

            return await stepContext.EndDialogAsync(query, cancellationToken);
        }

        private Task<DialogTurnResult> RefineSearchAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return stepContext.EndDialogAsync(null, cancellationToken);
        }
    }
}
