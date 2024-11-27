using Azure;
using Azure.Data.Tables;
using System;

namespace ITSupportBot.Models
{
    public class Holiday : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public string HolidayName { get; set; }
        public DateTime Date { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }

        // Parameterless constructor for Table SDK deserialization
        public Holiday() { }

        // Constructor for initializing key fields
        public Holiday(string partitionKey, string rowKey, string holidayName, DateTime date)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
            HolidayName = holidayName;
            Date = date;
        }
    }
}
