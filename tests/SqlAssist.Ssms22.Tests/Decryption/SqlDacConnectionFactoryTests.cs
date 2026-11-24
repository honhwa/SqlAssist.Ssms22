using System;
using System.Data.SqlClient;
using SqlAssist.Ssms22.Decryption;
using Xunit;

namespace SqlAssist.Ssms22.Tests.Decryption;

public sealed class SqlDacConnectionFactoryTests
{
    /// <remarks>
    /// SSMS 交出來的連線字串用過帶空格的關鍵字；net48 的 builder 在建構時就拋
    /// 「不支援關鍵字」，解密因此在開 DAC 之前就失敗。這裡鎖住正規化後仍能建出
    /// 正確的 DAC 連線字串。
    /// </remarks>
    [Theory]
    [InlineData("Data Source=SQL01;Multiple Active Result Sets=True;Initial Catalog=Db;", "SQL01")]
    [InlineData("Data Source=tcp:SQL01,1433;multiple   active   result   sets=True;", "SQL01")]
    [InlineData("Data Source=SQL01;MultipleActiveResultSets=True;", "SQL01")]
    [InlineData("Data Source=SQL01;Trust Server Certificate=True;Encrypt=Mandatory;", "SQL01")]
    [InlineData("Server=SQL01;Trusted_Connection=Yes;TrustServerCertificate=False;", "SQL01")]
    [InlineData("Data Source=SQL01;Authentication=Active Directory Interactive;Column Encryption Setting=Enabled;", "SQL01")]
    public void SSMS的空格與專用關鍵字仍可轉成DAC連線字串(string baseConnectionString, string expectedServer)
    {
        var normalized = SqlDacConnectionFactory.NormalizeKeywords(baseConnectionString);
        var builder = new SqlConnectionStringBuilder(normalized);

        Assert.Equal(expectedServer, SqlDacConnectionFactory.StripDacUnsupportedParts(builder.DataSource));
        Assert.DoesNotContain("Multiple Active Result Sets", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Trust Server Certificate", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authentication", normalized, StringComparison.OrdinalIgnoreCase);

        var dac = new SqlConnectionStringBuilder(
            SqlDacConnectionFactory.BuildConnectionString(baseConnectionString, "Db"));

        Assert.StartsWith("ADMIN:", dac.DataSource);
        Assert.Equal(expectedServer, SqlDacConnectionFactory.StripDacUnsupportedParts(dac.DataSource));
        Assert.False(dac.Pooling);
        Assert.False(dac.MultipleActiveResultSets);
    }

    [Fact]
    public void SSMS的引號值含分號仍可解析()
    {
        const string source = "Data Source=\"SQL01;second\";Initial Catalog=\"Db;one\";";
        var normalized = SqlDacConnectionFactory.NormalizeKeywords(source);
        var builder = new SqlConnectionStringBuilder(normalized);

        Assert.Equal("SQL01;second", builder.DataSource);
        Assert.Equal("Db;one", builder.InitialCatalog);
    }
}
