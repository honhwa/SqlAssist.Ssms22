using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using SqlAssist.Metadata.Querying;

namespace SqlAssist.Metadata.Tests.Caching;

/// <summary>
/// 一台只回答一個模組定義的假伺服器，用來量「這一次的答案從哪裡來」。
/// </summary>
/// <remarks>
/// 既有的 <c>FakeCatalogServer</c> 服務的是搜尋索引那幾條查詢，第二／三層的參數與說明
/// 它一條都不認得——拿它去載明細只會量到假物件自己的行為。這裡只實作模組需要的三條
/// （參數、定義、說明），刻意留空的那兩條回空結果：<b>零列就是答案</b>，預存程序沒有
/// 資料行也沒有說明，那不是「查不到」。
///
/// <see cref="Definition"/> 可以在兩次讀取之間換掉，而這正是整件事的重點：
/// 快取回答的是上一次讀到的那一份，重新整理回答的是現在這一份。
/// </remarks>
internal sealed class FakeDefinitionServer : ISqlConnectionSource
{
    internal string? Definition { get; set; } =
        "CREATE PROCEDURE [dbo].[usp_Loan]\r\nAS\r\n-- 第 1 版\r\nSELECT 1;\r\n";

    /// <summary>開過幾次連線；「有沒有真的再問一次」靠它。</summary>
    internal int Opened { get; private set; }

    public string CacheKey => "server-a|db1";

    public string ServerCacheKey => "server-a";

    public string DatabaseName => "db1";

    public IDbConnection OpenConnection()
    {
        Opened++;
        return new FakeDefinitionConnection(this);
    }
}

internal sealed class FakeDefinitionConnection : IDbConnection
{
    private readonly FakeDefinitionServer _server;

    internal FakeDefinitionConnection(FakeDefinitionServer server) => _server = server;

    [AllowNull] public string ConnectionString { get; set; } = string.Empty;

    public int ConnectionTimeout => 0;

    public string Database => _server.DatabaseName;

    public ConnectionState State => ConnectionState.Open;

    public IDbCommand CreateCommand() => new FakeDefinitionCommand(_server);

    public void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public void Open()
    {
    }

    public void Close()
    {
    }

    public void Dispose()
    {
    }

    public IDbTransaction BeginTransaction() => throw new NotSupportedException();

    public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
}

internal sealed class FakeDefinitionCommand : IDbCommand
{
    private readonly FakeDefinitionServer _server;
    private readonly FakeDefinitionParameters _parameters = new();

    internal FakeDefinitionCommand(FakeDefinitionServer server) => _server = server;

    [AllowNull] public string CommandText { get; set; } = string.Empty;

    public int CommandTimeout { get; set; }

    public CommandType CommandType { get; set; }

    public IDbConnection? Connection { get; set; }

    public IDbTransaction? Transaction { get; set; }

    public UpdateRowSource UpdatedRowSource { get; set; }

    public IDataParameterCollection Parameters => _parameters;

    public IDbDataParameter CreateParameter() => new FakeDefinitionParameter();

    public void Dispose()
    {
    }

    public void Cancel()
    {
    }

    public void Prepare() => throw new NotSupportedException();

    public int ExecuteNonQuery() => throw new NotSupportedException();

    public object ExecuteScalar() => throw new NotSupportedException();

    public IDataReader ExecuteReader(CommandBehavior behavior) => ExecuteReader();

    /// <remarks>
    /// 只認定義那一條（唯一提到 <c>sys.sql_modules</c> 的），其餘一律回零列：
    /// 參數與 <c>MS_Description</c> 這兩條對一個沒有參數、沒有說明的模組就是這個答案。
    /// </remarks>
    public IDataReader ExecuteReader()
    {
        using var table = new DataTable();

        if (CommandText.IndexOf("sys.sql_modules", StringComparison.Ordinal) >= 0)
        {
            table.Columns.Add("type", typeof(string));
            table.Columns.Add("definition", typeof(string));

            if (_server.Definition is { } definition) table.Rows.Add("P", definition);
        }

        return table.CreateDataReader();
    }
}

/// <summary>只夠 <c>@objectId</c> 一個參數用的集合。</summary>
internal sealed class FakeDefinitionParameters : IDataParameterCollection
{
    private readonly List<IDbDataParameter> _parameters = new();

    public object this[string parameterName]
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public object? this[int index]
    {
        get => _parameters[index];
        set => throw new NotSupportedException();
    }

    public bool IsFixedSize => false;

    public bool IsReadOnly => false;

    public int Count => _parameters.Count;

    public bool IsSynchronized => false;

    public object SyncRoot => _parameters;

    public int Add(object? value)
    {
        _parameters.Add((IDbDataParameter)(value ?? throw new ArgumentNullException(nameof(value))));
        return _parameters.Count - 1;
    }

    public bool Contains(string parameterName) => false;

    public bool Contains(object? value) => _parameters.Contains((IDbDataParameter?)value!);

    public void Clear() => _parameters.Clear();

    public int IndexOf(string parameterName) => -1;

    public int IndexOf(object? value) => _parameters.IndexOf((IDbDataParameter?)value!);

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(string parameterName) => throw new NotSupportedException();

    public void RemoveAt(int index) => _parameters.RemoveAt(index);

    public void CopyTo(Array array, int index) => throw new NotSupportedException();

    public IEnumerator GetEnumerator() => _parameters.GetEnumerator();
}

internal sealed class FakeDefinitionParameter : IDbDataParameter
{
    public byte Precision { get; set; }

    public byte Scale { get; set; }

    public int Size { get; set; }

    public DbType DbType { get; set; }

    public ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    public bool IsNullable => true;

    [AllowNull] public string ParameterName { get; set; } = string.Empty;

    [AllowNull] public string SourceColumn { get; set; } = string.Empty;

    public DataRowVersion SourceVersion { get; set; }

    public object? Value { get; set; }
}
