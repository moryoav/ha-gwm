using System.Text.Json.Serialization;

namespace libgwmapi.DTO.Vehicle;

/// <summary>
/// Response of AU/NZ <c>vehicleCharge/getChargingInfos</c> — the vehicle's charging schedule.
/// An empty/inactive plan reports <c>planType "-1"</c> with no window.
/// </summary>
public class ChargingInfos
{
    [JsonPropertyName("chargePlanList")]
    public ChargePlanItem[] ChargePlanList { get; set; }
}

public class ChargePlanItem
{
    [JsonPropertyName("planId")]
    public long PlanId { get; set; }

    [JsonPropertyName("planType")]
    public string PlanType { get; set; }

    // Epoch milliseconds; 0 when unset.
    [JsonPropertyName("startTime")]
    public long StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public long? EndTime { get; set; }

    [JsonPropertyName("weeks")]
    public string Weeks { get; set; }
}
