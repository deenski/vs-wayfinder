using System.Globalization;
using Wayfinding.Pathing.Teams;

namespace Wayfinding.Pathing.Tests.Teams;

public class TeamVisibilityTests
{
    private const int PartyId = 1;
    private const int CampId = 2;
    private const long MailiEntity = 4821;
    private const long OrenEntity = 4822;
    private const long LocalEntity = 100;
    private const long UnknownEntity = 9999;

    private static readonly OnlinePlayer Maili = new("maili", MailiEntity, true);
    private static readonly OnlinePlayer Oren = new("oren", OrenEntity, true);
    private static readonly OnlinePlayer Local = new("local", LocalEntity, true);

    [Fact]
    public void Non_teammate_is_never_visible()
    {
        var team = PartyOf("local");

        Assert.False(team.IsVisibleTeammate("stranger"));
        Assert.False(team.IsVisibleTeammate(null));
        Assert.False(team.IsVisibleTeammate(""));
        Assert.Equal(
            ShareChatDisposition.NotInGroup,
            Post(team, 99, sharing: true, "stranger", UnknownEntity, [new OnlinePlayer("stranger", UnknownEntity, true)]));
        Assert.False(team.IsVisibleTeammate("stranger"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Player_in_the_group_without_an_announcement_is_not_visible()
    {
        var team = PartyOf("local");

        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Announcement_with_share_on_makes_the_player_visible()
    {
        var team = PartyOf("local");

        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, true, Maili));

        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());
    }

    [Fact]
    public void Announcement_with_share_off_hides_the_player()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, true, Maili);
        Assert.True(team.IsVisibleTeammate("maili"));

        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, false, Maili));

        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Nav_team_restricts_visibility_to_that_group()
    {
        var team = PartyOf("local");
        team.SetMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);
        Announce(team, PartyId, true, Maili);
        Announce(team, CampId, true, Oren);
        Assert.Equal(new[] { "maili", "oren" }, team.GetVisibleTeammates().ToArray());

        var restricted = team.TryRestrictTo("scouts", out var canonical);

        Assert.Equal(TeamRestrictResult.Restricted, restricted);
        Assert.Equal("scouts", canonical);
        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.False(team.IsVisibleTeammate("oren"));
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());

        team.ClearRestriction();

        Assert.Null(team.TeamRestriction);
        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.True(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Nav_team_rejects_a_group_the_player_is_not_in()
    {
        var team = PartyOf("local");

        var result = team.TryRestrictTo("camp", out _);

        Assert.Equal(TeamRestrictResult.NotAMember, result);
        Assert.Null(team.TeamRestriction);
    }

    [Fact]
    public void Leaving_the_group_removes_visibility_until_a_new_announcement()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, true, Maili);
        Assert.True(team.IsVisibleTeammate("maili"));

        team.SetMembership([]);

        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());

        team.SetMembership([new TeamGroup(PartyId, "scouts")]);

        Assert.False(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Evidence_in_one_group_does_not_count_for_another()
    {
        var team = PartyOf("local");
        team.SetMembership([new TeamGroup(CampId, "camp")]);
        Announce(team, CampId, true, Maili);

        team.SetMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);
        Assert.Equal(TeamRestrictResult.Restricted, team.TryRestrictTo("scouts", out _));

        Assert.False(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Opt_out_on_any_member_group_hides_the_player_immediately()
    {
        var team = PartyOf("local");
        team.SetMembership(
        [
            new TeamGroup(PartyId, "scouts"),
            new TeamGroup(CampId, "camp"),
        ]);
        Announce(team, PartyId, true, Maili);
        Announce(team, CampId, true, Maili);
        Assert.Equal(TeamRestrictResult.Restricted, team.TryRestrictTo("scouts", out _));
        Assert.True(team.IsVisibleTeammate("maili"));

        Assert.Equal(ShareChatDisposition.Applied, Announce(team, CampId, false, Maili));

        Assert.False(team.IsVisibleTeammate("maili"));
        team.ClearRestriction();
        Assert.False(team.IsVisibleTeammate("maili"));
    }

    [Fact]
    public void Malformed_or_foreign_payloads_are_ignored()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, true, Maili);
        var before = team.RecordedShares();

        string?[] payloads =
        [
            null,
            "",
            "hello",
            "wayfinding:v1:share:maybe:maili",
            "wayfinding:v1:share:on:",
            "wayfinding:v1:share:ON:maili",
            "wayfinding:v2:share:on:other",
            "other:v1:share:on:other",
            "from: 12,withoutPrefix:[Wayfinding] Maili is sharing location",
        ];

        foreach (var payload in payloads)
        {
            Assert.Equal(ShareChatDisposition.Ignored, team.TryApplyChat(PartyId, payload, payload, [Maili]));
        }

        AssertUnchanged(team, before);
        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.False(team.IsVisibleTeammate("other"));
    }

    [Fact]
    public void Unknown_entity_id_does_not_change_state()
    {
        var team = VisibleMaili(out var before);

        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, sharing: false, "maili", UnknownEntity, [Maili, Oren]));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, sharing: true, "oren", UnknownEntity, [Maili, Oren]));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Known_unloaded_entity_does_not_change_state()
    {
        var team = VisibleMaili(out var before);
        var mailiUnloaded = new OnlinePlayer("maili", MailiEntity, false);
        var orenUnloaded = new OnlinePlayer("oren", OrenEntity, false);

        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, sharing: false, "maili", MailiEntity, [mailiUnloaded, Oren]));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, sharing: true, "oren", OrenEntity, [Maili, orenUnloaded]));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Payload_uid_that_does_not_match_the_sender_does_not_change_state()
    {
        var team = VisibleMaili(out var before);
        var someoneElse = new OnlinePlayer("someone-else", 7777, true);

        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, sharing: false, "maili", someoneElse.EntityId, [Maili, someoneElse]));
        Assert.Equal(
            ShareChatDisposition.Unverified,
            Post(team, PartyId, sharing: true, "oren", Maili.EntityId, [Maili, Oren]));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
        Assert.False(team.IsVisibleTeammate("someone-else"));
    }

    [Fact]
    public void Payload_typed_in_a_group_the_player_is_not_in_does_not_change_state()
    {
        var team = VisibleMaili(out var before);

        Assert.Equal(
            ShareChatDisposition.NotInGroup,
            Post(team, 99, sharing: false, "maili", MailiEntity, [Maili, Oren]));
        Assert.Equal(
            ShareChatDisposition.NotInGroup,
            Post(team, 99, sharing: true, "oren", OrenEntity, [Maili, Oren]));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Payload_without_a_from_entity_id_does_not_change_state()
    {
        var team = VisibleMaili(out var before);
        var off = ShareAnnouncement.FormatPayload(false, "maili");
        var on = ShareAnnouncement.FormatPayload(true, "oren");

        Assert.Equal(ShareChatDisposition.Unverified, team.TryApplyChat(PartyId, null, off, [Maili, Oren]));
        Assert.Equal(ShareChatDisposition.Unverified, team.TryApplyChat(PartyId, on, "", [Maili, Oren]));

        AssertUnchanged(team, before);
        Assert.False(team.IsVisibleTeammate("oren"));
    }

    [Fact]
    public void Verified_line_from_a_loaded_player_updates_visibility()
    {
        var team = PartyOf("local");
        var message = ShareAnnouncement.FormatChatMessage("Maili", true, "maili");
        var data = "from: " + MailiEntity.ToString(CultureInfo.InvariantCulture) + ",withoutPrefix:" + message;

        Assert.Equal(ShareChatDisposition.Applied, team.TryApplyChat(PartyId, message, data, [Maili, Oren]));

        Assert.True(team.IsVisibleTeammate("maili"));
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());
        Assert.Equal(new[] { new ShareRecord(PartyId, "maili", true) }, team.RecordedShares());

        var off = ShareAnnouncement.FormatChatMessage("Maili", false, "maili");
        var offData = "from: " + MailiEntity.ToString(CultureInfo.InvariantCulture) + ",withoutPrefix:" + off;
        Assert.Equal(ShareChatDisposition.Applied, team.TryApplyChat(PartyId, off, offData, [Maili]));
        Assert.False(team.IsVisibleTeammate("maili"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Local_player_is_never_a_visible_teammate()
    {
        var team = PartyOf("local");
        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, true, Local));

        Assert.False(team.IsVisibleTeammate("local"));
        Assert.Empty(team.GetVisibleTeammates());
    }

    [Fact]
    public void Share_on_after_share_off_is_visible_again()
    {
        var team = PartyOf("local");
        Announce(team, PartyId, false, Maili);
        Assert.False(team.IsVisibleTeammate("maili"));

        Announce(team, PartyId, true, Maili);

        Assert.True(team.IsVisibleTeammate("maili"));
    }

    private static TeamModel VisibleMaili(out IReadOnlyList<ShareRecord> recorded)
    {
        var team = PartyOf("local");
        Assert.Equal(ShareChatDisposition.Applied, Announce(team, PartyId, true, Maili));
        Assert.True(team.IsVisibleTeammate("maili"));
        recorded = team.RecordedShares();
        return team;
    }

    private static void AssertUnchanged(TeamModel team, IReadOnlyList<ShareRecord> recorded)
    {
        Assert.Equal(recorded, team.RecordedShares());
        Assert.Equal(new[] { "maili" }, team.GetVisibleTeammates().ToArray());
        Assert.True(team.IsVisibleTeammate("maili"));
    }

    private static ShareChatDisposition Announce(TeamModel team, int groupId, bool sharing, OnlinePlayer sender)
    {
        return Post(team, groupId, sharing, sender.Uid, sender.EntityId, [sender]);
    }

    private static ShareChatDisposition Post(
        TeamModel team,
        int groupId,
        bool sharing,
        string payloadUid,
        long entityId,
        IReadOnlyList<OnlinePlayer> online)
    {
        var message = ShareAnnouncement.FormatChatMessage(payloadUid, sharing, payloadUid);
        var data = "from: " + entityId.ToString(CultureInfo.InvariantCulture) + ",withoutPrefix:" + message;
        return team.TryApplyChat(groupId, message, data, online);
    }

    private static TeamModel PartyOf(string localUid)
    {
        var team = new TeamModel();
        team.SetLocalPlayer(localUid);
        team.SetMembership([new TeamGroup(PartyId, "scouts")]);
        return team;
    }
}
