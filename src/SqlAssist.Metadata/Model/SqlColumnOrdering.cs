using System;
using System.Collections.Generic;

namespace SqlAssist.Metadata.Model;

/// <summary>
/// 欄位清單的排序規則。
/// </summary>
/// <remarks>
/// 只做「把索引鍵移到前面」這一件事，不動其他欄位的相對順序——兩趟填入而不是排序，
/// 是因為搬移的依據只有「是不是索引鍵」一個位元，而原地排序會把資料表中的欄位順序
/// 打亂，那個順序本身對使用者是有意義的（他習慣的欄位就排在那裡）。
/// </remarks>
public static class SqlColumnOrdering
{
    /// <summary>
    /// 把索引鍵欄位排到最前面。
    /// </summary>
    /// <remarks>
    /// 用在「單一來源、述詞起點」的欄位清單上：那時使用者要接的是兩個來源，
    /// 而索引鍵九成是接點。於是 <c>ON </c> 之後第一眼看到的就是可用的那幾個。
    ///
    /// 一個索引鍵都沒有、或<b>全部</b>都是索引鍵時原樣回傳（不複製）：
    /// 前者沒東西搬，後者搬了也是同一份順序。這兩個情形很常見，而這條路徑在
    /// 每一次按鍵上，所以先數一遍再決定要不要配置。
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
