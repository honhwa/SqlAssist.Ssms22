using System;
using System.Collections.Generic;
using System.Text;

namespace SqlAssist.Core.Statements;

/// <summary>
/// 把模組的參數排成一段可以直接執行的 <c>EXEC</c>：先宣告、再呼叫、最後取回輸出。
/// </summary>
/// <remarks>
/// 展開出來的是三段而不是一句，因為「參數要先有變數」這件事在 T-SQL 裡是硬性的：
///
/// <list type="number">
/// <item>
/// 所有參數一律 <c>DECLARE 名稱 AS 型別 = 初始值;</c>。有預設值的用模組的預設值當初始值
/// （省略掉它等於呼叫時就是那個值），其餘依型別給預留值。值擺在宣告而不是擺在呼叫，
/// 是因為呼叫那一行留著變數名稱才看得出「這個值要改」——把 <c>0</c>、<c>N''</c> 直接寫在
/// 呼叫上，看不出哪幾個是預留位置，也看不出哪一個照抄了模組的預設值。
/// </item>
/// <item>
/// 呼叫本身只寫 <c>@參數 = @參數</c>，<c>OUTPUT</c> 的再加一個 <c>OUTPUT</c>；
/// 略過使用者刪掉的變數就等於略過那個參數。
/// </item>
/// <item>
/// 有 <c>OUTPUT</c> 的參數最後用一次 <c>SELECT</c> 列出來——那是使用者要看的結果，
/// 而 SSMS 的「訊息」頁看不到輸出參數的值。
/// </item>
/// </list>
///
/// <c>OUTPUT</c> 只寫在呼叫那一行，不寫在宣告上：那是<b>呼叫端</b>的語意
/// （「這個變數要接回傳值」），不是變數宣告的一部分，寫在 <c>DECLARE</c> 上是語法錯誤。
///
/// 續行對齊到第一個參數所在的欄，而不是固定縮排幾格。代價是名稱長的模組會把整段推向
/// 右邊——<c>EXEC dbo.usp_LoanDetail_ReadOverdueByBranch </c> 一開始就吃掉四十幾欄，
/// 再加上參數與註解很容易越過一般的行寬。換來的是每一列的 <c>@</c> 對齊在同一欄，
/// 掃過去就知道有幾個參數、少填了哪一個。
/// </remarks>
public static class SqlProcedureCallText
{
    /// <summary>
    /// 組出「先宣告所有參數、再具名呼叫、最後 SELECT 出輸出參數」的整段文字。
    /// </summary>
    /// <param name="executeKeyword">使用者原本寫的 <c>EXEC</c> 或 <c>EXECUTE</c>，照原文帶回去。</param>
    /// <param name="qualifiedName">已經加好結構描述與方括號的模組名稱。</param>
    /// <param name="parameters">參數，順序就是輸出順序。</param>
    /// <param name="indent">第二行起每一行的前導文字，通常是 <c>EXEC</c> 那一行的縮排。</param>
    /// <param name="newLine">緩衝區使用的換行字元。</param>
    /// <param name="caretOffset">回傳結果字串中第一個宣告的初始值的位置。</param>
    public static string Build(
        string executeKeyword,
        string qualifiedName,
        IReadOnlyList<SqlStatementParameter> parameters,
        string indent,
        string newLine,
        out int caretOffset)
    {
        if (string.IsNullOrEmpty(executeKeyword))
        {
            throw new ArgumentException("EXEC 關鍵字不可為空。", nameof(executeKeyword));
        }

        if (string.IsNullOrEmpty(qualifiedName))
        {
            throw new ArgumentException("模組名稱不可為空。", nameof(qualifiedName));
        }

        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        if (parameters.Count == 0)
        {
            throw new ArgumentException("沒有參數就不必展開。", nameof(parameters));
        }

        indent ??= string.Empty;
        newLine = string.IsNullOrEmpty(newLine) ? "\r\n" : newLine;

        var assignments = new string[parameters.Count];
        var widest = 0;

        for (var index = 0; index < parameters.Count; index++)
        {
            // 呼叫那一行傳的是變數名稱，OUTPUT 參數要再加一個 OUTPUT：
            // 「把值接回來」是呼叫端的語意，只有寫在這一行的引數上才算數，
            // 寫在宣告裡反而語法錯誤。
            var parameter = parameters[index];
            assignments[index] = parameter.IsOutput
                ? $"{parameter.Name} = {parameter.Name} OUTPUT"
                : $"{parameter.Name} = {parameter.Name}";
            var width = assignments[index].Length + (index == parameters.Count - 1 ? 0 : 1);

            if (width > widest)
            {
                widest = width;
            }
        }

        var builder = new StringBuilder();

        // 每一列宣告的初始值落在同一欄：與呼叫那一段同一個理由，
        // 掃過去就知道有幾個要改的值、改了哪幾個。
        var widestDeclaration = 0;

        for (var index = 0; index < parameters.Count; index++)
        {
            var width = DeclarationHead(parameters[index]).Length;

            if (width > widestDeclaration)
            {
                widestDeclaration = width;
            }
        }

        AppendDeclarations(builder, parameters, widestDeclaration, indent, newLine);

        builder.Append(executeKeyword).Append(' ').Append(qualifiedName).Append(' ');

        // 只補「EXEC 名稱 」那一段的寬度：行首的縮排由下面的 Append(indent) 原樣重複，
        // 縮排裡有定位字元時才不會因為一個定位字元只算一個字元而歪掉。
        var continuation = new string(' ', executeKeyword.Length + 1 + qualifiedName.Length + 1);

        for (var index = 0; index < parameters.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(continuation);
            }

            var assignment = assignments[index] + (index == parameters.Count - 1 ? string.Empty : ",");
            builder.Append(assignment);
            builder.Append(' ', widest - assignment.Length + 1);
            builder.Append("-- ").Append(Comment(parameters[index]));

            if (index != parameters.Count - 1)
            {
                builder.Append(newLine).Append(indent);
            }
        }

        AppendOutputSelect(builder, parameters, indent, newLine);

        caretOffset = CaretOffset(parameters, widestDeclaration);
        return builder.ToString();
    }

    /// <summary>
    /// 游標位置：第一個宣告的初始值上。
    /// </summary>
    /// <remarks>
    /// 從前擺在呼叫那一行的值上，改成宣告之後就該跟著搬過來——那才是使用者要動手的地方。
    ///
    /// 前綴長度直接由第一列的前綴組出來（<c>DECLARE 名稱 AS 型別</c> 加對齊空白加 <c>= </c>），
    /// 不從整段長度往回扣：往回扣要算對最後一列尾巴有幾樣東西，而那取決於縮排是不是空的，
    /// 少扣一項就偏移一格，而偏移一格在畫面上看不太出來。
    /// </remarks>
    private static int CaretOffset(IReadOnlyList<SqlStatementParameter> parameters, int widestDeclaration)
    {
        var head = DeclarationHead(parameters[0]);
        return head.Length + (widestDeclaration - head.Length) + " ".Length + "= ".Length;
    }

    /// <summary>
    /// 所有參數的 <c>DECLARE</c>，一行一個。
    /// </summary>
    /// <remarks>
    /// <c>OUTPUT</c> 參數也在這裡宣告，但<b>不</b>寫 <c>OUTPUT</c>：每一個參數在呼叫端
    /// 都是變數，所以每一個都要先宣告，否則那一句連編譯都過不了；而 <c>OUTPUT</c>
    /// 是呼叫端的語意，屬於 <c>EXEC</c> 那一行。
    /// </remarks>
    private static void AppendDeclarations(
        StringBuilder builder,
        IReadOnlyList<SqlStatementParameter> parameters,
        int widestDeclaration,
        string indent,
        string newLine)
    {
        foreach (var parameter in parameters)
        {
            var head = DeclarationHead(parameter);

            builder
                .Append(head)
                .Append(' ', widestDeclaration - head.Length + 1)
                .Append("= ")
                .Append(InitialValue(parameter))
                .Append(';')
                .Append(newLine)
                .Append(indent);
        }
    }

    /// <summary>
    /// 宣告的前半段，到初始值之前為止。
    /// </summary>
    /// <remarks>
    /// 不寫 <c>OUTPUT</c>：那是<b>呼叫端</b>的語意（「這個變數要接回傳值」），
    /// 不是變數宣告的一部分——<c>DECLARE @x INT OUTPUT</c> 根本是語法錯誤。
    /// 它寫在 <c>EXEC</c> 那一行的引數上。
    ///
    /// 型別前後加 <c>AS</c>：<c>DECLARE @x AS INT</c> 與 <c>DECLARE @x INT</c>
    /// 都合法，寫 <c>AS</c> 是為了讓型別的位置在長度不一的參數名稱之間對齊起來。
    /// </remarks>
    private static string DeclarationHead(SqlStatementParameter parameter)
    {
        return $"DECLARE {parameter.Name} AS {parameter.DataType}";
    }

    /// <summary>宣告的初始值：模組的預設值優先，讀不到才依型別給預留值。</summary>
    private static string InitialValue(SqlStatementParameter parameter)
    {
        return parameter.DefaultValue ?? SqlLiteralDefaults.ForType(parameter.DataType);
    }

    /// <summary>
    /// 輸出參數用一次 <c>SELECT</c> 列出來。
    /// </summary>
    /// <remarks>
    /// 沒有輸出參數時整段略過：多一個空的 <c>SELECT</c> 只會讓展開出來的東西更長。
    ///
    /// 一行一個而不是排成好幾欄：輸出的欄位通常只有兩三個，擠在同一行看不出
    /// 哪個變數對到哪個名稱，而註解裡的型別正是要拿來對照的。
    /// </remarks>
    private static void AppendOutputSelect(
        StringBuilder builder,
        IReadOnlyList<SqlStatementParameter> parameters,
        string indent,
        string newLine)
    {
        var outputs = 0;

        foreach (var parameter in parameters)
        {
            if (parameter.IsOutput)
            {
                outputs++;
            }
        }

        if (outputs == 0)
        {
            return;
        }

        // SELECT 與 EXEC 之間空一行：兩者是獨立的兩段，黏在一起時那個 SELECT
        // 看起來像是呼叫的一部分。
        builder.Append(newLine).Append(indent).Append(newLine).Append(indent).Append("SELECT ");

        var written = 0;

        foreach (var parameter in parameters)
        {
            if (!parameter.IsOutput)
            {
                continue;
            }

            written++;
            builder
                .Append(parameter.Name)
                .Append(" AS ")
                .Append(parameter.Name.TrimStart('@'))
                .Append(written == outputs ? ";" : ",")
                .Append(newLine)
                .Append(indent)
                .Append("       ");
        }

        // 上面每一列都多補了續行的縮排，把最後一列尾巴那一段收掉。
        builder.Length -= newLine.Length + indent.Length + "       ".Length;
    }

    /// <remarks>
    /// OUTPUT 不寫進註解——那個字已經在左邊的程式碼裡了。「選擇性」則沒有別的地方看得到，
    /// 而它正是使用者要決定「這一列能不能整列刪掉」的依據。
    /// </remarks>
    private static string Comment(SqlStatementParameter parameter)
    {
        return parameter.IsOptional
            ? parameter.DataType + "，選擇性"
            : parameter.DataType;
    }
}
