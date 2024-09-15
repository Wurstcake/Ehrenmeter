using Microsoft.Data.SqlClient;
using System.Data;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace Ehrenmeter.Backend.Services
{
    interface IDbService
    {
        Task<int> RegisterUser(string username, string password, string email);
        Task<bool> AuthenticateUser(string username, string password);
        Task<int> GiveEhre(int giverId, int receiverId, int amount, string description);
        Task<IEnumerable<dynamic>> GetUserEhreHistory(int userId);
        Task<IEnumerable<dynamic>> GetTopUsers(int limit = 10);
    }
    internal class DbService(ILogger<DbService> logger, IPasswordService passwordService) : IDbService
    {
        private AccessToken _token;
        private readonly SqlConnection _connection = new Lazy<SqlConnection>(() => new SqlConnection(
                    ConfigService.GetConnectionString("AzureSqlConnectionString"))).Value;

        private async Task Connect()
        {
            while (_connection.State == ConnectionState.Open)
            {
                await Task.Delay(200);
            }

            if (DateTimeOffset.Now.Add(TimeSpan.FromSeconds(10)) < _token.ExpiresOn)
            {
                logger.LogInformation("Using existing token for Azure SQL database.");
                return;
            }

#if DEBUG
            var credential = new AzureDeveloperCliCredential();
#else
            var credential = new ManagedIdentityCredential();
#endif
            _token = credential.GetToken(new TokenRequestContext(new[] { "https://database.windows.net/.default" }));
            _connection.AccessToken = _token.Token;
        }

        public async Task<int> RegisterUser(string username, string password, string email)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                byte[] salt;
                string passwordHash = passwordService.HashPassword(password, out salt);

                var sql = @"
                INSERT INTO Users (Username, PasswordHash, Salt, Email)
                VALUES (@Username, @PasswordHash, @Salt, @Email);
                SELECT CAST(SCOPE_IDENTITY() as int)";

                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@PasswordHash", passwordHash);
                command.Parameters.AddWithValue("@Salt", salt);
                command.Parameters.AddWithValue("@Email", email);

                return (int)await command.ExecuteScalarAsync();
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<bool> AuthenticateUser(string username, string password)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = "SELECT PasswordHash, Salt FROM Users WHERE Username = @Username";
                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.AddWithValue("@Username", username);

                using SqlDataReader reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    string storedHash = reader.GetString(0);
                    byte[] salt = (byte[])reader["Salt"];
                    return passwordService.VerifyPassword(storedHash, salt, password);
                }

                return false;
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<int> GiveEhre(int giverId, int receiverId, int amount, string description)
        {
            await Connect();
            await _connection.OpenAsync();
            using SqlTransaction transaction = await _connection.BeginTransactionAsync();

            try
            {
                var giverEhreSql = "SELECT Ehre FROM Users WHERE UserId = @UserId";
                using SqlCommand giverCommand = new SqlCommand(giverEhreSql, _connection, transaction);
                giverCommand.Parameters.AddWithValue("@UserId", giverId);

                var giverEhre = (int)await giverCommand.ExecuteScalarAsync();
                if (giverEhre < amount)
                {
                    throw new InvalidOperationException("Not enough Ehre to give.");
                }

                var sql = @"
                INSERT INTO EhreTransactions (GiverId, ReceiverId, Amount, Description)
                VALUES (@GiverId, @ReceiverId, @Amount, @Description);
                UPDATE Users SET Ehre = Ehre - @Amount WHERE UserId = @GiverId;
                UPDATE Users SET Ehre = Ehre + @Amount WHERE UserId = @ReceiverId;
                SELECT CAST(SCOPE_IDENTITY() as int)";

                using SqlCommand command = new SqlCommand(sql, _connection, transaction);
                command.Parameters.AddWithValue("@GiverId", giverId);
                command.Parameters.AddWithValue("@ReceiverId", receiverId);
                command.Parameters.AddWithValue("@Amount", amount);
                command.Parameters.AddWithValue("@Description", description);

                var transactionId = (int)await command.ExecuteScalarAsync();
                await transaction.CommitAsync();

                return transactionId;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<IEnumerable<dynamic>> GetUserEhreHistory(int userId)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = @"
                SELECT t.TransactionId, u.Username as GiverName, t.Amount, t.Description, t.TransactionDate
                FROM EhreTransactions t
                JOIN Users u ON t.GiverId = u.UserId
                WHERE t.ReceiverId = @UserId
                ORDER BY t.TransactionDate DESC";

                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.AddWithValue("@UserId", userId);

                using SqlDataReader reader = await command.ExecuteReaderAsync();

                var history = new List<dynamic>();
                while (await reader.ReadAsync())
                {
                    history.Add(new
                    {
                        TransactionId = reader.GetInt32(0),
                        GiverName = reader.GetString(1),
                        Amount = reader.GetInt32(2),
                        Description = reader.GetString(3),
                        TransactionDate = reader.GetDateTime(4)
                    });
                }

                return history;
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<IEnumerable<dynamic>> GetTopUsers(int limit = 10)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = @"
                SELECT TOP (@Limit) UserId, Username, Ehre
                FROM Users
                ORDER BY Ehre DESC";

                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.AddWithValue("@Limit", limit);

                using SqlDataReader reader = await command.ExecuteReaderAsync();

                var topUsers = new List<dynamic>();
                while (await reader.ReadAsync())
                {
                    topUsers.Add(new
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        Ehre = reader.GetInt32(2)
                    });
                }

                return topUsers;
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<int> GetUserEhre(int userId)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = "SELECT Ehre FROM Users WHERE UserId = @UserId";
                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.AddWithValue("@UserId", userId);

                return (int)await command.ExecuteScalarAsync();
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }
    }

}
