using System.Threading.Tasks;
using Cognexalgo.Core.Database;
using Cognexalgo.Core.Models;
using Microsoft.Data.Sqlite;

namespace Cognexalgo.Core.Repositories
{
    public interface IOrderRepository
    {
        Task AddAsync(Order order);
    }

    public class OrderRepository : IOrderRepository
    {
        private readonly DatabaseService _dbService;

        public OrderRepository(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        public async Task AddAsync(Order order)
        {
            using (var connection = _dbService.GetConnection())
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Orders (OrderId, StrategyId, Symbol, TransactionType, Qty, Price, Status, Timestamp, StrategyName)
                    VALUES ($orderId, $strategyId, $symbol, $type, $qty, $price, $status, $timestamp, $stratName)
                ";
                
                command.Parameters.AddWithValue("$orderId", order.OrderId);
                command.Parameters.AddWithValue("$strategyId", order.StrategyId);
                command.Parameters.AddWithValue("$symbol", order.Symbol);
                command.Parameters.AddWithValue("$type", order.TransactionType);
                command.Parameters.AddWithValue("$qty", order.Qty);
                command.Parameters.AddWithValue("$price", order.Price);
                command.Parameters.AddWithValue("$status", order.Status);
                command.Parameters.AddWithValue("$timestamp", order.Timestamp);
                command.Parameters.AddWithValue("$stratName", order.StrategyName ?? "");

                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
