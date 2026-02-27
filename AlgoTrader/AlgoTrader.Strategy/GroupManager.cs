using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlgoTrader.Core.Models;
using AlgoTrader.Core.Interfaces;
using AlgoTrader.Core.Enums;
using AlgoTrader.Data.Repositories;
using AlgoTrader.Data.LiteDb;

namespace AlgoTrader.Strategy;

public class StrategyGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<Guid> StrategyIds { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
}

public class GroupManager
{
    private readonly IStrategyEngine _engine;
    private readonly StrategyRepository _repo;
    private readonly LiteDbContext _db;
    
    public GroupManager(IStrategyEngine engine, StrategyRepository repo, LiteDbContext db)
    {
        _engine = engine;
        _repo = repo;
        _db = db;
    }
    
    public LiteDB.ILiteCollection<StrategyGroup> GetCollection() 
        => _db.GetCollection<StrategyGroup>("StrategyGroups");
    
    public IEnumerable<StrategyGroup> GetAllGroups() => GetCollection().FindAll();

    public void UpsertGroup(StrategyGroup group) => GetCollection().Upsert(group);

    public async Task StartGroupAsync(Guid groupId)
    {
        var group = GetCollection().FindById(groupId);
        if (group == null) return;
        
        foreach (var stratId in group.StrategyIds)
        {
            var config = _repo.FindById(stratId);
            if (config != null && !config.IsActive)
            {
                await _engine.StartStrategyAsync(stratId);
            }
        }
    }
    
    public async Task StopGroupAsync(Guid groupId)
    {
        var group = GetCollection().FindById(groupId);
        if (group == null) return;
        
        foreach (var stratId in group.StrategyIds)
        {
            var config = _repo.FindById(stratId);
            if (config != null && config.IsActive)
            {
                await _engine.StopStrategyAsync(stratId);
            }
        }
    }
    
    public async Task ExitGroupAsync(Guid groupId)
    {
        var group = GetCollection().FindById(groupId);
        if (group == null) return;
        
        foreach (var stratId in group.StrategyIds)
        {
            var config = _repo.FindById(stratId);
            if (config != null && config.State == StrategyState.ENTERED)
            {
                await _engine.ExitStrategyAsync(stratId, ExitReason.Manual);
            }
        }
    }
}
