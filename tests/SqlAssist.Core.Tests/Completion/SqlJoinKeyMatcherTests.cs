using System;
using System.Linq;
using SqlAssist.Core.Completion;
using Xunit;

namespace SqlAssist.Core.Tests.Completion;

/// <summary>
/// <c>ON</c> 與 <c>WHERE</c> 之後的配對鍵：當前對象的欄位與前面來源的同名欄位怎麼配。
/// </summary>
/// <remarks>
/// 這一層只回答「誰跟誰同名」，字串怎麼排（<see cref="SqlJoinKey"/>）與排名
/// （<see cref="SuggestionMatcher"/>）各有自己的測試。
/// </remarks>
public sealed class SqlJoinKeyMatcherTests
{
    private static SqlJoinKeySource Source(string? qualifier, params string[] names)
    {
        return new SqlJoinKeySource(qualifier, names);
    }

    /// <summary>限定字讀不出來時只寫名稱，攤平的字串才讀得出來少了什麼。</summary>
    private static string Label(string? qualifier, string name)
    {
        return qualifier is null ? name : $"{qualifier}.{name}";
    }

    /// <summary>把配對結果攤成 <c>當前.欄位=對方.欄位</c>，一次比對整份結果。</summary>
    private static string[] Describe(SqlJoinKeyMatch match)
    {
        return match.Pairs
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Label(pair.Value.Qualifier, pair.Key) +
                "=" +
                Label(pair.Value.CounterpartQualifier, pair.Value.CounterpartName))
            .ToArray();
    }

    private static string[] Pairs(params SqlJoinKeySource[] sources)
    {
        return Describe(SqlJoinKeyMatcher.Pair(sources));
    }

    [Fact]
    public void 同名欄位配成一條聯結條件()
    {
        Assert.Equal(
            new[] { "b.CopyNo=a.CopyNo" },
            Pairs(Source("a", "PUBL_CODE", "CopyNo"), Source("b", "CopyNo", "Qty")));
    }

    /// <summary>
    /// 同名認的是正規化後的名稱：<c>copy_no</c> 與 <c>CopyNo</c> 是同一個名稱。
    /// </summary>
    /// <remarks>
    /// 回報的是<b>來源那一側原本的寫法</b>，不自行改大小寫也不補分隔符——
    /// 寫進編輯器的名稱必須是資料庫裡真的有的那一個。
    /// </remarks>
    [Theory]
    [InlineData("COPY_NO", "CopyNo")]
    [InlineData("copyno", "CopyNo")]
    [InlineData("Copy_No", "copy_no")]
    [InlineData("COPY_NO", "Copy#No")]
    public void 去分隔符與大小寫後同名也算同名(string counterpart, string current)
    {
        Assert.Equal(
            new[] { $"b.{current}=a.{counterpart}" },
            Pairs(Source("a", counterpart), Source("b", current)));
    }

    /// <summary>
    /// 同一個欄位只配最近的來源：接 <c>a</c> 是同一個名稱卻跨過了一張表。
    /// </summary>
    [Fact]
    public void 只配最近的前一個來源()
    {
        Assert.Equal(
            new[] { "c.CopyNo=b.CopyNo" },
            Pairs(Source("a", "CopyNo"), Source("b", "CopyNo"), Source("c", "CopyNo")));
    }

    [Fact]
    public void 近的來源沒有同名時往外找()
    {
        Assert.Equal(
            new[] { "c.Code=a.Code" },
            Pairs(Source("a", "Code"), Source("b", "Qty"), Source("c", "Code")));
    }

    /// <summary>
    /// <c>FROM A a JOIN B b</c> 的 <c>ON</c> 之後，<c>b</c> 的每個欄位各配一條。
    /// </summary>
    [Fact]
    public void 當前對象的每個同名欄位都會配到()
    {
        Assert.Equal(
            new[] { "b.Code=a.Code", "b.CopyNo=a.CopyNo" },
            Pairs(Source("a", "CopyNo", "Code", "Note"), Source("b", "Code", "CopyNo", "Amount")));
    }

    [Fact]
    public void 只有一個來源時不配對()
    {
        var match = SqlJoinKeyMatcher.Pair(new[] { Source("a", "CopyNo") });

        Assert.True(match.IsEmpty);
        Assert.Empty(match.SourceIndexes);
        Assert.Empty(match.Pairs);
    }

    /// <remarks>
    /// 同名限定字的來源會先合併成一個群組，剩下一個來源就沒得配。這也是
    /// <c>ON</c> 寫在自己身上的情形——那不是聯結條件。
    /// </remarks>
    [Fact]
    public void 同一個名稱不跟自己配對()
    {
        Assert.Empty(Pairs(Source("a", "CopyNo"), Source("a", "CopyNo")));
    }

    /// <summary>
    /// <c>FROM (SELECT Id, * FROM T) d</c> 攤平出兩筆都叫 <c>d</c> 的來源。
    /// </summary>
    /// <remarks>
    /// 不合併的話當前來源只拿得到寫死的那一個名稱（<c>Id</c>），而它明明還有
    /// 一整套欄位。回報的索引要是<b>兩筆</b>，否則呼叫端只會換掉其中一半的欄位。
    /// </remarks>
    [Fact]
    public void 同名限定字的來源先合併再配對()
    {
        var match = SqlJoinKeyMatcher.Pair(new[]
        {
            Source("b", "CopyNo"),
            Source("d", "Id"),
            Source("d", "CopyNo")
        });

        Assert.Equal(new[] { "d.CopyNo=b.CopyNo" }, Describe(match));
        Assert.Equal(new[] { 1, 2 }, match.SourceIndexes);
    }

    /// <summary>
    /// 配不到欄位時仍說得出當前對象是哪幾筆來源。
    /// </summary>
    /// <remarks>
    /// 兩件事是分開的：索引講的是「當前對象是誰」，與名稱有沒有交集無關。
    /// 少了這份索引，呼叫端就只能靠限定字去猜，而同一個限定字在敘述裡不一定唯一。
    /// </remarks>
    [Fact]
    public void 配不到時仍說得出當前對象是哪幾筆來源()
    {
        var match = SqlJoinKeyMatcher.Pair(new[] { Source("a", "X"), Source("b", "Y") });

        Assert.True(match.IsEmpty);
        Assert.Equal(new[] { 1 }, match.SourceIndexes);
    }

    /// <summary>
    /// 名稱還不知道的來源要照樣佔一格，否則配對結果指的索引會落在別張表上。
    /// </summary>
    [Fact]
    public void 沒有名稱的來源仍佔一格()
    {
        var match = SqlJoinKeyMatcher.Pair(new[]
        {
            Source("a", "CopyNo"),
            Source("b"),
            Source("c", "CopyNo")
        });

        Assert.Equal(new[] { "c.CopyNo=a.CopyNo" }, Describe(match));
        Assert.Equal(new[] { 2 }, match.SourceIndexes);
    }

    [Fact]
    public void 當前來源沒有名稱時配不出東西()
    {
        Assert.True(SqlJoinKeyMatcher.Pair(new[] { Source("a", "CopyNo"), Source("b") }).IsEmpty);
    }

    /// <remarks>
    /// 正規化之後是空字串，那樣的兩個名稱互相配對等於說「所有名稱都同名」。
    /// </remarks>
    [Fact]
    public void 全部分隔符組成的名稱不參與配對()
    {
        Assert.Empty(Pairs(Source("a", "_"), Source("b", "__")));
    }

    [Fact]
    public void 同一個名稱出現兩次只配一筆()
    {
        Assert.Equal(
            new[] { "b.CopyNo=a.CopyNo" },
            Pairs(Source("a", "CopyNo"), Source("b", "CopyNo", "CopyNo")));
    }

    /// <remarks>
    /// 衍生資料表沒寫別名時限定字讀不出來，條件仍寫得出來，只是冠不上名字。
    /// </remarks>
    [Fact]
    public void 讀不出限定字時仍配得出條件()
    {
        Assert.Equal(
            new[] { "CopyNo=a.CopyNo" },
            Pairs(Source("a", "CopyNo"), Source(null, "CopyNo")));
    }

    /// <summary>正規化：去分隔符、轉小寫，全分隔符的名稱回空字串。</summary>
    [Theory]
    [InlineData("CopyNo", "copyno")]
    [InlineData("COPY_NO", "copyno")]
    [InlineData("Copy#No", "copyno")]
    [InlineData("copy.no", "copyno")]
    [InlineData("copy@no", "copyno")]
    [InlineData("copy$no", "copyno")]
    [InlineData("__", "")]
    public void 正規化去掉分隔符並轉小寫(string name, string expected)
    {
        Assert.Equal(expected, SqlJoinKeyMatcher.Normalize(name));
    }

    [Fact]
    public void 沒有來源清單是呼叫端的錯()
    {
        Assert.Throws<ArgumentNullException>(() => SqlJoinKeyMatcher.Pair(null!));
    }
}
