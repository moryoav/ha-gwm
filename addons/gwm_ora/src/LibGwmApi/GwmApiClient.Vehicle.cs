using libgwmapi.DTO.Vehicle;
using libgwmapi.DTO.UserAuth;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace libgwmapi;

public partial class GwmApiClient
{
    public Task<Vehicle[]> AcquireVehiclesAsync(CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            return _chinaClient.AcquireVehiclesAsync(cancellationToken);
        }

        return GetAppAsync<Vehicle[]>("globalapp/vehicle/acquireVehicles", cancellationToken);
    }

    public Task<VehicleBasicsInfo> GetVehicleBasicsInfoAsync(string vin, CancellationToken cancellationToken)
    {
        return GetAppAsync<VehicleBasicsInfo>($"vehicle/vehicleBasicsInfo?vin={vin}&flag=true", cancellationToken);
    }

    public async Task<VehicleBasicsInfo> GetVehicleBasicsInfoOrDefaultAsync(
        string vin,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            return _chinaClient.GetVehicleBasicsInfoOrDefault(vin);
        }

        try
        {
            return await GetVehicleBasicsInfoAsync(vin, cancellationToken);
        }
        catch (GwmApiException ex) when (_region == "aus" && ex.Code == "607099")
        {
            // The ANZ gateway may reject this optional endpoint. Keep status and
            // climate commands usable with their existing default temperature
            // while allowing all other failures to surface.
            _logger.LogDebug(
                "GWM (AU) vehicleBasicsInfo is unavailable: {Code} {Message}",
                ex.Code,
                ex.Message);
            return new VehicleBasicsInfo();
        }
    }

    public Task<VehicleStatus> GetLastVehicleStatusAsync(string vin, CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            return _chinaClient.GetLastVehicleStatusAsync(vin, cancellationToken);
        }

        return GetAppAsync<VehicleStatus>($"vehicle/getLastStatus?vin={vin}&seqNo=", cancellationToken);
    }

    public Task ModifyVehicleRemoteCtlInfoAsync(ModifyVecicleRemoteCtl request, CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            _chinaClient.SetClimateDefaults(request);
            return Task.CompletedTask;
        }

        if (_region == "rus")
        {
            return PostH5Async(
                "vehicle/modifyVehicleRemoteCtlInfo",
                request,
                new[] { ("vin", request.Vin) },
                cancellationToken);
        }

        return PostH5Async("vehicle/modifyVehicleRemoteCtlInfo", request, cancellationToken);
    }

    public async Task SendCmdAsync(SendCmd request, CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            await _chinaClient.SendCommandAsync(request, cancellationToken);
            return;
        }

        if (_region == "rus")
        {
            await CheckSecurityPasswordAsync(
                CheckSecurityPassword.FromHash(request.SecurityPassword, "3"),
                cancellationToken);
            await PostAppAsync(
                "vehicle/T5/sendCmd",
                request,
                new[] { ("vin", request.Vin) },
                cancellationToken);
            return;
        }

        await PostAppAsync("vehicle/T5/sendCmd", request, cancellationToken);
    }

    public Task SendRawCmdAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
    {
        return PostAppAsync("vehicle/T5/sendCmd", request, cancellationToken);
    }

    public Task<RemoteCtrlResultT5[]> GetRemoteCtrlResultAsync(string seqNo, CancellationToken cancellationToken)
    {
        return GetAppAsync<RemoteCtrlResultT5[]>(
            $"vehicle/getRemoteCtrlResultT5?seqNo={seqNo}",
            cancellationToken);
    }

    public Task<RemoteCtrlResultT5[]> GetRemoteCtrlResultAsync(
        string seqNo,
        string vin,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            return _chinaClient.GetRemoteCommandResultAsync(seqNo, vin, cancellationToken);
        }

        // AU/NZ and Russia require the VIN as a request header on this endpoint. The VIN is a
        // header rather than a signed query parameter, so it does not change the overseas
        // request signature. Keep EU on its existing header-less request path.
        if (_region is not ("aus" or "rus"))
        {
            return GetRemoteCtrlResultAsync(seqNo, cancellationToken);
        }

        return GetAppAsync<RemoteCtrlResultT5[]>(
            $"vehicle/getRemoteCtrlResultT5?seqNo={seqNo}",
            new[] { ("vin", vin) },
            cancellationToken);
    }

    // 智能预约充电（beantech 专属）。navinfo/海外用的是 charging plan 那套时间窗模型，
    // 这里是车端单一的 chargingMode 开关，两者不通用。
    public Task<(bool Enabled, string? StartTime, string? EndTime)> GetBeanTechChargeSettingAsync(
        string vin,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is null)
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_PLATFORM",
                "Smart scheduled charging is only available on the China BeanTech platform.");
        }

        return _chinaClient.GetBeanTechChargeSettingAsync(vin, cancellationToken);
    }

    public Task<string> SetBeanTechChargingModeAsync(
        string vin,
        bool enable,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is null)
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_PLATFORM",
                "Smart scheduled charging is only available on the China BeanTech platform.");
        }

        return _chinaClient.SetBeanTechChargingModeAsync(vin, enable, cancellationToken);
    }

    public Task<(string? ResultCode, string? ResultMessage)> GetBeanTechChargeResultAsync(
        string seqNo,
        string vin,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is null)
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_PLATFORM",
                "Smart scheduled charging is only available on the China BeanTech platform.");
        }

        return _chinaClient.GetBeanTechChargeResultAsync(seqNo, vin, cancellationToken);
    }

    public Task<(bool InsertGunKeepWarm, bool ActiveKeepWarm)> GetBeanTechSwitchStatusAsync(
        string vin,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is null)
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_PLATFORM",
                "Battery heating state is only available on the China BeanTech platform.");
        }

        return _chinaClient.GetBeanTechSwitchStatusAsync(vin, cancellationToken);
    }

    public Task<int?> GetBeanTechAirConditionerTemperatureAsync(
        string vin,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is null)
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_PLATFORM",
                "The A/C set temperature is only available on the China BeanTech platform.");
        }

        return _chinaClient.GetBeanTechAirConditionerTemperatureAsync(vin, cancellationToken);
    }

    public Task<JsonNode> GetBeanTechRemoteRecordsAsync(
        string vin,
        int pageNum,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (_chinaClient is null)
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_PLATFORM",
                "Remote control records are only available on the China BeanTech platform.");
        }

        return _chinaClient.GetBeanTechRemoteRecordsAsync(vin, pageNum, pageSize, cancellationToken);
    }

    public Task<ChargingInfos> GetChargingInfosAsync(string vin, CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            return _chinaClient.GetChargingInfosAsync(vin, cancellationToken);
        }

        // The charging API belongs to the h5-gateway family in the official apps. AU/NZ's
        // self-developed request variant additionally requires the VIN header.
        var url = $"vehicleCharge/getChargingInfos?vin={vin}";
        return _region == "aus"
            ? GetH5Async<ChargingInfos>(url, new[] { ("vin", vin) }, cancellationToken)
            : GetH5Async<ChargingInfos>(url, cancellationToken);
    }

    public Task SetChargingPlanAsync(SetChargingPlan request, CancellationToken cancellationToken)
    {
        if (_chinaClient is not null)
        {
            return _chinaClient.SetChargingPlanAsync(request, cancellationToken);
        }

        // Sets/clears the vehicle's charging-schedule window (startTime/endTime = epoch-ms strings,
        // planType 0 = one-off, minimum 5-minute window). A plan window gates charging (start/stop);
        // clearing it (enable=false) reverts to charge-on-plug. No security PIN is required.
        // AU/NZ's self-developed request variant additionally requires the VIN header.
        return _region == "aus"
            ? PostH5Async(
                "vehicleCharge/setChargingPlan",
                request,
                new[] { ("vin", request.Vin) },
                cancellationToken)
            : PostH5Async("vehicleCharge/setChargingPlan", request, cancellationToken);
    }
}
