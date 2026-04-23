using Microsoft.Data.SqlClient;

namespace S300CRE_to_SI.Source;

public class DatabaseConnection : IDisposable
{
    private readonly string _connectionString;
    private SqlConnection? _connection;

    public DatabaseConnection(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqlConnection GetConnection(string? databaseName = null)
    {
        if (_connection == null || _connection.State == System.Data.ConnectionState.Closed)
        {
            _connection = new SqlConnection(_connectionString);
            _connection.Open();

            if (!string.IsNullOrEmpty(databaseName))
                _connection.ChangeDatabase(databaseName);
        }
        return _connection;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
