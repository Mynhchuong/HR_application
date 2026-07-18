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
        cmd.InitialLOBFetchSize = -1;

        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                p.Value ??= DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        using var reader = (OracleDataReader)await cmd.ExecuteReaderAsync();
        while (reader.Read())
        {
            list.Add(map(reader));
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

        //using var transaction = conn.BeginTransaction();
        using var cmd = new OracleCommand(sql, conn);
        //cmd.Transaction = transaction;
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

        // try
        // {
        //     int result = await cmd.ExecuteNonQueryAsync();
        //     await transaction.CommitAsync();
        //     return result;
        // }
        // catch
        // {
        //     await transaction.RollbackAsync();
        //     throw;
        // }
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

    public static int ConvertToInt(object value)
    {
        if (value == null || value is DBNull) return 0;
        if (value is Oracle.ManagedDataAccess.Types.OracleDecimal od)
        {
            return od.IsNull ? 0 : decimal.ToInt32(od.Value);
        }
        return Convert.ToInt32(value);
    }

    // ============================================================
    // 🔥 TRANSACTION-SHARING SUPPORT
    // Dùng khi cần nhiều lệnh (SELECT FOR UPDATE, INSERT/UPDATE nhiều bước)
    // chạy chung 1 connection/transaction thật — thay vì mỗi ExecuteNonQueryAsync/
    // ExecuteQueryAsync ở trên tự mở 1 connection riêng (không share transaction được).
    // Dùng: using var scope = await db.BeginTransactionAsync();
    //       await db.ExecuteNonQueryAsync(scope, sql, ...);
    //       await scope.CommitAsync();   // hoặc RollbackAsync() — Dispose tự rollback nếu quên.
    // ============================================================
    public async Task<OracleTxScope> BeginTransactionAsync()
    {
        var conn = new OracleConnection(_connStr);
        await conn.OpenAsync();
        var tx = conn.BeginTransaction();
        return new OracleTxScope(conn, tx);
    }

    public async Task<int> ExecuteNonQueryAsync(OracleTxScope scope, string sql, params OracleParameter[] parameters)
    {
        using var cmd = new OracleCommand(sql, scope.Connection);
        cmd.Transaction = scope.Transaction;
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

    public async Task<List<T>> ExecuteQueryAsync<T>(OracleTxScope scope, string sql, Func<OracleDataReader, T> map, params OracleParameter[] parameters)
    {
        var list = new List<T>();

        using var cmd = new OracleCommand(sql, scope.Connection);
        cmd.Transaction = scope.Transaction;
        cmd.BindByName = true;
        cmd.InitialLOBFetchSize = -1;

        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                p.Value ??= DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        using var reader = (OracleDataReader)await cmd.ExecuteReaderAsync();
        while (reader.Read())
        {
            list.Add(map(reader));
        }

        return list;
    }

    // Bulk (array-bind) trong cùng 1 transaction chia sẻ — dùng cho MERGE/INSERT nhiều dòng
    // cần atomic chung với các lệnh khác trong cùng scope (vd merge + cleanup rồi mới commit).
    public async Task<int> ExecuteBulkInsertAsync(OracleTxScope scope, string sql, int arrayBindCount, params OracleParameter[] parameters)
    {
        using var cmd = new OracleCommand(sql, scope.Connection);
        cmd.Transaction = scope.Transaction;
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

// Scope nhỏ gọn cho 1 connection + 1 transaction dùng chung nhiều lệnh.
// IAsyncDisposable: nếu Commit/Rollback chưa được gọi tường minh thì Dispose sẽ tự Rollback
// (an toàn — tránh treo transaction mở nếu code giữa chừng throw).
public sealed class OracleTxScope : IAsyncDisposable
{
    public OracleConnection Connection { get; }
    public OracleTransaction Transaction { get; }
    private bool _completed;

    internal OracleTxScope(OracleConnection conn, OracleTransaction tx)
    {
        Connection = conn;
        Transaction = tx;
    }

    public async Task CommitAsync()
    {
        await Transaction.CommitAsync();
        _completed = true;
    }

    public async Task RollbackAsync()
    {
        if (_completed) return;
        await Transaction.RollbackAsync();
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try { await Transaction.RollbackAsync(); }
            catch { /* connection có thể đã đóng — bỏ qua */ }
        }
        Transaction.Dispose();
        try { await Connection.CloseAsync(); } catch { /* ignore */ }
        Connection.Dispose();
    }
}
