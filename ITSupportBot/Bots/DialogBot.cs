// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.22.0

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace ITSupportBot.Bots
{
    // This IBot implementation can run any type of Dialog. The use of type parameterization is to allows multiple different bots
    // to be run at different endpoints within the same project. This can be achieved by defining distinct Controller types
    // each with dependency on distinct IBot types, this way ASP Dependency Injection can glue everything together without ambiguity.
    // The ConversationState is used by the Dialog system. The UserState isn't, however, it might have been used in a Dialog implementation,
    // and the requirement is that all BotState objects are saved at the end of a turn.
    public class DialogBot<T> : ActivityHandler
        where T : Dialog
    {
#pragma warning disable SA1401 // Fields should be private
        protected readonly Dialog Dialog;
        protected readonly BotState ConversationState;
        protected readonly BotState UserState;
        protected readonly ILogger Logger;
#pragma warning restore SA1401 // Fields should be private

        public DialogBot(ConversationState conversationState, UserState userState, T dialog, ILogger<DialogBot<T>> logger)
        {
            ConversationState = conversationState;
            UserState = userState;
            Dialog = dialog;
            Logger = logger;
        }

        public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            await base.OnTurnAsync(turnContext, cancellationToken);

            // Save any state changes that might have occurred during the turn.
            await ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            await UserState.SaveChangesAsync(turnContext, false, cancellationToken);
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Running dialog with Message Activity.");

            // Retrieve Teams user information
            var userId = turnContext.Activity.From.Id;
            TeamsChannelAccount member = null;

            try
            {
                member = await TeamsInfo.GetMemberAsync(turnContext, userId, cancellationToken);
            }
            catch (ErrorResponseException e)
            {
                // Handle error (e.g., user not found in the current team)
                Logger.LogError($"Error retrieving Teams member info: {e.Message}");
            }

            // Pass user details to Dialog if needed
            if (member != null)
            {
                turnContext.TurnState.Add("TeamsUserEmail", member.Email);
                turnContext.TurnState.Add("TeamsUserName", member.Name);
                turnContext.TurnState.Add("TeamsUserId", member.AadObjectId); // Azure AD Object ID
            }

            // Run the Dialog with the new message Activity
            await Dialog.RunAsync(turnContext, ConversationState.CreateProperty<DialogState>("DialogState"), cancellationToken);
        }

    }
}
