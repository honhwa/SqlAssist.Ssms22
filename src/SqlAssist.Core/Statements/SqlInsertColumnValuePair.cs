using SqlAssist.Core.Parsing;

namespace SqlAssist.Core.Statements;

/// <summary>同一序位的欄位與值在原文中的位置。</summary>
public readonly struct SqlInsertColumnValuePair
{
    public SqlInsertColumnValuePair(SqlTextSpan column, SqlTextSpan value)
    {
        Column = column;
        Value = value;
    }

    public SqlTextSpan Column { get; }

    public SqlTextSpan Value { get; }
}
