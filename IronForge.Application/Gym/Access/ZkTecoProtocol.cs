using System.Globalization;
using System.Text;

namespace IronForge.Application.Gym.Access;

/// <summary>An entry the terminal logged (ATTLOG): who, and when in the terminal's local time.</summary>
public sealed record TerminalEntry(string Pin, DateTime LocalTime, int VerifyMode);

/// <summary>A user in the terminal's list (USER line). Fingerprint lines only mark <see cref="HasFingerprint"/>.</summary>
public sealed record TerminalUserRecord(string Pin, string? Name, bool HasFingerprint);

public sealed record TerminalCommandResult(long Id, int ReturnCode);

/// <summary>
/// The text formats of the ZKTeco push protocol (ADMS, "iclock") used by
/// ZKTeco attendance and access terminals. The terminal is the HTTP client:
/// <list type="bullet">
/// <item><c>GET /iclock/cdata?SN=…&amp;options=all</c>: it starts and reads its options;</item>
/// <item><c>POST /iclock/cdata?SN=…&amp;table=ATTLOG</c>: entries, one per line, tab-separated;</item>
/// <item><c>POST /iclock/cdata?SN=…&amp;table=OPERLOG</c>: enrolled users (USER) and fingerprints (FP);</item>
/// <item><c>GET /iclock/getrequest?SN=…</c>: it polls for commands (<c>C:{id}:{command}</c>);</item>
/// <item><c>POST /iclock/devicecmd?SN=…</c>: it reports each command's result.</item>
/// </list>
/// Field names follow ZKTeco's push protocol documentation; firmware differs
/// between models, so check validity dates (StartDatetime/EndDatetime) on the
/// gym's model before relying on offline expiry.
/// </summary>
public static class ZkTecoProtocol
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The terminal shows at most this many characters of a name.</summary>
    public const int MaxNameLength = 24;

    /// <summary>Verify mode 1 is fingerprint in ZKTeco's numbering.</summary>
    public const int VerifyFingerprint = 1;

    /// <summary>
    /// Options sent when the terminal starts. Realtime=1 uploads each entry
    /// at once; Delay is how often (seconds) it polls for commands. The
    /// terminal keeps its own clock and time zone.
    /// </summary>
    public static string Options(string serialNumber) =>
        string.Join('\n',
            $"GET OPTION FROM: {serialNumber}",
            "ATTLOGStamp=None",
            "OPERLOGStamp=9999",
            "ATTPHOTOStamp=None",
            "ErrorDelay=30",
            "Delay=10",
            "TransTimes=00:00;14:05",
            "TransInterval=1",
            "TransFlag=TransData AttLog\tOpLog\tEnrollUser\tChgUser\tEnrollFP\tChgFP",
            "Realtime=1",
            "Encrypt=None") + "\n";

    /// <summary>
    /// Creates or updates one user on the terminal without touching its
    /// fingerprints. A refused member gets an end date in the past, so the
    /// terminal refuses them even when it cannot reach IronForge. Never
    /// "DATA DELETE USERINFO": that would delete the fingerprints too.
    /// </summary>
    public static string UpdateUser(string pin, string name, bool enabled, DateOnly validUntil, DateOnly today)
    {
        var end = enabled ? validUntil : today.AddDays(-1);

        return string.Join('\t',
            $"DATA UPDATE USERINFO PIN={Clean(pin)}",
            $"Name={TerminalName(name)}",
            "Pri=0",
            "Grp=1",
            $"StartDatetime={new DateTime(2000, 1, 1).ToString(DateTimeFormat, CultureInfo.InvariantCulture)}",
            $"EndDatetime={end.ToDateTime(new TimeOnly(23, 59, 59)).ToString(DateTimeFormat, CultureInfo.InvariantCulture)}");
    }

    /// <summary>Asks the terminal to upload its whole user list (the migration of an existing terminal).</summary>
    public const string QueryUsers = "DATA QUERY USERINFO";

    /// <summary>Command lines for a poll, or "OK" when there is nothing to do.</summary>
    public static string Commands(IEnumerable<(long Id, string Text)> commands)
    {
        var builder = new StringBuilder();

        foreach (var (id, text) in commands)
        {
            builder.Append(CultureInfo.InvariantCulture, $"C:{id}:{text}\n");
        }

        return builder.Length == 0 ? "OK" : builder.ToString();
    }

    /// <summary>
    /// ATTLOG lines: PIN, local time, status, verify mode, work code… Lines
    /// that cannot be read are skipped, the rest are kept.
    /// </summary>
    public static List<TerminalEntry> ParseEntries(string body)
    {
        var entries = new List<TerminalEntry>();

        foreach (var line in Lines(body))
        {
            var fields = line.Split('\t');

            if (fields.Length < 2 ||
                Clean(fields[0]) is not { Length: > 0 } pin ||
                !DateTime.TryParseExact(fields[1].Trim(), DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            var verify = fields.Length > 3 && int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode)
                ? mode
                : 0;

            entries.Add(new TerminalEntry(pin, time, verify));
        }

        return entries;
    }

    /// <summary>
    /// OPERLOG / USERINFO upload. USER lines give the user list; FP and
    /// BIODATA lines carry fingerprint templates, which are read only to note
    /// that the user has a fingerprint: the template itself is dropped here.
    /// </summary>
    public static List<TerminalUserRecord> ParseUsers(string body)
    {
        var users = new Dictionary<string, TerminalUserRecord>(StringComparer.Ordinal);

        foreach (var line in Lines(body))
        {
            var (kind, fields) = SplitKind(line);

            switch (kind)
            {
                case "USER":
                {
                    var values = KeyValues(fields);

                    if (Get(values, "PIN") is { Length: > 0 } pin)
                    {
                        var name = Get(values, "Name");
                        var hasFinger = users.TryGetValue(pin, out var existing) && existing.HasFingerprint;
                        users[pin] = new TerminalUserRecord(pin, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), hasFinger);
                    }

                    break;
                }

                case "FP":
                case "BIODATA":
                {
                    var values = KeyValues(fields);

                    if (Get(values, "PIN") is { Length: > 0 } pin)
                    {
                        users[pin] = users.TryGetValue(pin, out var existing)
                            ? existing with { HasFingerprint = true }
                            : new TerminalUserRecord(pin, null, true);
                    }

                    break;
                }
            }
        }

        return users.Values.ToList();
    }

    /// <summary>devicecmd body: <c>ID=12&amp;Return=0&amp;CMD=DATA</c>, one per line.</summary>
    public static List<TerminalCommandResult> ParseResults(string body)
    {
        var results = new List<TerminalCommandResult>();

        foreach (var line in Lines(body))
        {
            var values = line.Split('&')
                .Select(x => x.Split('=', 2))
                .Where(x => x.Length == 2)
                .ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase);

            if (values.TryGetValue("ID", out var idText) &&
                long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                values.TryGetValue("Return", out var returnText) &&
                int.TryParse(returnText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                results.Add(new TerminalCommandResult(id, code));
            }
        }

        return results;
    }

    public static string NormalizeSerial(string? serial) =>
        new string((serial ?? "").Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Terminal user numbers are digits; anything else is not sent.</summary>
    public static string? Clean(string? pin)
    {
        var value = (pin ?? "").Trim();
        return value.Length is > 0 and <= 24 && value.All(char.IsAsciiDigit) ? value : null;
    }

    public static string TerminalName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c) && c != '=').ToArray()).Trim();
        return clean.Length > MaxNameLength ? clean[..MaxNameLength] : clean;
    }

    private static IEnumerable<string> Lines(string body) =>
        body.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (string Kind, string Values) SplitKind(string line)
    {
        // "USER PIN=1\tName=…": the record type, then tab-separated values.
        // A bare "PIN=1\tName=…" (table=USERINFO) is a user too.
        if (line.StartsWith("PIN=", StringComparison.OrdinalIgnoreCase))
            return ("USER", line);

        var space = line.IndexOf(' ');
        return space < 0 ? (line, "") : (line[..space].ToUpperInvariant(), line[(space + 1)..]);
    }

    private static Dictionary<string, string> KeyValues(string text) =>
        text.Split('\t')
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .GroupBy(x => x[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First()[1], StringComparer.OrdinalIgnoreCase);

    private static string? Get(Dictionary<string, string> values, string key)
    {
        var value = values.TryGetValue(key, out var found) ? found : null;
        return key == "PIN" ? Clean(value) : value;
    }
}
