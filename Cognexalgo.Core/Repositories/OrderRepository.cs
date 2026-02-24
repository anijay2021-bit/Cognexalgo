using System.Threading.Tasks;
using Cognexalgo.Core.Database;
using Cognexalgo.Core.Models;
using Microsoft.Data.Sqlite;

namespace Cognexalgo.Core.Repositories
{
    public interface IOrderRepository
    {
        Task AddAsync(Order order);
        Task<System.Collections.Generic.List<Order>> GetAllAsync();
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
        
        public async Task<System.Collections.Generic.List<Order>> GetAllAsync()
        {
            var result = new System.Collections.Generic.List<Order>();
            using (var connection = _dbService.GetConnection())
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT OrderId, StrategyId, StrategyName, Symbol, TransactionType, Qty, Price, Status, Timestamp FROM Orders ORDER BY Timestamp DESC";
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var o = new Order
                        {
                            OrderId = reader.GetString(0),
                            StrategyId = reader.GetInt32(1),
                            StrategyName = !reader.IsDBNull(2) ? reader.GetString(2) : "",
                            Symbol = reader.GetString(3),
                            TransactionType = reader.GetString(4),
                            Qty = reader.GetInt32(5),
                            Price = reader.GetDouble(6),
                            Status = reader.GetString(7),
                            Timestamp = reader.GetDateTime(8)
                        };
                        result.Add(o);
                    }
                }
            }
            return result;
        }
    }
}
