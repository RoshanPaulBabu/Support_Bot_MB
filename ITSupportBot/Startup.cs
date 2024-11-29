using Microsoft.EntityFrameworkCore;
using ITSupportBot.Bots;
using ITSupportBot.Dialogs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ITSupportBot.Services;
using Microsoft.Extensions.Configuration;
using ITSupportBot.Models;
using ITSupportBot.Helpers;

namespace ITSupportBot
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient().AddControllers().AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.MaxDepth = HttpHelper.BotMessageSerializerSettings.MaxDepth;
            });

            // Create the Bot Framework Authentication to be used with the Bot Adapter.
            services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

            // Create the Bot Adapter with error handling enabled.
            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            // Create the storage for User and Conversation state.
            services.AddSingleton<IStorage, MemoryStorage>();

            // Register UserState and ConversationState
            services.AddSingleton<UserState>();
            services.AddSingleton<ConversationState>();

            // Register IStatePropertyAccessor for UserProfile
            services.AddSingleton<IStatePropertyAccessor<UserProfile>>(provider =>
            {
                var userState = provider.GetService<UserState>();
                return userState.CreateProperty<UserProfile>("UserProfile");
            });

            // Register dialogs
            services.AddSingleton<MainDialog>();
            services.AddSingleton<QnAHandlingDialog>();
            services.AddSingleton<ParameterCollectionDialog>();

            // Register bot
            services.AddTransient<IBot, DialogAndWelcomeBot<MainDialog>>();

            // Register services
            services.AddSingleton<AzureOpenAIService>();
            services.AddSingleton<AzureSearchService>();

            services.AddSingleton<ExternalServiceHelper>();

            services.AddSingleton(provider =>
            {
                string storageConnectionString = _configuration.GetConnectionString("TableString");
                return new LeaveService(storageConnectionString);
            });

            // Register ITSupportService with configuration
            services.AddSingleton(provider =>
            {
                string storageConnectionString = _configuration.GetConnectionString("TableString");
                return new TicketService(storageConnectionString);
            });

            services.AddSingleton(provider =>
            {
                string storageConnectionString = _configuration.GetConnectionString("TableString");
                return new HolidayService(storageConnectionString);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles()
               .UseStaticFiles()
               .UseWebSockets()
               .UseRouting()
               .UseAuthorization()
               .UseEndpoints(endpoints =>
               {
                   endpoints.MapControllers();
               });

            // Uncomment this line if you want to enforce HTTPS
            // app.UseHttpsRedirection();
        }
    }
}
