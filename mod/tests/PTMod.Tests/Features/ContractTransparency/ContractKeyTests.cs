using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>TSK-006 and D7: the key that ties log entries to a contract is stable, tells contracts apart and can always be taken apart again.</summary>
    [Covers(typeof(ContractKeyBuilder), typeof(ContractKeyParts))]
    public class ContractKeyTests
    {
        [Test]
        public void TheKey_HasTheFivePartsOfTheDesign()
        {
            Assert.Equal("Apple|1986-1|52000|299|1", ContractKeyBuilder.Build("Apple", 1986, 1, 52000, 299f, 1));
            Assert.Equal("IBM|1990-12|1000|99.5|0", ContractKeyBuilder.Build("IBM", 1990, 12, 1000, 99.5f, 0));
        }

        [Test]
        public void TheKey_IsStable_AndTellsContractsApart()
        {
            string key = ContractKeyBuilder.Build("Apple", 1986, 1, 52000, 299f, 1);

            Assert.Equal(key, ContractKeyBuilder.Build("Apple", 1986, 1, 52000, 299f, 1));
            Assert.NotEqual(key, ContractKeyBuilder.Build("Apple", 1986, 2, 52000, 299f, 1), "another month");
            Assert.NotEqual(key, ContractKeyBuilder.Build("Apple", 1987, 1, 52000, 299f, 1), "another year");
            Assert.NotEqual(key, ContractKeyBuilder.Build("Apple", 1986, 1, 52001, 299f, 1), "other units");
            Assert.NotEqual(key, ContractKeyBuilder.Build("Apple", 1986, 1, 52000, 300f, 1), "other payment");
            Assert.NotEqual(key, ContractKeyBuilder.Build("Apple", 1986, 1, 52000, 299f, 2), "other type");
            Assert.NotEqual(key, ContractKeyBuilder.Build("Atari", 1986, 1, 52000, 299f, 1), "other client");
        }

        [Test]
        public void APipeInTheNameOfTheClient_IsEscaped()
        {
            string key = ContractKeyBuilder.Build("A|B", 1986, 1, 100, 10f, 1);

            Assert.Equal("A\\|B|1986-1|100|10|1", key);
            ContractKeyParts parts;
            Assert.True(ContractKeyBuilder.TryParse(key, out parts));
            Assert.Equal("A|B", parts.Client, "the name comes back whole");
            Assert.Equal(1986, parts.Year);
            Assert.Equal(1, parts.Month);
            Assert.Equal(100, parts.Units);
            Assert.Near(10.0, parts.Payment);
            Assert.Equal(1, parts.ContractType);
        }

        [Test]
        public void ABackslashAndSeveralPipes_SurviveTheRoundTrip()
        {
            string name = "a\\|b||c\\";
            string key = ContractKeyBuilder.Build(name, 1975, 12, 7, 0.5f, 2);

            ContractKeyParts parts;
            Assert.True(ContractKeyBuilder.TryParse(key, out parts), key);
            Assert.Equal(name, parts.Client);
            Assert.Equal(12, parts.Month);
            Assert.Near(0.5, parts.Payment);
            Assert.NotEqual(ContractKeyBuilder.Build("a|b", 1975, 12, 7, 0.5f, 2), ContractKeyBuilder.Build("a\\|b", 1975, 12, 7, 0.5f, 2), "the escape does not make two names one");
        }

        [Test]
        public void TwoNames_ThatOnlyDifferByWhereThePipeIs_GiveTwoKeys()
        {
            // Without the escape "A|B" + "1986-1" and "A" + "B|1986-1" could not be told apart by eye or by a split.
            string first = ContractKeyBuilder.Build("A|B", 1986, 1, 1, 1f, 0);
            string second = ContractKeyBuilder.Build("A", 1986, 1, 1, 1f, 0);

            Assert.NotEqual(first, second);
            ContractKeyParts parts;
            Assert.True(ContractKeyBuilder.TryParse(first, out parts));
            Assert.Equal("A|B", parts.Client);
        }

        [Test]
        public void ANullOrLongClient_IsHandled_AndTheKeyFitsTheLimitOfTheLog()
        {
            Assert.Equal("|1986-1|1|1|0", ContractKeyBuilder.Build(null, 1986, 1, 1, 1f, 0));

            string hostile = new string('|', 500);
            string key = ContractKeyBuilder.Build(hostile, 1990, 12, 2000000000, 99999.99f, 2);
            Assert.True(key.Length <= TextSafe.MaxLength, "the key is " + key.Length + " characters, the log keeps " + TextSafe.MaxLength);
            ContractKeyParts parts;
            Assert.True(ContractKeyBuilder.TryParse(key, out parts), "the parts after the client are not lost");
            Assert.Equal(ContractKeyBuilder.MaxClientLength, parts.Client.Length);
            Assert.Equal(12, parts.Month);
            Assert.Equal(2, parts.ContractType);
        }

        [Test]
        public void ThePayment_IsWrittenPlainly()
        {
            Assert.True(ContractKeyBuilder.Build("A", 1986, 1, 1, 299.123f, 0).Contains("|299.12|"));
            Assert.True(ContractKeyBuilder.Build("A", 1986, 1, 1, 0f, 0).Contains("|0|"));
            Assert.True(ContractKeyBuilder.Build("A", 1986, 1, 1, float.NaN, 0).Contains("|0|"), "a payment that is not a number is 0");
            Assert.True(ContractKeyBuilder.Build("A", 1986, 1, 1, float.PositiveInfinity, 0).Contains("|0|"));
            Assert.False(ContractKeyBuilder.Build("A", 1986, 1, 1, 1e9f, 0).Contains("E"), "no exponent form");
        }

        [Test]
        public void TryParse_RefusesWhatIsNotAKey()
        {
            ContractKeyParts parts;

            Assert.False(ContractKeyBuilder.TryParse(null, out parts));
            Assert.False(ContractKeyBuilder.TryParse(string.Empty, out parts));
            Assert.False(ContractKeyBuilder.TryParse("Apple", out parts));
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986-1|100|10", out parts), "four parts");
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986-1|100|10|1|9", out parts), "six parts");
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986-13|100|10|1", out parts), "month 13");
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986-0|100|10|1", out parts), "month 0");
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986|100|10|1", out parts), "no month");
            Assert.False(ContractKeyBuilder.TryParse("Apple|x-1|100|10|1", out parts));
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986-1|many|10|1", out parts));
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986-1|100|cheap|1", out parts));
            Assert.False(ContractKeyBuilder.TryParse("Apple|1986-1|100|10|one", out parts));
            Assert.False(ContractKeyBuilder.TryParse("Apple\\", out parts), "an escape at the very end");
            Assert.Null(parts.Client);
        }

        [Test]
        public void Escape_PutsABackslashBeforeThePipeAndBeforeItself()
        {
            Assert.Equal(string.Empty, ContractKeyBuilder.Escape(null));
            Assert.Equal("plain", ContractKeyBuilder.Escape("plain"));
            Assert.Equal("a\\|b", ContractKeyBuilder.Escape("a|b"));
            Assert.Equal("a\\\\b", ContractKeyBuilder.Escape("a\\b"));
        }
    }
}
