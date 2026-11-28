using System;
using System.Linq;
using OpenCMTools.Core.Models;
using Xunit;

namespace OpenCMTools.Tests
{
    public sealed class ClientActionRegistryTests
    {
        [Fact]
        public void Registry_ContainsAll12ExpectedClientActions()
        {
            Assert.Equal(12, ClientActionRegistry.All.Count);
            Assert.Equal(12, ClientActionRegistry.All.Select(a => a.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void AllScheduleIds_AreValidBracedGuids()
        {
            foreach (var action in ClientActionRegistry.All)
            {
                Assert.NotEmpty(action.ScheduleIds);
                foreach (var sid in action.ScheduleIds)
                {
                    Assert.StartsWith("{", sid);
                    Assert.EndsWith("}", sid);
                    Assert.True(Guid.TryParse(sid, out _), $"Invalid schedule GUID '{sid}' for {action.DisplayName}");
                }
            }
        }

        [Fact]
        public void MachinePolicyAction_TriggersBothRetrievalAndEvaluation()
        {
            var action = ClientActionRegistry.GetRequired(ClientActionRegistry.MachinePolicyRetrievalAndEval);
            Assert.Equal(2, action.ScheduleIds.Length);
            Assert.Contains("{00000000-0000-0000-0000-000000000021}", action.ScheduleIds);
            Assert.Contains("{00000000-0000-0000-0000-000000000022}", action.ScheduleIds);
        }

        [Fact]
        public void StateMessageCacheCleanup_SetsCleanupFlag()
        {
            var action = ClientActionRegistry.GetRequired(ClientActionRegistry.StateMessageCacheCleanup);
            Assert.True(action.IsStateMessageCacheCleanup);
        }
    }
}
