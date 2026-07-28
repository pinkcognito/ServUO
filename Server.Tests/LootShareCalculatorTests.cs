using Server.Custom.AIAgents;

namespace Server.Tests
{
    public class LootShareCalculatorTests
    {
        [Fact]
        public void ComputeFairShare_TwentyPercentOfCorpseGold()
        {
            Assert.Equal(20, LootShareCalculator.ComputeFairShare(100));
        }

        [Fact]
        public void ComputeFairShare_ZeroWhenNoGold()
        {
            Assert.Equal(0, LootShareCalculator.ComputeFairShare(0));
        }

        [Fact]
        public void ComputeFairShare_ZeroWhenNegativeGold()
        {
            Assert.Equal(0, LootShareCalculator.ComputeFairShare(-50));
        }

        [Fact]
        public void ComputeFairShare_RoundsAwayFromZero()
        {
            // 7 * 0.20 = 1.4 -> rounds to 1.
            Assert.Equal(1, LootShareCalculator.ComputeFairShare(7));
            // 13 * 0.20 = 2.6 -> rounds to 3.
            Assert.Equal(3, LootShareCalculator.ComputeFairShare(13));
        }

        [Fact]
        public void AccumulateOwed_AddsFairShare()
        {
            Assert.Equal(50, LootShareCalculator.AccumulateOwed(30, 20));
        }

        [Fact]
        public void AccumulateOwed_IgnoresNegativeFairShare()
        {
            Assert.Equal(30, LootShareCalculator.AccumulateOwed(30, -20));
        }

        [Fact]
        public void AccumulateOwed_CapsAtMaxOwed()
        {
            Assert.Equal(LootShareCalculator.MaxOwedCap, LootShareCalculator.AccumulateOwed(LootShareCalculator.MaxOwedCap - 5, 1000));
        }

        [Fact]
        public void ApplyPayment_SettlesUpToOwedAmount()
        {
            var newOwed = LootShareCalculator.ApplyPayment(currentOwed: 100, paymentAmount: 40, out var settled);

            Assert.Equal(60, newOwed);
            Assert.Equal(40, settled);
        }

        [Fact]
        public void ApplyPayment_OverpaymentSettlesOnlyWhatWasOwed()
        {
            var newOwed = LootShareCalculator.ApplyPayment(currentOwed: 30, paymentAmount: 500, out var settled);

            Assert.Equal(0, newOwed);
            Assert.Equal(30, settled);
        }

        [Fact]
        public void ApplyPayment_NoOwedBalanceSettlesNothing()
        {
            var newOwed = LootShareCalculator.ApplyPayment(currentOwed: 0, paymentAmount: 100, out var settled);

            Assert.Equal(0, newOwed);
            Assert.Equal(0, settled);
        }

        [Fact]
        public void ApplyPayment_NegativePaymentSettlesNothing()
        {
            var newOwed = LootShareCalculator.ApplyPayment(currentOwed: 50, paymentAmount: -20, out var settled);

            Assert.Equal(50, newOwed);
            Assert.Equal(0, settled);
        }

        [Fact]
        public void IsShorted_FalseBelowThreshold()
        {
            Assert.False(LootShareCalculator.IsShorted(LootShareCalculator.ShortedOwedThreshold - 1));
        }

        [Fact]
        public void IsShorted_TrueAtThreshold()
        {
            Assert.True(LootShareCalculator.IsShorted(LootShareCalculator.ShortedOwedThreshold));
        }

        [Fact]
        public void RepeatedShortfalls_EventuallyCrossShortedThreshold()
        {
            // Acceptance: "repeatedly shorting the companion lowers it" -
            // a run of small assisted kills with no settlement should
            // eventually accumulate past the shorted threshold.
            var owed = 0;

            for (var i = 0; i < 10; i++)
            {
                owed = LootShareCalculator.AccumulateOwed(owed, LootShareCalculator.ComputeFairShare(300));
            }

            Assert.True(LootShareCalculator.IsShorted(owed));
        }

        [Fact]
        public void SettlingInFull_ClearsShortedStatus()
        {
            var owed = LootShareCalculator.AccumulateOwed(0, LootShareCalculator.ShortedOwedThreshold);
            Assert.True(LootShareCalculator.IsShorted(owed));

            owed = LootShareCalculator.ApplyPayment(owed, owed, out _);

            Assert.False(LootShareCalculator.IsShorted(owed));
        }
    }
}
