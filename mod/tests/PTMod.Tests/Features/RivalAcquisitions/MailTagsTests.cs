using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>MailTags</c> (TSK-013, TSK-014; design.md "Письма и уведомления"): the tags of the design (<c>acq:default:&lt;uniqueId&gt;</c>,
    /// <c>acq:auction:&lt;uniqueId&gt;</c>, <c>acq:deal:&lt;dealId&gt;</c>, <c>acq:stakeLost:&lt;uniqueId&gt;</c>, <c>acq:rescueFailed:&lt;uniqueId&gt;</c>) and the way back
    /// from a tag to the company of an action.
    /// </summary>
    [Covers(typeof(MailTags))]
    public class MailTagsTests
    {
        [Test]
        public void TheTags_AreThoseOfTheDesign_WithThePrefixOfTheFeature()
        {
            Assert.Equal("acq:default:3", MailTags.Default(3));
            Assert.Equal("acq:auction:7", MailTags.Auction(7));
            Assert.Equal("acq:deal:12", MailTags.Deal(12));
            Assert.Equal("acq:stakeLost:10", MailTags.StakeLost(10));
            Assert.Equal("acq:rescueFailed:10", MailTags.RescueFailed(10));
            Assert.Equal("deals", MailTags.ActionDeals);
            Assert.Equal("rescue", MailTags.ActionRescue);
        }

        [Test]
        public void TryParse_GivesTheKindAndTheNumberBack()
        {
            string kind;
            int number;
            foreach (string tag in new[] { MailTags.Default(0), MailTags.Auction(8), MailTags.Deal(1), MailTags.StakeLost(10), MailTags.RescueFailed(1) })
            {
                Assert.True(MailTags.TryParse(tag, out kind, out number), tag);
                Assert.Equal(tag, MailTags.Prefix + kind + ":" + number, tag + " round trip");
            }

            Assert.True(MailTags.TryParse("acq:deal:42", out kind, out number));
            Assert.Equal(MailTags.KindDeal, kind);
            Assert.Equal(42, number);
        }

        [Test]
        public void TryParse_RefusesWhatIsNotATagOfTheFeature()
        {
            string kind;
            int number;
            foreach (string tag in new[] { null, "", "acq:", "acq:deal", "acq:deal:", "acq::5", "acq:deal:-1", "acq:deal:x", "rival:deal:5", "onb:step:1" })
            {
                Assert.False(MailTags.TryParse(tag, out kind, out number), "'" + tag + "'");
                Assert.Null(kind);
                Assert.Equal(-1, number);
            }
        }
    }
}
