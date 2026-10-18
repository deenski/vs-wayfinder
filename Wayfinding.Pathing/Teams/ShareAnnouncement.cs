namespace Wayfinding.Pathing.Teams;

/// <summary>A parsed share payload.</summary>
/// <param name="Sharing">True when the player is sharing location.</param>
/// <param name="PlayerUid">Uid named by the payload.</param>
public readonly record struct ShareNotice(bool Sharing, string PlayerUid);

/// <summary>
/// Share announcements. The human sentence is what players read. The payload
/// is <c>wayfinding:v1:share:on|off:playerUid</c>.
/// </summary>
public static class ShareAnnouncement
{
    /// <summary>Payload prefix <c>wayfinding:v1:share:</c>.</summary>
    public const string Prefix = "wayfinding:v1:share:";

    /// <summary>Builds <c>wayfinding:v1:share:on|off:playerUid</c>.</summary>
    public static string FormatPayload(bool sharing, string playerUid)
    {
        return Prefix + (sharing ? "on:" : "off:") + playerUid;
    }

    /// <summary>The sentence players read. An empty name becomes "A player".</summary>
    public static string FormatHumanLine(string playerName, bool sharing)
    {
        var name = string.IsNullOrWhiteSpace(playerName) ? "A player" : playerName.Trim();
        return sharing
            ? "[Wayfinding] " + name + " is sharing location"
            : "[Wayfinding] " + name + " stopped sharing location";
    }

    /// <summary>Human sentence and payload in one message, because 1.22.7 drops client chat data.</summary>
    public static string FormatChatMessage(string playerName, bool sharing, string playerUid)
    {
        return FormatHumanLine(playerName, sharing) + " " + FormatPayload(sharing, playerUid);
    }

    /// <summary>
    /// Reads one payload from text. The prefix must start the text or follow whitespace,
    /// so a glued token such as <c>xwayfinding:</c> does not match. A uid that contains
    /// <c>:</c> is rejected.
    /// </summary>
    public static bool TryParsePayload(string? text, out ShareNotice notice)
    {
        notice = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var start = -1;
        var index = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf(Prefix, index, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            if (found == 0 || char.IsWhiteSpace(text[found - 1]))
            {
                start = found;
                break;
            }

            index = found + Prefix.Length;
        }

        if (start < 0)
        {
            return false;
        }

        var rest = text.AsSpan(start + Prefix.Length);
        bool sharing;
        if (rest.StartsWith("on:"))
        {
            sharing = true;
            rest = rest[3..];
        }
        else if (rest.StartsWith("off:"))
        {
            sharing = false;
            rest = rest[4..];
        }
        else
        {
            return false;
        }

        var length = 0;
        while (length < rest.Length && !char.IsWhiteSpace(rest[length]))
        {
            length++;
        }

        if (length == 0)
        {
            return false;
        }

        var uid = rest[..length].ToString();
        if (uid.IndexOf(':') >= 0)
        {
            return false;
        }

        notice = new ShareNotice(sharing, uid);
        return true;
    }
}
