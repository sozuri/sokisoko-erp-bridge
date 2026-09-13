using System.Net.Sockets;

namespace SokiSoko.SapBridge.Ui;

/// <summary>
/// Turns raw connection exceptions into actionable guidance for self-serve setup —
/// the fix is usually a typo'd URL, a firewall, or a missing license, and the operator
/// shouldn't need us to tell them which.
/// </summary>
public static class Diagnostics
{
    public static string ExplainB1(Exception ex)
    {
        var msg = ex.Message;
        // Plain HTTP to the Service Layer's TLS port is refused by Apache with an empty 400
        // long before B1 sees the request - it looks like a credential fault but is not.
        if (msg.Contains("login to http://", StringComparison.OrdinalIgnoreCase))
            return "The Service Layer URL starts with http://. B1 serves HTTPS on port 50000 and " +
                   "rejects plain HTTP with an empty 400 before the login is ever evaluated. " +
                   "Change the URL to https:// — the credentials are not the problem here.";
        if (msg.Contains("Invalid login credential", StringComparison.OrdinalIgnoreCase))
            return "B1 rejected the credentials. It returns this same message for a wrong password, " +
                   "an unknown user and a company database it doesn't recognise, so check all three: " +
                   "the company DB is case-sensitive, and the user needs a Service Layer licence.";
        if (msg.Contains("NONE-SSO", StringComparison.OrdinalIgnoreCase) || msg.Contains("-304"))
            return "The SLD rejected the login (-304). The username or password is wrong, or the user " +
                   "has no B1 licence assigned (B1 → Administration → License → License Administration).";
        if (msg.Contains("401") || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            return "B1 rejected the login (401). Check the company database name, username and password; " +
                   "the user needs a B1 license with Service Layer access (B1 → Administration → License).";
        if (msg.Contains("certificate", StringComparison.OrdinalIgnoreCase) || msg.Contains("SSL", StringComparison.OrdinalIgnoreCase))
            return "The B1 Service Layer's TLS certificate isn't trusted. If it's self-signed, set " +
                   "'Untrusted cert' to yes — or install the cert into the machine's Trusted Root store.";
        if (msg.Contains("404"))
            return "Got 404 — the URL reached a server but not the Service Layer. " +
                   "Use the Service Layer base URL (https://host:50000), not the B1 client or Web Client.";
        if (msg.Contains("400"))
            return "B1 rejected the login request (400) — the Service Layer answered, so the URL and port " +
                   "are right. Check the company database, username and password are all filled in and spelt " +
                   "exactly as in B1 (the company DB is case-sensitive and must not have stray spaces).";
        if (IsUnreachable(ex))
            return "The B1 host is unreachable. Check the URL and port (default 50000), that the Service Layer " +
                   "is running (SAP B1 Service Manager), and that the firewall allows this machine through.";
        return msg;
    }

    public static string ExplainSokisoko(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("401"))
            return "SokiSoko rejected the API key (401). Check for typos — the key starts with tgk_ and is " +
                   "shown only once at creation; create a fresh one under Settings → API keys if unsure.";
        if (msg.Contains("403"))
            return "The API key works but lacks the erp.manage scope (403). Edit the key in " +
                   "Settings → API keys and add erp.manage.";
        if (msg.Contains("404"))
            return "Got 404 — the connection ID doesn't exist (or the URL isn't the SokiSoko API). " +
                   "Copy the ID from Settings → ERP sync → your SAP Business One connection.";
        if (msg.Contains("502") || msg.Contains("503") || msg.Contains("504"))
            return $"The SokiSoko gateway returned {msg[..Math.Min(3, msg.Length)]} — the API behind it is down or " +
                   "starting. Check the SokiSoko server, then retry.";
        if (IsUnreachable(ex))
            return "The SokiSoko URL is unreachable from this machine. Check the URL, DNS, and that outbound " +
                   "HTTPS (443) is allowed.";
        return msg;
    }

    public static string ExplainOdoo(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("rejected the login") || msg.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase))
            return "Odoo rejected the login. Check the database name and username; use an API key " +
                   "(Odoo → Preferences → Account Security → API keys) rather than the account password.";
        if (msg.Contains("404") || msg.Contains("database", StringComparison.OrdinalIgnoreCase) && msg.Contains("not", StringComparison.OrdinalIgnoreCase))
            return "The Odoo database wasn't found. Check the database name (Odoo login screen → Manage Databases) " +
                   "and that the URL is the Odoo base (https://mycompany.odoo.com), not /web.";
        if (IsUnreachable(ex))
            return "The Odoo host is unreachable. Check the URL and that outbound HTTPS (443) is allowed " +
                   "from this machine.";
        return msg;
    }

    /// <summary>
    /// Only true when nothing answered. A HttpRequestException carrying a StatusCode came from
    /// EnsureSuccessStatusCode, i.e. the server replied — reporting that as "unreachable" sends
    /// the operator off checking firewalls when the real fault is in the request.
    /// </summary>
    private static bool IsUnreachable(Exception ex) =>
        ex is HttpRequestException { StatusCode: null } or SocketException ||
        ex.InnerException is SocketException ||
        ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("resolve", StringComparison.OrdinalIgnoreCase);
}
