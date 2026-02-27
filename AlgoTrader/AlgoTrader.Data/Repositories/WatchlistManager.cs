using System;
using System.Collections.Generic;
using System.Linq;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Enums;
using AlgoTrader.Data.Repositories;

namespace AlgoTrader.Data.Repositories;

public class WatchlistItem
{
    public string Symbol { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public Exchange Exchange { get; set; }
}

public class Watchlist
{
    [LiteDB.BsonId]
    public string Name { get; set; } = string.Empty;
    public List<WatchlistItem> Items { get; set; } = new();
}

public class WatchlistManager
{
    private readonly LiteDb.LiteDbContext _db;
    
    public WatchlistManager(LiteDb.LiteDbContext db)
    {
        _db = db;
    }
    
    private LiteDB.ILiteCollection<Watchlist> Collection => _db.GetCollection<Watchlist>("Watchlists");

    public Watchlist CreateWatchlist(string name)
    {
        var existing = Collection.FindById(name);
        if (existing != null) return existing;
        
        var wl = new Watchlist { Name = name };
        Collection.Insert(wl);
        return wl;
    }

    public void AddSymbol(string watchlistName, string symbol, string token, Exchange exchange)
    {
        var wl = Collection.FindById(watchlistName);
        if (wl == null) return;
        
        if (!wl.Items.Any(i => i.Token == token))
        {
            wl.Items.Add(new WatchlistItem { Symbol = symbol, Token = token, Exchange = exchange });
            Collection.Update(wl);
        }
    }

    public void RemoveSymbol(string watchlistName, string token)
    {
        var wl = Collection.FindById(watchlistName);
        if (wl == null) return;
        
        var item = wl.Items.FirstOrDefault(i => i.Token == token);
        if (item != null)
        {
            wl.Items.Remove(item);
            Collection.Update(wl);
        }
    }

    public List<Watchlist> GetAll()
    {
        return Collection.FindAll().ToList();
    }
}
