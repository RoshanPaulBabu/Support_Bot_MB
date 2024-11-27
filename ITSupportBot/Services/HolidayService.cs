using Azure;
using Azure.Data.Tables;
using ITSupportBot.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ITSupportBot.Services
{
    public class HolidayService
    {
        private readonly TableClient _tableClient;

        public HolidayService(string storageConnectionString)
        {
            var serviceClient = new TableServiceClient(storageConnectionString);
            _tableClient = serviceClient.GetTableClient("Holidays");
            _tableClient.CreateIfNotExists(); // Ensure the table exists
        }

        public async Task<List<Holiday>> GetHolidaysAfterDateAsync(string startDate)
        {
            var holidays = new List<Holiday>();
            var Date = DateTime.SpecifyKind(DateTime.Parse(startDate), DateTimeKind.Utc);

            try
            {
                // Query the table for holidays with a date greater than the specified startDate
                await foreach (var holiday in _tableClient.QueryAsync<Holiday>(h => h.Date > Date))
                {
                    holidays.Add(holiday);
                }
            }
            catch (RequestFailedException ex)
            {
                // Handle Azure Table Storage errors
                Console.WriteLine($"Error retrieving holidays: {ex.Message}");
                throw;
            }

            return holidays;
        }
    }
}
