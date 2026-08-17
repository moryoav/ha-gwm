using System.Text.Json.Serialization;

namespace libgwmapi.DTO.Vehicle;

/// <summary>
/// Body of AU/NZ <c>vehicleCharge/setChargingPlan</c>. A plan window makes the car charge only
/// within it (start at <see cref="StartTime"/>, stop at <see cref="EndTime"/>); with no plan the
/// car charges on plug-in. No security PIN is required. Times are epoch-milliseconds strings and
/// the window must be at least 5 minutes.
/// </summary>
public class SetChargingPlan
{
    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("seqNo")]
    public string SeqNo { get; } = Guid.NewGuid().ToString("N") + "1234";

    [JsonPropertyName("vin")]
    public string Vin { get; set; }

    // 0 = one-off. Omitted when disabling/clearing the plan.
    [JsonPropertyName("planType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PlanType { get; set; }

    [JsonPropertyName("startTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string StartTime { get; set; }

    [JsonPropertyName("endTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string EndTime { get; set; }

    [JsonPropertyName("weeks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Weeks { get; set; }
}
