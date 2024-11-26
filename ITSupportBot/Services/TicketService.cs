using Azure.Data.Tables;
using ITSupportBot.Models;
using System;
using System.Threading.Tasks;

namespace ITSupportBot.Services
{
    public class TicketService
    {
        private readonly TableClient _tableClient;

        public TicketService(string storageConnectionString)
        {
            var serviceClient = new TableServiceClient(storageConnectionString);
            _tableClient = serviceClient.GetTableClient("Tickets");
            _tableClient.CreateIfNotExists(); // Ensure the table exists
        }

        public async Task SaveTicketAsync(string title, string description, string RowKey)
        {
            var ticket = new Ticket("SupportTickets", RowKey)
            {
                Title = title,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            await _tableClient.AddEntityAsync(ticket);
        }

        public async Task<Ticket> GetTicketAsync(string rowKey)
        {
            var ticket = await _tableClient.GetEntityAsync<Ticket>("SupportTickets", rowKey);
            return ticket.Value;
        }



        public async Task UpdateTicketAsync(string rowKey, string updatedTitle, string updatedDescription)
        {
            var ticket = await _tableClient.GetEntityAsync<Ticket>("SupportTickets", rowKey);

            if (ticket != null)
            {
                var updatedTicket = ticket.Value;
                updatedTicket.Title = updatedTitle;
                updatedTicket.Description = updatedDescription;

                await _tableClient.UpdateEntityAsync(updatedTicket, updatedTicket.ETag, TableUpdateMode.Replace);
            }
            else
            {
                throw new Exception("Ticket not found");
            }
        }
    }
}
