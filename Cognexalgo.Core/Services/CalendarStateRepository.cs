using System;
using System.IO;
using LiteDB;
using Newtonsoft.Json;
using Cognexalgo.Core.Data.Entities;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Services
{
    public interface ICalendarStateRepository
    {
        void    Save(CalendarStrategyConfig cfg, CalendarStrategyState state);
        (CalendarStrategyConfig? cfg, CalendarStrategyState? state) Load(string strategyName);
        void    Delete(string strategyName);
        bool    Exists(string strategyName);
    }

    /// <summary>
    /// Persists CalendarStrategy session state to the shared LiteDB file
    /// (same HistoryCache.db used by candle storage — Connection=shared is safe).
    /// </summary>
    public class CalendarStateRepository : ICalendarStateRepository, IDisposable
    {
        private readonly LiteDatabase _db;

        private ILiteCollection<PersistedCalendarSession> Col =>
            _db.GetCollection<PersistedCalendarSession>("calendar_sessions");

        public CalendarStateRepository(string dbPath = null)
        {
            var path = dbPath
                ?? Path.Combine(AppContext.BaseDirectory, "Data", "HistoryCache.db");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _db = new LiteDatabase($"Filename={path};Connection=shared");
        }

        public void Save(CalendarStrategyConfig cfg, CalendarStrategyState state)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None };
            var doc = new PersistedCalendarSession
            {
                StrategyName = cfg.Name,
                ConfigJson   = JsonConvert.SerializeObject(cfg,    settings),
                StateJson    = JsonConvert.SerializeObject(state,  settings),
                SavedAt      = DateTime.Now
            };
            Col.Upsert(doc);
        }

        public (CalendarStrategyConfig? cfg, CalendarStrategyState? state) Load(string strategyName)
        {
            var doc = Col.FindById(strategyName);
            if (doc == null) return (null, null);
            return (
                JsonConvert.DeserializeObject<CalendarStrategyConfig>(doc.ConfigJson),
                JsonConvert.DeserializeObject<CalendarStrategyState>(doc.StateJson)
            );
        }

        public void Delete(string strategyName) => Col.Delete(strategyName);

        public bool Exists(string strategyName) => Col.Exists(x => x.StrategyName == strategyName);

        public void Dispose() => _db?.Dispose();
    }
}
