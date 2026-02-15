using System.Collections.Generic;
using System.Threading.Tasks;
using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Repositories
{
    public interface IStrategyRepository
    {
        // Legacy methods (can restrict or remove later)
        // Legacy methods (required for compatibility)
        Task<IEnumerable<StrategyConfig>> GetAllAsync();
        Task<IEnumerable<StrategyConfig>> GetAllActiveAsync();
        Task AddAsync(StrategyConfig strategy);
        Task UpdateAsync(StrategyConfig strategy);
        Task DeleteAsync(int id);
        Task UpdateStatusAsync(int id, bool isActive);

        // Hybrid Strategy CRUD
        Task<HybridStrategyConfig> GetHybridStrategyAsync(int id);
        Task<HybridStrategyConfig> GetHybridStrategyByNameAsync(string name);
        Task<List<HybridStrategyConfig>> GetAllHybridStrategiesAsync();
        Task<List<HybridStrategyConfig>> GetActiveHybridStrategiesAsync();
        
        Task<int> SaveHybridStrategyAsync(HybridStrategyConfig config, string modifiedBy = null);
        Task<bool> UpdateHybridStrategyAsync(HybridStrategyConfig config, string modifiedBy = null);
        Task<bool> DeleteHybridStrategyAsync(int id);
        
        // Versioning
        Task<List<HybridStrategyConfig>> GetStrategyHistoryAsync(int id);
        Task<HybridStrategyConfig> GetStrategyVersionAsync(int id, int version);
        
        // Activation
        Task<bool> ActivateStrategyAsync(int id);
        Task<bool> DeactivateStrategyAsync(int id);
    }
}
