using System;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using SqlAssist.Metadata.Querying;
using SqlAssist.Ssms22;
using SqlAssist.Ssms22.Decryption;

namespace SqlAssist.Ssms22.Connections;

/// <summary>
/// 以 SSMS 查詢視窗目前的連線為樣板，供中繼資料查詢另外開連線。
/// </summary>
/// <remarks>
/// 建構時（在 UI 執行緒上、握有 SSMS 的實際連線物件時）先複製一份<b>樣板連線</b>並保持關閉。
/// 之後每次查詢都從樣板再複製一份來用，好處有二：
/// 一是不會碰到 SSMS 正在使用的那條連線，使用者的長時間查詢與明確交易都不受影響；
/// 二是複製 SqlConnection 會保留認證，若改用 ConnectionString 重建，
/// SQL 驗證的密碼會因為預設不回傳而遺失。
/// </remarks>
internal sealed class SsmsConnectionSource : ISqlConnectionSource, IDisposable
{
    private readonly IDbConnection _template;
    private bool _disposed;

    private SsmsConnectionSource(
        IDbConnection template,
        string databaseName,
        string serverCacheKey)
    {
        _template = template;
        DatabaseName = databaseName;
        ServerCacheKey = serverCacheKey;
        CacheKey = SqlConnectionCacheKey.Compose(serverCacheKey, databaseName);
    }

    public string CacheKey { get; }

    public string ServerCacheKey { get; }

    public string DatabaseName { get; }

    /// <summary>
    /// 從 SSMS 的連線建立來源；無法複製時回傳 null，呼叫端應視為「沒有資料庫建議」。
    /// </summary>
    public static SsmsConnectionSource? TryCreate(IDbConnection? editorConnection)
    {
        if (editorConnection is null)
        {
            return null;
        }

        var template = Clone(editorConnection);

        if (template is null)
        {
            SqlAssistDiagnostics.WriteAlways("無法複製目前 SQL 連線，略過資料庫物件建議");
            return null;
        }

        var databaseName = editorConnection.Database ?? string.Empty;
        var serverCacheKey = SqlConnectionCacheKey.CreateServerKey(template.ConnectionString);
        return new SsmsConnectionSource(template, databaseName, serverCacheKey);
    }

    [Localizable(false)]
    public IDbConnection OpenConnection()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SsmsConnectionSource));
        }

        var connection = Clone(_template)
            ?? throw new InvalidOperationException("無法從樣板連線複製新的連線。");

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            if (!string.IsNullOrWhiteSpace(DatabaseName) &&
                !string.Equals(connection.Database, DatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                // 跟隨查詢視窗選取的資料庫，而不是登入的預設資料庫。
                connection.ChangeDatabase(DatabaseName);
            }

            return connection;
        }
        catch
        {
            // 不走 SqlAssistPlatformGuard：這裡不吞例外，只是把開到一半的連線收掉。
            // 吞掉的話呼叫端會拿到 null 而不知道是連線失敗還是根本沒有連線。
            connection.Dispose();
            throw;
        }
    }

    /// <summary>解密記憶用的伺服器識別；與 DAC 連線字串算出來的那一份必須逐字相同。</summary>
    internal string ServerForDecryption
    {
        get
        {
            try
            {
                return SqlDacConnectionFactory.StripDacUnsupportedParts(
                    new SqlConnectionStringBuilder(_template.ConnectionString).DataSource);
            }
            catch
            {
                return ServerCacheKey;
            }
        }
    }

    /// <summary>
    /// 從同一份樣板連線另外組一條 DAC 連線字串。不能直接用 <see cref="OpenConnection"/>：
    /// DAC 要 ADMIN: 前綴、關掉集區、去掉明確的埠，而且一個執行個體只有一條。
    /// </summary>
    /// <remarks>
    /// SQL 驗證的密碼若沒有留在連線字串裡就明確失敗，不猜憑證；Windows 驗證不受影響。
    /// Entra token 無法寫進連線字串，因此也走同一句提示。
    /// </remarks>
    internal bool TryBuildDacConnectionString(out string connectionString, out string failure)
    {
        connectionString = string.Empty;
        failure = string.Empty;

        try
        {
            var builder = new SqlConnectionStringBuilder(_template.ConnectionString)
            {
                Pooling = false,
                MultipleActiveResultSets = false,
                ConnectTimeout = 10,
                ApplicationName = "SqlAssist for SSMS (DAC)",
                InitialCatalog = DatabaseName,
            };
            builder.DataSource = "ADMIN:" + SqlDacConnectionFactory.StripDacUnsupportedParts(builder.DataSource);
            if (!builder.IntegratedSecurity && string.IsNullOrEmpty(builder.Password))
            {
                failure = DecryptionText.DacNoCredentials;
                return false;
            }

            connectionString = builder.ConnectionString;
            return true;
        }
        catch (Exception ex)
        {
            failure = DecryptionText.DacBadConnectionString(ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _template.Dispose();
    }

    private static IDbConnection? Clone(IDbConnection source)
    {
        // SqlConnection 實作 ICloneable 正是為了在複製時保留認證。
        return SqlAssistPlatformGuard.Probe<IDbConnection?>(
            "複製 SQL 連線",
            () => source is ICloneable cloneable && cloneable.Clone() is IDbConnection cloned
                ? cloned
                : null,
            fallback: null);
    }
}
