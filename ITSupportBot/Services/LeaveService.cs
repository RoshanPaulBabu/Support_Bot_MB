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

        // Get all leaves for a specific employee by EmpID
        public async Task<List<Leave>> GetLeavesByEmpIDAsync(string empID)
        {
            var queryResults = _tableClient.QueryAsync<Leave>(l => l.EmpID == empID);

            var leaves = new List<Leave>();
            await foreach (var leave in queryResults)
            {
                leaves.Add(leave);
            }

            return leaves;
        }

        // Update leaves for an employee by EmpID and RowKey
        public async Task UpdateLeaveByEmpIDAsync(string empID, string rowKey, string updatedLeaveType, DateTime updatedStartDate, DateTime updatedEndDate, string updatedReason, string updatedStatus)
        {
            var leave = await _tableClient.GetEntityAsync<Leave>("EmployeeLeaves", rowKey);

            if (leave != null && leave.Value.EmpID == empID)
            {
                var updatedLeave = leave.Value;
                updatedLeave.LeaveType = updatedLeaveType;
                updatedLeave.StartDate = updatedStartDate;
                updatedLeave.EndDate = updatedEndDate;
                updatedLeave.Reason = updatedReason;
                updatedLeave.Status = updatedStatus;

                await _tableClient.UpdateEntityAsync(updatedLeave, updatedLeave.ETag, TableUpdateMode.Replace);
            }
            else
            {
                throw new Exception("Leave not found for the specified employee.");
            }
        }

        // Update the status of a leave by EmpID and RowKey
        public async Task UpdateLeaveStatusByEmpIDAsync(string empID, string rowKey, string updatedStatus)
        {
            var leave = await _tableClient.GetEntityAsync<Leave>("EmployeeLeaves", rowKey);

            if (leave != null && leave.Value.EmpID == empID)
            {
                var updatedLeave = leave.Value;
                updatedLeave.Status = updatedStatus;

                await _tableClient.UpdateEntityAsync(updatedLeave, updatedLeave.ETag, TableUpdateMode.Replace);
            }
            else
            {
                throw new Exception("Leave not found for the specified employee.");
            }
        }
    }
}
