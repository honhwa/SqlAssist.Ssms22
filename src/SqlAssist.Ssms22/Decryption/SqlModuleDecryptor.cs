using System;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Core.Decryption;
using SqlAssist.Core.Notifications;
using SqlAssist.Metadata.Model;
using SqlAssist.Metadata.Querying;
using SqlAssist.Ssms22.Connections;

namespace SqlAssist.Ssms22.Decryption;

/// <summary>
/// 把「物件結構缺少定義」接上 DAC 解密：查種類與版本 → 開 DAC → 還原本文 → 通知與診斷。
/// </summary>
/// <remarks>
/// 只由 <see cref="SqlMetadataService.GetStructureAsync"/> 在背景呼叫，也就是 F12、結構預覽與
/// 停靠工具窗那些「使用者主動要看定義」的路徑。按鍵建議路徑一個字都不碰，解密也不會在 UI 執行緒跑。
/// 解密是 best effort：失敗只讓定義維持原本的「取不到」，不能把整份結構弄不見。
/// </remarks>
internal static class SqlModuleDecryptor
{
    /// <summary>
    /// 試著取回加密模組的本文；不是加密模組、設定關著或拿了就不知道怎麼連時回 null。
    /// </summary>
    internal static async Task<string?> TryDecryptAsync(
        SqlObjectInfo objectInfo,
        ISqlConnectionSource source,
        CancellationToken cancellationToken)
    {
        if (!SqlModuleDecryptionService.Enabled) return null;

        var ssms = Unwrap(source) as SsmsConnectionSource;
        if (ssms is null) return null;

        var module = await Task
            .Run(() => ReadModule(source, objectInfo, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (module is null) return null;

        var database = objectInfo.DatabaseName is { Length: > 0 } named ? named : source.DatabaseName;
        var server = ssms.ServerForDecryption;

        // 記憶命中不開 DAC，也不再送一次通知——同一個物件每次預覽都報「解密完成」只會蓋掉真正的活動。
        if (SqlModuleDecryptionService.TryGetKnown(server, database, module, out var known, out _))
            return known is null ? null : SqlEncryptedModuleCrypto.NormalizeToCreate(known);

        using var notification = NotificationCenter.Default.Begin(
            NotificationCatalog.DecryptingEncryptedModule,
            NotificationKind.Metadata,
            NotificationOrigin.User,
            NotificationLevel.Info,
            module.Key,
            database);

        if (!ssms.TryBuildDacConnectionString(out var connectionString, out var connectionFailure))
        {
            var connectionReport = NotificationCatalog.DecryptionFailure(0, 1, connectionFailure);
            notification.Report(connectionReport);
            notification.Fail();
            SqlAssistDiagnostics.WriteAlways(connectionReport);
            return null;
        }

        (string? Text, string? Error) decrypted;
        try
        {
            decrypted = await Task
                .Run(
                    () =>
                    {
                        var text = SqlModuleDecryptionService.DecryptSingle(
                            connectionString, database, module, out var failure);
                        return (text, failure);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            notification.Cancel();
            return null;
        }
        catch (Exception exception)
        {
            decrypted = (null, exception.Message);
        }

        if (decrypted.Text is { } definition)
        {
            var summary = NotificationCatalog.DecryptionSummary(1, 1);
            notification.Report(summary);
            SqlAssistDiagnostics.WriteAlways(summary);
            return SqlEncryptedModuleCrypto.NormalizeToCreate(definition);
        }

        var reason = decrypted.Error is { Length: > 0 } message
            ? message
            : DecryptionText.DecryptInvalidDefinition(module.Name);
        var failure = NotificationCatalog.DecryptionFailure(0, 1, reason);
        notification.Report(failure);
        notification.Fail();
        SqlAssistDiagnostics.WriteAlways(failure);
        return null;
    }

    /// <summary>
    /// 所有「看定義」路徑共用的那一道門：只有 T-SQL 模組缺定義才試解密，失敗就原樣交回結構。
    /// </summary>
    internal static async Task<SqlObjectStructure> EnrichAsync(
        SqlObjectStructure structure,
        SqlObjectInfo objectInfo,
        ISqlConnectionSource source,
        CancellationToken cancellationToken)
    {
        if (structure.Definition is not null ||
            !objectInfo.Kind.IsModule() ||
            !structure.Detail.Implementation.HasTransactSqlBody())
        {
            return structure;
        }

        var definition = await TryDecryptAsync(objectInfo, source, cancellationToken).ConfigureAwait(false);
        return definition is null ? structure : structure.WithDefinition(definition);
    }

    /// <summary>
    /// 從普通連線讀出建立假定義需要的類型、版本與所屬資料表。定義不是 NULL 時不算加密，
    /// 查不到列（沒有 VIEW DEFINITION 或物件已不存在）也只能照原本的「取不到」回答。
    /// </summary>
    /// <remarks>
    /// 解密是附加價值，目錄查詢失敗不能把原本的結構一起弄壞：例外往上拋給
    /// <see cref="SqlMetadataService.GetStructureAsync"/> 的呼叫端，它已經有一套降級路徑。
    /// </remarks>
    private static EncryptedModule? ReadModule(
        ISqlConnectionSource source,
        SqlObjectInfo objectInfo,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT o.type, o.modify_date, ps.name AS parent_schema, po.name AS parent_name
FROM sys.sql_modules m
JOIN sys.objects o ON m.object_id = o.object_id
LEFT JOIN sys.objects po ON o.parent_object_id = po.object_id
LEFT JOIN sys.schemas ps ON po.schema_id = ps.schema_id
WHERE m.object_id = @objectId AND m.definition IS NULL";

        using var connection = source.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 15;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@objectId";
        parameter.Value = objectInfo.ObjectId;
        command.Parameters.Add(parameter);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        cancellationToken.ThrowIfCancellationRequested();
        return new EncryptedModule
        {
            Schema = objectInfo.SchemaName ?? "",
            Name = objectInfo.Name,
            ObjectType = reader.GetString(0).Trim(),
            ModifyDate = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            ParentSchema = reader.IsDBNull(2) ? null : reader.GetString(2),
            ParentName = reader.IsDBNull(3) ? null : reader.GetString(3),
            Database = objectInfo.DatabaseName,
        };
    }

    /// <summary>跨資料庫來源的外層只換資料庫，DAC 憑證在查詢視窗那一份上面。</summary>
    private static ISqlConnectionSource Unwrap(ISqlConnectionSource source)
    {
        while (source is SqlDatabaseScopedConnectionSource scoped) source = scoped.Unwrap();
        return source;
    }
}
