using System.Linq;
using SqlAssist.Metadata.Model;
using Xunit;

namespace SqlAssist.Metadata.Tests.Model;

/// <summary>
/// 欄位清單的排序：只把索引鍵搬到前面，其餘的相對順序不動。
/// </summary>
/// <remarks>
/// 搬移的依據只有「是不是索引鍵」一個位元，所以是兩趟填入而不是排序——原地排序
/// 會把資料表的定義順序打亂，而那個順序本身對使用者是有意義的。
/// </remarks>
public sealed class SqlColumnOrderingTests
{
    private static SqlColumnInfo Column(string name, bool isIndexKey = false)
    {
        return new SqlColumnInfo(1, name, "int", isNullable: false, isIndexKey: isIndexKey);
    }

    [Fact]
    public void 索引鍵排到最前面其餘照原順序()
    {
        var columns = new[]
        {
            Column("Note"),
            Column("CopyNo", isIndexKey: true),
            Column("Amount"),
            Column("Code", isIndexKey: true)
        };

        Assert.Equal(
            new[] { "CopyNo", "Code", "Note", "Amount" },
            SqlColumnOrdering.IndexKeysFirst(columns).Select(column => column.Name));
    }

    /// <summary>
    /// 一個索引鍵都沒有時原樣回傳同一份：這條路徑在每一次按鍵上，不必為沒東西搬的
    /// 情形配置一份新陣列。
    /// </summary>
    [Fact]
    public void 沒有索引鍵時原樣回傳()
    {
        var columns = new[] { Column("CopyNo"), Column("Code") };

        Assert.Same(columns, SqlColumnOrdering.IndexKeysFirst(columns));
    }

    /// <remarks>全部都是索引鍵時搬了也是同一份順序。</remarks>
    [Fact]
    public void 全部都是索引鍵時原樣回傳()
    {
        var columns = new[] { Column("CopyNo", isIndexKey: true), Column("Code", isIndexKey: true) };

        Assert.Same(columns, SqlColumnOrdering.IndexKeysFirst(columns));
    }
}
