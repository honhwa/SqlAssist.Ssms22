using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Parsing;
using SqlAssist.Core.Settings;
using SqlAssist.Core.Snippets;
using Xunit;

namespace SqlAssist.Core.Tests.Completion;

/// <summary>分派問資料庫的那幾格：問哪一支、帶什麼、關掉資料庫時一支都不問。</summary>
public sealed class SqlCompletionCandidatesTests
{
    private static readonly IReadOnlyList<SqlSuggestion> BuiltIn = BuiltInSuggestionCatalog.Create(SqlSnippetDefaults.Current);

    [Fact]
    public async Task 限定字指到別的資料庫時只列那個地方的物件()
    {
        var metadata = new RecordingMetadata { Objects = { new SqlSuggestion("Loan", "Loan", string.Empty, string.Empty, SuggestionKind.Table, schemaName: "dbo") } };

        var list = await GetAsync("SELECT * FROM LibArchive.dbo.|", new SqlAssistSettings(), metadata);

        Assert.Equal(new[] { "Loan" }, list.Select(item => item.DisplayText));
        Assert.Equal("LibArchive", Assert.Single(metadata.ObjectPaths)?.DatabaseName);
    }

    [Fact]
    public async Task 多個限定字的敘述裡快取欄位帶著別名查()
    {
        var metadata = new RecordingMetadata();

        await GetAsync("SELECT | FROM Lib_Reader r JOIN Loan l ON r.Id = l.ReaderId", new SqlAssistSettings(), metadata);

        Assert.Equal(new[] { "r", "l" }, metadata.PeekQualifiers);
    }

    [Fact]
    public async Task 關掉資料庫物件時不問資料庫_指令碼裡的欄位照列()
    {
        var metadata = new RecordingMetadata { Throws = true };
        var settings = new SqlAssistSettings { IncludeDatabaseObjects = false };

        var list = await GetAsync("WITH c AS (SELECT Id, Name FROM Lib_Reader) SELECT c.| FROM c", settings, metadata);

        Assert.Contains(list, item => item.DisplayText == "Name" && item.Kind == SuggestionKind.Column);
    }

    /// <summary>ODBC <c>{fn EXTRACT(</c> 只列附錄 E 的六個欄位，不列 ODBC 不收的日期部分；封閉清單不問資料庫。</summary>
    [Fact]
    public async Task ODBC的EXTRACT只列六個欄位()
    {
        var list = await GetAsync("SELECT {fn EXTRACT(|", new SqlAssistSettings(), new RecordingMetadata { Throws = true });

        Assert.Equal(new[] { "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND" }, list.Select(item => item.DisplayText));
    }

    /// <summary>ODBC <c>{fn TIMESTAMPDIFF(</c> 只列 <c>SQL_TSI_</c> 那九個，不列 T-SQL 的日期部分。</summary>
    [Fact]
    public async Task ODBC的TIMESTAMPDIFF只列九個間隔()
    {
        var list = await GetAsync("SELECT {fn TIMESTAMPDIFF(|", new SqlAssistSettings(), new RecordingMetadata { Throws = true });

        Assert.Equal(9, list.Count);
        Assert.All(list, item => Assert.StartsWith("SQL_TSI_", item.DisplayText));
    }

    /// <summary>
    /// 述詞的起點把當前對象的同名欄位換成整條聯結條件，並排到欄位清單的最前面。
    /// </summary>
    /// <remarks>
    /// 排在<b>欄位</b>的最前面而不是整份清單的最前面：關鍵字與片段照樣先列，
    /// 這裡管的是「欄位之間誰先」。對面那張表的同名欄位仍留著——它沒有被改掉，
    /// 只是不再是條件的主角。
    /// </remarks>
    [Fact]
    public async Task 述詞的起點把同名欄位換成整條聯結條件()
    {
        var metadata = Joined();

        var list = await GetAsync("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON |", new SqlAssistSettings(), metadata);

        var columns = list.Where(item => item.Kind == SuggestionKind.Column).ToArray();

        Assert.Equal("CopyNo", columns[0].DisplayText);
        Assert.Equal("c.CopyNo = l.CopyNo", columns[0].InsertionText);
        Assert.NotNull(columns[0].JoinKey);
        Assert.Equal("Code", columns[1].DisplayText);
        Assert.Equal("c.Code = l.Code", columns[1].InsertionText);
        Assert.Equal("l.CopyNo", columns[2].InsertionText);
    }

    /// <summary>配不到同名欄位的欄位照舊列在後面，插入文字不動。</summary>
    [Fact]
    public async Task 沒有同名對象的欄位留在原位()
    {
        var metadata = Joined();

        var list = await GetAsync("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON |", new SqlAssistSettings(), metadata);

        Assert.Contains(list, item => item.DisplayText == "Amount" && item.JoinKey is null);
        Assert.Equal("l.CopyNo", list.Single(item => item.DisplayText == "CopyNo" && item.JoinKey is null).InsertionText);
    }

    /// <summary>
    /// 條件左邊寫完之後就不是述詞的起點：那裡要的是右邊那一個欄位。
    /// </summary>
    /// <remarks>
    /// 插入整條條件會寫出 <c>= a.CopyNo = b.CopyNo</c>。
    /// </remarks>
    [Fact]
    public async Task 條件的左邊寫完之後不配對()
    {
        var metadata = Joined();

        var list = await GetAsync("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = |", new SqlAssistSettings(), metadata);

        Assert.All(list.Where(item => item.Kind == SuggestionKind.Column), item => Assert.Null(item.JoinKey));
    }

    /// <summary>
    /// 只有一個來源時沒有「另一邊」可配，改成請中繼資料把索引鍵排到最前面。
    /// </summary>
    [Fact]
    public async Task 單一來源的述詞起點要求索引鍵排前面()
    {
        var metadata = Joined();

        await GetAsync("SELECT * FROM dbo.Loan l WHERE |", new SqlAssistSettings(), metadata);

        Assert.Equal(new[] { true }, metadata.IndexKeysFirst);
    }

    /// <summary>多個來源時不排：那會讓每一張表的索引鍵一起浮上來，蓋掉配對鍵的順序。</summary>
    [Fact]
    public async Task 多個來源的述詞起點不要求索引鍵排前面()
    {
        var metadata = Joined();

        await GetAsync("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON |", new SqlAssistSettings(), metadata);

        Assert.Equal(new[] { false, false }, metadata.IndexKeysFirst);
    }

    /// <summary><c>SELECT</c> 之後不配對：那裡要的是欄位本身。</summary>
    [Fact]
    public async Task 選取清單不配對()
    {
        var metadata = Joined();

        var list = await GetAsync("SELECT | FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo", new SqlAssistSettings(), metadata);

        Assert.All(list.Where(item => item.Kind == SuggestionKind.Column), item => Assert.Null(item.JoinKey));
        Assert.Equal(new[] { false, false }, metadata.IndexKeysFirst);
    }

    /// <summary>
    /// <c>ORDER BY</c> 與 <c>GROUP BY</c> 之後按空白鍵：整句來源的欄位一次列完，每個都帶別名，
    /// 而且允許為此查一次資料庫——不是等人先打出 <c>a.</c>。
    /// </summary>
    /// <remarks>
    /// 只讀快取的話，第一次停在這一格時一張表的欄位都沒有，而畫面上只有關鍵字與 ASC／DESC，
    /// 看不出是「還沒載入」還是「這裡就是沒有欄位」。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo ORDER BY |")]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo GROUP BY |")]
    [InlineData("SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON l.CopyNo = c.CopyNo ORDER BY l.CopyNo, |")]
    public async Task ORDER_BY的欄位帶別名並允許查資料庫(string sqlWithCaret)
    {
        var metadata = Joined();

        var list = await GetAsync(sqlWithCaret, new SqlAssistSettings(), metadata);

        Assert.Equal(new[] { "l", "c" }, metadata.ColumnQualifiers);
        Assert.Equal(
            new[] { "l.CopyNo", "l.Code", "c.CopyNo", "c.Code", "c.Amount" },
            list.Where(item => item.Kind == SuggestionKind.Column).Select(item => item.InsertionText));
    }

    /// <summary>指令碼自己寫出欄位名稱的來源（CTE）同樣帶別名，不必問資料庫。</summary>
    [Fact]
    public async Task ORDER_BY的CTE欄位也帶別名()
    {
        var metadata = new RecordingMetadata();

        var list = await GetAsync(
            "WITH c (CopyNo, Title) AS (SELECT 1, 'a') SELECT * FROM c ORDER BY |",
            new SqlAssistSettings(),
            metadata);

        Assert.Empty(metadata.ColumnQualifiers);
        Assert.Equal(
            new[] { "c.CopyNo", "c.Title" },
            list.Where(item => item.Kind == SuggestionKind.Column).Select(item => item.InsertionText));
    }

    /// <summary>兩個來源各有一組欄位，<c>CopyNo</c> 與 <c>Code</c> 同名。</summary>
    private static RecordingMetadata Joined()
    {
        var metadata = new RecordingMetadata();
        metadata.Columns["Loan"] = new[] { "CopyNo", "Code" };
        metadata.Columns["Copy"] = new[] { "CopyNo", "Code", "Amount" };
        return metadata;
    }

    /// <param name="text">整份指令碼；<c>|</c> 是游標。</param>
    private static async Task<IReadOnlyList<SqlSuggestion>> GetAsync(
        string text,
        SqlAssistSettings settings,
        ISqlCompletionMetadata metadata)
    {
        var caret = text.IndexOf('|');
        var context = SqlCompletionContextAnalyzer.Analyze(text.Remove(caret, 1), caret);
        return await SqlCompletionCandidates.GetAsync(context, BuiltIn, settings, metadata, CancellationToken.None);
    }

    private sealed class RecordingMetadata : ISqlCompletionMetadata
    {
        public bool Throws { get; init; }

        public List<SqlSuggestion> Objects { get; } = new();

        public List<SqlObjectPath?> ObjectPaths { get; } = new();

        public List<string?> PeekQualifiers { get; } = new();

        /// <summary>每一輪問到「這個來源的欄位」時帶的限定字，依來源順序。</summary>
        public List<string?> ColumnQualifiers { get; } = new();

        /// <summary>每個資料表名稱要回哪些欄位名稱；沒登記的回空清單。</summary>
        public Dictionary<string, string[]> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>每一輪問到的「索引鍵排前面」旗標，依來源順序。</summary>
        public List<bool> IndexKeysFirst { get; } = new();

        public Task<SqlCompletionContext> ResolveQualifierAsync(SqlCompletionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(context);

        public Task<IReadOnlyList<SqlSuggestion>> GetObjectsAsync(SqlObjectPath? qualifierPath, CancellationToken cancellationToken)
        {
            Asked();
            ObjectPaths.Add(qualifierPath);
            return Task.FromResult<IReadOnlyList<SqlSuggestion>>(Objects);
        }

        public Task<IReadOnlyList<SqlSuggestion>> GetSystemObjectsAsync(SqlObjectPath? qualifierPath, CancellationToken cancellationToken) =>
            Nothing();

        public Task<IReadOnlyList<SqlSuggestion>> GetColumnsAsync(
            SqlTableReference table,
            SqlAssistSettings settings,
            string? qualifier,
            CancellationToken cancellationToken)
        {
            Asked();
            ColumnQualifiers.Add(qualifier);
            return Task.FromResult(ColumnsFor(table, settings, qualifier));
        }

        public IReadOnlyList<SqlSuggestion> PeekColumns(
            SqlTableReference table,
            string? qualifier,
            SqlAssistSettings settings,
            bool indexKeysFirst = false)
        {
            Asked();
            PeekQualifiers.Add(qualifier);
            IndexKeysFirst.Add(indexKeysFirst);

            return ColumnsFor(table, settings, qualifier);
        }

        private IReadOnlyList<SqlSuggestion> ColumnsFor(
            SqlTableReference table,
            SqlAssistSettings settings,
            string? qualifier)
        {
            if (!Columns.TryGetValue(table.ObjectName, out var names))
            {
                return Array.Empty<SqlSuggestion>();
            }

            return names
                .Select(name => new SqlSuggestion(
                    name,
                    SqlInsertionText.Column(name, qualifier, settings),
                    table.ObjectName,
                    table.ObjectName,
                    SuggestionKind.Column))
                .ToArray();
        }

        public Task WarmColumnsAsync(IReadOnlyList<SqlColumnSource> sources, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SqlSuggestion>> GetParametersAsync(SqlExecutedModule module, CancellationToken cancellationToken) =>
            Nothing();

        public Task<SqlInstanceListData> GetInstanceListAsync(SqlInstanceList list, CancellationToken cancellationToken)
        {
            Asked();
            return Task.FromResult(SqlInstanceListData.Empty);
        }

        public Task<IReadOnlyList<string>> GetCatalogEntityNamesAsync(SqlCatalogEntity entity, CancellationToken cancellationToken)
        {
            Asked();
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        private Task<IReadOnlyList<SqlSuggestion>> Nothing()
        {
            Asked();
            return Task.FromResult<IReadOnlyList<SqlSuggestion>>(Array.Empty<SqlSuggestion>());
        }

        private void Asked()
        {
            if (Throws)
            {
                throw new InvalidOperationException("關掉資料庫物件時不該問資料庫。");
            }
        }
    }
}
