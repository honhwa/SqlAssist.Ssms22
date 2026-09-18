using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Metadata.Model;
using Xunit;

namespace SqlAssist.Metadata.Tests.Model;

public sealed class SqlColumnOrderingTests
{
    /// <remarks>
    /// 敘述裡只有一個資料來源時，WHERE 之後要的是拿來篩選的欄位，而篩選走得動的
    /// 就是索引鍵。其餘欄位不重排——資料表的定義順序是使用者對它的心智模型，
    /// 打散之後反而找不到原本緊鄰的那一欄。
    /// </remarks>
    [Fact]
    public void 索引鍵欄位排到最前面而其餘保持資料行順序()
    {
        var ordered = SqlColumnOrdering.IndexKeysFirst(new[]
        {
            Column(1, "CopyNo"),
            Column(2, "ReaderId", isIndexKey: true),
            Column(3, "DueDate"),
            Column(4, "Status", isIndexKey: true),
            Column(5, "Remark")
        });

        Assert.Equal(
            new[] { "ReaderId", "Status", "CopyNo", "DueDate", "Remark" },
            ordered.Select(column => column.Name));
    }

    /// <remarks>
    /// 沒有索引鍵的資料表（堆積）、整張表都是索引鍵，以及空的欄位清單，三者都不必
    /// 搬動任何東西，直接回傳原來的清單——這條路徑在每一次按鍵上，白複製一份
    /// 只是浪費。回傳同一個參考也讓呼叫端可以放心比較。
    /// </remarks>
    [Fact]
    public void 無需重排時回傳同一份清單()
    {
        var plain = new[] { Column(1, "CopyNo"), Column(2, "ReaderId") };
        Assert.Same(plain, SqlColumnOrdering.IndexKeysFirst(plain));

        var allKeys = new[]
        {
            Column(1, "CopyNo", isIndexKey: true),
            Column(2, "ReaderId", isIndexKey: true)
        };
        Assert.Same(allKeys, SqlColumnOrdering.IndexKeysFirst(allKeys));

        IReadOnlyList<SqlColumnInfo> empty = Array.Empty<SqlColumnInfo>();
        Assert.Same(empty, SqlColumnOrdering.IndexKeysFirst(empty));
    }

    /// <summary>單一索引鍵也要搬，而且搬完之後其餘欄位的相對順序不變。</summary>
    [Fact]
    public void 只有一個索引鍵時只搬那一欄()
    {
        var ordered = SqlColumnOrdering.IndexKeysFirst(new[]
        {
            Column(1, "A"),
            Column(2, "B"),
            Column(3, "C", isIndexKey: true),
            Column(4, "D")
        });

        Assert.Equal(new[] { "C", "A", "B", "D" }, ordered.Select(column => column.Name));
    }

    private static SqlColumnInfo Column(int ordinal, string name, bool isIndexKey = false) =>
        new(ordinal, name, "int", isNullable: false, isIndexKey: isIndexKey);
}
