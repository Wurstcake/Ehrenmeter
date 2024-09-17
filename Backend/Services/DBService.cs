using Microsoft.Data.SqlClient;
using System.Data;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Ehrenmeter.Backend.Models;

namespace Ehrenmeter.Backend.Services
{
    interface IDbService
    {
        Task<User?> RegisterUser(string username, string password);
        Task<(bool isAuthenticated, int userId)> AuthenticateUser(string username, string password);
        Task GiveEhre(int giverId, int receiverId, int amount, string description);
        Task<List<EhreTransaction>> GetUserEhreHistory(User receiver);
        Task<List<User>> GetTopUsers(int limit = 10);
        Task<User?> GetUser(int userId);
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

        public async Task<User?> RegisterUser(string username, string password)
        {
            await Connect();
            await _connection.OpenAsync();

            logger.LogInformation($"Creating user with username ${username}");

            try
            {
                byte[] salt;
                string passwordHash = passwordService.HashPassword(password, out salt);

                logger.LogInformation("Successfully hashed password");

                var sql = @"
                INSERT INTO dbo.Users (Username, PasswordHash, Salt, Ehre)
                VALUES (@Username, @PasswordHash, @Salt, @Ehre);
                SELECT CAST(SCOPE_IDENTITY() as int)";

                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.Add("@Username", SqlDbType.NVarChar).Value = username;
                command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar).Value = passwordHash;
                command.Parameters.Add("@Salt", SqlDbType.VarBinary).Value = salt;
                command.Parameters.Add("@Ehre", SqlDbType.Int).Value = 0;

                if (int.TryParse((await command.ExecuteScalarAsync())?.ToString(), out var userId))
                {
                    logger.LogInformation("Successfully created user");
                    var user = new User
                    {
                        UserId = userId,
                        Username = username,
                        Ehre = 0
                    };
                    return user;

                }
                return null;
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<(bool isAuthenticated, int userId)> AuthenticateUser(string username, string password)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = "SELECT UserId, PasswordHash, Salt FROM dbo.Users WHERE Username = @Username";
                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.Add("@Username", SqlDbType.NVarChar).Value = username;

                using SqlDataReader reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var userId = reader.GetInt32(0);
                    string storedHash = reader.GetString(1);
                    var salt = new byte[16];
                    reader.GetBytes(2, 0, salt, 0, salt.Length);
                    var isAuthenticated = passwordService.VerifyPassword(storedHash, salt, password);

                    logger.LogInformation($"Authenticated user {username} with id {userId}");

                    return (isAuthenticated, userId);
                }

                return (false, 0);
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task GiveEhre(int giverId, int receiverId, int amount, string description)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = @"
                INSERT INTO EhreTransactions (GiverId, ReceiverId, Amount, Description)
                VALUES (@GiverId, @ReceiverId, @Amount, @Description);
                UPDATE Users SET Ehre = Ehre + @Amount WHERE UserId = @ReceiverId;";

                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.Add("@GiverId", SqlDbType.Int).Value = giverId;
                command.Parameters.Add("@ReceiverId", SqlDbType.Int).Value = receiverId;
                command.Parameters.Add("@Amount", SqlDbType.Int).Value = amount;
                command.Parameters.Add("@Description", SqlDbType.NVarChar).Value = description;

                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<List<EhreTransaction>> GetUserEhreHistory(User receiver)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = @"
                SELECT u.UserId as GiverId, u.Username as GiverName, t.Amount, t.Description, t.TransactionDate
                FROM EhreTransactions t
                JOIN Users u ON t.GiverId = u.UserId
                WHERE t.ReceiverId = @ReceiverId
                ORDER BY t.TransactionDate DESC";

                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.Add("@ReceiverId", SqlDbType.Int).Value = receiver.UserId;

                using SqlDataReader reader = await command.ExecuteReaderAsync();

                logger.LogInformation($"Retrieved transactions for receiver {receiver.UserId}");

                var history = new List<EhreTransaction>();
                while (await reader.ReadAsync())
                {
                    var transaction = new EhreTransaction()
                    {
                        Giver = new User
                        {
                            UserId = reader.GetInt32(0),
                            Username = reader.GetString(1)
                        },
                        Receiver = receiver,
                        Amount = reader.GetInt32(2),
                        Description = reader.GetString(3),
                        TransactionDate = reader.GetDateTime(4)
                    };

                    history.Add(transaction);
                }

                return history;
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<List<User>> GetTopUsers(int limit = 10)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = @"
                SELECT TOP (@Limit) UserId, Username, Ehre
                FROM dbo.Users
                ORDER BY Ehre DESC";

                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;

                using SqlDataReader reader = await command.ExecuteReaderAsync();

                var topUsers = new List<User>();
                while (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        Ehre = reader.GetInt32(2)
                    };
                    topUsers.Add(user);
                }
                return topUsers;

            }
            finally
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<User?> GetUser(int userId)
        {
            await Connect();
            await _connection.OpenAsync();

            try
            {
                var sql = "SELECT Username, Ehre FROM Users WHERE UserId = @UserId";
                using SqlCommand command = new SqlCommand(sql, _connection);
                command.Parameters.AddWithValue("@UserId", userId);

                var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new User
                    {
                        UserId = userId,
                        Username = reader.GetString(0),
                        Ehre = reader.GetInt32(1)
                    };
                }

                return null;
            }
            finally
            {
                await _connection.CloseAsync();
            }
        }
    }

}
