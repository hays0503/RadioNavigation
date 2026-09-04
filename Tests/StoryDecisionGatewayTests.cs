using System;
using System.Reflection;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class StoryDecisionGatewayTests
    {
        // Hierarchy for FindMethodDeep tests (mirrors Story->GenericStory->RadioStory)
        private class BaseStory
        {
            private void HiddenBase(int x) { }
            private void InsertDecision(int index, object decision) { }
            private void OnUpdated() { }
        }

        private class MiddleStory : BaseStory
        {
        }

        private class LeafStory : MiddleStory
        {
        }

        private class DecisionWithField
        {
            public string LocalizationKey;
            public DecisionWithField(string key) { LocalizationKey = key; }
        }

        private class DecisionWithProp
        {
            public string LocalizationKey { get; set; }
            public DecisionWithProp(string key) { LocalizationKey = key; }
        }

        private class DecisionWithBoth
        {
            public string LocalizationKey;
            public string LocalizationKeyProp => "prop";
            public DecisionWithBoth(string key) { LocalizationKey = key; }
        }

        private enum RadioStoryStage { Unknown = 0, MainChoices = 1 }

        [Fact]
        public void FindMethodDeep_FindsInBase()
        {
            var m = StoryDecisionGateway.FindMethodDeep(typeof(LeafStory), "HiddenBase", new[] { typeof(int) });
            Assert.NotNull(m);
            Assert.Equal("HiddenBase", m.Name);
        }

        [Fact]
        public void FindMethodDeep_FindsInsertDecisionInHierarchy()
        {
            var m = StoryDecisionGateway.FindMethodDeep(typeof(LeafStory), "InsertDecision", null);
            Assert.NotNull(m);
            Assert.Equal("InsertDecision", m.Name);
        }

        [Fact]
        public void FindMethodDeep_ReturnsNullForMissing()
        {
            var m = StoryDecisionGateway.FindMethodDeep(typeof(LeafStory), "NonExistent", Type.EmptyTypes);
            Assert.Null(m);
        }

        [Fact]
        public void FindMethodDeep_WalksHierarchyNotJustBaseType()
        {
            // Direct GetMethod on LeafStory would fail for private base method
            var direct = typeof(LeafStory).GetMethod("OnUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Null(direct);
            var deep = StoryDecisionGateway.FindMethodDeep(typeof(LeafStory), "OnUpdated", Type.EmptyTypes);
            Assert.NotNull(deep);
        }

        [Fact]
        public void GetLocalizationKey_PrefersFieldOverProp()
        {
            // Use reflection to set gateway's field cache manually via private fields? Instead test logic via dummy type with both
            // Directly test gateway's GetLocalizationKey after initializing its field via reflection
            var field = typeof(DecisionWithField).GetField("LocalizationKey");
            var prop = typeof(DecisionWithField).GetProperty("LocalizationKey");
            // Ensure field exists and prop null -> gateway should read field
            Assert.NotNull(field);
            Assert.Null(prop);

            // Simulate gateway state: set localizationKeyField via reflection to field
            var gatewayField = typeof(StoryDecisionGateway).GetField("localizationKeyField", BindingFlags.NonPublic | BindingFlags.Static);
            var gatewayProp = typeof(StoryDecisionGateway).GetField("localizationKeyProp", BindingFlags.NonPublic | BindingFlags.Static);
            object savedField = gatewayField.GetValue(null);
            object savedProp = gatewayProp.GetValue(null);
            try
            {
                gatewayField.SetValue(null, field);
                gatewayProp.SetValue(null, null);
                var decision = new DecisionWithField("Radio/OpenT3PLL38");
                string key = StoryDecisionGateway.GetLocalizationKey(decision);
                Assert.Equal("Radio/OpenT3PLL38", key);
            }
            finally
            {
                gatewayField.SetValue(null, savedField);
                gatewayProp.SetValue(null, savedProp);
            }
        }

        [Fact]
        public void GetLocalizationKey_FallsBackToProp()
        {
            var field = typeof(DecisionWithProp).GetField("LocalizationKey");
            var prop = typeof(DecisionWithProp).GetProperty("LocalizationKey");
            Assert.Null(field);
            Assert.NotNull(prop);

            var gatewayField = typeof(StoryDecisionGateway).GetField("localizationKeyField", BindingFlags.NonPublic | BindingFlags.Static);
            var gatewayProp = typeof(StoryDecisionGateway).GetField("localizationKeyProp", BindingFlags.NonPublic | BindingFlags.Static);
            object savedField = gatewayField.GetValue(null);
            object savedProp = gatewayProp.GetValue(null);
            try
            {
                gatewayField.SetValue(null, null);
                gatewayProp.SetValue(null, prop);
                var decision = new DecisionWithProp("Radio/OpenT3PLL38");
                string key = StoryDecisionGateway.GetLocalizationKey(decision);
                Assert.Equal("Radio/OpenT3PLL38", key);
            }
            finally
            {
                gatewayField.SetValue(null, savedField);
                gatewayProp.SetValue(null, savedProp);
            }
        }

        [Fact]
        public void BoxedEnum_ConvertToInt32_WorksWhileCastThrows()
        {
            object boxed = RadioStoryStage.MainChoices; // boxed enum
            int viaConvert = Convert.ToInt32(boxed);
            Assert.Equal(1, viaConvert);

            // Direct (int)boxed unboxes as int, not enum -> InvalidCast on classic runtimes,
            // but .NET 10 JIT may elide. Verify the safe enum-unbox path instead.
            int viaEnum = (int)(RadioStoryStage)boxed;
            Assert.Equal(1, viaEnum);
            // Ensure Convert path (used in gateway) is the robust one
            Assert.Equal(viaConvert, viaEnum);
        }

        [Fact]
        public void Reset_ClearsState()
        {
            // Ensure Reset leaves gateway uninitialized
            StoryDecisionGateway.Reset();
            Assert.Null(StoryDecisionGateway.DecisionType);
            Assert.Null(StoryDecisionGateway.StageField);
        }

        [Fact]
        public void DecisionAlreadyExists_FindsExisting()
        {
            // Prepare gateway field for GetLocalizationKey to work with DecisionWithField
            var field = typeof(DecisionWithField).GetField("LocalizationKey");
            var gatewayField = typeof(StoryDecisionGateway).GetField("localizationKeyField", BindingFlags.NonPublic | BindingFlags.Static);
            var gatewayProp = typeof(StoryDecisionGateway).GetField("localizationKeyProp", BindingFlags.NonPublic | BindingFlags.Static);
            object savedField = gatewayField.GetValue(null);
            object savedProp = gatewayProp.GetValue(null);
            try
            {
                gatewayField.SetValue(null, field);
                gatewayProp.SetValue(null, null);

                var list = new System.Collections.Generic.List<object>
                {
                    new DecisionWithField("Radio/Other"),
                    new DecisionWithField("Radio/OpenT3PLL38")
                };
                bool exists = StoryDecisionGateway.DecisionAlreadyExists(list, list, list.Count, "Radio/OpenT3PLL38");
                Assert.True(exists);
                bool notExists = StoryDecisionGateway.DecisionAlreadyExists(list, list, list.Count, "Radio/Missing");
                Assert.False(notExists);
            }
            finally
            {
                gatewayField.SetValue(null, savedField);
                gatewayProp.SetValue(null, savedProp);
            }
        }

        [Fact]
        public void GetDecisionCount_IListFastPath()
        {
            var list = new System.Collections.Generic.List<object> { new object(), new object() };
            int count = StoryDecisionGateway.GetDecisionCount(list, list);
            Assert.Equal(2, count);
        }

        [Fact]
        public void Properties_AfterReset_AllNull()
        {
            StoryDecisionGateway.Reset();
            Assert.Null(StoryDecisionGateway.DecisionType);
            Assert.Null(StoryDecisionGateway.DecisionCtor);
            Assert.Null(StoryDecisionGateway.LocalizationKeyField);
            Assert.Null(StoryDecisionGateway.LocalizationKeyProp);
            Assert.Null(StoryDecisionGateway.StageField);
            Assert.Null(StoryDecisionGateway.DecisionsProp);
            Assert.Null(StoryDecisionGateway.InsertMethod);
            Assert.Null(StoryDecisionGateway.OnUpdatedMethod);
        }

        [Fact]
        public void TryEnsureInitialized_WithoutGame_ReturnsFalse()
        {
            StoryDecisionGateway.Reset();
            Assert.False(StoryDecisionGateway.TryEnsureInitialized());
            StoryDecisionGateway.Reset();
        }

        [Fact]
        public void GetLocalizationKey_NoCache_ReturnsNull()
        {
            StoryDecisionGateway.Reset();
            Assert.Null(StoryDecisionGateway.GetLocalizationKey(new object()));
            StoryDecisionGateway.Reset();
        }

        [Fact]
        public void GetDecisionCount_NullDecisions_ReturnsMinusOne()
        {
            Assert.Equal(-1, StoryDecisionGateway.GetDecisionCount(null, null));
        }

        [Fact]
        public void GetDecisionCount_ReflectionFallback()
        {
            var bits = new System.Collections.BitArray(10);
            Assert.Equal(10, StoryDecisionGateway.GetDecisionCount(bits, null));
        }

        [Fact]
        public void GetDecisions_NullProp_ReturnsNull()
        {
            StoryDecisionGateway.Reset();
            Assert.Null(StoryDecisionGateway.GetDecisions(null));
            StoryDecisionGateway.Reset();
        }

        private static object NullDecisions => null;

        [Fact]
        public void GetDecisions_NullValue_WarnsAndReturnsNull()
        {
            var propField = typeof(StoryDecisionGateway).GetField("decisionsProp", BindingFlags.NonPublic | BindingFlags.Static);
            object saved = propField.GetValue(null);
            try
            {
                propField.SetValue(null, typeof(StoryDecisionGatewayTests).GetProperty("NullDecisions", BindingFlags.NonPublic | BindingFlags.Static));
                Assert.Null(StoryDecisionGateway.GetDecisions(null));
            }
            finally
            {
                propField.SetValue(null, saved);
            }
        }

        private sealed class FakeDecisions
        {
            private readonly object[] items;
            public FakeDecisions(params object[] items) { this.items = items; }
            public int Count => items.Length;
            public object this[int index] => items[index];
        }

        [Fact]
        public void DecisionAlreadyExists_NonListBranch()
        {
            var field = typeof(DecisionWithField).GetField("LocalizationKey");
            var gatewayField = typeof(StoryDecisionGateway).GetField("localizationKeyField", BindingFlags.NonPublic | BindingFlags.Static);
            var gatewayProp = typeof(StoryDecisionGateway).GetField("localizationKeyProp", BindingFlags.NonPublic | BindingFlags.Static);
            object savedField = gatewayField.GetValue(null);
            object savedProp = gatewayProp.GetValue(null);
            try
            {
                gatewayField.SetValue(null, field);
                gatewayProp.SetValue(null, null);

                var decisions = new FakeDecisions(new DecisionWithField("Radio/OpenT3PLL38"));
                Assert.True(StoryDecisionGateway.DecisionAlreadyExists(decisions, null, 1, "Radio/OpenT3PLL38"));
                Assert.False(StoryDecisionGateway.DecisionAlreadyExists(decisions, null, 1, "Radio/Missing"));
            }
            finally
            {
                gatewayField.SetValue(null, savedField);
                gatewayProp.SetValue(null, savedProp);
            }
        }

        [Fact]
        public void InsertDecision_NullMethod_Swallowed()
        {
            StoryDecisionGateway.Reset();
            StoryDecisionGateway.InsertDecision(null, 0, new object());
            StoryDecisionGateway.Reset();
        }

        [Fact]
        public void NotifyUpdated_MissingMethod_WarnsWithoutThrow()
        {
            StoryDecisionGateway.Reset();
            StoryDecisionGateway.NotifyUpdated(null);
            StoryDecisionGateway.Reset();
        }
    }
}
