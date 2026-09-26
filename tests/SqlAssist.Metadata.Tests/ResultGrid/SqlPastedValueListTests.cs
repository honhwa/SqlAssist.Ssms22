using System;
using System.Collections.Generic;
using SqlAssist.Metadata.ResultGrid;
using Xunit;

namespace SqlAssist.Metadata.Tests.ResultGrid;

/// <summary>
/// 剪貼簿的一欄值寫成 <c>IN</c> 條件或值清單。
/// </summary>
/// <remarks>
/// 測的是「跑得動而答案是錯的」那一類：少了引號不會有編譯或執行錯誤，
/// 只是條件永遠比不到那一列，而畫面上完全看不出來。排版也一起釘住——
/// 兩個形狀共用同一份縮排與逗號規則，分歧時只會在其中一個入口發作。
/// </remarks>
public sealed class SqlPastedValueListTests
{
    private const string Nl = "\n";

    private static IReadOnlyList<string> Read(string text)
    {
        Assert.True(SqlPastedValueList.TryRead(text, out var literals, out var failure), failure);
        return literals;
    }

    private static string ReadFailure(string? text)
    {
        Assert.False(SqlPastedValueList.TryRead(text, out _, out var failure));
        return failure;
    }

    private static string Build(
        string text,
        SqlPasteShape shape = SqlPasteShape.InPredicate,
        string indent = "",
        string unit = "    ",
        string newLine = Nl) =>
        SqlPastedValueList.Build(Read(text), shape, indent, unit, newLine);

    [Fact]
    public void EveryLineBecomesOneValue()
    {
        Assert.Equal(new[] { "N'A01'", "N'B02'", "3.5" }, Read("A01\nB02\n3.5"));
    }

    /// <remarks>
    /// 這一組不加引號是需求本身：貼進數值欄位時加了引號要靠隱含轉換，
    /// 而含索引的欄位逐列轉型。
    /// </remarks>
    [Theory]
    [InlineData("3.5")]
    [InlineData("42")]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("-0.25")]
    public void PlainNumbersKeepNoQuotes(string value)
    {
        Assert.Equal(value, Assert.Single(Read(value)));
    }

    /// <remarks>
    /// 每一個都要加引號，理由都是「不加會安靜地換一個值」：<c>007</c> 變成 7、
    /// <c>2024-03-04</c> 是減法算出來 2017、<c>1,5</c> 變成兩個值而整段
    /// 參數個數不符。加引號最多是多一次隱含轉換，兩邊代價不對稱。
    /// </remarks>
    [Theory]
    [InlineData("007")]
    [InlineData("0912")]
    [InlineData("00")]
    [InlineData("2024-03-04")]
    [InlineData("1e5")]
    [InlineData("0x1F")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1,5")]
    [InlineData("1 234")]
    [InlineData("3.5.1")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData("１２３")]
    public void LeadingZerosDatesAndExponentsStayStrings(string value)
    {
        Assert.Equal(SqlValueLiteral.Text(value), Assert.Single(Read(value)));
    }

    [Fact]
    public void SingleQuotesAreDoubled()
    {
        Assert.Equal("N'O''Brien'", Assert.Single(Read("O'Brien")));
    }

    [Fact]
    public void NonLatinTextKeepsTheNPrefix()
    {
        Assert.Equal("N'借閱'", Assert.Single(Read("借閱")));
    }

    /// <remarks>
    /// 剪貼簿上的文字沒有欄位型別，<c>NULL</c> 這四個字分不出「真的 NULL」與
    /// 「內容剛好是 NULL 的字串」。當字串至少比得到東西；當成關鍵字的話
    /// 條件恆為 UNKNOWN，而使用者看到的是「明明有這一列卻查不到」。
    /// </remarks>
    [Fact]
    public void LiteralNullIsAString()
    {
        Assert.Equal("N'NULL'", Assert.Single(Read("NULL")));
    }

    [Fact]
    public void BlankLinesAreSkipped()
    {
        Assert.Equal(new[] { "N'A01'", "N'B02'" }, Read("A01\n\n   \nB02"));
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        Assert.Equal(new[] { "N'A01'", "N'B02'" }, Read("  A01  \r\n  B02\t"));
    }

    /// <remarks>
    /// 行首的定位字元是「第一格是空的」的意思，也就是真的多欄——從 Excel 複製
    /// 單一欄時不會出現。它與行內的空白不同，所以不當成縮排吃掉。
    /// </remarks>
    [Fact]
    public void LeadingTabIsTreatedAsASecondColumn()
    {
        Assert.Contains("多欄", ReadFailure("\tB02"));
    }

    /// <remarks>CRLF 算一次換行；只認 CR 或只認 LF 的來源也要切得開。</remarks>
    [Theory]
    [InlineData("A01\r\nB02")]
    [InlineData("A01\rB02")]
    [InlineData("A01\nB02")]
    public void EveryLineBreakStyleSplits(string text)
    {
        Assert.Equal(new[] { "N'A01'", "N'B02'" }, Read(text));
    }

    /// <remarks>
    /// 多欄就不做，不是取第一欄：取第一欄會安靜地丟掉其餘欄位的值，
    /// 而使用者以為整個選取範圍都貼上去了。
    /// </remarks>
    [Fact]
    public void MultiColumnContentIsRejected()
    {
        Assert.Contains("多欄", ReadFailure("A01\tB02\nA03\tB04"));
    }

    /// <remarks>
    /// 選取一個矩形範圍時，尾端空的欄位只會多出定位字元，那不是多欄——
    /// 為了幾個尾端的空欄整段拒絕，等於逼使用者回去重新精準地選一次。
    /// </remarks>
    [Fact]
    public void TrailingEmptyCellsAreNotMultiColumn()
    {
        Assert.Equal("N'A01'", Assert.Single(Read("A01\t\t")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData("\t\t")]
    public void NothingUsableExplainsItself(string? text)
    {
        Assert.Contains("沒有可用的值", ReadFailure(text));
    }

    [Fact]
    public void InPredicateWrapsEachValueOnItsOwnLine()
    {
        Assert.Equal("IN (\n    N'A01',\n    N'B02'\n)", Build("A01\nB02"));
    }

    /// <remarks>T-SQL 不接受右括號前面多出來的那一個逗號。</remarks>
    [Fact]
    public void LastValueHasNoTrailingComma()
    {
        Assert.Equal("IN (\n    N'A01'\n)", Build("A01"));
    }

    /// <remarks>
    /// 值清單那一個前後各留一個換行與一層內縮：使用者已經打好的右括號
    /// 會落在述詞那一行的縮排上，不必再自己排版。
    /// </remarks>
    [Fact]
    public void ValuesOnlyLeavesThePunctuationToTheCaller()
    {
        Assert.Equal("\n    N'A01',\n    N'B02'\n", Build("A01\nB02", SqlPasteShape.ValuesOnly));
    }

    [Fact]
    public void IndentFollowsTheStatement()
    {
        Assert.Equal(
            "IN (\n    N'A01',\n    N'B02'\n  )",
            Build("A01\nB02", indent: "  ", unit: "  "));
    }

    [Fact]
    public void ValuesOnlyClosesAtTheStatementIndent()
    {
        Assert.Equal(
            "\n        N'A01'\n    ",
            Build("A01", SqlPasteShape.ValuesOnly, indent: "    ", unit: "    "));
    }

    [Fact]
    public void NewLineFollowsTheFile()
    {
        Assert.Equal(
            "IN (\r\n    N'A01',\r\n    N'B02'\r\n)",
            Build("A01\nB02", newLine: "\r\n"));
    }

    /// <remarks>
    /// 空清單排出來的是「IN ()」這種語法錯誤，而它唯一的來源是呼叫端漏了
    /// <see cref="SqlPastedValueList.TryRead"/> 的失敗判斷，所以大聲失敗。
    /// </remarks>
    [Fact]
    public void EmptyListIsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => SqlPastedValueList.Build(
            Array.Empty<string>(), SqlPasteShape.InPredicate, string.Empty, "    ", Nl));
    }
}
