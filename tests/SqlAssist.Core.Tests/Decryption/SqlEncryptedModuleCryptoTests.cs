using System;
using System.Text;
using SqlAssist.Core.Decryption;
using Xunit;

namespace SqlAssist.Core.Tests.Decryption;

/// <summary>
/// 加密模組文字的還原演算法。用固定的替代金流模擬 SQL Server：重點不是金流內容，
/// 而是同一個物件永遠得到同一段金流，與本文內容及長度無關——這正是整個技巧的依據。
/// </summary>
public sealed class SqlEncryptedModuleCryptoTests
{
    private static byte[] Encrypt(string plain, int seed)
    {
        var bytes = Encoding.Unicode.GetBytes(plain);
        var cipher = new byte[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            cipher[i] = (byte)(bytes[i] ^ (byte)((seed * 31) + (i * 7) + 13));
        return cipher;
    }

    /// <summary>伺服器端Submit的是 ALTER，儲存的卻是 CREATE 形式；所有往返都走這裡。</summary>
    private static byte[] StoreDummy(string body, int seed) =>
        Encrypt(SqlEncryptedModuleCrypto.StoredPlaintext(body)!, seed);

    [Fact]
    public void 兩份密文可以還原原始定義()
    {
        const string original = "CREATE PROCEDURE [dbo].[Borrow_Reader] @id int WITH ENCRYPTION AS SELECT * FROM dbo.Cat_BookCopy WHERE CopyNo = @id";
        var body = SqlEncryptedModuleCrypto.BuildDummyBody("P", "dbo", "Borrow_Reader", null, null, original.Length);

        var recovered = SqlEncryptedModuleCrypto.Xor(
            Encrypt(original, 42), StoreDummy(body!, 42), SqlEncryptedModuleCrypto.StoredPlaintext(body));

        Assert.Equal(original, recovered);
    }

    [Fact]
    public void 用ALTER形式當遮罩會得到垃圾()
    {
        // 回歸測試：ALTER 比 CREATE 少一個字，前五個字對、之後全部錯一位，而且仍舊回傳字串。
        const string original = "CREATE PROCEDURE [dbo].[Borrow_Reader] WITH ENCRYPTION AS SELECT 1";
        var body = SqlEncryptedModuleCrypto.BuildDummyBody("P", "dbo", "Borrow_Reader", null, null, original.Length);

        var wrong = SqlEncryptedModuleCrypto.Xor(
            Encrypt(original, 11), StoreDummy(body!, 11), SqlEncryptedModuleCrypto.AlterStatement(body));

        Assert.NotEqual(original, wrong);
        Assert.False(SqlEncryptedModuleCrypto.LooksLikeModuleDefinition(wrong, "Borrow_Reader"));
    }

    [Fact]
    public void 非英文字與多行文字可以還原()
    {
        var original = "CREATE VIEW [dbo].[Lib_TagViewSecret] WITH ENCRYPTION AS\r\n  SELECT N'Grüße — ünïcode' AS Note\r\n  FROM dbo.Lib_Tag";
        var body = SqlEncryptedModuleCrypto.BuildDummyBody("V", "dbo", "Lib_TagViewSecret", null, null, original.Length);

        Assert.Equal(original, SqlEncryptedModuleCrypto.Xor(
            Encrypt(original, 7), StoreDummy(body!, 7), SqlEncryptedModuleCrypto.StoredPlaintext(body)));
    }

    [Fact]
    public void 假定義比原文長仍然完整還原()
    {
        const string original = "CREATE VIEW v WITH ENCRYPTION AS SELECT 1 x";
        var body = SqlEncryptedModuleCrypto.BuildDummyBody("V", "dbo", "v", null, null, original.Length);
        var stored = SqlEncryptedModuleCrypto.StoredPlaintext(body)!;

        Assert.True(stored.Length > original.Length);
        Assert.Equal(original, SqlEncryptedModuleCrypto.Xor(Encrypt(original, 3), StoreDummy(body!, 3), stored));
    }

    [Fact]
    public void 假定義比原文短時不回傳部分結果()
    {
        const string original = "CREATE PROCEDURE [dbo].[Borrow_Reader] WITH ENCRYPTION AS SELECT 1";
        const string dummy = "ALTER PROCEDURE [dbo].[Borrow_Reader] AS RETURN";

        Assert.Null(SqlEncryptedModuleCrypto.Xor(Encrypt(original, 1), Encrypt(dummy, 1), dummy));
    }

    [Fact]
    public void 輸入不足時不回傳結果()
    {
        Assert.Null(SqlEncryptedModuleCrypto.Xor(null, new byte[4], "ab"));
        Assert.Null(SqlEncryptedModuleCrypto.Xor(new byte[4], null, "ab"));
        Assert.Null(SqlEncryptedModuleCrypto.Xor(Array.Empty<byte>(), new byte[4], "ab"));
    }

    [Fact]
    public void 執行用ALTER而Xor用CREATE()
    {
        var body = SqlEncryptedModuleCrypto.BuildDummyBody("P", "dbo", "Borrow_Reader", null, null, 0);

        Assert.Equal("ALTER PROCEDURE [dbo].[Borrow_Reader] WITH ENCRYPTION AS RETURN", SqlEncryptedModuleCrypto.AlterStatement(body));
        Assert.Equal("CREATE PROCEDURE [dbo].[Borrow_Reader] WITH ENCRYPTION AS RETURN", SqlEncryptedModuleCrypto.StoredPlaintext(body));
    }

    [Theory]
    [InlineData("P", " PROCEDURE [dbo].[Borrow_Reader] WITH ENCRYPTION AS RETURN")]
    [InlineData("V", " VIEW [dbo].[Borrow_Reader] WITH ENCRYPTION AS SELECT 1 AS c")]
    [InlineData("FN", " FUNCTION [dbo].[Borrow_Reader]() RETURNS int WITH ENCRYPTION AS BEGIN RETURN 1 END")]
    [InlineData("IF", " FUNCTION [dbo].[Borrow_Reader]() RETURNS TABLE WITH ENCRYPTION AS RETURN SELECT 1 AS c")]
    [InlineData("TF", " FUNCTION [dbo].[Borrow_Reader]() RETURNS @r TABLE (c int) WITH ENCRYPTION AS BEGIN RETURN END")]
    public void 每種物件類型用自己的假定義形狀(string type, string expected)
    {
        Assert.Equal(expected, SqlEncryptedModuleCrypto.BuildDummyBody(type, "dbo", "Borrow_Reader", null, null, 0));
    }

    [Fact]
    public void 觸發程序帶出所屬資料表()
    {
        var body = SqlEncryptedModuleCrypto.BuildDummyBody("TR", "dbo", "tr_LoanAudit", "library", "Loan", 0);
        Assert.Equal(" TRIGGER [dbo].[tr_LoanAudit] ON [library].[Loan] WITH ENCRYPTION AFTER INSERT AS RETURN", body);
    }

    [Fact]
    public void 不知道所屬資料表的觸發程序不猜()
    {
        Assert.Null(SqlEncryptedModuleCrypto.BuildDummyBody("TR", "dbo", "tr_LoanAudit", null, null, 0));
        Assert.Null(SqlEncryptedModuleCrypto.BuildDummyBody("U", "dbo", "t", null, null, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(40)]
    public void 補白後的CREATE形式正好到目標長度(int extra)
    {
        var minimum = SqlEncryptedModuleCrypto.StoredPlaintext(
            SqlEncryptedModuleCrypto.BuildDummyBody("P", "dbo", "Borrow_Reader", null, null, 0))!;
        var target = minimum.Length + extra;

        var padded = SqlEncryptedModuleCrypto.StoredPlaintext(
            SqlEncryptedModuleCrypto.BuildDummyBody("P", "dbo", "Borrow_Reader", null, null, target))!;

        Assert.Equal(target, padded.Length);
        Assert.StartsWith(minimum, padded, StringComparison.Ordinal);
    }

    [Fact]
    public void 名稱中的括號被跳脫()
    {
        Assert.Equal("[dbo].[x]] DROP TABLE y --]", SqlEncryptedModuleCrypto.Quote("dbo", "x] DROP TABLE y --"));
        Assert.Equal("[p]", SqlEncryptedModuleCrypto.Quote(null, "p"));
    }

    [Fact]
    public void 假定義內嵌的名稱也跳脫()
    {
        var body = SqlEncryptedModuleCrypto.BuildDummyBody("P", "dbo", "x] DROP TABLE y --", null, null, 0);
        Assert.Equal("ALTER PROCEDURE [dbo].[x]] DROP TABLE y --] WITH ENCRYPTION AS RETURN",
            SqlEncryptedModuleCrypto.AlterStatement(body));
    }

    [Theory]
    [InlineData("CREATE PROCEDURE [dbo].[Borrow_Reader] AS SELECT 1")]
    [InlineData("  \r\n ALTER PROC dbo.Borrow_Reader AS SELECT 1")]
    [InlineData("-- header comment\r\nCREATE PROCEDURE dbo.Borrow_Reader AS SELECT 1")]
    [InlineData("/* header\r\n   block */ CREATE PROCEDURE dbo.Borrow_Reader AS SELECT 1")]
    public void 真正的模組定義通過驗證(string text) =>
        Assert.True(SqlEncryptedModuleCrypto.LooksLikeModuleDefinition(text, "Borrow_Reader"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("亂碼 garbage that decrypted wrong")]
    [InlineData("SELECT 1 FROM dbo.Borrow_Reader")]
    [InlineData("CREATEPROCEDURE dbo.Borrow_Reader AS SELECT 1")]
    [InlineData("-- only a comment")]
    public void 其他文字不通過驗證(string? text) =>
        Assert.False(SqlEncryptedModuleCrypto.LooksLikeModuleDefinition(text, "Borrow_Reader"));

    [Fact]
    public void 別的物件的定義不通過驗證() =>
        Assert.False(SqlEncryptedModuleCrypto.LooksLikeModuleDefinition(
            "CREATE PROCEDURE dbo.Loan_Other AS SELECT 1", "Borrow_Reader"));

    [Fact]
    public void 只改開頭的ALTER為CREATE()
    {
        Assert.Equal("CREATE PROCEDURE dbo.Borrow_Reader AS ALTER TABLE dbo.Loan ADD c int",
            SqlEncryptedModuleCrypto.NormalizeToCreate(
                "ALTER PROCEDURE dbo.Borrow_Reader AS ALTER TABLE dbo.Loan ADD c int"));

        const string script = "CREATE PROCEDURE dbo.Borrow_Reader AS SELECT 1";
        Assert.Equal(script, SqlEncryptedModuleCrypto.NormalizeToCreate(script));
    }

    [Fact]
    public void 密文區塊照valnum順序合併()
    {
        var joined = SqlEncryptedModuleCrypto.Concat(
            new[] { new byte[] { 1, 2 }, new byte[] { 3 }, Array.Empty<byte>(), new byte[] { 4, 5 } });
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, joined);
        Assert.Null(SqlEncryptedModuleCrypto.Concat(null));
    }
}
