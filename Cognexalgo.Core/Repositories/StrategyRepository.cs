using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Cognexalgo.Core.Data;
using Cognexalgo.Core.Data.Entities;
using Cognexalgo.Core.Models;
using Newtonsoft.Json;

namespace Cognexalgo.Core.Repositories
{
    public class StrategyRepository : IStrategyRepository
    {
        private readonly AlgoDbContext _context;

        public StrategyRepository(AlgoDbContext context)
        {
            _context = context;
        }

        // Legacy Methods Implementation
        public async Task<IEnumerable<StrategyConfig>> GetAllAsync() 
        {
            var hybrid = await GetAllHybridStrategiesAsync();
            return hybrid.Select(h => new StrategyConfig 
            { 
               Id = h.Id, 
               Name = h.Name, 
               IsActive = h.IsActive, 
               StrategyType = h.StrategyType, 
               Parameters = h.Parameters,
               Symbol = h.Legs.FirstOrDefault()?.Index ?? "NIFTY"
            });
        }

        public async Task<IEnumerable<StrategyConfig>> GetAllActiveAsync()
        {
            var active = await GetActiveHybridStrategiesAsync();
            return active.Select(h => new StrategyConfig 
            { 
               Id = h.Id, 
               Name = h.Name, 
               IsActive = h.IsActive, 
               StrategyType = h.StrategyType, 
               Parameters = h.Parameters,
               Symbol = h.Legs.FirstOrDefault()?.Index ?? "NIFTY"
            });
        }

        public async Task AddAsync(StrategyConfig strategy) { }
        public async Task UpdateAsync(StrategyConfig strategy) { }
        public async Task DeleteAsync(int id) { }
        public async Task UpdateStatusAsync(int id, bool isActive) 
        {
             var entity = await _context.HybridStrategies.FindAsync(id);
             if (entity != null)
             {
                 entity.IsActive = isActive;
                 await _context.SaveChangesAsync();
             }
        }

        // Hybrid Strategy Implementation

        public async Task<HybridStrategyConfig> GetHybridStrategyAsync(int id)
        {
            var entity = await _context.HybridStrategies.FindAsync(id);
            if (entity == null) return null;

            return DeserializeConfig(entity);
        }

        public async Task<HybridStrategyConfig> GetHybridStrategyByNameAsync(string name)
        {
            var entity = await _context.HybridStrategies
                .FirstOrDefaultAsync(s => s.Name == name);
                
            if (entity == null) return null;

            return DeserializeConfig(entity);
        }

        public async Task<List<HybridStrategyConfig>> GetAllHybridStrategiesAsync()
        {
            var entities = await _context.HybridStrategies.ToListAsync();
            return entities.Select(DeserializeConfig).ToList();
        }

        public async Task<List<HybridStrategyConfig>> GetActiveHybridStrategiesAsync()
        {
            var entities = await _context.HybridStrategies
                .Where(s => s.IsActive)
                .ToListAsync();
            return entities.Select(DeserializeConfig).ToList();
        }

        public async Task<int> SaveHybridStrategyAsync(HybridStrategyConfig config, string modifiedBy = null)
        {
            // Use Newtonsoft.Json
            var json = JsonConvert.SerializeObject(config);
            
            var existingEntity = await _context.HybridStrategies
                .FirstOrDefaultAsync(s => s.Name == config.Name);

            if (existingEntity != null)
            {
                // Update existing
                existingEntity.ConfigJson = json;
                existingEntity.LastModified = DateTime.UtcNow;
                existingEntity.LastModifiedBy = modifiedBy ?? "System";
                existingEntity.Version++;
                existingEntity.IsActive = config.IsActive; // Sync active status
            }
            else
            {
                // Create new
                var newEntity = new HybridStrategyEntity
                {
                    Name = config.Name,
                    ConfigJson = json,
                    IsActive = config.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    LastModified = DateTime.UtcNow,
                    CreatedBy = modifiedBy ?? "System",
                    LastModifiedBy = modifiedBy ?? "System",
                    Version = 1
                };
                _context.HybridStrategies.Add(newEntity);
            }

            await _context.SaveChangesAsync();
            return existingEntity?.Id ?? _context.HybridStrategies.Local.Last().Id;
        }

        public async Task<bool> UpdateHybridStrategyAsync(HybridStrategyConfig config, string modifiedBy = null)
        {
            // SaveHybridStrategyAsync handles both update and create
            await SaveHybridStrategyAsync(config, modifiedBy);
            return true;
        }

        public async Task<bool> DeleteHybridStrategyAsync(int id)
        {
            var entity = await _context.HybridStrategies.FindAsync(id);
            if (entity == null) return false;

            _context.HybridStrategies.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<HybridStrategyConfig>> GetStrategyHistoryAsync(int id)
        {
            // For now, we only keep the latest version in the main table.
            var strategy = await GetHybridStrategyAsync(id);
            return strategy != null ? new List<HybridStrategyConfig> { strategy } : new List<HybridStrategyConfig>();
        }

        public async Task<HybridStrategyConfig> GetStrategyVersionAsync(int id, int version)
        {
            return await GetHybridStrategyAsync(id);
        }

        public async Task<bool> ActivateStrategyAsync(int id)
        {
            var entity = await _context.HybridStrategies.FindAsync(id);
            if (entity == null) return false;

            entity.IsActive = true;
            entity.LastModified = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeactivateStrategyAsync(int id)
        {
            var entity = await _context.HybridStrategies.FindAsync(id);
            if (entity == null) return false;

            entity.IsActive = false;
            entity.LastModified = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        private HybridStrategyConfig DeserializeConfig(HybridStrategyEntity entity)
        {
            // Use Newtonsoft.Json
            var config = JsonConvert.DeserializeObject<HybridStrategyConfig>(entity.ConfigJson);
            if (config != null)
            {
                config.Id = entity.Id; // [Added] Populate ID from Entity
                config.IsActive = entity.IsActive; 
            }
            return config;
        }
    }
}
