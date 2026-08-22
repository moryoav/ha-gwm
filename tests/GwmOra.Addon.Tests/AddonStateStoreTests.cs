using GwmOra.Addon.Configuration;

namespace GwmOra.Addon.Tests;

public class AddonStateStoreTests
{
    [Fact]
    public async Task PersistsTrackedChargingPlansPerVehicle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gwm-state-{Guid.NewGuid():N}.json");
        try
        {
            var store = AddonStateStore.Load(path);
            await store.UpdateAsync(state =>
            {
                state.ChargingPlansSetByAddon["VIN-A"] = new TrackedChargingPlan
                {
                    PlanId = 10,
                    PlanType = 0,
                    StartTime = 1_000,
                    EndTime = 301_000
                };
                state.ChargingPlansSetByAddon["VIN-B"] = new TrackedChargingPlan
                {
                    PlanId = 20,
                    PlanType = 0,
                    StartTime = 2_000,
                    EndTime = 302_000
                };
            }, CancellationToken.None);

            var reloaded = AddonStateStore.Load(path);

            Assert.Equal(2, reloaded.State.ChargingPlansSetByAddon.Count);
            Assert.Equal(10, reloaded.State.ChargingPlansSetByAddon["VIN-A"].PlanId);
            Assert.Equal(20, reloaded.State.ChargingPlansSetByAddon["VIN-B"].PlanId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
