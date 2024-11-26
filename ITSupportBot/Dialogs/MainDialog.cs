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
   
    namespace ITSupportBot.Dialogs
{
public class MainDialog : ComponentDialog
{
        private readonly IStatePropertyAccessor<UserProfile> _userProfileAccessor;
        private readonly AzureOpenAIService _AzureOpenAIService;
        private readonly TicketService _ITSupportService;
        private readonly AzureSearchService _AzureSearchService;

        public MainDialog(UserState userState, AzureOpenAIService AzureOpenAIService, TicketService ITSupportService, AzureSearchService AzureSearchService)
        : base(nameof(MainDialog))
    {
        _userProfileAccessor = userState.CreateProperty<UserProfile>("UserProfile");
        _AzureOpenAIService = AzureOpenAIService;
        _ITSupportService = ITSupportService;
        _AzureSearchService = AzureSearchService;

            var waterfallSteps = new WaterfallStep[]
        {   WelcomeStepAsync,
            ThankYouStepAsync,
        };

        AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
        AddDialog(new QnAHandlingDialog(_AzureSearchService, _AzureOpenAIService));
        AddDialog(new ParameterCollectionDialog(_AzureOpenAIService, _userProfileAccessor, _ITSupportService, _AzureSearchService));
        InitialDialogId = nameof(WaterfallDialog);
    }
    
    private async Task<DialogTurnResult> WelcomeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.BeginDialogAsync(nameof(ParameterCollectionDialog), null, cancellationToken);
        }

    private async Task<DialogTurnResult> ThankYouStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync(MessageFactory.Text("Thank you for using IT Support Bot!"), cancellationToken);
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

    }
}