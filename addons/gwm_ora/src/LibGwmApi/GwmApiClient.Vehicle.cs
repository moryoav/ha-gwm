using libgwmapi.DTO.Vehicle;
using libgwmapi.DTO.UserAuth;
using Microsoft.Extensions.Logging;

namespace libgwmapi;

public partial class GwmApiClient
{
    public Task<Vehicle[]> AcquireVehiclesAsync(CancellationToken cancellationToken)
    {
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
        return GetAppAsync<VehicleStatus>($"vehicle/getLastStatus?vin={vin}&seqNo=", cancellationToken);
    }

    public Task ModifyVehicleRemoteCtlInfoAsync(ModifyVecicleRemoteCtl request, CancellationToken cancellationToken)
    {
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
}
