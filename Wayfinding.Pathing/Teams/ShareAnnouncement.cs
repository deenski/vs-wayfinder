using System.Globalization;

namespace Wayfinding.Pathing.Teams;

/// <summary>A parsed share payload.</summary>
/// <param name="Sharing">True when the player is sharing location.</param>
/// <param name="PlayerUid">Uid named by the payload.</param>
public readonly record struct ShareNotice(bool Sharing, string PlayerUid);

/// <summary>A parsed incoming chat line that carries a share payload.</summary>
/// <param name="Notice">The payload.</param>
/// <param name="SenderEntityId">Entity id from <c>from:</c>, or null when that wrapper is absent.</param>
public readonly record struct ParsedShareChat(ShareNotice Notice, long? SenderEntityId);

/// <summary>
/// Share announcements. The human sentence is what players read. The payload
/// is <c>wayfinding:v1:share:on|off:playerUid</c>.
/// </summary>
public static class ShareAnnouncement
{
    /// <summary>Payload prefix <c>wayfinding:v1:share:</c>.</summary>
    public const string Prefix = "wayfinding:v1:share:";

    // Vintage Story 1.22.7 ServerMain.HandleChatLine reads the chat packet's
    // message and group id and drops the client data field. The broadcast
    // data is replaced with "from: {entityId},withoutPrefix:{message}".
    // The payload therefore also rides in the message text, which the server
    // does forward. Receivers accept the prefix from either field.
    private const string SenderPrefix = "from: ";
    private const string SenderTail = ",withoutPrefix:";

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

    /// <summary>Reads one payload from text. False for a missing, malformed, or foreign prefix.</summary>
    public static bool TryParsePayload(string? text, out ShareNotice notice)
    {
        notice = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var index = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var rest = text.AsSpan(index + Prefix.Length);
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

    /// <summary>Reads the entity id from <c>from: &lt;entityId&gt;,withoutPrefix:</c>.</summary>
    public static bool TryReadSenderEntityId(string? data, out long entityId)
    {
        entityId = 0;
        if (string.IsNullOrEmpty(data) || !data.StartsWith(SenderPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var end = data.IndexOf(SenderTail, SenderPrefix.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        var number = data.Substring(SenderPrefix.Length, end - SenderPrefix.Length);
        return long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out entityId);
    }

    /// <summary>
    /// Reads a payload from the message, the data, or both. False when both exist and disagree,
    /// or when neither contains a payload.
    /// </summary>
    public static bool TryParseIncoming(string? message, string? data, out ParsedShareChat parsed)
    {
        parsed = default;
        long? senderEntityId = TryReadSenderEntityId(data, out var entityId) ? entityId : null;

        ShareNotice? found = null;
        if (TryParsePayload(data, out var fromData))
        {
            found = fromData;
        }

        if (TryParsePayload(message, out var fromMessage))
        {
            if (found is ShareNotice existing && existing != fromMessage)
            {
                return false;
            }

            found = fromMessage;
        }

        if (found is not ShareNotice notice)
        {
            return false;
        }

        parsed = new ParsedShareChat(notice, senderEntityId);
        return true;
    }
}
