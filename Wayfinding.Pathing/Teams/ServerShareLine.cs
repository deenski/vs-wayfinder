namespace Wayfinding.Pathing.Teams;

/// <summary>How <see cref="ServerShareLine.Resolve"/> classified a chat line.</summary>
public enum ShareLineStatus
{
    /// <summary>The line has no share payload.</summary>
    None,

    /// <summary>A payload was present, but the sender was not verified. No state should change.</summary>
    Unverified,

    /// <summary>The server prefix names one online player, and that player's uid matches the payload.</summary>
    Verified,
}

/// <summary>A chat line after the server name prefix has been checked.</summary>
/// <param name="Status">Whether a payload was found and the sender verified.</param>
/// <param name="Notice">The payload. Default when <see cref="Status"/> is <see cref="ShareLineStatus.None"/>.</param>
/// <param name="SenderName">Name inside the server prefix, or empty when the prefix is missing.</param>
public readonly record struct ShareLineResolution(ShareLineStatus Status, ShareNotice Notice, string SenderName);

/// <summary>
/// Reads a share line the way the 1.22.7 server broadcasts it.
/// The server escapes <c>&lt;</c> and <c>&gt;</c> in typed text, then wraps the
/// line as <c>&lt;strong&gt;{PlayerName}:&lt;/strong&gt; </c> (or the same tags
/// inside a font color). That prefix cannot be forged by the player. The payload
/// is read only from the text after the prefix, and the name is mapped through
/// the online player list. An unknown name, a missing prefix, or a uid that
/// does not match is unverified. Callers drop an unverified share-on.
/// An unverified share-off still hides that uid.
/// </summary>
public static class ServerShareLine
{
    private const string StrongHead = "<strong>";
    private const string StrongTail = ":</strong> ";
    private const string FontHead = "<font color=\"";
    private const string FontMid = "\"><strong>";
    private const string FontTail = ":</strong></font> ";

    /// <summary>
    /// Splits a server-built chat line into the player name and the text after the prefix.
    /// False when the line does not start with the server's strong prefix and the required space.
    /// </summary>
    public static bool TrySplit(string? message, out string senderName, out string body)
    {
        senderName = "";
        body = "";
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        if (message.StartsWith(FontHead, StringComparison.Ordinal))
        {
            return TrySplitFont(message, out senderName, out body);
        }

        if (!message.StartsWith(StrongHead, StringComparison.Ordinal))
        {
            return false;
        }

        var nameEnd = message.IndexOf(StrongTail, StrongHead.Length, StringComparison.Ordinal);
        if (nameEnd < 0)
        {
            return false;
        }

        senderName = message[StrongHead.Length..nameEnd];
        body = message[(nameEnd + StrongTail.Length)..];
        if (!IsPlayerName(senderName))
        {
            senderName = "";
            body = "";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the sender of a share line. Ordinary chat is <see cref="ShareLineStatus.None"/>.
    /// A payload with no real server prefix, an unknown name, two players with the same name,
    /// or a payload uid that is not that player's uid is <see cref="ShareLineStatus.Unverified"/>.
    /// </summary>
    public static ShareLineResolution Resolve(string? message, IReadOnlyList<OnlinePlayer>? onlinePlayers)
    {
        if (TrySplit(message, out var senderName, out var body))
        {
            if (!ShareAnnouncement.TryParsePayload(body, out var notice))
            {
                return new ShareLineResolution(ShareLineStatus.None, default, senderName);
            }

            if (!TryMapName(onlinePlayers, senderName, out var uid)
                || !string.Equals(uid, notice.PlayerUid, StringComparison.Ordinal))
            {
                return new ShareLineResolution(ShareLineStatus.Unverified, notice, senderName);
            }

            return new ShareLineResolution(ShareLineStatus.Verified, notice, senderName);
        }

        if (ShareAnnouncement.TryParsePayload(message, out var bare))
        {
            return new ShareLineResolution(ShareLineStatus.Unverified, bare, "");
        }

        return new ShareLineResolution(ShareLineStatus.None, default, "");
    }

    private static bool TrySplitFont(string message, out string senderName, out string body)
    {
        senderName = "";
        body = "";
        var quote = message.IndexOf('"', FontHead.Length);
        if (quote <= FontHead.Length || !message.AsSpan(quote).StartsWith(FontMid))
        {
            return false;
        }

        var nameStart = quote + FontMid.Length;
        var nameEnd = message.IndexOf(FontTail, nameStart, StringComparison.Ordinal);
        if (nameEnd < 0)
        {
            return false;
        }

        senderName = message[nameStart..nameEnd];
        body = message[(nameEnd + FontTail.Length)..];
        if (!IsPlayerName(senderName))
        {
            senderName = "";
            body = "";
            return false;
        }

        return true;
    }

    private static bool TryMapName(IReadOnlyList<OnlinePlayer>? onlinePlayers, string senderName, out string uid)
    {
        uid = "";
        if (onlinePlayers is null)
        {
            return false;
        }

        var found = false;
        foreach (var player in onlinePlayers)
        {
            if (string.IsNullOrEmpty(player.Name)
                || string.IsNullOrEmpty(player.Uid)
                || !string.Equals(player.Name, senderName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!found)
            {
                uid = player.Uid;
                found = true;
                continue;
            }

            if (!string.Equals(uid, player.Uid, StringComparison.Ordinal))
            {
                uid = "";
                return false;
            }
        }

        return found;
    }

    private static bool IsPlayerName(string name)
    {
        return name.Length > 0 && name.IndexOf('<') < 0 && name.IndexOf('>') < 0;
    }
}
