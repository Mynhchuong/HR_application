using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Data;

public class OracleService
{
    private readonly string _connStr;
    private readonly IConfiguration _configuration;

    public OracleService(IConfiguration configuration)
    {
        _configuration = configuration;
        _connStr = _configuration.GetConnectionString("OracleDb_VSTI") 
                   ?? throw new InvalidOperationException("Connection string 'OracleDb_VSTI' not found.");
    }

    // ============================================================
    // 🔥 ASYNC QUERY
    // ============================================================
    public async Task<List<T>> ExecuteQueryAsync<T>(string sql, Func<OracleDataReader, T> map, params OracleParameter[] parameters)
    {
        var list = new List<T>();

        using var conn = new OracleConnection(_connStr);
        await conn.OpenAsync();

        using var cmd = new OracleCommand(sql, conn);
        cmd.BindByName = true;

        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                p.Value ??= DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(map((OracleDataReader)reader));
        }

        return list;
    }

    // ============================================================
    // 🔥 ASYNC NON QUERY
    // ============================================================
    public async Task<int> ExecuteNonQueryAsync(string sql, params OracleParameter[] parameters)
    {
        using var conn = new OracleConnection(_connStr);
        await conn.OpenAsync();

        using var cmd = new OracleCommand(sql, conn);
        cmd.BindByName = true;

        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                p.Value ??= DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        return await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    // 🔥 ASYNC PROCEDURE
    // ============================================================
    public async Task<int> ExecuteProcedureAsync(string procedureName, params OracleParameter[] parameters)
    {
        using var conn = new OracleConnection(_connStr);
        await conn.OpenAsync();

        using var cmd = new OracleCommand(procedureName, conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.BindByName = true;

        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                p.Value ??= DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        return await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    // 🔥 ASYNC BULK INSERT
    // ============================================================
    public async Task<int> ExecuteBulkInsertAsync(string sql, int arrayBindCount, params OracleParameter[] parameters)
    {
        using var conn = new OracleConnection(_connStr);
        await conn.OpenAsync();
        
        using var cmd = new OracleCommand(sql, conn);
        cmd.BindByName = true;
        cmd.ArrayBindCount = arrayBindCount;

        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                cmd.Parameters.Add(p);
            }
        }

        return await cmd.ExecuteNonQueryAsync();
    }
}
