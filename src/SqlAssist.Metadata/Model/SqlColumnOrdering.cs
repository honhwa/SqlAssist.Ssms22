using System;
using System.Collections.Generic;

namespace SqlAssist.Metadata.Model;

/// <summary>
/// 欄位清單的呈現順序。
/// </summary>
/// <remarks>
/// 排在這裡而不是排在建議清單那一層，是因為這一條規則與連線、與 SSMS 都無關，
/// 只讀模型本身——放進 Ssms22 的話就只剩「跑起來看」一種驗證方式。
/// </remarks>
public static class SqlColumnOrdering
{
    /// <summary>
    /// 索引鍵欄位排到最前面，其餘維持原本的資料行順序。
    /// </summary>
    /// <remarks>
    /// 敘述裡只看得到一個資料來源時（<c>SELECT … FROM dbo.Loan WHERE |</c>），
    /// 這個位置要的幾乎都是拿來篩選的那幾欄，而篩選走得動的就是索引鍵。
    /// 其餘欄位不重排：資料表的定義順序是使用者對它的心智模型，
    /// 打散之後反而找不到原本緊鄰的那一欄。
    ///
    /// 沒有索引鍵、或整張表都是索引鍵時直接回傳原來的清單而不是複製一份，
    /// 讓呼叫端可以放心比較參考是否相同；這條路徑在每一次按鍵上。
    /// </remarks>
    public static IReadOnlyList<SqlColumnInfo> IndexKeysFirst(IReadOnlyList<SqlColumnInfo> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        var keys = 0;

        foreach (var column in columns)
        {
            if (column.IsIndexKey)
            {
                keys++;
            }
        }

        if (keys == 0 || keys == columns.Count)
        {
            return columns;
        }

        var ordered = new SqlColumnInfo[columns.Count];
        var next = 0;

        foreach (var column in columns)
        {
            if (column.IsIndexKey)
            {
                ordered[next++] = column;
            }
        }

        foreach (var column in columns)
        {
            if (!column.IsIndexKey)
            {
                ordered[next++] = column;
            }
        }

        return ordered;
    }
}
