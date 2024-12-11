using AdaptiveCards;
using ITSupportBot.Models;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;


namespace ITSupportBot.Helpers
{
    public class AdaptiveCardHelper
    {
        public Attachment CreateAdaptiveCardAttachment(string cardFileName, Dictionary<string, string> placeholders = null)
        {
            // Locate the resource path for the card file
            var resourcePath = GetType().Assembly.GetManifestResourceNames()
                                .FirstOrDefault(name => name.EndsWith(cardFileName, StringComparison.OrdinalIgnoreCase));

            if (resourcePath == null)
            {
                throw new FileNotFoundException($"The specified card file '{cardFileName}' was not found as an embedded resource.");
            }

            using (var stream = GetType().Assembly.GetManifestResourceStream(resourcePath))
            using (var reader = new StreamReader(stream))
            {
                // Read the card template
                var adaptiveCard = reader.ReadToEnd();

                // Replace placeholders dynamically if provided
                if (placeholders != null)
                {
                    foreach (var placeholder in placeholders)
                    {
                        adaptiveCard = adaptiveCard.Replace($"${{{placeholder.Key}}}", placeholder.Value);
                    }
                }

                // Return the populated adaptive card as an attachment
                return new Attachment
                {
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Content = JsonConvert.DeserializeObject(adaptiveCard, new JsonSerializerSettings { MaxDepth = null }),
                };
            }
        }

        public Attachment GenerateCategorizedLeaveStatusCard(IEnumerable<Leave> leaveRecords)
        {
            var pendingLeaves = leaveRecords.Where(l => l.Status == "Pending");
            var approvedLeaves = leaveRecords.Where(l => l.Status == "Approved");
            var rejectedLeaves = leaveRecords.Where(l => l.Status == "Rejected");

            var card = new AdaptiveCard("1.3")
            {
                Body = new List<AdaptiveElement>
        {
            new AdaptiveTextBlock
            {
                Text = "Leave Status",
                Weight = AdaptiveTextWeight.Bolder,
                Size = AdaptiveTextSize.Large
            },
            new AdaptiveActionSet
            {
                Actions = new List<AdaptiveAction>
                {
                    new AdaptiveToggleVisibilityAction
                    {
                        Title = "Pending ⏳",
                        TargetElements = new List<AdaptiveTargetElement>
                        {
                            new AdaptiveTargetElement { ElementId = "PendingSection" }
                        }
                    },
                    new AdaptiveToggleVisibilityAction
                    {
                        Title = "Approved ✅",
                        TargetElements = new List<AdaptiveTargetElement>
                        {
                            new AdaptiveTargetElement { ElementId = "ApprovedSection" }
                        }
                    },
                    new AdaptiveToggleVisibilityAction
                    {
                        Title = "Rejected ❌",
                        TargetElements = new List<AdaptiveTargetElement>
                        {
                            new AdaptiveTargetElement { ElementId = "RejectedSection" }
                        }
                    }
                }
            }
        }
            };

            // Add categorized sections after the toggle buttons
            AddLeaveSection(card, "PendingSection", "**Pending Leaves**", pendingLeaves);
            AddLeaveSection(card, "ApprovedSection", "**Approved Leaves**", approvedLeaves);
            AddLeaveSection(card, "RejectedSection", "**Rejected Leaves**", rejectedLeaves);

            return new Attachment
            {
                ContentType = AdaptiveCard.ContentType,
                Content = card
            };
        }






        private void AddLeaveSection(AdaptiveCard card, string sectionId, string sectionTitle, IEnumerable<Leave> leaves)
        {
            var section = new AdaptiveContainer
            {
                Id = sectionId,
                IsVisible = false,
                Items = new List<AdaptiveElement>
        {
            new AdaptiveTextBlock
            {
                Text = sectionTitle,
                Weight = AdaptiveTextWeight.Bolder,
                Size = AdaptiveTextSize.Medium,
                Spacing = AdaptiveSpacing.Medium
            }
        }
            };

            if (leaves.Any())
            {
                // Add table header using ColumnSet
                section.Items.Add(new AdaptiveColumnSet
                {
                    Columns = new List<AdaptiveColumn>
            {
                new AdaptiveColumn
                {
                    Items = new List<AdaptiveElement>
                    {
                        new AdaptiveTextBlock { Text = "**Created At**", Weight = AdaptiveTextWeight.Bolder, Wrap = true }
                    },
                    Width = "stretch"
                },
                new AdaptiveColumn
                {
                    Items = new List<AdaptiveElement>
                    {
                        new AdaptiveTextBlock { Text = "**Type**", Weight = AdaptiveTextWeight.Bolder, Wrap = true }
                    },
                    Width = "stretch"
                },
                new AdaptiveColumn
                {
                    Items = new List<AdaptiveElement>
                    {
                        new AdaptiveTextBlock { Text = "**Start Date**", Weight = AdaptiveTextWeight.Bolder, Wrap = true }
                    },
                    Width = "stretch"
                },
                new AdaptiveColumn
                {
                    Items = new List<AdaptiveElement>
                    {
                        new AdaptiveTextBlock { Text = "**End Date**", Weight = AdaptiveTextWeight.Bolder, Wrap = true }
                    },
                    Width = "stretch"
                }
            }
                });

                // Add rows for each leave record
                foreach (var leave in leaves)
                {
                    section.Items.Add(new AdaptiveColumnSet
                    {
                        Columns = new List<AdaptiveColumn>
                {
                    new AdaptiveColumn
                    {
                        Items = new List<AdaptiveElement>
                        {
                            new AdaptiveTextBlock { Text = leave.CreatedAt.ToString("yyyy-MM-dd"), Wrap = true }
                        },
                        Width = "stretch"
                    },
                    new AdaptiveColumn
                    {
                        Items = new List<AdaptiveElement>
                        {
                            new AdaptiveTextBlock { Text = leave.LeaveType, Wrap = true }
                        },
                        Width = "stretch"
                    },
                    new AdaptiveColumn
                    {
                        Items = new List<AdaptiveElement>
                        {
                            new AdaptiveTextBlock { Text = leave.StartDate.ToString("yyyy-MM-dd"), Wrap = true }
                        },
                        Width = "stretch"
                    },
                    new AdaptiveColumn
                    {
                        Items = new List<AdaptiveElement>
                        {
                            new AdaptiveTextBlock { Text = leave.EndDate.ToString("yyyy-MM-dd"), Wrap = true }
                        },
                        Width = "stretch"
                    }
                }
                    });
                }
            }
            else
            {
                section.Items.Add(new AdaptiveTextBlock
                {
                    Text = "No records found.",
                    Wrap = true,
                    Spacing = AdaptiveSpacing.Medium
                });
            }

            card.Body.Add(section);
        }



        public Attachment CreateHolidaysAdaptiveCardAsync(List<Holiday> holidays)
        {
            // Create a new Adaptive Card
            var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 3))
            {
                Body = new List<AdaptiveElement>()
            };

            // Add Heading
            card.Body.Add(new AdaptiveTextBlock
            {
                Text = "Upcoming Holidays",
                Size = AdaptiveTextSize.Large,
                Weight = AdaptiveTextWeight.Bolder,
                Spacing = AdaptiveSpacing.Large
            });

            // Create a table column set
            var columnSet = new AdaptiveColumnSet();

            // Define columns
            var holidayNameColumn = new AdaptiveColumn
            {
                Width = "stretch",
                Items = new List<AdaptiveElement>
        {
            new AdaptiveTextBlock
            {
                Text = "**Holiday**",
                Weight = AdaptiveTextWeight.Bolder
            }
        }
            };

            var holidayDateColumn = new AdaptiveColumn
            {
                Width = "stretch",
                Items = new List<AdaptiveElement>
        {
            new AdaptiveTextBlock
            {
                Text = "**Date**",
                Weight = AdaptiveTextWeight.Bolder
            }
        }
            };

            // Add header columns
            columnSet.Columns = new List<AdaptiveColumn> { holidayNameColumn, holidayDateColumn };
            card.Body.Add(columnSet);

            // Populate rows dynamically
            foreach (var holiday in holidays)
            {
                var rowColumnSet = new AdaptiveColumnSet();

                var nameColumn = new AdaptiveColumn
                {
                    Width = "stretch",
                    Items = new List<AdaptiveElement>
            {
                new AdaptiveTextBlock
                {
                    Text = holiday.HolidayName,
                    Wrap = true
                }
            }
                };

                var dateColumn = new AdaptiveColumn
                {
                    Width = "stretch",
                    Items = new List<AdaptiveElement>
            {
                new AdaptiveTextBlock
                {
                    Text = holiday.Date.ToString("dddd, MMMM dd, yyyy"),
                    Wrap = true
                }
            }
                };

                rowColumnSet.Columns = new List<AdaptiveColumn> { nameColumn, dateColumn };
                card.Body.Add(rowColumnSet);
            }

            // Add a fallback text for accessibility
            card.FallbackText = "List of Holidays";

            // Create the attachment
            var attachment = new Attachment
            {
                ContentType = AdaptiveCard.ContentType,
                Content = card
            };

            return attachment;
        }
    }
}
