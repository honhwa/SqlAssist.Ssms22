using System;
using System.Collections.Generic;
using System.Text;

namespace SqlAssist.Core.Statements;

/// <summary>
/// 把模組的參數排成一整句具名傳值的 <c>EXEC</c>。
/// </summary>
/// <remarks>
/// 續行對齊到第一個參數所在的欄，而不是固定縮排幾格。代價是名稱長的模組會把整段推向
/// 右邊——<c>EXEC dbo.usp_Announcement_ReadByDepartment </c> 一開始就吃掉四十幾欄，
/// 再加上參數與註解很容易越過一般的行寬。換來的是每一列的 <c>@</c> 對齊在同一欄，
/// 掃過去就知道有幾個參數、少填了哪一個。
/// </remarks>
public static class SqlProcedureCallText
{
    /// <summary>
    /// 組出 <c>DECLARE …</c> ＋ <c>EXEC 名稱 @參數 = 值, …</c> ＋ <c>SELECT …</c>。
    /// </summary>
    /// <param name="executeKeyword">使用者原本寫的 <c>EXEC</c> 或 <c>EXECUTE</c>，照原文帶回去。</param>
    /// <param name="qualifiedName">已經加好結構描述與方括號的模組名稱。</param>
    /// <param name="parameters">參數，順序就是輸出順序。</param>
    /// <param name="indent">第二行起每一行的前導文字，通常是 <c>EXEC</c> 那一行的縮排。</param>
    /// <param name="newLine">緩衝區使用的換行字元。</param>
    /// <param name="caretOffset">回傳結果字串中第一個參數的值的位置。</param>
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
            assignments[index] = Assignment(parameters[index]);
            var width = assignments[index].Length + (index == parameters.Count - 1 ? 0 : 1);

            if (width > widest)
            {
                widest = width;
            }
        }

        var builder = new StringBuilder();

        // 每一個參數都先宣告。以前只宣告 OUTPUT 的那幾個，理由是「只有它們非變數不可」——
        // 但展開出來的骨架本來就是要使用者接著改值的半成品，而改值最自然的位置就是
        // 變數本身：整份宣告在上、呼叫在下，一次改一處。把值直接寫進呼叫裡的話，
        // 同一個參數要重新執行一次就得先把上一句的常值改掉。
        //
        // 一律宣告也讓後面那一段 SELECT 說得通：不管那個參數是不是 OUTPUT，
        // 值都住在一個有名字的變數裡，看得到也改得動。
        // 使用者已經宣告過同名變數時會撞名，但那是一個當場看得見的編譯錯誤。
        AppendDeclarations(builder, parameters, indent, newLine);

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

        caretOffset = CaretOffset(executeKeyword, qualifiedName, parameters, indent, newLine);
        return builder.ToString();
    }

    /// <remarks>
    /// 第一個參數的值就在 <c>EXEC 名稱 @參數 = </c> 之後，而前面是每一個參數的宣告。
    /// 這裡重算一次而不是在迴圈裡記，是因為 DECLARE 那一段的長度只有這裡算得準。
    /// </remarks>
    private static int CaretOffset(
        string executeKeyword,
        string qualifiedName,
        IReadOnlyList<SqlStatementParameter> parameters,
        string indent,
        string newLine)
    {
        var declarations = new StringBuilder();
        AppendDeclarations(declarations, parameters, indent, newLine);

        return declarations.Length
            + executeKeyword.Length + 1
            + qualifiedName.Length + 1
            + parameters[0].Name.Length
            + " = ".Length;
    }

    private static void AppendDeclarations(
        StringBuilder builder,
        IReadOnlyList<SqlStatementParameter> parameters,
        string indent,
        string newLine)
    {
        // 第一行<b>不</b>加 indent，其餘每一行加。原因是這個區塊會插在語句的關鍵字前面，
        // 而關鍵字那一行的前導空白留在緩衝區裡沒有被換掉（替換範圍從 EXEC 起算）——
        // 第一行補了 indent 就會多縮一次，整段比 EXEC 往右突出一個縮排。
        //
        // 這個函式同時被 CaretOffset 拿來算游標位移，所以這裡的縮排規則有兩處要對得上。
        var first = true;

        foreach (var parameter in parameters)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                builder.Append(indent);
            }

            builder.Append("DECLARE ").Append(parameter.Name).Append(' ');

            // OUTPUT 的那一份要在宣告裡保留 OUTPUT：少了它，程序把值寫進來的權限
            // 就不存在，而錯誤訊息（「參數不是 OUTPUT 參數」）出現在呼叫那一行，
            // 跟宣告隔了好幾行，不容易一眼連起來。
            if (parameter.IsOutput)
            {
                builder.Append(parameter.DataType).Append(" OUTPUT");
            }
            else
            {
                builder.Append(parameter.DataType);
            }

            // 宣告時就先填好值：預設值來自模組定義（SqlModuleParameterDefaults），
            // 沒有的話才退回型別的預留值（SqlLiteralDefaults）。
            builder.Append(" = ").Append(InitialValue(parameter)).Append(';')
                .Append(newLine);
        }
    }

    /// <summary>
    /// 把每個 OUTPUT 參數的值印出來。
    /// </summary>
    /// <remarks>
    /// 展開出來的整段是要拿去執行的，執行完最想知道的就是「程序到底回了什麼」。
    /// 少了這一段，使用者得自己再打一行 SELECT，而且很容易漏掉其中幾個。
    ///
    /// 只印 OUTPUT：非 OUTPUT 的參數傳進去的是宣告時就填好的字面值，執行前後一模一樣，
    /// 印出來只是一份使用者自己剛寫過的東西。一個 OUTPUT 都沒有時整段不寫——
    /// 沒有參數要看的 SELECT 是雜訊。
    /// </remarks>
    private static void AppendOutputSelect(
        StringBuilder builder,
        IReadOnlyList<SqlStatementParameter> parameters,
        string indent,
        string newLine)
    {
        var hasOutput = false;

        foreach (var parameter in parameters)
        {
            if (parameter.IsOutput)
            {
                hasOutput = true;
                break;
            }
        }

        if (!hasOutput)
        {
            return;
        }

        builder.Append(newLine).Append(indent).Append(newLine).Append(indent).Append("SELECT ");

        var first = true;

        foreach (var parameter in parameters)
        {
            if (!parameter.IsOutput)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',').Append(newLine).Append(indent).Append("       ");
            }

            builder.Append(parameter.Name).Append(" AS ").Append(Alias(parameter.Name));
            first = false;
        }

        builder.Append(';');
    }

    /// <summary>把 <c>@NewDueDate</c> 變成 <c>NewDueDate</c>。</summary>
    private static string Alias(string name)
    {
        var alias = name.TrimStart('@');
        return alias.Length == 0 ? name : alias;
    }

    /// <summary>
    /// 宣告時填進去的值：模組定義寫了就用它，沒有才依型別給預留字面值。
    /// </summary>
    /// <remarks>
    /// 有預設值就不叫預留值了——那是模組開發者替這個參數挑的起點，比型別本身精確得多，
    /// 使用者多半只要改幾個字元就好。沒有的話仍然走型別預留值：看得出來要改（空字串、零），
    /// 而且插得進去（不會在轉型那一步就失敗）。
    /// </remarks>
    private static string InitialValue(SqlStatementParameter parameter)
    {
        // 那個 ! 是給編譯器的：DefaultValue 是「可為 null」的欄位，`is null or empty`
        // 這種寫法在 netstandard2.0 的 BCL 裡沒有，而比較寫完編譯器仍然不認為它非 null。
        return string.IsNullOrEmpty(parameter.DefaultValue)
            ? SqlLiteralDefaults.ForType(parameter.DataType)
            : parameter.DefaultValue!;
    }

    /// <summary>左邊與右邊現在是同一個名字，差別只在 OUTPUT 多一個關鍵字。</summary>
    private static string Assignment(SqlStatementParameter parameter)
    {
        // 宣告在上、呼叫在下，兩邊寫同一個變數——中間那段宣告就是要使用者改值的地方。
        // OUTPUT 那個字仍然只寫在呼叫這一側：T-SQL 要求它出現在傳值的位置，
        // 寫進宣告會變成另一件事（宣告一個 OUTPUT 變數沒有意義）。
        return parameter.IsOutput
            ? $"{parameter.Name} = {parameter.Name} OUTPUT"
            : $"{parameter.Name} = {parameter.Name}";
    }

    /// <remarks>
    /// OUTPUT 不寫進註解——那個字已經在左邊的程式碼裡了，而且宣告那一行也寫著。
    /// 「選擇性」則沒有別的地方看得到，而它正是使用者要決定
    /// 「這一列能不能整列刪掉」的依據。
    /// </remarks>
    private static string Comment(SqlStatementParameter parameter)
    {
        return parameter.IsOptional
            ? parameter.DataType + "，選擇性"
            : parameter.DataType;
    }
}
