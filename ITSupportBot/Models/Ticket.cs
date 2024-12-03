using Azure;
using Azure.Data.Tables;
using System;

namespace ITSupportBot.Models
{
    public class Ticket : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public string EmpID { get; set; }
        public string EmpName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }

        public Ticket() { }

        public Ticket(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }
    }
}
