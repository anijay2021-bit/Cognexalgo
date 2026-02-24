using System.Threading.Tasks;
using Cognexalgo.Core.Database;
using Cognexalgo.Core.Models;
using Dapper;
using System.Linq;

namespace Cognexalgo.Core.Repositories
{
    public class CredentialsRepository
    {
        private readonly DatabaseService _db;

        public CredentialsRepository(DatabaseService db)
        {
            _db = db;
        }

        public async Task<BrokerCredentials> GetAsync()
        {
            using (var conn = _db.GetConnection())
            {
                var sql = "SELECT * FROM Credentials ORDER BY Id DESC LIMIT 1";
                return await conn.QueryFirstOrDefaultAsync<BrokerCredentials>(sql);
            }
        }

        public async Task SaveAsync(BrokerCredentials creds)
        {
            using (var conn = _db.GetConnection())
            {
                // Simple logic: Delete all and insert new (Single User/Single Broker for now)
                await conn.ExecuteAsync("DELETE FROM Credentials");
                
                var sql = @"
                    INSERT INTO Credentials (BrokerName, ApiKey, ClientCode, Password, TotpKey)
                    VALUES (@BrokerName, @ApiKey, @ClientCode, @Password, @TotpKey)";
                
                await conn.ExecuteAsync(sql, creds);
            }
        }
    }
}
