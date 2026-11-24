using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;

namespace SqlAssist.Ssms22.Decryption;

/// <summary>
/// 開啟系統管理員專用連線（DAC）。<c>sys.sysobjvalues</c>——加密模組文字所在——只有 DAC 讀得到。
/// </summary>
/// <remarks>
/// 三個連線字串規則不是偏好：<c>ADMIN:</c> 前綴是要求 DAC 的方式；<c>Pooling=false</c> 是因為
/// 一個執行個體只有一條 DAC，而集區連線在 Dispose 之後還開著，會把下一輪（包含 SSMS 自己的
/// DAC 查詢視窗）擋掉；明確的 <c>,port</c> 必須去掉，DAC 的連接埠由 SQL Browser 從執行個體名稱解析。
///
/// 連接埠探測是給錯誤 43 用的：SQL Browser 只是用戶端問埠的方式，Express 沒裝它也能連；
/// 真正常見的是 Express 沒以 trace flag 7806 啟動、或用戶端解析不到具名執行個體的埠。
/// <c>sys.tcp_endpoints.port</c> 對動態配置的 DAC 一律回 0，所以候選從
/// <c>sys.dm_tcp_listener_states</c> 來，再用 <c>sys.endpoints</c> 確認真的落在 DAC 上。
/// </remarks>
internal static class SqlDacConnectionFactory
{
    private const string ApplicationName = "SqlAssist for SSMS (DAC)";

    /// <summary>
    /// SSMS 交出來的連線字串可能混用帶空格的關鍵字（<c>Multiple Active Result Sets</c>、
    /// <c>Trust Server Certificate</c>）與 net48 builder 不認得的 SSMS 專用關鍵字
    /// （<c>Authentication</c>、<c>Column Encryption Setting</c>）。builder 在建構時就拋
    /// 「不支援關鍵字」，根本還沒嘗試開 DAC；逐條改寫會永遠慢一個關鍵字，所以這裡改成
    /// 白名單重建：只留下 DAC 需要的欄位，其餘丟掉。
    /// </summary>
    private static readonly Dictionary<string, string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        ["datasource"] = "Data Source",
        ["server"] = "Data Source",
        ["address"] = "Data Source",
        ["addr"] = "Data Source",
        ["networkaddress"] = "Data Source",
        ["initialcatalog"] = "Initial Catalog",
        ["database"] = "Initial Catalog",
        ["integratedsecurity"] = "Integrated Security",
        ["trustedconnection"] = "Integrated Security",
        ["userid"] = "User ID",
        ["uid"] = "User ID",
        ["user"] = "User ID",
        ["password"] = "Password",
        ["pwd"] = "Password",
        ["trustservercertificate"] = "TrustServerCertificate",
        ["encrypt"] = "Encrypt",
        ["currentlanguage"] = "Current Language",
        ["workstationid"] = "Workstation ID",
        ["wsid"] = "Workstation ID",
        ["packetsize"] = "Packet Size",
        ["networklibrary"] = "Network Library",
        ["network"] = "Network Library",
        ["net"] = "Network Library",
        ["connecttimeout"] = "Connect Timeout",
        ["connectiontimeout"] = "Connect Timeout",
        ["multipleactiveresultsets"] = "MultipleActiveResultSets",
    };

    /// <summary>
    /// 把 SSMS 的連線字串重建成 <see cref="SqlConnectionStringBuilder"/> 認得的形式。
    /// 所有進 builder 的入口都得先過這一道。
    /// </summary>
    public static string NormalizeKeywords(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return connectionString ?? "";

        var normalized = new SqlConnectionStringBuilder();
        foreach (var pair in ParsePairs(connectionString!))
        {
            if (!SupportedKeywords.TryGetValue(Compact(pair.Key), out var keyword)) continue;

            var value = pair.Value;
            switch (keyword)
            {
                case "Integrated Security":
                    var integrated = ParseFlag(value);
                    if (integrated.HasValue) normalized.IntegratedSecurity = integrated.Value;
                    break;
                case "TrustServerCertificate":
                    var trust = ParseFlag(value);
                    if (trust.HasValue) normalized.TrustServerCertificate = trust.Value;
                    break;
                case "Encrypt":
                    var encrypt = ParseEncrypt(value);
                    if (encrypt.HasValue) normalized.Encrypt = encrypt.Value;
                    break;
                case "MultipleActiveResultSets":
                    var mars = ParseFlag(value);
                    if (mars.HasValue) normalized.MultipleActiveResultSets = mars.Value;
                    break;
                case "Packet Size":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var packet))
                        normalized.PacketSize = packet;
                    break;
                case "Connect Timeout":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
                        normalized.ConnectTimeout = timeout;
                    break;
                default:
                    normalized[keyword] = value;
                    break;
            }
        }

        return normalized.ConnectionString;
    }

    /// <summary>關鍵字大小寫、空白都可能不同；移除空白後再查表。</summary>
    private static string Compact(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return "";
        if (keyword.IndexOf(' ') < 0 && keyword.IndexOf('\t') < 0) return keyword.ToLowerInvariant();

        var text = new StringBuilder(keyword.Length);
        foreach (var c in keyword)
            if (!char.IsWhiteSpace(c)) text.Append(char.ToLowerInvariant(c));
        return text.ToString();
    }

    /// <summary>自行拆解 key=value，避開值裡用引號包起來的分號。</summary>
    private static IEnumerable<KeyValuePair<string, string>> ParsePairs(string connectionString)
    {
        var index = 0;
        while (index < connectionString.Length)
        {
            while (index < connectionString.Length &&
                   (connectionString[index] == ';' || char.IsWhiteSpace(connectionString[index])))
                index++;
            if (index >= connectionString.Length) break;

            var keyStart = index;
            while (index < connectionString.Length &&
                   connectionString[index] != '=' && connectionString[index] != ';')
                index++;
            if (index >= connectionString.Length || connectionString[index] == ';')
            {
                while (index < connectionString.Length && connectionString[index] != ';') index++;
                continue;
            }

            var key = connectionString.Substring(keyStart, index - keyStart).Trim();
            index++; // '='

            var value = new StringBuilder();
            var quote = '\0';
            while (index < connectionString.Length)
            {
                var c = connectionString[index];
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        if (index + 1 < connectionString.Length && connectionString[index + 1] == quote)
                        {
                            value.Append(c);
                            index += 2;
                            continue;
                        }
                        quote = '\0';
                        index++;
                        continue;
                    }
                    value.Append(c);
                    index++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    quote = c;
                    index++;
                    continue;
                }
                if (c == ';')
                {
                    index++;
                    break;
                }
                value.Append(c);
                index++;
            }

            if (key.Length > 0)
                yield return new KeyValuePair<string, string>(key, value.ToString().Trim());
        }
    }

    private static bool? ParseFlag(string value)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.Ordinal) ||
            value.Equals("sspi", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.Ordinal))
            return false;

        return null;
    }

    /// <summary>新版 SSMS 寫 Mandatory／Optional；net48 builder 的 Encrypt 只接受布林。</summary>
    private static bool? ParseEncrypt(string value)
    {
        var flag = ParseFlag(value);
        if (flag.HasValue) return flag;

        if (value.Equals("mandatory", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("strict", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Equals("optional", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>把資料來源縮成 DAC 認得出來的形式：去掉 ADMIN:、通訊協定前綴與明確的埠。</summary>
    public static string StripDacUnsupportedParts(string? dataSource)
    {
        var source = (dataSource ?? "").Trim();

        if (source.StartsWith("ADMIN:", StringComparison.OrdinalIgnoreCase))
            source = source.Substring("ADMIN:".Length);

        foreach (var prefix in new[] { "tcp:", "np:", "lpc:" })
        {
            if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            source = source.Substring(prefix.Length);
            break;
        }

        var comma = source.IndexOf(',');
        if (comma >= 0) source = source.Substring(0, comma);
        return source.Trim();
    }

    /// <summary>只留機器名稱；明確指定 DAC 埠時不能留下執行個體名稱再問用戶端解析一次。</summary>
    public static string StripToHost(string? dataSource)
    {
        var source = StripDacUnsupportedParts(dataSource);
        var slash = source.IndexOf('\\');
        return (slash >= 0 ? source.Substring(0, slash) : source).Trim();
    }

    /// <summary>一般連線轉成 DAC 連線字串。</summary>
    public static string BuildConnectionString(string baseConnectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(NormalizeKeywords(baseConnectionString))
        {
            Pooling = false,
            MultipleActiveResultSets = false,
            ConnectTimeout = 10,
            ApplicationName = ApplicationName,
        };

        if (!string.IsNullOrEmpty(database)) builder.InitialCatalog = database;
        builder.DataSource = "ADMIN:" + StripDacUnsupportedParts(builder.DataSource);
        return builder.ConnectionString;
    }

    /// <summary>
    /// 用查到的 DAC 埠直接連。<b>不能</b>加 ADMIN: 前綴：<c>ADMIN:host,port</c> 會被 SqlClient 直接
    /// 判為無效連線字串；<c>tcp:host,port</c> 指到 DAC 的埠照樣落在 DAC endpoint 上。
    /// </summary>
    public static string BuildDirectConnectionString(string baseConnectionString, string database, int dacPort)
    {
        var builder = new SqlConnectionStringBuilder(NormalizeKeywords(baseConnectionString))
        {
            Pooling = false,
            MultipleActiveResultSets = false,
            ConnectTimeout = 5,
            ApplicationName = ApplicationName,
        };

        if (!string.IsNullOrEmpty(database)) builder.InitialCatalog = database;
        builder.DataSource = "tcp:" + StripToHost(builder.DataSource) + "," + dacPort;
        return builder.ConnectionString;
    }

    /// <summary>
    /// 開一條 DAC。<paramref name="probeHolder"/> 只在最後一次失敗時為真：查占用者要多一條普通連線，
    /// 而既然還要重試，那時候的答案沒有人會讀。
    /// </summary>
    public static SqlConnection Open(string baseConnectionString, string database, bool probeHolder = true)
    {
        if (string.IsNullOrEmpty(baseConnectionString))
            throw new DacUnavailableException(DecryptionText.DacNoConnection);

        string dacConnectionString;
        try
        {
            dacConnectionString = BuildConnectionString(baseConnectionString, database);
        }
        catch (Exception ex)
        {
            throw new DacUnavailableException(DecryptionText.DacBadConnectionString(ex.Message), ex);
        }

        try
        {
            return Connect(dacConnectionString);
        }
        catch (Exception ex)
        {
            if (IsBusy(ex) || IsLoginRefused(ex))
                throw new DacUnavailableException(Explain(ex, baseConnectionString, probeHolder), ex, IsLoginRefused(ex));

            return OpenOnListeningPort(ex, baseConnectionString, database, probeHolder);
        }
    }

    private static SqlConnection Connect(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>連不上時問伺服器自己監聽哪些埠，逐個試，並用 session 的 endpoint 確認。</summary>
    private static SqlConnection OpenOnListeningPort(
        Exception failure,
        string baseConnectionString,
        string database,
        bool probeHolder)
    {
        var endpoint = ProbeDacEndpoint(baseConnectionString);

        if (!endpoint.Reached)
            throw new DacUnavailableException(Explain(failure, baseConnectionString, probeHolder), failure);

        foreach (var port in endpoint.Ports)
        {
            SqlConnection? candidate = null;
            try
            {
                candidate = Connect(BuildDirectConnectionString(baseConnectionString, database, port));
                if (IsDacSession(candidate)) return candidate;
                candidate.Dispose();
            }
            catch
            {
                candidate?.Dispose();
            }
        }

        throw new DacUnavailableException(
            ExplainUnresolvedPort(failure, endpoint), failure, endpoint.HasNoDac);
    }

    /// <summary>每一個監聽埠都接受連線，只有它落在哪一個 endpoint 上才分得出是不是 DAC。</summary>
    private static bool IsDacSession(SqlConnection connection)
    {
        const string sql = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM sys.dm_exec_sessions s
    JOIN sys.endpoints e ON s.endpoint_id = e.endpoint_id
    WHERE s.session_id = @@SPID AND e.name = 'Dedicated Admin Connection')
THEN 1 ELSE 0 END;";

        try
        {
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 忙碌（17810／17809）與登入被拒（18456／18452／297）要分開：前者幾秒後就可用，後者到工作階段結束都不會變。
    /// 只看 <see cref="SqlException"/>，不看外包裝的訊息——包裝自己的文字就提到 DAC，
    /// 沿著整條鏈找會把「不是 sysadmin」也當成忙碌而無限重試。錯誤號碼也要看，訊息是當地語言。
    /// </summary>
    public static bool IsBusy(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && (sql.Number == 17810 || sql.Number == 17809 || LooksLikeBusyText(sql.Message)))
                return true;
        }

        return false;
    }

    /// <summary>從錯誤文字判斷忙碌；引擎有兩種說法，且文字可能是英文以外的語言。</summary>
    internal static bool LooksLikeBusyText(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        if (message!.IndexOf("maximum number of", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return message.IndexOf("dedicated admin", StringComparison.OrdinalIgnoreCase) >= 0 &&
               message.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsLoginRefused(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && (sql.Number == 18456 || sql.Number == 18452 || sql.Number == 297))
                return true;
        }

        return false;
    }

    /// <summary>用普通連線問是誰占著 DAC；best effort，問不到就回 null，不拋出。</summary>
    public static string? DescribeDacHolder(string baseConnectionString)
    {
        const string sql = @"
SELECT TOP (5) s.login_name, s.program_name, s.host_name
FROM sys.dm_exec_sessions s
JOIN sys.endpoints e ON s.endpoint_id = e.endpoint_id
WHERE e.name = 'Dedicated Admin Connection' AND s.session_id <> @@SPID
ORDER BY s.session_id;";

        try
        {
            var builder = ProbeBuilder(baseConnectionString, "SqlAssist for SSMS (DAC probe)");
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();

            using var command = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            using var reader = command.ExecuteReader();

            var holders = new List<string>();
            while (reader.Read() && holders.Count < 5)
            {
                var login = reader.IsDBNull(0) ? "?" : reader.GetString(0);
                var program = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var host = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                holders.Add(login +
                            (program.Length == 0 ? "" : " via \"" + program + "\"") +
                            (host.Length == 0 ? "" : " on " + host));
            }

            return holders.Count == 0 ? null : string.Join("; ", holders);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>從普通連線問出 DAC 有沒有監聽、可能在哪些埠、是否接受遠端；每條查詢各自降級。</summary>
    public static DacEndpoint ProbeDacEndpoint(string baseConnectionString)
    {
        var endpoint = new DacEndpoint();
        if (string.IsNullOrEmpty(baseConnectionString))
        {
            endpoint.Error = DecryptionText.DacNoConnection;
            return endpoint;
        }

        try
        {
            var builder = ProbeBuilder(baseConnectionString, "SqlAssist for SSMS (DAC port probe)");
            endpoint.Host = StripToHost(builder.DataSource);

            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            endpoint.Reached = true;

            endpoint.EngineEdition = Scalar(connection,
                "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int);") ?? 0;
            endpoint.RemoteAdminEnabled = Scalar(connection,
                "SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'remote admin connections';") == 1;

            var machine = ScalarText(connection, "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128));");
            if (!string.IsNullOrEmpty(machine))
                endpoint.ClientIsRemote = !string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

            endpoint.AddPort(Scalar(connection,
                "SELECT port FROM sys.tcp_endpoints WHERE name = 'Dedicated Admin Connection';") ?? 0);

            foreach (var port in ScalarList(connection,
                         "SELECT DISTINCT port FROM sys.dm_tcp_listener_states WHERE state_desc = 'ONLINE';"))
                endpoint.AddPort(port);

            if (endpoint.Ports.Count == 0)
            {
                endpoint.TcpEnabled = Scalar(connection,
                    "SELECT TOP (1) CAST(value_data AS int) FROM sys.dm_server_registry " +
                    "WHERE registry_key LIKE '%SuperSocketNetLib\\Tcp' AND value_name = 'Enabled';") == 1;
            }
        }
        catch (Exception ex)
        {
            endpoint.Error = ex.Message;
        }

        return endpoint;
    }

    private static SqlConnectionStringBuilder ProbeBuilder(string baseConnectionString, string applicationName)
    {
        var builder = new SqlConnectionStringBuilder(NormalizeKeywords(baseConnectionString))
        {
            ConnectTimeout = 3,
            ApplicationName = applicationName,
            Pooling = false,
        };
        builder.DataSource = StripDacUnsupportedParts(builder.DataSource);
        return builder;
    }

    private static int? Scalar(SqlConnection connection, string sql)
    {
        try
        {
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            var value = command.ExecuteScalar();
            return value is null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? ScalarText(SqlConnection connection, string sql)
    {
        try
        {
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            var value = command.ExecuteScalar();
            return value is null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        catch
        {
            return null;
        }
    }

    private static List<int> ScalarList(SqlConnection connection, string sql)
    {
        var values = new List<int>();
        try
        {
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (!reader.IsDBNull(0)) values.Add(Convert.ToInt32(reader.GetValue(0)));
        }
        catch
        {
            // 舊版本沒有這一個 DMV，或沒有 VIEW SERVER STATE：只剩固定埠那個候選。
        }

        return values;
    }

    private static string Explain(Exception exception, string baseConnectionString, bool probeHolder)
    {
        var message = exception.Message ?? "";

        if (LooksLikeBusyText(message))
        {
            var holder = probeHolder ? DescribeDacHolder(baseConnectionString) : null;
            return DecryptionText.DacBusy(holder is null
                ? DecryptionText.DacBusyNoHolder
                : DecryptionText.DacBusyHolder(holder));
        }

        if (IsLoginRefused(exception)) return DecryptionText.DacLoginRefused(message);
        return DecryptionText.DacUnavailable(message);
    }

    private static string ExplainUnresolvedPort(Exception failure, DacEndpoint endpoint)
    {
        if (IsLoginRefused(failure)) return DecryptionText.DacLoginRefused(failure.Message ?? "");

        var ports = endpoint.Ports.Count == 0 ? "-" : string.Join(", ", endpoint.Ports);
        var reason = failure.Message ?? "";
        if (!string.IsNullOrEmpty(endpoint.Error)) reason += " (" + endpoint.Error + ")";

        return DecryptionText.DacPortUnresolved(ports, reason);
    }
}

/// <summary>伺服器對自己 DAC 的說法；每一欄都是 best effort，只有 <see cref="Reached"/> 自己就代表意義。</summary>
internal sealed class DacEndpoint
{
    private readonly List<int> _ports = new();

    public bool Reached { get; set; }

    /// <summary>值得試的埠，不是「這些就是 DAC」——普通 endpoint 也在裡面，要靠連上去問才知道。</summary>
    public IReadOnlyList<int> Ports => _ports;

    public bool? RemoteAdminEnabled { get; set; }

    public bool? ClientIsRemote { get; set; }

    public bool? TcpEnabled { get; set; }

    /// <summary>4 是 Express，5 是 Azure SQL Database（沒有可連的 DAC）。</summary>
    public int EngineEdition { get; set; }

    public string? Host { get; set; }

    public string? Error { get; set; }

    public bool HasNoDac => EngineEdition == 5;

    internal void AddPort(int port)
    {
        if (port > 0 && !_ports.Contains(port)) _ports.Add(port);
    }
}

/// <summary>DAC 開不起來；與單一物件的失敗分開，因為它讓整輪停止而不是只少一個物件。</summary>
internal sealed class DacUnavailableException : Exception
{
    /// <summary>工作階段期間不會改變的失敗（沒有 sysadmin、平台沒有 DAC）才為真。</summary>
    public bool Permanent { get; }

    public DacUnavailableException(string message, Exception? inner = null, bool permanent = false)
        : base(message, inner) => Permanent = permanent;
}
