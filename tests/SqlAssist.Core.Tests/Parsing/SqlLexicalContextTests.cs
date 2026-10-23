using SqlAssist.Core.Parsing;
using Xunit;

namespace SqlAssist.Core.Tests.Parsing;

/// <summary>
/// 游標落在哪一種語彙狀態：程式碼、字串、註解，還是加引號的名稱。
/// </summary>
/// <remarks>
/// 這裡釘住的是「游標在哪裡」這個問題本身。至於那個答案要不要把建議清單收掉，
/// 是 <c>SqlAsyncCompletionItemManager</c> 與 <c>SqlCompletionReopen</c> 的事。
///
/// 兩個判斷的差別正是這一頁存在的理由：方括號與雙引號名稱<b>不是</b>程式碼，
/// 但裡面放的是物件名稱，打 <c>FROM [Lib</c> 打到一半不該把清單收掉。
/// 用「不是程式碼」當判準的症狀就是那個。
/// </remarks>
public sealed class SqlLexicalContextTests
{
    /// <remarks>
    /// 位置以 <c>|</c> 標記，測試裡先把它拿掉再換算成索引：
    /// <c>SELECT ''</c> 的游標落在兩個單引號之間，那正是自動配對補完之後的位置。
    /// </remarks>
    private static (string Sql, int Position) At(string sqlWithCaret)
    {
        var position = sqlWithCaret.IndexOf('|');

        return (sqlWithCaret.Remove(position, 1), position);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Loan WHERE Name = '|'")]
    [InlineData("SELECT * FROM dbo.Loan WHERE Name = 'abc|def'")]
    [InlineData("SELECT * FROM dbo.Loan WHERE Name = 'abc|")]
    [InlineData("SELECT 'It''s |here'")]
    [InlineData("-- 註解裡的 publ|")]
    [InlineData("/* 區塊註解裡的 publ| */")]
    public void 字串與註解裡不算程式碼(string sqlWithCaret)
    {
        var (sql, position) = At(sqlWithCaret);

        Assert.False(SqlLexicalContext.IsCode(sql, position));
        Assert.True(SqlLexicalContext.IsInStringOrComment(sql, position));
    }

    /// <remarks>
    /// 收掉清單的判準比「不是程式碼」窄：加引號的名稱裡是物件名稱，
    /// 打到一半把清單收掉等於逼使用者把整個名稱打完。
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM [Lib|")]
    [InlineData("SELECT * FROM \"Lib|")]
    [InlineData("SELECT * FROM dbo.|Lib_Reader")]
    [InlineData("SELECT * FROM dbo.Loan|")]
    [InlineData("SELECT * FROM dbo.Loan WHERE Name = 'abc'|")]
    [InlineData("SELECT * FROM dbo.Loan /* 註解 */|")]
    public void 程式碼與加引號的名稱不算字串(string sqlWithCaret)
    {
        var (sql, position) = At(sqlWithCaret);

        Assert.False(SqlLexicalContext.IsInStringOrComment(sql, position));
    }

    /// <remarks>
    /// 關掉的那個單引號不算結束：<c>'abc'</c> 的游標停在右引號之後是程式碼，
    /// 而右引號<b>之前</b>仍然是字串。這兩者的差別就是整條規則的邊界。
    /// </remarks>
    [Fact]
    public void 收尾的單引號之後恢復程式碼()
    {
        Assert.True(SqlLexicalContext.IsInStringOrComment("SELECT 'abc", 11));
        Assert.False(SqlLexicalContext.IsInStringOrComment("SELECT 'abc'", 12));
    }

    /// <remarks>
    /// 行註解到換行就結束，換行之後又回到程式碼——少了這一條，游標停在檔案裡
    /// 第一個註解之後的所有位置都會被當成「在註解裡」。
    /// </remarks>
    [Fact]
    public void 行註解到換行為止()
    {
        const string sql = "-- 說明\nFROM dbo.Loan";

        Assert.True(SqlLexicalContext.IsInStringOrComment(sql, 5));
        Assert.False(SqlLexicalContext.IsInStringOrComment(sql, sql.Length));
    }

    [Theory]
    [InlineData("SELECT 1", 0)]
    [InlineData("SELECT 1", 8)]
    public void 程式碼本身不算字串(string sql, int position)
    {
        Assert.False(SqlLexicalContext.IsInStringOrComment(sql, position));
    }
}
