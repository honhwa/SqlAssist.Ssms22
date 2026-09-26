using SqlAssist.Core.Completion;
using Xunit;

namespace SqlAssist.Core.Tests.Completion;

/// <summary>
/// 自動別名的取名規則。
/// </summary>
/// <remarks>
/// 只測「名字怎麼來」與「撞名怎麼排」。**要不要接、接在哪裡**不在這裡：
/// 那是 <c>SqlFunctionCallInsertion</c> 的模式與三個呼叫端的事，
/// 由 <see cref="SqlInsertionTextTests"/> 釘住。
/// </remarks>
public sealed class SqlAutoAliasTests
{
    /// <remarks>
    /// 分段看的是底線、空白、連字號、點號與大小寫交替，各段取第一個字母小寫。
    /// </remarks>
    [Theory]
    [InlineData("Lib_Reader", "lr")]
    [InlineData("LoanDetail", "ld")]
    [InlineData("Cat_BookCopy", "cbc")]
    [InlineData("HTTPServer", "hs")]
    [InlineData("Loan", "l")]
    public void 各段首字母小寫(string name, string expected)
    {
        Assert.Equal(expected, SqlAutoAlias.Create(name));
    }

    /// <remarks>
    /// 函式前綴留著的話，每一個資料表值函式的別名都以 <c>f</c> 或 <c>t</c> 開頭，
    /// 而 <c>flbr</c> 與 <c>fll</c> 要看出差在哪裡得數到第三個字母。
    /// </remarks>
    [Theory]
    [InlineData("fn_LoansByReader", "lbr")]
    [InlineData("ufn_LoanList", "ll")]
    [InlineData("ifn_BranchStat", "bs")]
    [InlineData("tf_GetStock", "gs")]
    [InlineData("FN_LoanList", "ll")]
    public void 函式前綴先去掉再取首字母(string name, string expected)
    {
        Assert.Equal(expected, SqlAutoAlias.Create(name));
    }

    /// <remarks>
    /// 整串就是前綴本身時不去掉：去掉之後一個字都不剩，而空別名會讓呼叫端整個放棄，
    /// 使用者於是在那個位置連一個別名都拿不到。
    /// </remarks>
    [Theory]
    [InlineData("fn_", "f")]
    [InlineData("tf_", "t")]
    public void 整串只有前綴時不去掉(string name, string expected)
    {
        Assert.Equal(expected, SqlAutoAlias.Create(name));
    }

    /// <remarks>
    /// 名稱裡一個字母都沒有時退回「去掉分隔符後的小寫」：<c>[2024]</c> 這種名稱
    /// 在真實的資料庫裡存在，而沒有別名比一個不好看的別名糟。
    /// </remarks>
    [Theory]
    [InlineData("2024", "2024")]
    [InlineData("fn_2024", "2024")]
    [InlineData("[2024]", "2024")]
    public void 沒有字母時退回數字(string name, string expected)
    {
        Assert.Equal(expected, SqlAutoAlias.Create(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空名稱給空別名(string? name)
    {
        Assert.Equal(string.Empty, SqlAutoAlias.Create(name!));
    }

    /// <remarks>
    /// 比對不區分大小寫：T-SQL 的識別碼在大多排序規則下就是這樣，
    /// 而 <c>LR</c> 與 <c>lr</c> 並存只是看起來像兩個東西。
    /// </remarks>
    [Fact]
    public void 撞名時往後加序號()
    {
        Assert.Equal("lr", SqlAutoAlias.MakeUnique("lr", new[] { "l" }));
        Assert.Equal("lr2", SqlAutoAlias.MakeUnique("lr", new[] { "LR" }));
        Assert.Equal("lr3", SqlAutoAlias.MakeUnique("lr", new[] { "lr", "LR2" }));
    }
}
