using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Matching;
using SqlAssist.Core.Snippets;
using Xunit;

namespace SqlAssist.Core.Tests.Completion;

[Collection(nameof(SqlSuggestionUsageCollection))]
public sealed class SuggestionMatcherTests
{
    private static SqlSuggestion Table(string name, string schema = "dbo")
    {
        return new SqlSuggestion(
            name,
            $"[{schema}].[{name}]",
            $"Table · {schema}",
            $"Table {name}",
            SuggestionKind.Table,
            schemaName: schema);
    }

    private static SqlSuggestion Procedure(string name, string schema = "dbo")
    {
        return new SqlSuggestion(
            name,
            $"[{schema}].[{name}]",
            $"Procedure · {schema}",
            $"Procedure {name}",
            SuggestionKind.Procedure,
            schemaName: schema);
    }

    private static SqlSuggestion Column(string name)
    {
        return new SqlSuggestion(
            name,
            name,
            "varchar(20) NOT NULL",
            name,
            SuggestionKind.Column);
    }

    /// <summary>
    /// 帶配對鍵的欄位：插入的是整條聯結條件，顯示文字仍是欄位本身。
    /// </summary>
    private static SqlSuggestion JoinKeyColumn(string name)
    {
        return new SqlSuggestion(
            name,
            $"{name} = other.{name}",
            $"int NOT NULL · = other.{name}",
            name,
            SuggestionKind.Column,
            joinKey: new SqlJoinKey(name, "self", name, "other"));
    }

    /// <summary>兩個來源都看得到的述詞起點，配對鍵成立的位置。</summary>
    private const string OnPredicate = "SELECT * FROM dbo.Loan l JOIN dbo.Copy c ON ";

    /// <summary>
    /// 沒有限定字的位置也要看得到欄位：SELECT | FROM PUBLISHER a 這種情形，
    /// 使用者要的幾乎都是欄位而不是整個資料庫的物件清單。
    /// </summary>
    [Fact]
    public void 沒有限定字時仍建議欄位()
    {
        var names = RankNames(
            new[] { Table("PUBL_Log"), Column("PUBL_CODE") },
            "SELECT publ");

        Assert.Contains("PUBL_CODE", names);
    }

    [Fact]
    public void 分數相同時欄位排在資料表之前()
    {
        var names = RankNames(
            new[] { Table("PUBLCODE"), Column("PUBLCODE") },
            "SELECT publcode");

        Assert.Equal("PUBLCODE", names[0]);
        Assert.Equal(SuggestionKind.Column, SuggestionMatcher
            .Rank(new[] { Table("PUBLCODE"), Column("PUBLCODE") },
                SqlCompletionContextAnalyzer.Analyze("SELECT publcode"))[0]
            .Suggestion.Kind);
    }

    /// <summary>
    /// FROM 之後只能是資料來源，欄位不該出現在那裡。
    /// </summary>
    [Fact]
    public void 資料來源位置不建議欄位()
    {
        var names = RankNames(
            new[] { Table("PUBLISHER"), Column("PUBL_CODE") },
            "SELECT * FROM publ");

        Assert.Equal(new[] { "PUBLISHER" }, names);
    }

    private static IReadOnlyList<string> RankNames(
        IEnumerable<SqlSuggestion> suggestions,
        string textBeforeCaret)
    {
        return SuggestionMatcher
            .Rank(suggestions, SqlCompletionContextAnalyzer.Analyze(textBeforeCaret))
            .Select(item => item.Suggestion.DisplayText)
            .ToArray();
    }

    /// <summary>
    /// 使用者回報的情境：輸入 libr 時，Lib_Reader 必須是第一順位，
    /// 不必打到 lib_re 才浮上來。
    /// </summary>
    [Fact]
    public void 輸入libr時Lib_Reader排第一()
    {
        var candidates = new[]
        {
            Table("Lib_Reader"),
            Table("MyLibrTable"),
            Table("Lib_ReaderTag"),
            Table("LibBackupRecord"),
            Table("Lib_Tag")
        };

        var ranked = RankNames(candidates, "SELECT * FROM libr");

        Assert.Equal("Lib_Reader", ranked[0]);
        Assert.DoesNotContain("Lib_Tag", ranked);
    }

    [Fact]
    public void 完全相同的輸入一定排第一()
    {
        var candidates = BuiltInSuggestionCatalog.Create(SqlSnippetDefaults.Current)
            .Concat(new[] { Table("ssf_Archive"), Table("ssfLog") })
            .ToArray();

        var ranked = RankNames(candidates, "ssf");

        Assert.Equal("ssf", ranked[0]);
    }

    [Fact]
    public void 分數相同時較短的名稱排前面()
    {
        var candidates = new[] { Table("LoanDetailHistory"), Table("Loan") };

        var ranked = RankNames(candidates, "SELECT * FROM loan");

        Assert.Equal("Loan", ranked[0]);
    }

    /// <remarks>
    /// 結構描述是名稱的第二段，也在這個位置；預存程序與片段不在。
    /// </remarks>
    [Fact]
    public void FROM之後只顯示資料來源與結構描述()
    {
        var candidates = BuiltInSuggestionCatalog.Create(SqlSnippetDefaults.Current)
            .Concat(new[] { Table("Publisher"), Procedure("usp_Publisher") })
            .ToArray();

        var ranked = SuggestionMatcher.Rank(
            candidates,
            SqlCompletionContextAnalyzer.Analyze("SELECT * FROM "));

        Assert.NotEmpty(ranked);
        Assert.All(ranked, item => Assert.Contains(
            item.Suggestion.Kind,
            new[] { SuggestionKind.Table, SuggestionKind.Schema }));
    }

    [Fact]
    public void ALTER_PROCEDURE之後只顯示Procedure()
    {
        var candidates = new[] { Table("Publisher"), Procedure("usp_Publisher") };

        var ranked = SuggestionMatcher.Rank(
            candidates,
            SqlCompletionContextAnalyzer.Analyze("ALTER PROCEDURE "));

        Assert.Single(ranked);
        Assert.Equal(SuggestionKind.Procedure, ranked[0].Suggestion.Kind);
    }

    [Fact]
    public void Schema限定後只顯示該Schema的物件()
    {
        var candidates = new[] { Table("Publisher", "dbo"), Table("Publisher", "sales") };

        var ranked = SuggestionMatcher.Rank(
            candidates,
            SqlCompletionContextAnalyzer.Analyze("SELECT * FROM [sales]."));

        Assert.Single(ranked);
        Assert.Equal("sales", ranked[0].Suggestion.SchemaName);
    }

    [Fact]
    public void 回傳結果帶有可供高亮的命中區段()
    {
        var ranked = SuggestionMatcher.Rank(
            new[] { Table("Lib_Reader") },
            SqlCompletionContextAnalyzer.Analyze("SELECT * FROM libr"));

        var match = Assert.Single(ranked);
        Assert.Equal(2, match.Spans.Count);
        Assert.Equal(0, match.Spans[0].Start);
        Assert.Equal(3, match.Spans[0].Length);
        Assert.Equal(4, match.Spans[1].Start);
        Assert.Equal(1, match.Spans[1].Length);
    }

    [Fact]
    public void 遵守數量上限且保留分數最高的項目()
    {
        var candidates = Enumerable.Range(0, 50)
            .Select(index => Table($"Lib_Reader{index:D2}"))
            .Concat(new[] { Table("Lib_Reader") })
            .ToArray();

        var ranked = SuggestionMatcher.Rank(
            candidates,
            SqlCompletionContextAnalyzer.Analyze("SELECT * FROM libr"),
            maximumCount: 5);

        Assert.Equal(5, ranked.Count);
        Assert.Equal("Lib_Reader", ranked[0].Suggestion.DisplayText);
    }

    [Fact]
    public void 分數由高到低排列()
    {
        var candidates = new[]
        {
            Table("Lib_Reader"),
            Table("LibBackupRecord"),
            Table("MyLibrTable")
        };

        var ranked = SuggestionMatcher.Rank(
            candidates,
            SqlCompletionContextAnalyzer.Analyze("SELECT * FROM libr"));

        var scores = ranked.Select(item => item.Score).ToArray();
        Assert.Equal(scores.OrderByDescending(score => score), scores);
    }

    [Fact]
    public void 上下文無效時回傳空清單()
    {
        var ranked = SuggestionMatcher.Rank(
            new[] { Table("Publisher") },
            SqlCompletionContextAnalyzer.Analyze("-- 註解 publ"));

        Assert.Empty(ranked);
    }

    [Fact]
    public void 輸入單一字母時關鍵字與Snippet排在資料表之前()
    {
        var candidates = BuiltInSuggestionCatalog.Create(SqlSnippetDefaults.Current)
            .Concat(new[] { Table("Lib_Reader") })
            .ToArray();

        var ranked = SuggestionMatcher.Rank(
            candidates,
            SqlCompletionContextAnalyzer.Analyze("s"));

        var topKinds = ranked.Take(3).Select(item => item.Suggestion.Kind).ToArray();
        Assert.All(topKinds, kind =>
            Assert.True(kind == SuggestionKind.Keyword || kind == SuggestionKind.Snippet));
    }

    /// <summary>
    /// 類別偏好不該被名稱長度翻掉。
    /// </summary>
    /// <remarks>
    /// 兩者的模糊分數相同（都是首字元詞首命中），差別只在類別與長度。
    /// 曾經長度懲罰與類別加成同一量級，於是長欄位輸給短資料表，
    /// 正好違反「欄位優先於資料表」——而欄位名普遍比資料表名長，那不是特例。
    /// </remarks>
    [Fact]
    public void 長欄位仍排在短資料表之前()
    {
        var names = RankNames(
            new[] { Table("BOOKS"), Column("BOOK_LOAN_HISTORY_DETAIL") },
            "SELECT bo");

        Assert.Equal("BOOK_LOAN_HISTORY_DETAIL", names[0]);
    }

    [Fact]
    public void 名稱長度只在同類別內決定順序()
    {
        var names = RankNames(
            new[] { Column("BOOK_LOAN_HISTORY_DETAIL"), Column("BOOKS") },
            "SELECT bo");

        Assert.Equal("BOOKS", names[0]);
    }

    /// <summary>最近提交過的項目要排到前面。</summary>
    [Fact]
    public void 最近用過的排在同類別的其他項目之前()
    {
        SqlSuggestionUsage.Clear();

        try
        {
            var candidates = new[] { Table("BOOK_A"), Table("BOOK_B") };

            Assert.Equal("BOOK_A", RankNames(candidates, "SELECT * FROM book")[0]);

            SqlSuggestionUsage.Record(Table("BOOK_B"));

            Assert.Equal("BOOK_B", RankNames(candidates, "SELECT * FROM book")[0]);
        }
        finally
        {
            SqlSuggestionUsage.Clear();
        }
    }

    /// <summary>
    /// 最近用過壓不過類別偏好，也壓不過更好的比對品質。
    /// </summary>
    /// <remarks>
    /// 前者：剛提交過的資料表仍然排在敘述自己的欄位之後。這正是編輯的實際順序
    /// ——使用者才剛從清單裡挑完 FROM 後面那幾張表，游標一移到 SET 或 WHERE，
    /// 那幾張表就會帶著加成蓋住他真正要挑的欄位。類別說的是「這個位置文法上要
    /// 什麼」，使用紀錄只是跨敘述的猜測，猜測不該翻過眼前這句話的證據。
    ///
    /// 後者：<c>publ</c> 之下，真正以它開頭的候選項仍要贏過只是包含這幾個字母的
    /// 那一個。
    /// </remarks>
    [Fact]
    public void 最近用過壓不過類別也壓不過比對品質()
    {
        SqlSuggestionUsage.Clear();

        try
        {
            SqlSuggestionUsage.Record(Table("PUBLCODE"));
            Assert.Equal(
                SuggestionKind.Column,
                SuggestionMatcher.Rank(
                    new[] { Table("PUBLCODE"), Column("PUBLCODE") },
                    SqlCompletionContextAnalyzer.Analyze("SELECT publcode"))[0].Suggestion.Kind);

            SqlSuggestionUsage.Clear();
            SqlSuggestionUsage.Record(Table("MyPublisherLog"));
            Assert.Equal(
                "PUBLISHER",
                RankNames(new[] { Table("MyPublisherLog"), Table("PUBLISHER") }, "SELECT * FROM publ")[0]);
        }
        finally
        {
            SqlSuggestionUsage.Clear();
        }
    }

    /// <summary>
    /// 同分時保留候選清單原本的順序。
    /// </summary>
    /// <remarks>
    /// 欄位交進來的順序是資料表的定義順序，那才是使用者對一張表的心智模型；
    /// 以前的字母序 tie-break 會把它打散。
    /// </remarks>
    [Fact]
    public void 同分時保留原本的順序()
    {
        var names = RankNames(
            new[] { Column("N_ZULU"), Column("N_LIMA"), Column("N_MIKE") },
            "SELECT n_");

        Assert.Equal(new[] { "N_ZULU", "N_LIMA", "N_MIKE" }, names);
    }

    /// <summary>
    /// <c>ON </c> 之後配對鍵排在最前面，其餘欄位照舊。
    /// </summary>
    /// <remarks>
    /// 「照舊」指的是原來的分數與原來的順序，不是字母序：三筆普通欄位同類別、同為
    /// 空前綴，剩下的差別只有名稱長度，所以短的在前（<c>Qty</c> 三、<c>Note</c> 四、
    /// <c>Amount</c> 六）。配對鍵即使名稱比它們都長也壓得過去——加成的層級在長度之上。
    /// </remarks>
    [Fact]
    public void ON之後配對鍵排在最前面()
    {
        var ranked = SuggestionMatcher
            .Rank(
                new[]
                {
                    Column("Amount"),
                    Column("Note"),
                    JoinKeyColumn("CopyNo"),
                    Column("Qty")
                },
                SqlCompletionContextAnalyzer.Analyze(OnPredicate))
            .Select(item => item.Suggestion.DisplayText)
            .ToArray();

        Assert.Equal(new[] { "CopyNo", "Qty", "Note", "Amount" }, ranked);
    }

    /// <summary>
    /// 配對鍵壓得過最近用過的那一筆，也壓得過名稱更短的那一筆。
    /// </summary>
    /// <remarks>
    /// 這正是想要的取捨：<c>ON </c> 之後使用者要寫的是那條同名條件，而不是他上一次
    /// 剛好挑過、或短一點的那個欄位。對照組先立下基準——兩筆都是普通欄位時，
    /// 勝負由最近用過決定。
    /// </remarks>
    [Fact]
    public void 配對鍵壓過最近用過的欄位()
    {
        SqlSuggestionUsage.Clear();

        try
        {
            SqlSuggestionUsage.Record(Column("Amount"));

            Assert.Equal(
                "Amount",
                RankNames(new[] { Column("Amount"), Column("CopyNo") }, OnPredicate)[0]);

            Assert.Equal(
                "CopyNo",
                RankNames(new[] { Column("Amount"), JoinKeyColumn("CopyNo") }, OnPredicate)[0]);
        }
        finally
        {
            SqlSuggestionUsage.Clear();
        }
    }

    /// <summary>
    /// 配對鍵的加成翻不過比對品質那一層。
    /// </summary>
    /// <remarks>
    /// 兩層的差距是刻意留的：比對品質每差一分放大 <c>8192</c> 倍，而整個「與輸入
    /// 無關」的分數加起來也到不了 <c>8192</c>——類別最高 <c>40</c>、配對鍵 <c>3</c>、
    /// 最近用過 <c>64</c>，全部乘上類別倍率 <c>128</c>。所以使用者打了前綴之後，
    /// 命中的好壞仍然先說話，配對鍵只在同一層裡往前挪。
    /// </remarks>
    [Fact]
    public void 配對鍵的加成翻不過比對品質那一層()
    {
        // 一分比對品質的差距，就足以蓋過配對鍵全部的加成。
        var barelyBetter = FuzzyMatchResult.Matched(1, Array.Empty<MatchSpan>());

        var plain = SuggestionMatcher.ComposeScore(Column("CopyNo"), barelyBetter, string.Empty);
        var key = SuggestionMatcher.ComposeScore(
            JoinKeyColumn("CopyNo"),
            FuzzyMatchResult.NoMatch,
            string.Empty);

        Assert.True(plain > key, $"比對品質多一分（{plain}）應該壓過配對鍵的加成（{key}）。");
    }
}
