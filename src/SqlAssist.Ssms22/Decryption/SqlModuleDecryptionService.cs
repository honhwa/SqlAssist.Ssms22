using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Decryption;
using SqlAssist.Core.Notifications;
using SqlAssist.Ssms22.Settings;

namespace SqlAssist.Ssms22.Decryption;

/// <summary>
/// 讀回以 <c>WITH ENCRYPTION</c> 建立的模組本文，供 F12 與物件結構使用。
/// </summary>
/// <remarks>
/// 做法要 ALTER 物件，因此每一條規則都從那裡來：ALTER 與 ROLLBACK 在同一批；成功與失敗都依物件版本記憶，
/// 避免每次預覽都重新拿 Sch-M 鎖；全處理序同時只開一條 DAC；驗證不過的文字一律不交出去。
/// </remarks>
internal static class SqlModuleDecryptionService
{
    /// <summary>
    /// 一個執行個體只允許一條 DAC；不同查詢視窗的背景工作必須排隊。
    /// 用 Monitor 而不是 SemaphoreSlim：這支只在背景執行緒上等，而且一定要有逾時；
    /// 平台把 SemaphoreSlim 列為禁用，就是因為它在 UI 執行緒上等待會掛起 SSMS。
    /// </summary>
    private static readonly object DacGate = new();

    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<string, string> Memo = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> Failures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> UnavailableServers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, TransientSlot> Transient = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>「別人正在用 DAC」只記幾秒；「沒有 sysadmin」才記到工作階段結束。</summary>
    private static readonly TimeSpan BusyMemo = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UnreachableMemo = TimeSpan.FromMinutes(2);
    private static readonly int[] BusyRetryDelaysMs = { 400, 1200 };

    private sealed class TransientSlot
    {
        public DateTime UntilUtc;
        public string Message = "";
    }

    public static bool Enabled => SqlAssistSettingsStore.Current.DecryptEncryptedModules;

    /// <summary>最近一輪的結論；沒有其他出口時至少診斷記錄說得出做了什麼。</summary>
    public static string? LastSummary { get; private set; }

    public static void ClearCache()
    {
        Memo.Clear();
        Failures.Clear();
        UnavailableServers.Clear();
        Transient.Clear();
        LastSummary = null;
    }

    public static string ServerOf(string? connectionString)
    {
        try
        {
            return new SqlConnectionStringBuilder(connectionString ?? "").DataSource;
        }
        catch
        {
            return connectionString ?? "";
        }
    }

    /// <summary>已经在记忆里的答案；呼叫端靠它跳过重复的 DAC 与重复的通知。</summary>
    public static bool TryGetKnown(
        string server,
        string database,
        EncryptedModule module,
        out string? definition,
        out string? failure)
    {
        var key = MemoKey(server, database, module);
        if (Memo.TryGetValue(key, out definition))
        {
            failure = null;
            return true;
        }

        if (Failures.TryGetValue(key, out failure))
        {
            definition = null;
            return true;
        }

        definition = null;
        return false;
    }

    public static ModuleDecryptionResult Decrypt(
        string? connectionString,
        string database,
        IReadOnlyList<EncryptedModule> modules,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = DecryptCore(connectionString, database, modules, progress, cancellationToken);
        if (modules is { Count: > 0 })
        {
            LastSummary = result.DacError is { Length: > 0 } error
                ? NotificationCatalog.DecryptionFailure(result.Definitions.Count, modules.Count, error)
                : result.Warnings.Count > 0
                    ? NotificationCatalog.DecryptionFailure(result.Definitions.Count, modules.Count, result.Warnings[0])
                    : NotificationCatalog.DecryptionSummary(result.Definitions.Count, modules.Count);
        }

        return result;
    }

    private static ModuleDecryptionResult DecryptCore(
        string? connectionString,
        string database,
        IReadOnlyList<EncryptedModule> modules,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = new ModuleDecryptionResult();
        if (modules is null || modules.Count == 0) return result;
        if (!Enabled)
        {
            result.DacError = DecryptionText.DacNoConnection;
            return result;
        }

        var server = ServerOf(connectionString);

        // 已解密或已知讀不出來的物件直接回答，不開連線、不再 ALTER。
        var outstanding = new List<EncryptedModule>();
        foreach (var module in modules)
        {
            if (TryGetKnown(server, database, module, out var known, out var knownFailure))
            {
                if (known is not null) result.Definitions[module.Key] = known;
                else result.Warnings.Add(module.Key + ": " + knownFailure);
            }
            else
            {
                outstanding.Add(module);
            }
        }

        if (outstanding.Count == 0) return result;

        // 背景執行緒是呼叫端的責任；這裡是讓忘了的呼叫端只是錯、而不是把 SSMS 卡死的那一道。
        if (ThreadHelper.CheckAccess())
        {
            result.DacError = DecryptionText.DacUiThread;
            return result;
        }

        if (UnavailableServers.TryGetValue(server, out var knownUnavailable))
        {
            result.DacError = knownUnavailable;
            return result;
        }

        if (Transient.TryGetValue(server, out var transient) && transient.UntilUtc > DateTime.UtcNow)
        {
            result.DacError = transient.Message;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var gateTaken = false;
        Monitor.TryEnter(DacGate, GateTimeout, ref gateTaken);
        if (!gateTaken)
        {
            result.DacError = DecryptionText.DacGateTimeout;
            return result;
        }

        try
        {
            using var dac = OpenDac(connectionString, database, cancellationToken);

            foreach (var module in outstanding)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(module.Key);

                try
                {
                    var definition = DecryptOne(dac, module);
                    if (definition is null)
                    {
                        Fail(server, database, module, result, DecryptionText.DecryptInvalidDefinition(module.Name));
                        continue;
                    }

                    Memo[MemoKey(server, database, module)] = definition;
                    result.Definitions[module.Key] = definition;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Fail(server, database, module, result, RootMessage(ex));
                }
            }
        }
        catch (DacUnavailableException ex)
        {
            result.DacError = ex.Message;
            if (ex.Permanent)
                UnavailableServers[server] = ex.Message;
            else
                Transient[server] = new TransientSlot
                {
                    UntilUtc = DateTime.UtcNow.Add(SqlDacConnectionFactory.IsBusy(ex) ? BusyMemo : UnreachableMemo),
                    Message = ex.Message,
                };
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
        }
        finally
        {
            Monitor.Exit(DacGate);
        }

        return result;
    }

    /// <summary>忙碌的埠最多重試兩次，其餘失敗一次就回答：沒有 sysadmin 重試三次只是多付三次逾時。</summary>
    private static SqlConnection OpenDac(string? connectionString, string database, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return SqlDacConnectionFactory.Open(
                    connectionString ?? "", database, probeHolder: attempt == BusyRetryDelaysMs.Length);
            }
            catch (DacUnavailableException ex) when (attempt < BusyRetryDelaysMs.Length && SqlDacConnectionFactory.IsBusy(ex))
            {
                Thread.Sleep(BusyRetryDelaysMs[attempt]);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>單一物件；解密不了時 <paramref name="error"/> 說出原因。</summary>
    public static string? DecryptSingle(
        string? connectionString,
        string database,
        EncryptedModule module,
        out string? error)
    {
        error = null;
        if (module is null) throw new ArgumentNullException(nameof(module));

        module.Database = database;
        var result = Decrypt(connectionString, database, new[] { module });
        if (result.Definitions.TryGetValue(module.Key, out var definition)) return definition;

        error = result.DacError ?? (result.Warnings.Count > 0 ? result.Warnings[0] : null);
        return null;
    }

    /// <summary>
    /// 列出這個資料庫所有加密模組。<c>sys.sql_modules.definition IS NULL</c> 只有一個意思：模組加密；
    /// 沒有 VIEW DEFINITION 權限拿到的是沒有列，不是 NULL。
    /// </summary>
    public static List<EncryptedModule> ListEncryptedModules(IDbConnection connection, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT s.name AS schema_name, o.name AS object_name, o.type, o.modify_date,
       ps.name AS parent_schema, po.name AS parent_name
FROM sys.sql_modules m
JOIN sys.objects o ON m.object_id = o.object_id
JOIN sys.schemas s ON o.schema_id = s.schema_id
LEFT JOIN sys.objects po ON o.parent_object_id = po.object_id
LEFT JOIN sys.schemas ps ON po.schema_id = ps.schema_id
WHERE m.definition IS NULL AND o.is_ms_shipped = 0";

        var modules = new List<EncryptedModule>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            modules.Add(new EncryptedModule
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                ObjectType = reader.GetString(2).Trim(),
                ModifyDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                ParentSchema = reader.IsDBNull(4) ? null : reader.GetString(4),
                ParentName = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }

        return modules;
    }

    /// <summary>讀原密文→ ALTER 假定義並在同批再讀→回復→XOR。</summary>
    private static string? DecryptOne(SqlConnection dac, EncryptedModule module)
    {
        var original = ReadCipher(dac, module, null, out _);
        if (original is null || original.Length < 2)
            throw new InvalidOperationException(DecryptionText.DecryptNoCipher);

        var body = SqlEncryptedModuleCrypto.BuildDummyBody(
            module.ObjectType, module.Schema, module.Name, module.ParentSchema, module.ParentName, original.Length / 2);
        if (body is null)
            throw new InvalidOperationException(DecryptionText.DecryptUnsupportedType(module.ObjectType));

        // 用 ALTER 執行、用 CREATE 的明文當 XOR 遮罩；這是整件事唯一不能弄反的地方。
        var dummyCipher = ReadCipher(dac, module, SqlEncryptedModuleCrypto.AlterStatement(body), out var recheckedOriginal);
        var definition = SqlEncryptedModuleCrypto.Xor(
            recheckedOriginal ?? original, dummyCipher, SqlEncryptedModuleCrypto.StoredPlaintext(body));

        return SqlEncryptedModuleCrypto.LooksLikeModuleDefinition(definition, module.Name) ? definition : null;
    }

    /// <summary>
    /// 一次來回。ALTER 與 ROLLBACK 同一批，密文放進資料表變數——資料表變數不參與交易，
    /// 所以 ROLLBACK 後列還在，而假定義永遠不會留下。<c>LOCK_TIMEOUT 5000</c> 必須有：
    /// ALTER 要 Sch-M 鎖，等待時會把這個程序的所有新呼叫者一起堵在後面。
    /// </summary>
    private static byte[]? ReadCipher(
        SqlConnection dac,
        EncryptedModule module,
        string? dummyDefinition,
        out byte[]? originalAgain)
    {
        const string readOnly = @"
SET NOCOUNT ON;
DECLARE @objid int = OBJECT_ID(@qualified);
SELECT CAST(0 AS tinyint) AS which, valnum, CAST(imageval AS varbinary(max)) AS val
FROM sys.sysobjvalues
WHERE valclass = 1 AND objid = @objid AND subobjid = 1
ORDER BY valnum;";

        const string alterAndRead = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
DECLARE @objid int = OBJECT_ID(@qualified);
DECLARE @blobs TABLE (which tinyint NOT NULL, valnum int NOT NULL, val varbinary(max) NULL);

INSERT @blobs (which, valnum, val)
SELECT 0, valnum, CAST(imageval AS varbinary(max))
FROM sys.sysobjvalues
WHERE valclass = 1 AND objid = @objid AND subobjid = 1;

BEGIN TRY
    BEGIN TRAN;
    EXEC sp_executesql @dummy;
    INSERT @blobs (which, valnum, val)
    SELECT 1, valnum, CAST(imageval AS varbinary(max))
    FROM sys.sysobjvalues
    WHERE valclass = 1 AND objid = @objid AND subobjid = 1;
    ROLLBACK;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

SELECT which, valnum, val FROM @blobs ORDER BY which, valnum;";

        var altering = dummyDefinition is not null;
        using var command = new SqlCommand(altering ? alterAndRead : readOnly, dac) { CommandTimeout = 30 };
        command.Parameters.Add("@qualified", SqlDbType.NVarChar, 776).Value =
            SqlEncryptedModuleCrypto.Quote(module.Schema, module.Name);
        if (altering) command.Parameters.Add("@dummy", SqlDbType.NVarChar, -1).Value = dummyDefinition;

        var originalChunks = new List<byte[]>();
        var dummyChunks = new List<byte[]>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.IsDBNull(2)) continue;
                var bytes = (byte[])reader.GetValue(2);
                (reader.GetByte(0) == 0 ? originalChunks : dummyChunks).Add(bytes);
            }
        }

        originalAgain = altering ? SqlEncryptedModuleCrypto.Concat(originalChunks) : null;
        return altering
            ? SqlEncryptedModuleCrypto.Concat(dummyChunks)
            : SqlEncryptedModuleCrypto.Concat(originalChunks);
    }

    private static void Fail(
        string server,
        string database,
        EncryptedModule module,
        ModuleDecryptionResult result,
        string reason)
    {
        Failures[MemoKey(server, database, module)] = reason;
        result.Warnings.Add(module.Key + ": " + reason);
    }

    private static string MemoKey(string server, string database, EncryptedModule module) =>
        server + "|" + database + "|" + module.Key + "|" + (module.ModifyDate?.Ticks ?? 0).ToString();

    private static string RootMessage(Exception exception)
    {
        while (exception.InnerException is not null) exception = exception.InnerException;
        return exception.Message;
    }
}

/// <summary>一個以 WITH ENCRYPTION 建立的模組，以及寫出合法假定義需要的資料。</summary>
internal sealed class EncryptedModule
{
    public string Schema { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>sys.objects 的類型代碼：P、V、FN、IF、TF、TR。</summary>
    public string ObjectType { get; set; } = "";

    /// <summary>DML 觸發程序所屬的資料表；ALTER TRIGGER 必須指回原表。</summary>
    public string? ParentSchema { get; set; }

    public string? ParentName { get; set; }

    /// <summary>記憶用的版本戳；模組改過就要重讀。</summary>
    public DateTime? ModifyDate { get; set; }

    public string? Database { get; set; }

    public string Key => Schema + "." + Name;
}

/// <summary>一輪解密的產出與失敗原因。</summary>
internal sealed class ModuleDecryptionResult
{
    /// <summary>通過驗證的本文，以「schema.name」為鍵。</summary>
    public Dictionary<string, string> Definitions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; } = new();

    public string? DacError { get; set; }

    public bool Cancelled { get; set; }
}
