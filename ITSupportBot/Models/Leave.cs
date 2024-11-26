using Azure;
using Azure.Data.Tables;
using System;

namespace ITSupportBot.Models
{
    public class Leave : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public string EmpID { get; set; }
        public string EmpName { get; set; }
        public string LeaveType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; } // e.g., "Pending", "Approved", "Rejected"
        public DateTime CreatedAt { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }

        // Parameterless constructor for Table SDK deserialization
        public Leave() { }

        // Constructor for initializing key fields
        public Leave(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }
    }
} 
