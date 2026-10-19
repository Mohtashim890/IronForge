using System.Globalization;
using System.Net;
using System.Text;

namespace IronForge.UI.Admin.Simulator;

/// <summary>A person enrolled on the simulated terminal, as the terminal stores them.</summary>
public sealed class SimulatedUser
{
    public string Pin { get; init; } = "";

    public string Name { get; set; } = "";

    public bool HasFingerprint { get; set; } = true;

    /// <summary>Last moment the user may enter (terminal local time); null: no limit (never synced).</summary>
    public DateTime? EndsAt { get; set; }
}

public enum SimulatedScreenTone { Idle, Granted, Denied, Info }

public sealed record SimulatedScreen(SimulatedScreenTone Tone, string Title, string Detail);

public sealed record SimulatedLogLine(DateTime At, string Direction, string Text);

/// <summary>
/// A virtual ZKTeco fingerprint terminal. It talks to the IronForge API with
/// the same push protocol (ADMS/iclock) a real terminal uses, keeps its own
/// user list, and decides at the door from that list alone, so it keeps
/// working when "offline" exactly like the hardware. "Placing a finger"
/// stands in for the terminal's own fingerprint match.
/// </summary>
public sealed class SimulatedTerminal : IDisposable
{
    private const int MaxLogLines = 80;

    private readonly Func<HttpClient> _http;
    private readonly object _gate = new();
    private readonly List<SimulatedUser> _users = [];
    private readonly List<string> _pendingEntries = [];
    private readonly List<string> _pendingUserLines = [];
    private readonly LinkedList<SimulatedLogLine> _log = new();
    private CancellationTokenSource? _power;
    private bool _connected;

    public SimulatedTerminal(string serialNumber, string name, string timeZoneId, Func<HttpClient> http)
    {
        SerialNumber = serialNumber;
        Name = name;
        TimeZone = ResolveZone(timeZoneId);
        _http = http;
        Screen = new SimulatedScreen(SimulatedScreenTone.Idle, "Powered off", "");
    }

    public string SerialNumber { get; }

    public string Name { get; }

    public TimeZoneInfo TimeZone { get; }

    public bool IsOn => _power is not null;

    /// <summary>The network cable: when false the terminal cannot reach IronForge but still decides at the door.</summary>
    public bool IsOnline { get; private set; } = true;

    /// <summary>Moves the terminal's clock forward, to show an offline end date taking effect.</summary>
    public int ClockOffsetDays { get; private set; }

    public DateTime LocalNow =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone).AddDays(ClockOffsetDays);

    public SimulatedScreen Screen { get; private set; }

    public string? ConnectionProblem { get; private set; }

    public DateTime? LastContactAtUtc { get; private set; }

    public event Action? Changed;

    public IReadOnlyList<SimulatedUser> Users
    {
        get
        {
            lock (_gate)
            {
                return _users.OrderBy(x => x.Pin.Length).ThenBy(x => x.Pin).ToList();
            }
        }
    }

    public IReadOnlyList<SimulatedLogLine> Log
    {
        get
        {
            lock (_gate)
            {
                return _log.ToList();
            }
        }
    }

    public int WaitingUploads
    {
        get
        {
            lock (_gate)
            {
                return _pendingEntries.Count + _pendingUserLines.Count;
            }
        }
    }

    public void PowerOn()
    {
        if (_power is not null)
            return;

        _power = new CancellationTokenSource();
        _connected = false;
        Show(SimulatedScreenTone.Info, "Starting…", "Connecting to IronForge");

        _ = Task.Run(() => RunAsync(_power.Token));
    }

    public void PowerOff()
    {
        _power?.Cancel();
        _power = null;
        Show(SimulatedScreenTone.Idle, "Powered off", "");
    }

    public void SetOnline(bool online)
    {
        IsOnline = online;
        AddLog("·", online ? "Network cable plugged in." : "Network cable unplugged: the terminal decides from its own list.");
        Notify();
    }

    public void AdvanceClock(int days)
    {
        ClockOffsetDays = Math.Max(0, ClockOffsetDays + days);
        AddLog("·", $"Terminal clock is now {LocalNow:yyyy-MM-dd HH:mm}.");
        Notify();
    }

    /// <summary>Loads people as if the gym had enrolled them on this terminal before IronForge.</summary>
    public void LoadExistingUsers(IEnumerable<(string Pin, string Name)> users)
    {
        lock (_gate)
        {
            foreach (var (pin, name) in users)
            {
                if (_users.All(x => x.Pin != pin))
                {
                    var user = new SimulatedUser { Pin = pin, Name = Clean(name) };
                    _users.Add(user);

                    // Reported like any change on the terminal (ChgUser / EnrollFP).
                    _pendingUserLines.AddRange(UserLines(user));
                }
            }
        }

        AddLog("·", "Existing fingerprints loaded (enrolled before IronForge; no end dates).");
        Notify();
    }

    /// <summary>A new person enrols a finger at the terminal; it reports them to IronForge.</summary>
    public SimulatedUser Enroll(string name)
    {
        SimulatedUser user;

        lock (_gate)
        {
            var next = _users.Select(x => long.TryParse(x.Pin, out var n) ? n : 0).DefaultIfEmpty(2000).Max() + 1;
            user = new SimulatedUser { Pin = next.ToString(CultureInfo.InvariantCulture), Name = Clean(name) };
            _users.Add(user);
            _pendingUserLines.AddRange(UserLines(user));
        }

        Show(SimulatedScreenTone.Info, "Enrolled", $"User {user.Pin} · {user.Name}: finger saved on the terminal");
        return user;
    }

    /// <summary>Enrols a finger for a user IronForge created (a new member given a number).</summary>
    public void EnrollFinger(string pin)
    {
        lock (_gate)
        {
            if (_users.FirstOrDefault(x => x.Pin == pin) is not { } user)
                return;

            user.HasFingerprint = true;
            _pendingUserLines.AddRange(UserLines(user));
        }

        Show(SimulatedScreenTone.Info, "Enrolled", $"User {pin}: finger saved on the terminal");
    }

    /// <summary>
    /// A finger is placed. The terminal matches it against its own templates
    /// (simulated: the person is chosen) and decides from its own list.
    /// </summary>
    public void PlaceFinger(string? pin)
    {
        SimulatedUser? user;

        lock (_gate)
        {
            user = pin is null ? null : _users.FirstOrDefault(x => x.Pin == pin && x.HasFingerprint);
        }

        if (!IsOn)
            return;

        if (user is null)
        {
            Show(SimulatedScreenTone.Denied, "Not registered", "Fingerprint not recognised. Please see the front desk.");
            AddLog("·", "Unknown finger: refused by the terminal (no entry recorded).");
            return;
        }

        var now = LocalNow;

        if (user.EndsAt is { } end && now > end)
        {
            Show(SimulatedScreenTone.Denied, "Access denied", $"{user.Name}: membership not valid. Please see the front desk.");
            AddLog("·", $"User {user.Pin} refused by the terminal itself: valid until {end:yyyy-MM-dd} (IronForge's last sync).");
            return;
        }

        Show(SimulatedScreenTone.Granted, "Door open", $"Welcome, {user.Name}!");

        lock (_gate)
        {
            // ATTLOG: PIN, local time, status (0 = in), verify mode (1 = fingerprint), work code, reserved.
            _pendingEntries.Add($"{user.Pin}\t{now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}\t0\t1\t0\t0\t0");
        }

        AddLog("·", $"User {user.Pin} let in by the terminal." + (IsOnline ? "" : " Entry kept until the network is back."));
    }

    public void Dispose() => PowerOff();

    // ---- The push protocol --------------------------------------------------------

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (IsOnline)
            {
                try
                {
                    if (!_connected)
                    {
                        await ConnectAsync(token);
                    }

                    if (_connected)
                    {
                        await UploadAsync(token);
                        await PollAsync(token);
                        LastContactAtUtc = DateTime.UtcNow;
                    }
                }
                catch (HttpRequestException ex)
                {
                    ConnectionProblem = $"Cannot reach IronForge: {ex.Message}";
                    Notify();
                }
                catch (TaskCanceledException) when (!token.IsCancellationRequested)
                {
                    ConnectionProblem = "IronForge did not answer in time.";
                    Notify();
                }
            }

            try
            {
                // A real terminal polls every few seconds (Delay); faster here for the demo.
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, $"iclock/cdata?SN={SerialNumber}&options=all&pushver=2.4.1&language=69", null, token);

        if (status != HttpStatusCode.OK)
        {
            ConnectionProblem = $"IronForge refused the terminal ({(int)status}): {body}";
            Show(SimulatedScreenTone.Denied, "Server refused", body);
            return;
        }

        _connected = true;
        ConnectionProblem = null;
        Show(SimulatedScreenTone.Idle, "Ready", "Place your finger");
    }

    private async Task UploadAsync(CancellationToken token)
    {
        string[] users, entries;

        lock (_gate)
        {
            users = _pendingUserLines.ToArray();
            entries = _pendingEntries.ToArray();
        }

        if (users.Length > 0)
        {
            var (status, _) = await SendAsync(HttpMethod.Post, $"iclock/cdata?SN={SerialNumber}&table=OPERLOG&Stamp=9999", string.Join('\n', users) + "\n", token);

            if (status == HttpStatusCode.OK)
            {
                lock (_gate)
                {
                    _pendingUserLines.RemoveRange(0, Math.Min(users.Length, _pendingUserLines.Count));
                }
            }
        }

        if (entries.Length > 0)
        {
            var (status, _) = await SendAsync(HttpMethod.Post, $"iclock/cdata?SN={SerialNumber}&table=ATTLOG&Stamp=9999", string.Join('\n', entries) + "\n", token);

            if (status == HttpStatusCode.OK)
            {
                lock (_gate)
                {
                    _pendingEntries.RemoveRange(0, Math.Min(entries.Length, _pendingEntries.Count));
                }
            }
        }
    }

    private async Task PollAsync(CancellationToken token)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, $"iclock/getrequest?SN={SerialNumber}", null, token, logQuietOk: true);

        if (status != HttpStatusCode.OK)
        {
            _connected = false;
            return;
        }

        var results = new List<string>();

        foreach (var line in body.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // C:{id}:{command}
            var parts = line.Split(':', 3);

            if (parts.Length < 3 || parts[0] != "C")
                continue;

            var code = Execute(parts[2]);
            results.Add($"ID={parts[1]}&Return={code}&CMD=DATA");
        }

        if (results.Count > 0)
        {
            await SendAsync(HttpMethod.Post, $"iclock/devicecmd?SN={SerialNumber}", string.Join('\n', results) + "\n", token);
        }
    }

    /// <summary>Runs one command on the terminal; 0 is success.</summary>
    private int Execute(string command)
    {
        if (command.StartsWith("DATA UPDATE USERINFO", StringComparison.Ordinal))
        {
            var values = command["DATA UPDATE USERINFO".Length..].Trim()
                .Split('\t')
                .Select(x => x.Split('=', 2))
                .Where(x => x.Length == 2)
                .ToDictionary(x => x[0], x => x[1]);

            if (!values.TryGetValue("PIN", out var pin))
                return -1;

            lock (_gate)
            {
                var user = _users.FirstOrDefault(x => x.Pin == pin);

                if (user is null)
                {
                    // Created from IronForge: the person still has to enrol a finger here.
                    user = new SimulatedUser { Pin = pin, HasFingerprint = false };
                    _users.Add(user);
                }

                if (values.TryGetValue("Name", out var name))
                    user.Name = name;

                user.EndsAt = values.TryGetValue("EndDatetime", out var end) &&
                              DateTime.TryParseExact(end, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endsAt)
                    ? endsAt
                    : null;
            }

            Notify();
            return 0;
        }

        if (command.StartsWith("DATA QUERY USERINFO", StringComparison.Ordinal))
        {
            lock (_gate)
            {
                foreach (var user in _users)
                {
                    _pendingUserLines.AddRange(UserLines(user));
                }
            }

            return 0;
        }

        // Anything else this simulator does not know.
        return -1;
    }

    /// <summary>
    /// A real terminal sends the fingerprint template too (FP line, TMP=…).
    /// The simulator sends a fake one, so you can check IronForge drops it.
    /// </summary>
    private static IEnumerable<string> UserLines(SimulatedUser user)
    {
        yield return $"USER PIN={user.Pin}\tName={user.Name}\tPri=0\tPasswd=\tCard=\tGrp=1\tTZ=0000000100000000\tVerify=0";

        if (user.HasFingerprint)
        {
            yield return $"FP PIN={user.Pin}\tFID=6\tSize=1184\tValid=1\tTMP=SIMULATED-TEMPLATE-{Guid.NewGuid():N}";
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method,
        string url,
        string? body,
        CancellationToken token,
        bool logQuietOk = false)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        using var response = await _http().SendAsync(request, timeout.Token);
        var text = (await response.Content.ReadAsStringAsync(timeout.Token)).Trim();

        // Polls with nothing to do ("OK") would flood the log.
        if (!(logQuietOk && text == "OK" && response.IsSuccessStatusCode))
        {
            AddLog("→", $"{method} /{url.Split('?')[0]}" + (body is null ? "" : "\n" + Shorten(body)));
            AddLog("←", $"{(int)response.StatusCode} {Shorten(text)}");
        }

        return (response.StatusCode, text);
    }

    private void Show(SimulatedScreenTone tone, string title, string detail)
    {
        Screen = new SimulatedScreen(tone, title, detail);
        Notify();
    }

    private void AddLog(string direction, string text)
    {
        lock (_gate)
        {
            _log.AddFirst(new SimulatedLogLine(DateTime.Now, direction, text));

            while (_log.Count > MaxLogLines)
            {
                _log.RemoveLast();
            }
        }

        Notify();
    }

    private void Notify() => Changed?.Invoke();

    private static string Shorten(string text) =>
        text.Length > 600 ? text[..600] + " …" : text;

    private static string Clean(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c) && c != '=').ToArray()).Trim();
        return clean.Length > 24 ? clean[..24] : clean;
    }

    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>
/// The simulated terminals of this web server, by serial number. They keep
/// running while nobody watches the page, like hardware on a wall.
/// </summary>
public sealed class TerminalSimulatorHub : IDisposable
{
    private readonly Dictionary<string, SimulatedTerminal> _terminals = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SimulatedTerminal GetOrCreate(string serialNumber, string name, string timeZoneId, Func<HttpClient> http)
    {
        lock (_gate)
        {
            if (!_terminals.TryGetValue(serialNumber, out var terminal))
            {
                terminal = new SimulatedTerminal(serialNumber, name, timeZoneId, http);
                _terminals[serialNumber] = terminal;
            }

            return terminal;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var terminal in _terminals.Values)
            {
                terminal.Dispose();
            }

            _terminals.Clear();
        }
    }
}
