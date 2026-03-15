using System;
using LiteDB;

namespace Cognexalgo.Core.Data.Entities
{
    /// <summary>LiteDB document that stores a serialised CalendarStrategy session.</summary>
    public class PersistedCalendarSession
    {
        [BsonId]
        public string StrategyName { get; set; } = "";
        public string ConfigJson   { get; set; } = "";
        public string StateJson    { get; set; } = "";
        public DateTime SavedAt    { get; set; }
    }
}
