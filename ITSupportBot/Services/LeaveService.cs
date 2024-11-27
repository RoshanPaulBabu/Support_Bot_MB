using Azure;
using Azure.Data.Tables;
using ITSupportBot.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ITSupportBot.Services
{

    public class LeaveService
    {
        private readonly TableClient _tableClient;

        public LeaveService(string storageConnectionString)
        {
            var serviceClient = new TableServiceClient(storageConnectionString);
            _tableClient = serviceClient.GetTableClient("Leaves");
            _tableClient.CreateIfNotExists(); // Ensure the table exists
        }

        // Save Leave with default status as "Pending"
        public async Task SaveLeaveAsync(string empID, string empName, string leaveType, string startDate, string endDate, string reason, string rowKey)
        {
            var leave = new Leave("EmployeeLeaves", rowKey)
            {
                EmpID = empID,
                EmpName = empName,
                LeaveType = leaveType,
                StartDate = DateTime.SpecifyKind(DateTime.Parse(startDate), DateTimeKind.Utc),
                EndDate = DateTime.SpecifyKind(DateTime.Parse(endDate), DateTimeKind.Utc),
                Reason = reason,
                Status = "Pending", // Default status
                CreatedAt = DateTime.UtcNow
            };

            await _tableClient.AddEntityAsync(leave);
        }

        public async Task<Leave> GetLatestLeaveStatusAsync(string empID)
        {
            try
            {
                // Fetch all leave records for the given EmpID
                var leaveRecords = new List<Leave>();

                await foreach (var leave in _tableClient.QueryAsync<Leave>(leave => leave.EmpID == empID))
                {
                    leaveRecords.Add(leave);
                }

                // Find the latest leave by sorting in memory
                var latestLeave = leaveRecords
                    .OrderByDescending(leave => leave.CreatedAt)
                    .FirstOrDefault();

                return latestLeave;
            }
            catch (RequestFailedException ex)
            {
                // Handle Azure Table Storage errors
                Console.WriteLine($"Error retrieving leave records: {ex.Message}");
                throw;
            }
        }
    }
}
