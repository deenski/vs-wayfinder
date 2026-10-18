namespace Wayfinding.Pathing.Teams;

/// <summary>Which side of a chat line this client is on.</summary>
public enum ShareChatKind
{
    /// <summary>The server echoed this client's own chat.</summary>
    Own,

    /// <summary>Another player's chat line.</summary>
    Other,

    /// <summary>Command, notification, or any other chat type. It does not change teams or spacing.</summary>
    Ignored,
}

/// <summary>What one incoming chat line did.</summary>
/// <param name="Disposition">How the line was classified.</param>
/// <param name="QueuedReply">True when this line queued one reply of the local share state.</param>
/// <param name="SenderName">Name from the server prefix, or empty when there was none.</param>
/// <param name="PayloadUid">Uid named by the payload, or empty when there was no payload.</param>
public readonly record struct ShareReceipt(
    ShareChatDisposition Disposition,
    bool QueuedReply,
    string SenderName,
    string PayloadUid)
{
    /// <summary>A line that was not a share announcement.</summary>
    public static ShareReceipt Ignored { get; } = new(ShareChatDisposition.Ignored, false, "", "");
}

/// <summary>
/// Membership announcements, incoming share lines, and the per-group send queue.
/// The first verified announcement from a uid in a group queues one reply of
/// this client's current share state. A repeat queues nothing. The queue keeps
/// one message per group.
/// </summary>
public sealed class ShareSession
{
    /// <summary>Creates a session over an existing model and outbox.</summary>
    public ShareSession(TeamModel model, ShareOutbox outbox)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    /// <summary>Membership, evidence, and the visibility filter.</summary>
    public TeamModel Model { get; }

    /// <summary>Announcements waiting for this client's own echo.</summary>
    public ShareOutbox Outbox { get; }

    /// <summary>Display name used in the local announcement. Empty becomes "A player".</summary>
    public string LocalName { get; set; } = "";

    /// <summary>Local share flag copied into the next announcement.</summary>
    public bool Sharing { get; set; } = true;

    /// <summary>The chat line this client would post for its current share flag.</summary>
    public string CurrentAnnouncement()
    {
        return ShareAnnouncement.FormatChatMessage(LocalName, Sharing, Model.LocalPlayerUid ?? "");
    }

    /// <summary>Queues the current share state to every membership group. Share-off is included.</summary>
    public void AnnounceToMembership()
    {
        if (string.IsNullOrEmpty(Model.LocalPlayerUid))
        {
            return;
        }

        var text = CurrentAnnouncement();
        foreach (var group in Model.Membership)
        {
            Outbox.Enqueue(group.GroupId, text);
        }
    }

    /// <summary>
    /// Replaces membership, drops queues for groups that were left, and announces
    /// the current share state to every group that remains.
    /// </summary>
    public void ReplaceMembership(IReadOnlyList<TeamGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Model.SetMembership(groups);
        var ids = new List<int>(Model.Membership.Count);
        foreach (var group in Model.Membership)
        {
            ids.Add(group.GroupId);
        }

        Outbox.DropExcept(ids);
        AnnounceToMembership();
    }

    /// <summary>
    /// Handles one chat line. Own chat restarts spacing and acknowledges a matching
    /// echo. Other players' verified lines update evidence. The first one from a
    /// uid in that group queues a single reply. Ignored chat types do neither.
    /// </summary>
    public ShareReceipt Receive(
        ShareChatKind kind,
        int groupId,
        string? message,
        IReadOnlyList<OnlinePlayer>? onlinePlayers)
    {
        if (kind == ShareChatKind.Own)
        {
            Outbox.NoteOwnEcho(groupId, message);
            return ShareReceipt.Ignored;
        }

        if (kind != ShareChatKind.Other)
        {
            return ShareReceipt.Ignored;
        }

        var resolution = ServerShareLine.Resolve(message, onlinePlayers);
        if (resolution.Status == ShareLineStatus.None)
        {
            return new ShareReceipt(ShareChatDisposition.Ignored, false, resolution.SenderName, "");
        }

        if (resolution.Status != ShareLineStatus.Verified)
        {
            return new ShareReceipt(
                ShareChatDisposition.Unverified,
                false,
                resolution.SenderName,
                resolution.Notice.PlayerUid);
        }

        var uid = resolution.Notice.PlayerUid;
        var known = Model.HasEvidence(groupId, uid);
        var disposition = Model.TryApplyChat(groupId, message, onlinePlayers);
        var queued = false;
        if (disposition == ShareChatDisposition.Applied
            && !known
            && !string.IsNullOrEmpty(Model.LocalPlayerUid)
            && !string.Equals(uid, Model.LocalPlayerUid, StringComparison.Ordinal))
        {
            Outbox.Enqueue(groupId, CurrentAnnouncement());
            queued = true;
        }

        return new ShareReceipt(disposition, queued, resolution.SenderName, uid);
    }
}
