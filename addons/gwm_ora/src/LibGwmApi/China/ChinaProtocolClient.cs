#nullable enable

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using libgwmapi.DTO.China;
using libgwmapi.DTO.Vehicle;
using Microsoft.Extensions.Logging;

namespace libgwmapi.China;

/// <summary>
/// Experimental client for the protocol used by the mainland-China GWM Android app.
/// It deliberately lives beside, rather than inside, the overseas gateway client so
/// its three token/signing systems cannot alter established EU, AU/NZ, or RU behavior.
/// </summary>
public sealed class ChinaProtocolClient
{
    internal const string GAppBaseUrl = "https://gapp-api.gwmapp-h.com/";
    internal const string CarBaseUrl = "https://car-api.gwmapp-h.com/";
    internal const string BeanTechBaseUrl = "https://gw-app-gateway.gwmapp-h.com/";
    internal const string AutoAiDirectUrl = "https://ti.gwm.com.cn:8443/tsp/ead";
    internal const string SourceAppVersion = "2.1.5";
    internal const string SourceAppCode = "2150";
    internal const string OfficialUserAgent = "okhttp/4.2.2";

    private static readonly HashSet<string> SafeDiagnosticFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",
        "description",
        "error",
        "message",
        "msg",
        "path",
        "requestId",
        "status",
        "timestamp",
        "traceId"
    };

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CredentialAssignmentPattern = new(
        @"\b(access[_-]?token|authorization|g-token|password|refresh[_-]?token|secret|token)\s*[=:]\s*[^\s,;&]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PhonePattern = new(@"\b1[3-9]\d{9}\b", RegexOptions.Compiled);
    private static readonly Regex VinPattern = new(
        @"\b[A-HJ-NPR-Z0-9]{17}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongSecretPattern = new(
        @"\b[A-Z0-9+/_=-]{48,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringOrNumberConverter() }
    };

    private readonly HttpClient _gAppClient;
    private readonly HttpClient _carClient;
    private readonly HttpClient _beanTechClient;
    private readonly HttpClient _autoAiClient;
    private readonly ILogger<ChinaProtocolClient> _logger;
    private readonly Dictionary<string, Vehicle> _vehicles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonNode> _lastStatusBodies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VehicleBasicsInfo> _climateDefaults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _commandTransactions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChargingInfos> _writtenChargingPlans = new(StringComparer.OrdinalIgnoreCase);
    private Vehicle[]? _cachedVehicles;

    public ChinaProtocolClient(
        HttpClient gAppClient,
        HttpClient carClient,
        HttpClient beanTechClient,
        HttpClient autoAiClient,
        ILoggerFactory loggerFactory)
    {
        _gAppClient = gAppClient;
        _carClient = carClient;
        _beanTechClient = beanTechClient;
        _autoAiClient = autoAiClient;
        _logger = loggerFactory.CreateLogger<ChinaProtocolClient>();
    }

    public string DeviceId { get; set; } = String.Empty;

    public ChinaSession Session { get; private set; } = new();

    public void SetSession(ChinaSession? session)
    {
        Session = session?.Clone() ?? new ChinaSession();
        _cachedVehicles = null;
        _vehicles.Clear();
    }

    public async Task RequestSmsCodeAsync(string phone, CancellationToken cancellationToken)
    {
        var logicalBody = new JsonObject
        {
            ["phone"] = phone,
            ["flag"] = "LOGIN"
        };
        await SendDefaultPostAsync(
            _gAppClient,
            GAppBaseUrl + "api-guser/v5/user/login-sms/send",
            null,
            logicalBody,
            encryptBody: true,
            cancellationToken);
    }

    public async Task<ChinaSession> LoginWithSmsAsync(
        string phone,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        var logicalBody = new JsonObject
        {
            ["code"] = verificationCode,
            ["phone"] = phone,
            // The official app permits this value to be empty. Some accounts may
            // nevertheless be challenged by Alibaba risk control; surface that as
            // an actionable experimental-region error rather than inventing a token.
            ["deviceToken"] = String.Empty
        };
        var data = await SendDefaultPostAsync(
            _gAppClient,
            GAppBaseUrl + "api-guser/v5/user/sms-login",
            null,
            logicalBody,
            encryptBody: true,
            cancellationToken);

        Session = new ChinaSession
        {
            GToken = Value(data, "gToken"),
            GRefreshToken = Value(data, "gRefreshToken"),
            SsoToken = Value(data, "ssoToken"),
            PtToken = Value(data, "ptToken"),
            UserId = Value(data, "userId"),
            BeanId = Value(data, "beanId"),
            Phone = FirstNonEmpty(Value(data, "phone"), phone)
        };
        EnsureGAppSession();
        await LoginVehiclePlatformsAsync(cancellationToken);
        return Session.Clone();
    }

    public async Task<ChinaSession> RefreshSessionAsync(CancellationToken cancellationToken)
    {
        EnsureGAppSession();
        var logicalBody = new JsonObject
        {
            ["token"] = Session.GToken,
            ["refreshToken"] = Session.GRefreshToken
        };
        var data = await SendDefaultPostAsync(
            _gAppClient,
            GAppBaseUrl + "api-guser/v5/token/refresh",
            null,
            logicalBody,
            encryptBody: true,
            cancellationToken);
        Session.GToken = FirstNonEmpty(Value(data, "gToken"), Value(data, "token"), Session.GToken);
        Session.GRefreshToken = FirstNonEmpty(
            Value(data, "gRefreshToken"),
            Value(data, "refreshToken"),
            Session.GRefreshToken);
        Session.SsoToken = FirstNonEmpty(Value(data, "ssoToken"), Session.SsoToken);
        Session.PtToken = FirstNonEmpty(Value(data, "ptToken"), Session.PtToken);
        await LoginVehiclePlatformsAsync(cancellationToken);
        return Session.Clone();
    }

    public async Task ValidateSessionAsync(CancellationToken cancellationToken)
    {
        EnsureCompleteSession();
        await AcquireVehiclesAsync(cancellationToken);
    }

    public async Task<Vehicle[]> AcquireVehiclesAsync(CancellationToken cancellationToken)
    {
        EnsureCompleteSession();
        if (_cachedVehicles is not null)
        {
            return _cachedVehicles;
        }

        var physicalUrl = CarBaseUrl + "gcar/v1/app/android/vehicle/query-vehicle-list";
        var signingUrl = physicalUrl.Replace(CarBaseUrl, GAppBaseUrl, StringComparison.Ordinal);
        var data = await SendDefaultPostAsync(
            _carClient,
            physicalUrl,
            signingUrl,
            new JsonObject { ["vehicleVersion"] = 13 },
            encryptBody: false,
            cancellationToken);
        var list = Property(data, "acquireVehiclesList") ?? data;
        var vehicles = list.Deserialize<Vehicle[]>(SerializerOptions) ?? Array.Empty<Vehicle>();
        _vehicles.Clear();
        foreach (var vehicle in vehicles.Where(vehicle => !String.IsNullOrWhiteSpace(vehicle?.Vin)))
        {
            _vehicles[vehicle.Vin] = vehicle;
        }

        _cachedVehicles = vehicles;
        return vehicles;
    }

    public async Task<VehicleStatus> GetLastVehicleStatusAsync(
        string vin,
        CancellationToken cancellationToken)
    {
        var vehicle = await RequireNavInfoVehicleAsync(vin, cancellationToken);
        var body = await SendAutoAiAsync(
            _autoAiClient,
            AutoAiDirectUrl,
            "GW.M.GET_VEHICLE_STATE",
            new JsonObject { ["vin"] = vin },
            mobileId: DeviceId,
            cancellationToken);
        _lastStatusBodies[vin] = body.DeepClone();
        return ChinaStatusMapper.Map(body, vehicle);
    }

    public VehicleBasicsInfo GetVehicleBasicsInfoOrDefault(string vin)
    {
        if (_climateDefaults.TryGetValue(vin, out var existing))
        {
            return existing;
        }

        return new VehicleBasicsInfo
        {
            Config = new VehicleConfig
            {
                AirConditionerTemperature = "22",
                AirConditionerStatusTime = "900",
                Vin = vin
            }
        };
    }

    public void SetClimateDefaults(ModifyVecicleRemoteCtl request)
    {
        _climateDefaults[request.Vin] = new VehicleBasicsInfo
        {
            Config = new VehicleConfig
            {
                AirConditionerTemperature = request.AirConditionerTemperature,
                AirConditionerStatusTime = request.AirConditionerTime,
                Vin = request.Vin
            }
        };
    }

    public async Task SendCommandAsync(SendCmd request, CancellationToken cancellationToken)
    {
        await RequireNavInfoVehicleAsync(request.Vin, cancellationToken);

        string function;
        JsonObject command;
        if (request.Instructions?.X05 is not null)
        {
            function = "GW.M.SEND_COMMON_COMMAND";
            command = BaseControlRequest(request.Vin);
            command["cmdCode"] = request.Instructions.X05.SwitchOrder == "2" ? 2 : 1;
        }
        else if (request.Instructions?.X08 is not null)
        {
            function = "GW.M.SEND_COMMON_COMMAND";
            command = BaseControlRequest(request.Vin);
            command["cmdCode"] = 3;
        }
        else if (request.Instructions?.X04?.AirConditioner is not null)
        {
            var air = request.Instructions.X04.AirConditioner;
            var start = air.SwitchOrder != "0";
            function = start ? "GW.M.SET_AND_OPEN_COMMAND" : "GW.M.SEND_COMMON_COMMAND";
            command = BaseControlRequest(request.Vin);
            command["cmdCode"] = start ? 6 : 7;
            if (start)
            {
                command["airParams"] = new JsonObject
                {
                    ["runTime"] = ParseInt(air.OperationTime, 15),
                    ["temperature"] = ParseInt(air.Temperature, 22)
                };
            }
        }
        else
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_COMMAND",
                "The experimental China region does not support this remote-command payload.");
        }

        var result = await SendAutoAiAsync(
            _autoAiClient,
            AutoAiDirectUrl,
            function,
            command,
            mobileId: DeviceId,
            cancellationToken);
        var transactionId = Value(result, "transactionId");
        if (String.IsNullOrWhiteSpace(transactionId))
        {
            throw new GwmApiException(
                "CN_COMMAND_RESPONSE",
                "The China vehicle service accepted no command transaction id.");
        }

        _commandTransactions[request.SeqNo] = transactionId;
    }

    public async Task<RemoteCtrlResultT5[]> GetRemoteCommandResultAsync(
        string sequenceNumber,
        string vin,
        CancellationToken cancellationToken)
    {
        var transactionId = _commandTransactions.TryGetValue(sequenceNumber, out var mapped)
            ? mapped
            : sequenceNumber;
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seqNo"] = transactionId,
            ["vin"] = vin,
            ["msgType"] = "remote"
        };
        var data = await SendBeanTechGetAsync(
            BeanTechBaseUrl + "app-api/api/v3.0/vehicle/remote-ctrl/result",
            query,
            vin,
            cancellationToken);
        var list = Property(data, "messageList") as JsonArray;
        if (list is null)
        {
            return Array.Empty<RemoteCtrlResultT5>();
        }

        var results = new List<RemoteCtrlResultT5>();
        foreach (var message in list)
        {
            var messageData = Property(message, "messageData") ?? message;
            if (messageData is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var messageJson)
                && !String.IsNullOrWhiteSpace(messageJson))
            {
                try
                {
                    messageData = JsonNode.Parse(messageJson);
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            var resultTransaction = Value(messageData, "transactionId");
            if (!String.IsNullOrWhiteSpace(resultTransaction)
                && !String.Equals(resultTransaction, transactionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resultCode = Value(messageData, "resultCode");
            if (String.IsNullOrWhiteSpace(resultCode))
            {
                continue;
            }

            // AutoAI reuses BeanTech's result stream: 2=waiting and 3=ongoing.
            // The existing add-on poller represents all non-terminal states as 2000.
            var normalizedCode = resultCode is "2" or "3" ? "2000" : resultCode;
            results.Add(new RemoteCtrlResultT5
            {
                HwCommandId = sequenceNumber,
                RemoteType = Value(message, "messageType"),
                ResultCode = normalizedCode,
                ResultMsg = FirstNonEmpty(
                    Value(messageData, "resultMessage"),
                    normalizedCode == "2000" ? "Command is still running" : String.Empty)
            });
        }

        return results.ToArray();
    }

    public async Task<ChargingInfos> GetChargingInfosAsync(
        string vin,
        CancellationToken cancellationToken)
    {
        if (_writtenChargingPlans.TryGetValue(vin, out var written))
        {
            return written;
        }

        if (!_lastStatusBodies.TryGetValue(vin, out var statusBody))
        {
            await GetLastVehicleStatusAsync(vin, cancellationToken);
            statusBody = _lastStatusBodies[vin];
        }

        return ChinaStatusMapper.ChargingInfo(statusBody, vin);
    }

    public async Task SetChargingPlanAsync(
        SetChargingPlan request,
        CancellationToken cancellationToken)
    {
        await RequireNavInfoVehicleAsync(request.Vin, cancellationToken);
        var command = BaseControlRequest(request.Vin);
        var repeatTimes = request.Enable ? ChinaRepeatTimes(request) : "0000000";
        command["chargeingMode"] = request.Enable ? "0" : "1";
        command["chargingStartTime"] = request.Enable ? ChinaClockTime(request.StartTime) : "00:00";
        command["chargingEndTime"] = request.Enable ? ChinaClockTime(request.EndTime) : "00:00";
        command["repeatTimes"] = repeatTimes;

        await SendAutoAiAsync(
            _autoAiClient,
            AutoAiDirectUrl,
            "GW.M.SEND_CHARGE_SETTINGS_WEEKLY",
            command,
            mobileId: DeviceId,
            cancellationToken);

        if (request.Enable)
        {
            // Keep the caller's persisted ownership record identical to the actual
            // China command, including the derived single day for a one-off plan.
            request.Weeks = repeatTimes;
        }

        _writtenChargingPlans[request.Vin] = request.Enable
            ? new ChargingInfos
            {
                ChargePlanList = new[]
                {
                    new ChargePlanItem
                    {
                        PlanId = StablePlanId(request.Vin),
                        PlanType = (request.PlanType ?? 0).ToString(CultureInfo.InvariantCulture),
                        StartTime = ParseLong(request.StartTime),
                        EndTime = ParseLong(request.EndTime),
                        Weeks = repeatTimes
                    }
                }
            }
            : new ChargingInfos();
    }

    private async Task LoginVehiclePlatformsAsync(CancellationToken cancellationToken)
    {
        var beanTask = RetryAsync(LoginBeanTechAsync, "BeanTech", cancellationToken);
        var autoTask = RetryAsync(LoginAutoAiAsync, "AutoAI", cancellationToken);
        await Task.WhenAll(beanTask, autoTask);
        var bean = await beanTask;
        var auto = await autoTask;

        Session.BeanTechAccessToken = Value(bean, "accessToken");
        Session.BeanTechRefreshToken = Value(bean, "refreshToken");
        Session.BeanTechSsoToken = Value(bean, "ssoToken");
        Session.BeanTechBeanId = FirstNonEmpty(Value(bean, "beanId"), Session.BeanId);
        Session.AutoAiTokenId = Value(auto, "tokenId");
        Session.AutoAiUserId = Value(auto, "userId");
        Session.AutoAiGwId = FirstNonEmpty(Value(auto, "gwid"), Value(auto, "gwId"));
        EnsureCompleteSession();
    }

    private Task<JsonNode> LoginBeanTechAsync(CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["appType"] = 0,
            ["deviceId"] = DeviceId,
            ["phone"] = Session.Phone,
            ["ssoId"] = Session.UserId,
            ["ssoToken"] = FirstNonEmpty(Session.SsoToken, Session.PtToken)
        };
        return SendBeanTechPostAsync(
            BeanTechBaseUrl + "app-api/api/v1.0/userAuth/loginSSOAccount",
            body,
            vin: null,
            cancellationToken);
    }

    private Task<JsonNode> LoginAutoAiAsync(CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["appType"] = 0,
            ["phone"] = Session.Phone,
            ["pushId"] = "0",
            ["pushKey"] = "0",
            ["ssoid"] = Session.UserId,
            ["ssoTk"] = Session.SsoToken
        };
        return SendAutoAiAsync(
            _gAppClient,
            GAppBaseUrl + "tsp/v1/proxy/navinfo/GW.M.APP_LOGIN",
            "GW.M.APP_LOGIN",
            body,
            DeviceId,
            cancellationToken);
    }

    private async Task<JsonNode> RetryAsync(
        Func<CancellationToken, Task<JsonNode>> operation,
        string service,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < 3)
                {
                    _logger.LogDebug(
                        "Experimental China {Service} login attempt {Attempt} failed; retrying",
                        service,
                        attempt);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }

        throw new GwmApiException(
            "CN_TSP_LOGIN",
            $"Experimental China {service} vehicle-service login failed: {lastException?.Message}");
    }

    private async Task<Vehicle> RequireNavInfoVehicleAsync(
        string vin,
        CancellationToken cancellationToken)
    {
        if (!_vehicles.TryGetValue(vin, out var vehicle))
        {
            await AcquireVehiclesAsync(cancellationToken);
            _vehicles.TryGetValue(vin, out vehicle);
        }

        if (vehicle is null)
        {
            throw new GwmApiException("CN_VEHICLE_NOT_FOUND", "The vehicle was not returned by the China account.");
        }

        if (!String.Equals(vehicle.BelongPlatform, "navinfo", StringComparison.OrdinalIgnoreCase))
        {
            throw new GwmApiException(
                "CN_UNSUPPORTED_PLATFORM",
                $"Experimental China support currently implements NavInfo/AutoAI vehicles; " +
                $"this vehicle reports '{vehicle.BelongPlatform ?? "unknown"}'.");
        }

        return vehicle;
    }

    private JsonObject BaseControlRequest(string vin) => new()
    {
        ["flag"] = 1,
        ["signStr"] = ChinaCrypto.Md5Hex(vin + Session.AutoAiTokenId),
        ["userId"] = Session.AutoAiUserId,
        ["userType"] = "0",
        ["vin"] = vin
    };

    private async Task<JsonNode> SendDefaultPostAsync(
        HttpClient client,
        string physicalUrl,
        string? signingUrl,
        JsonObject logicalBody,
        bool encryptBody,
        CancellationToken cancellationToken)
    {
        var json = logicalBody.ToJsonString();
        var rawBody = encryptBody ? ChinaCrypto.EncryptGApp(json) : json;
        var timestamp = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000 * 1000)
            .ToString(CultureInfo.InvariantCulture);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The official app keeps the account G-Token and the vehicle-platform
            // access token separate. Authorization is the BeanTech access token;
            // G-TOKEN below is the G-App account token.
            ["Authorization"] = Session.BeanTechAccessToken ?? String.Empty,
            ["SourceApp"] = "GWM",
            ["SourceType"] = "ANDROID",
            ["SourceAppVer"] = SourceAppVersion,
            ["Timestamp"] = timestamp,
            ["DeviceId"] = DeviceId,
            ["AppId"] = "GWM-APP-ANDROID-1100018",
            ["NoteId"] = ChinaCrypto.DefaultNoteId
        };
        headers["Sign"] = ChinaCrypto.DefaultSign(
            HttpMethod.Post.Method,
            signingUrl ?? physicalUrl,
            rawBody,
            headers);

        using var request = new HttpRequestMessage(HttpMethod.Post, physicalUrl)
        {
            Content = CreateOfficialJsonContent(rawBody)
        };
        AddHeader(request, "G-TOKEN", Session.GToken);
        AddHeader(request, "Authorization", headers["Authorization"]);
        AddHeader(request, "ssoId", Session.UserId);
        AddHeader(request, "SourceApp", headers["SourceApp"]);
        AddHeader(request, "SourceType", headers["SourceType"]);
        AddHeader(request, "SourceAppVer", headers["SourceAppVer"]);
        AddHeader(request, "SourceAppCode", SourceAppCode);
        AddHeader(request, "Timestamp", timestamp);
        AddHeader(request, "DeviceId", DeviceId);
        AddHeader(request, "AppId", headers["AppId"]);
        AddHeader(request, "beanId", Session.BeanId);
        AddHeader(request, "NoteId", ChinaCrypto.DefaultNoteId);
        AddHeader(request, "Sign", headers["Sign"]);
        ApplyChinaTransportDefaults(request);

        using var response = await client.SendAsync(request, cancellationToken);
        var root = await ReadJsonAsync(response, "China G-App", cancellationToken);
        return ExtractDefaultData(root, "China G-App");
    }

    private async Task<JsonNode> SendBeanTechPostAsync(
        string url,
        JsonObject body,
        string? vin,
        CancellationToken cancellationToken)
    {
        var rawBody = body.ToJsonString();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = CreateOfficialJsonContent(rawBody)
        };
        AddBeanTechHeaders(request, "json=" + rawBody, vin);
        ApplyChinaTransportDefaults(request);
        using var response = await _beanTechClient.SendAsync(request, cancellationToken);
        var root = await ReadJsonAsync(response, "China BeanTech", cancellationToken);
        return ExtractDefaultData(root, "China BeanTech");
    }

    private async Task<JsonNode> SendBeanTechGetAsync(
        string url,
        IReadOnlyDictionary<string, string> query,
        string? vin,
        CancellationToken cancellationToken)
    {
        var queryString = String.Join("&", query.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        var parameter = String.Concat(query
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key.ToLowerInvariant() + "=" + pair.Value));
        using var request = new HttpRequestMessage(HttpMethod.Get, url + "?" + queryString);
        AddBeanTechHeaders(request, parameter, vin);
        ApplyChinaTransportDefaults(request);
        using var response = await _beanTechClient.SendAsync(request, cancellationToken);
        var root = await ReadJsonAsync(response, "China BeanTech", cancellationToken);
        return ExtractDefaultData(root, "China BeanTech");
    }

    private void AddBeanTechHeaders(HttpRequestMessage request, string parameter, string? vin)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = ChinaCrypto.Sha256Hex(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))[..16];
        var path = request.RequestUri!.AbsolutePath;
        AddHeader(request, "bt-auth-appkey", ChinaCrypto.BeanTechAppKey);
        AddHeader(request, "bt-auth-nonce", nonce);
        AddHeader(request, "bt-auth-timestamp", timestamp);
        AddHeader(request, "bt-auth-sign", ChinaCrypto.BeanTechSign(
            request.Method.Method,
            path,
            nonce,
            timestamp,
            parameter));
        AddHeader(request, "rs", "2");
        AddHeader(request, "appId", "097a7099af30d960");
        AddHeader(request, "brand", "10");
        AddHeader(request, "terminal", "GW_APP_GWM");
        AddHeader(request, "enterPriseId", "CC01");
        AddHeader(request, "accessToken", Session.BeanTechAccessToken);
        AddHeader(request, "beanId", FirstNonEmpty(Session.BeanTechBeanId, Session.BeanId));
        AddHeader(request, "cVer", SourceAppVersion);
        AddHeader(request, "vin", vin);
        AddHeader(request, "tenantId", "1");
        AddHeader(request, "operatorRole", "0");
        AddHeader(request, "tokenId", Session.AutoAiTokenId);
    }

    private async Task<JsonNode> SendAutoAiAsync(
        HttpClient client,
        string url,
        string function,
        JsonObject body,
        string mobileId,
        CancellationToken cancellationToken)
    {
        var chinaNow = ChinaTime.Convert(DateTimeOffset.UtcNow);
        var wrapper = new JsonObject
        {
            ["body"] = body,
            ["header"] = new JsonObject
            {
                ["brandType"] = "gwm",
                ["cVer"] = SourceAppVersion,
                ["fn"] = function,
                ["fv"] = "0202",
                ["mobileId"] = mobileId,
                ["osType"] = "Android",
                ["osVer"] = String.Empty,
                ["rs"] = "2",
                ["ts"] = chinaNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
                ["tk"] = Session.AutoAiTokenId ?? String.Empty,
                ["v"] = "1.0"
            }
        };
        var payload = wrapper.ToJsonString();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url + "?p=" + Uri.EscapeDataString(payload));
        AddHeader(request, "v", "1.0");
        AddHeader(request, "cid", DeviceId);
        // The official interceptor sends this literal value; it is not the user's
        // phone number despite the header name.
        AddHeader(request, "client", "phone");
        AddHeader(request, "sign", ChinaCrypto.AutoAiSign(timestamp));
        AddHeader(request, "time", timestamp);
        AddHeader(request, "ckey", ChinaCrypto.AutoAiCKey);
        AddHeader(request, "protocolVer", "2.1.2");
        AddHeader(request, "token", Session.AutoAiTokenId);
        AddHeader(request, "brandType", "GWM");
        ApplyChinaTransportDefaults(request);

        using var response = await client.SendAsync(request, cancellationToken);
        var root = await ReadJsonAsync(response, "China AutoAI", cancellationToken);
        if (Property(root, "header") is null && Property(root, "data") is not null)
        {
            root = ExtractDefaultData(root, "China AutoAI proxy");
        }

        var responseHeader = Property(root, "header");
        var code = Value(responseHeader, "c");
        if (!String.IsNullOrWhiteSpace(code) && code != "0")
        {
            throw new GwmApiException(
                code,
                FirstNonEmpty(Value(responseHeader, "m"), "China AutoAI request failed."));
        }

        return Property(root, "body") ?? root;
    }

    private async Task<JsonNode> ReadJsonAsync(
        HttpResponseMessage response,
        string service,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogHttpFailureAsync(response, service, content, cancellationToken);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }
        catch (JsonException ex)
        {
            throw new GwmApiException(
                "CN_INVALID_RESPONSE",
                $"{service} returned an unreadable response: {ex.Message}");
        }

        response.EnsureSuccessStatusCode();
        return root ?? throw new GwmApiException("CN_EMPTY_RESPONSE", $"{service} returned an empty response.");
    }

    private async Task LogHttpFailureAsync(
        HttpResponseMessage response,
        string service,
        string content,
        CancellationToken cancellationToken)
    {
        var request = response.RequestMessage;
        var requestHeaderNames = HeaderNames(request?.Headers, request?.Content?.Headers);
        var responseHeaderNames = HeaderNames(response.Headers, response.Content.Headers);
        var path = SanitizeDiagnosticValue(RequestPath(request?.RequestUri));
        var host = SanitizeDiagnosticValue(request?.RequestUri?.Host ?? "<unknown>");
        var reason = SanitizeDiagnosticValue(response.ReasonPhrase ?? "<none>");
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "<missing>";
        var contentLength = response.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture)
                            ?? "<missing>";
        var responseDate = response.Headers.Date?.ToString("O", CultureInfo.InvariantCulture)
                           ?? "<missing>";
        var dnsCandidates = await ResolveDnsCandidatesAsync(request?.RequestUri, cancellationToken);

        _logger.LogWarning(
            "Experimental GWM China HTTP failure: {Service} {Method} {Host}{Path} returned {StatusCode} " +
            "{ReasonPhrase} over HTTP/{HttpVersion}; response content type {ContentType}, content length " +
            "{ContentLength}, date {ResponseDate}; request headers [{RequestHeaders}]; request shape " +
            "[{RequestShape}]; response headers [{ResponseHeaders}]; DNS candidates [{DnsCandidates}]; " +
            "safe response: {SafeResponse}",
            service,
            request?.Method.Method ?? "<unknown>",
            host,
            path,
            (int)response.StatusCode,
            reason,
            response.Version,
            contentType,
            contentLength,
            responseDate,
            requestHeaderNames,
            SafeRequestShape(request, Session, DeviceId),
            responseHeaderNames,
            dnsCandidates,
            SafeResponsePreview(content));
    }

    internal static string SafeRequestShape(
        HttpRequestMessage? request,
        ChinaSession session,
        string expectedDeviceId)
    {
        var userAgent = HeaderValue(request, "User-Agent");
        var acceptEncoding = HeaderValue(request, "Accept-Encoding");
        var contentType = HeaderValue(request, "Content-Type");
        var contentLength = HeaderValue(request, "Content-Length");
        var gToken = HeaderValue(request, "G-TOKEN");
        var authorization = HeaderValue(request, "Authorization");
        var ssoId = HeaderValue(request, "ssoId");
        var beanId = HeaderValue(request, "beanId");
        var deviceId = HeaderValue(request, "DeviceId");
        var timestamp = HeaderValue(request, "Timestamp");
        var sign = HeaderValue(request, "Sign");
        var officialStaticHeaders =
            String.Equals(HeaderValue(request, "SourceApp"), "GWM", StringComparison.Ordinal)
            && String.Equals(HeaderValue(request, "SourceType"), "ANDROID", StringComparison.Ordinal)
            && String.Equals(HeaderValue(request, "SourceAppVer"), SourceAppVersion, StringComparison.Ordinal)
            && String.Equals(HeaderValue(request, "SourceAppCode"), SourceAppCode, StringComparison.Ordinal)
            && String.Equals(
                HeaderValue(request, "AppId"),
                "GWM-APP-ANDROID-1100018",
                StringComparison.Ordinal)
            && String.Equals(
                HeaderValue(request, "NoteId"),
                ChinaCrypto.DefaultNoteId,
                StringComparison.Ordinal);

        return String.Join(
            ", ",
            $"user-agent={SafeKnownHeader(userAgent, OfficialUserAgent)}",
            $"accept-encoding={SafeKnownHeader(acceptEncoding, "gzip")}",
            $"content-type={SafeKnownHeader(contentType, "application/json; charset=UTF-8")}",
            $"content-length={SafeNumericHeader(contentLength)}",
            $"official-static-headers={officialStaticHeaders}",
            $"g-token-length={ValueLength(gToken)}",
            $"authorization-length={ValueLength(authorization)}",
            $"sso-id-length={ValueLength(ssoId)}",
            $"bean-id-length={ValueLength(beanId)}",
            $"device-id-length={ValueLength(deviceId)}",
            $"timestamp-length={ValueLength(timestamp)}",
            $"timestamp-second-aligned={timestamp?.EndsWith("000", StringComparison.Ordinal) == true}",
            $"sign-length={ValueLength(sign)}",
            $"g-token-session-match={PresentAndEqual(gToken, session.GToken)}",
            $"authorization-session-match={PresentAndEqual(authorization, session.BeanTechAccessToken)}",
            $"sso-id-session-match={PresentAndEqual(ssoId, session.UserId)}",
            $"bean-id-gapp-match={PresentAndEqual(beanId, session.BeanId)}",
            $"bean-id-beantech-match={PresentAndEqual(beanId, session.BeanTechBeanId)}",
            $"gapp-beantech-bean-id-match={PresentAndEqual(session.BeanId, session.BeanTechBeanId)}",
            $"device-id-session-match={PresentAndEqual(deviceId, expectedDeviceId)}");
    }

    internal static string SafeResponsePreview(string content)
    {
        var byteCount = Encoding.UTF8.GetByteCount(content);
        if (String.IsNullOrWhiteSpace(content))
        {
            return "empty response body";
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return $"non-JSON response body ({byteCount} UTF-8 bytes; content omitted)";
        }

        if (root is null)
        {
            return "JSON null response body";
        }

        var fields = new List<string>();
        CollectSafeDiagnosticFields(root, fields, 0);
        return fields.Count == 0
            ? $"JSON response body ({byteCount} UTF-8 bytes; no safe error fields)"
            : String.Join("; ", fields);
    }

    private static void CollectSafeDiagnosticFields(
        JsonNode node,
        List<string> fields,
        int depth)
    {
        if (depth >= 6 || fields.Count >= 12)
        {
            return;
        }

        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Value is null)
                {
                    continue;
                }

                if (SafeDiagnosticFields.Contains(pair.Key)
                    && pair.Value is JsonValue value)
                {
                    var rawValue = JsonScalarValue(value);
                    if (pair.Key.Equals("path", StringComparison.OrdinalIgnoreCase))
                    {
                        rawValue = StripQuery(rawValue);
                    }
                    fields.Add(pair.Key + "=" + SanitizeDiagnosticValue(rawValue));
                    if (fields.Count >= 12)
                    {
                        return;
                    }
                }
                else if (pair.Value is JsonObject or JsonArray)
                {
                    CollectSafeDiagnosticFields(pair.Value, fields, depth + 1);
                }
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    CollectSafeDiagnosticFields(item, fields, depth + 1);
                }
                if (fields.Count >= 12)
                {
                    return;
                }
            }
        }
    }

    private static string JsonScalarValue(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }
        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean ? "true" : "false";
        }
        if (value.TryGetValue<long>(out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }
        if (value.TryGetValue<double>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
        return value.ToJsonString();
    }

    private static string SanitizeDiagnosticValue(string value)
    {
        var sanitized = BearerPattern.Replace(value, "[redacted credential]");
        sanitized = EmailPattern.Replace(sanitized, "[redacted email]");
        sanitized = CredentialAssignmentPattern.Replace(sanitized, "$1=[redacted credential]");
        sanitized = PhonePattern.Replace(sanitized, "[redacted phone]");
        sanitized = VinPattern.Replace(sanitized, "[redacted VIN]");
        sanitized = LongSecretPattern.Replace(sanitized, "[redacted credential]");
        sanitized = WhitespacePattern.Replace(sanitized, " ").Trim();
        return sanitized.Length <= 256 ? sanitized : sanitized[..256] + "...";
    }

    private static string RequestPath(Uri? uri)
    {
        if (uri is null)
        {
            return "<unknown>";
        }
        return uri.IsAbsoluteUri
            ? uri.AbsolutePath
            : StripQuery(uri.OriginalString);
    }

    private static string StripQuery(string value)
    {
        var queryIndex = value.IndexOfAny(['?', '#']);
        return queryIndex >= 0 ? value[..queryIndex] : value;
    }

    private static string HeaderNames(
        HttpHeaders? headers,
        HttpHeaders? contentHeaders)
    {
        var names = Enumerable.Empty<string>();
        if (headers is not null)
        {
            names = names.Concat(headers.Select(header => header.Key));
        }
        if (contentHeaders is not null)
        {
            names = names.Concat(contentHeaders.Select(header => header.Key));
        }
        return String.Join(
            ", ",
            names
                .Select(SanitizeDiagnosticValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
    }

    private static string? HeaderValue(HttpRequestMessage? request, string name)
    {
        if (request?.Headers.TryGetValues(name, out var values) == true)
        {
            return String.Join(",", values);
        }
        if (request?.Content?.Headers.TryGetValues(name, out values) == true)
        {
            return String.Join(",", values);
        }
        return null;
    }

    private static string SafeKnownHeader(string? value, string expected)
    {
        if (value is null)
        {
            return "<missing>";
        }
        return String.Equals(value, expected, StringComparison.Ordinal)
            ? expected
            : $"<unexpected length {value.Length}>";
    }

    private static string SafeNumericHeader(string? value) =>
        Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : value is null
                ? "<missing>"
                : "<invalid>";

    private static string ValueLength(string? value) =>
        value is null ? "<missing>" : value.Length.ToString(CultureInfo.InvariantCulture);

    private static bool PresentAndEqual(string? left, string? right) =>
        !String.IsNullOrEmpty(left)
        && !String.IsNullOrEmpty(right)
        && String.Equals(left, right, StringComparison.Ordinal);

    private static async Task<string> ResolveDnsCandidatesAsync(
        Uri? uri,
        CancellationToken cancellationToken)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return "<unknown host>";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, timeout.Token);
            return addresses.Length == 0
                ? "<no addresses>"
                : String.Join(", ", addresses.Select(address => address.ToString()).Order());
        }
        catch (OperationCanceledException)
        {
            return "<lookup timed out>";
        }
        catch (SocketException ex)
        {
            return $"<lookup failed: {ex.SocketErrorCode}>";
        }
    }

    private static JsonNode ExtractDefaultData(JsonNode root, string service)
    {
        var code = Value(root, "code");
        if (!String.IsNullOrWhiteSpace(code) && code is not ("0" or "000000" or "200"))
        {
            var message = FirstNonEmpty(
                Value(root, "description"),
                Value(root, "message"),
                Value(root, "msg"),
                $"{service} request failed.");
            if (code == "1013")
            {
                message = "GWM China requested an interactive account risk-control challenge (1013). " +
                          "Complete the challenge in the official app, then restart the add-on.";
            }
            throw new GwmApiException(code, message);
        }

        var data = Property(root, "data") ?? root;
        if (data is JsonValue value
            && value.TryGetValue<string>(out var encrypted)
            && encrypted.StartsWith("G_A(", StringComparison.Ordinal))
        {
            try
            {
                return JsonNode.Parse(ChinaCrypto.DecryptGApp(encrypted))
                       ?? throw new JsonException("Decrypted data was empty.");
            }
            catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
            {
                throw new GwmApiException(
                    "CN_DECRYPT_RESPONSE",
                    $"{service} returned data that could not be decrypted: {ex.Message}");
            }
        }

        return data;
    }

    private void EnsureGAppSession()
    {
        if (!Session.HasGAppTokens)
        {
            throw new GwmApiException(
                "CN_INCOMPLETE_LOGIN",
                "The experimental China login response did not contain the required account tokens.");
        }
    }

    private void EnsureCompleteSession()
    {
        if (!Session.IsComplete)
        {
            throw new GwmApiException(
                "CN_INCOMPLETE_SESSION",
                "The experimental China vehicle-service session is incomplete; sign in again with a new SMS code.");
        }
    }

    private static void ApplyChinaTransportDefaults(HttpRequestMessage request)
    {
        // Match the Android app's observable network profile. Servers may route
        // generic .NET clients differently even when the signed request is valid.
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        AddHeader(request, "Accept-Encoding", "gzip");
        AddHeader(request, "User-Agent", OfficialUserAgent);
    }

    private static HttpContent CreateOfficialJsonContent(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=UTF-8");
        content.Headers.ContentLength = bytes.Length;
        return content;
    }

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (value is not null)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static JsonNode? Property(JsonNode? node, string name)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }
        if (obj.TryGetPropertyValue(name, out var direct))
        {
            return direct;
        }
        return obj.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static string? Value(JsonNode? node, string name)
    {
        var property = Property(node, name);
        if (property is not JsonValue value)
        {
            return null;
        }
        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }
        if (value.TryGetValue<long>(out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }
        if (value.TryGetValue<double>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static int ParseInt(string? value, int fallback) =>
        Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static long ParseLong(string? value) =>
        Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static string ChinaClockTime(string? epochMilliseconds)
    {
        var milliseconds = ParseLong(epochMilliseconds);
        if (milliseconds <= 0)
        {
            return "00:00";
        }
        var local = ChinaTime.Convert(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
        return local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string ChinaRepeatTimes(SetChargingPlan request)
    {
        if (!String.IsNullOrWhiteSpace(request.Weeks)
            && request.Weeks.Length == 7
            && request.Weeks.All(character => character is '0' or '1'))
        {
            return request.Weeks;
        }

        var start = ParseLong(request.StartTime);
        if (start <= 0)
        {
            return "0000000";
        }
        var local = ChinaTime.Convert(DateTimeOffset.FromUnixTimeMilliseconds(start));
        // AutoAI order is Sunday, Saturday, Friday, Thursday, Wednesday, Tuesday, Monday.
        var index = local.DayOfWeek switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Saturday => 1,
            DayOfWeek.Friday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Wednesday => 4,
            DayOfWeek.Tuesday => 5,
            _ => 6
        };
        var days = "0000000".ToCharArray();
        days[index] = '1';
        return new String(days);
    }

    private static long StablePlanId(string vin) =>
        Int64.Parse(ChinaCrypto.Sha256Hex(vin ?? String.Empty)[..15], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !String.IsNullOrWhiteSpace(value)) ?? String.Empty;
}
