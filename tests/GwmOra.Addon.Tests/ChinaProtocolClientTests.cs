using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GwmOra.Addon.Gwm;
using GwmOra.Addon.RemoteCommands;
using libgwmapi.China;
using libgwmapi.DTO.Vehicle;
using Microsoft.Extensions.Logging.Abstractions;

namespace GwmOra.Addon.Tests;

public sealed class ChinaProtocolClientTests
{
    private const string Vin = "LGWTEST0000000001";

    [Fact]
    public void ChinaClockUsesUtcPlusEightWithoutSystemTimeZoneLookup()
    {
        var china = ChinaTime.Convert(
            new DateTimeOffset(2026, 8, 26, 16, 30, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromHours(8), china.Offset);
        Assert.Equal(new DateTime(2026, 8, 27, 0, 30, 0), china.DateTime);
    }

    [Fact]
    public void CryptoMatchesAppDerivedSigningVectorsAndGAppRoundTrips()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "token-abcdef123456",
            ["SourceApp"] = "GWM",
            ["SourceType"] = "ANDROID",
            ["SourceAppVer"] = "2.1.5",
            ["Timestamp"] = "1723456789000",
            ["DeviceId"] = "0123456789abcdef0123456789abcdef",
            ["AppId"] = "GWM-APP-ANDROID-1100018",
            ["NoteId"] = ChinaCrypto.DefaultNoteId
        };

        Assert.Equal(
            "ef8be13f75ea09d4f0c009b6cf870b21a4f1a91b2269ba15fff9e852f2051bad",
            ChinaCrypto.DefaultSign(
                "POST",
                "https://gapp-api.gwmapp-h.com/api-guser/v5/token/refresh",
                "{\"token\":\"abc\",\"refreshToken\":\"def\"}",
                headers));
        Assert.Equal(
            "70b1d45225c49dfa9086528eaf7df04e578df1b2df46dfce5135ebec77641b3b",
            ChinaCrypto.BeanTechSign(
                "POST",
                "/app-api/api/v1.0/userAuth/loginSSOAccount",
                "0123456789abcdef",
                "1723456789123",
                "json={\"appType\":0,\"deviceId\":\"abc\"}"));
        Assert.Equal("bI5QLYve+aQBeu2pyb0yLUf3GuU=", ChinaCrypto.AutoAiSign("1723456789123"));

        const string logicalJson = "{\"phone\":\"13800138000\",\"flag\":\"LOGIN\"}";
        Assert.Equal(logicalJson, ChinaCrypto.DecryptGApp(ChinaCrypto.EncryptGApp(logicalJson)));
    }

    [Fact]
    public void HttpFailurePreviewKeepsRoutingErrorsAndRedactsPrivateValues()
    {
        const string secret = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var preview = ChinaProtocolClient.SafeResponsePreview("""
            {
              "code": 404,
              "message": "No route for 13800138000, LGWEE6A50GK123456, Bearer exposed-token, or token=short-secret",
              "path": "/missing?access_token=private",
              "token": "$SECRET$",
              "LGWEE6A50GK123456": { "traceId": "trace-123" }
            }
            """.Replace("$SECRET$", secret, StringComparison.Ordinal));

        Assert.Contains("code=404", preview);
        Assert.Contains("path=/missing", preview);
        Assert.Contains("traceId=trace-123", preview);
        Assert.DoesNotContain("13800138000", preview);
        Assert.DoesNotContain("LGWEE6A50GK123456", preview);
        Assert.DoesNotContain("exposed-token", preview);
        Assert.DoesNotContain("short-secret", preview);
        Assert.DoesNotContain("access_token", preview);
        Assert.DoesNotContain(secret, preview);
        Assert.DoesNotContain("; token=", preview);
    }

    [Fact]
    public void ChinaTransportAndFailureShapeMatchTheAppWithoutExposingCredentials()
    {
        using var handler = GwmApiClientFactory.CreateChinaHandler();
        Assert.Equal(DecompressionMethods.GZip, handler.AutomaticDecompression);

        const string gToken = "g-token-value-that-must-not-be-logged";
        const string accessToken = "access-token-value-that-must-not-be-logged";
        const string userId = "1234567890123456789";
        const string beanId = "9876543210987654321";
        const string deviceId = "0123456789abcdef0123456789abcdef";
        const string signature = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string body = "{\"vehicleVersion\":13}";
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ChinaProtocolClient.GAppBaseUrl + "gcar/v1/app/android/vehicle/query-vehicle-list")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Type",
            "application/json; charset=UTF-8");
        request.Content.Headers.ContentLength = bytes.Length;
        request.Headers.TryAddWithoutValidation("G-TOKEN", gToken);
        request.Headers.TryAddWithoutValidation("Authorization", accessToken);
        request.Headers.TryAddWithoutValidation("ssoId", userId);
        request.Headers.TryAddWithoutValidation("SourceApp", "GWM");
        request.Headers.TryAddWithoutValidation("SourceType", "ANDROID");
        request.Headers.TryAddWithoutValidation("SourceAppVer", ChinaProtocolClient.SourceAppVersion);
        request.Headers.TryAddWithoutValidation("SourceAppCode", ChinaProtocolClient.SourceAppCode);
        request.Headers.TryAddWithoutValidation("Timestamp", "1723456789000");
        request.Headers.TryAddWithoutValidation("DeviceId", deviceId);
        request.Headers.TryAddWithoutValidation("AppId", "GWM-APP-ANDROID-1100018");
        request.Headers.TryAddWithoutValidation("beanId", beanId);
        request.Headers.TryAddWithoutValidation("NoteId", ChinaCrypto.DefaultNoteId);
        request.Headers.TryAddWithoutValidation("Sign", signature);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
        request.Headers.TryAddWithoutValidation("User-Agent", ChinaProtocolClient.OfficialUserAgent);
        var session = new libgwmapi.DTO.China.ChinaSession
        {
            GToken = gToken,
            BeanTechAccessToken = accessToken,
            UserId = userId,
            BeanId = beanId,
            BeanTechBeanId = beanId
        };

        var shape = ChinaProtocolClient.SafeRequestShape(request, session, deviceId);

        Assert.Contains("user-agent=okhttp/4.2.2", shape);
        Assert.Contains("accept-encoding=gzip", shape);
        Assert.Contains("content-type=application/json; charset=UTF-8", shape);
        Assert.Contains("content-length=21", shape);
        Assert.Contains("official-static-headers=True", shape);
        Assert.Contains($"g-token-length={gToken.Length}", shape);
        Assert.Contains($"authorization-length={accessToken.Length}", shape);
        Assert.Contains("g-token-session-match=True", shape);
        Assert.Contains("authorization-session-match=True", shape);
        Assert.Contains("gapp-beantech-bean-id-match=True", shape);
        Assert.DoesNotContain(gToken, shape);
        Assert.DoesNotContain(accessToken, shape);
        Assert.DoesNotContain(userId, shape);
        Assert.DoesNotContain(beanId, shape);
        Assert.DoesNotContain(deviceId, shape);
        Assert.DoesNotContain(signature, shape);
    }

    [Fact]
    public async Task SmsRequestAndSessionRefreshUseTheAppAccountFlowOffline()
    {
        JsonObject? smsRequestBody = null;
        JsonObject? refreshBody = null;
        using var gApp = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api-guser/v5/user/login-sms/send":
                    smsRequestBody = ParseEncryptedRequest(request);
                    return Json("{\"code\":\"000000\",\"data\":{}}");
                case "/api-guser/v5/token/refresh":
                    refreshBody = ParseEncryptedRequest(request);
                    Assert.Equal("bt-access", Assert.Single(request.Headers.GetValues("Authorization")));
                    Assert.Equal("g-token", Assert.Single(request.Headers.GetValues("G-TOKEN")));
                    return Json("""
                        {"code":"000000","data":{"gToken":"g-token-2","gRefreshToken":"g-refresh-2","ssoToken":"sso-token-2"}}
                        """);
                default:
                    Assert.EndsWith("/GW.M.APP_LOGIN", request.RequestUri.AbsolutePath);
                    var wrapper = ParseAutoAiWrapper(request);
                    Assert.Equal("sso-token-2", wrapper["body"]!["ssoTk"]!.GetValue<string>());
                    return Json("""
                        {"code":"000000","data":{"header":{"c":0},"body":{"tokenId":"auto-token-2","userId":"auto-user-2"}}}
                        """);
            }
        }));
        using var beanTech = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            return Json("""
                {"code":"000000","data":{"accessToken":"bt-access-2","refreshToken":"bt-refresh-2","beanId":"bt-bean-2"}}
                """);
        }));
        using var unusedCar = new HttpClient(new DelegateHandler(_ =>
            throw new Xunit.Sdk.XunitException("Car service should not be called")));
        using var unusedAutoAi = new HttpClient(new DelegateHandler(_ =>
            throw new Xunit.Sdk.XunitException("Direct AutoAI service should not be called")));
        var client = new ChinaProtocolClient(
            gApp,
            unusedCar,
            beanTech,
            unusedAutoAi,
            NullLoggerFactory.Instance)
        {
            DeviceId = "0123456789abcdef0123456789abcdef"
        };

        await client.RequestSmsCodeAsync("13800138000", CancellationToken.None);
        client.SetSession(CompleteSession());
        var refreshed = await client.RefreshSessionAsync(CancellationToken.None);

        Assert.Equal("13800138000", smsRequestBody!["phone"]!.GetValue<string>());
        Assert.Equal("LOGIN", smsRequestBody["flag"]!.GetValue<string>());
        Assert.Equal("g-token", refreshBody!["token"]!.GetValue<string>());
        Assert.Equal("g-refresh", refreshBody["refreshToken"]!.GetValue<string>());
        Assert.Equal("g-token-2", refreshed.GToken);
        Assert.Equal("bt-access-2", refreshed.BeanTechAccessToken);
        Assert.Equal("auto-token-2", refreshed.AutoAiTokenId);
        Assert.True(refreshed.IsComplete);
    }

    [Fact]
    public void StatusMappingProducesExistingHomeAssistantSignalsForVv6()
    {
        var response = JsonNode.Parse(StatusBody)!.AsObject();
        var vehicle = new Vehicle
        {
            Vin = Vin,
            VehicleId = "vehicle-1",
            VehicleNetworkType = 2,
            TankCapacity = 56,
            AppShowSeriesName = "VV6",
            BrandName = "WEY",
            Vtype = "VV6"
        };

        var mapped = ChinaStatusMapper.Map(response, vehicle);
        var snapshot = VehicleSnapshotMapper.Map(
            vehicle,
            mapped,
            new VehicleBasicsInfo
            {
                Config = new VehicleConfig
                {
                    AirConditionerTemperature = "22",
                    AirConditionerStatusTime = "900"
                }
            },
            true,
            "idle");

        Assert.Equal(78, snapshot.Values.Soc);
        Assert.Equal(123, snapshot.Values.RangeKm);
        Assert.Equal(45678, snapshot.Values.OdometerKm);
        Assert.Equal(28, snapshot.Values.FuelLevelL);
        Assert.Equal(320, snapshot.Values.FuelRangeKm);
        Assert.True(snapshot.Values.Locked);
        Assert.True(snapshot.Values.AcActive);
        Assert.True(snapshot.Values.ChargingActive);
        Assert.True(snapshot.Values.ChargePlugConnected);
        Assert.True(snapshot.Values.DoorFrontDriverOpen);
        Assert.True(snapshot.Values.DoorRearPassengerSideOpen);
        Assert.True(snapshot.Values.WindowFrontDriverOpen);
        Assert.True(snapshot.Values.WindowRearDriverSideOpen);
        Assert.Equal(240, snapshot.Values.TirePressureFrontLeftKpa);
        Assert.Equal(31.2, snapshot.Location!.Latitude);
        Assert.Equal(121.5, snapshot.Location.Longitude);
    }

    [Fact]
    public void StatusMappingLeavesMissingBooleanSignalsUnknown()
    {
        var mapped = ChinaStatusMapper.Map(
            JsonNode.Parse("{\"vehicleSts\":{\"carStatus\":{\"soc\":\"80\"},\"battSts\":{}}}")!,
            new Vehicle { Vin = Vin, VehicleNetworkType = 2, TankCapacity = "not-a-number" });
        var snapshot = VehicleSnapshotMapper.Map(
            new Vehicle { Vin = Vin },
            mapped,
            new VehicleBasicsInfo(),
            true,
            "idle");

        Assert.Null(snapshot.Values.Locked);
        Assert.Null(snapshot.Values.AcActive);
        Assert.Null(snapshot.Values.ChargePlugConnected);
        Assert.Null(snapshot.Values.Soc);
        Assert.Null(snapshot.Values.FuelLevelL);
    }

    [Fact]
    public async Task ProtocolFlowCoversLoginDiscoveryStatusControlsAndChargingOffline()
    {
        JsonObject? smsLoginBody = null;
        JsonObject? lockCommandBody = null;
        JsonObject? chargeCommandBody = null;

        using var gApp = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            if (request.RequestUri!.AbsolutePath.EndsWith("/sms-login", StringComparison.Ordinal))
            {
                smsLoginBody = ParseEncryptedRequest(request);
                return Json("""
                {"code":"000000","data":{"gToken":"g-token","gRefreshToken":"g-refresh","ssoToken":"sso-token","ptToken":"pt-token","userId":"g-user","beanId":"g-bean","phone":"13800138000"}}
                """);
            }

            Assert.EndsWith("/GW.M.APP_LOGIN", request.RequestUri.AbsolutePath);
            var wrapper = ParseAutoAiWrapper(request);
            Assert.Equal("GW.M.APP_LOGIN", wrapper["header"]!["fn"]!.GetValue<string>());
            return Json("""
                {"code":"000000","data":{"header":{"c":0},"body":{"tokenId":"auto-token","userId":"auto-user","gwid":"auto-gw"}}}
                """);
        }));
        using var car = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            Assert.Equal("/gcar/v1/app/android/vehicle/query-vehicle-list", request.RequestUri!.AbsolutePath);
            Assert.Equal("gapp-api.gwmapp-h.com", request.RequestUri.Host);
            Assert.True(request.Headers.Contains("Sign"));
            Assert.Equal("bt-access", Assert.Single(request.Headers.GetValues("Authorization")));
            Assert.Equal("g-token", Assert.Single(request.Headers.GetValues("G-TOKEN")));
            return Json("""
                {"code":"000000","data":{"acquireVehiclesList":[{"vin":"$VIN$","vehicleId":"vehicle-1","belongPlatform":"navinfo","vehicleNetworkType":2,"tankCapacity":56,"appShowSeriesName":"VV6","brandName":"WEY","vtype":"VV6"}]}}
                """.Replace("$VIN$", Vin, StringComparison.Ordinal));
        }));
        using var beanTech = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            Assert.True(request.Headers.Contains("bt-auth-sign"));
            if (request.Method == HttpMethod.Post)
            {
                return Json("""
                    {"code":"000000","data":{"accessToken":"bt-access","refreshToken":"bt-refresh","ssoToken":"bt-sso","beanId":"bt-bean"}}
                    """);
            }

            Assert.Contains("seqNo=transaction-1", request.RequestUri!.Query);
            return Json("""
                {"code":"000000","data":{"messageList":[{"messageType":"remote","messageData":{"transactionId":"transaction-1","resultCode":0,"resultMessage":"Success"}}]}}
                """);
        }));
        using var autoAi = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            Assert.Equal("ti.gwm.com.cn", request.RequestUri!.Host);
            Assert.True(request.Headers.Contains("sign"));
            Assert.Equal("phone", Assert.Single(request.Headers.GetValues("client")));
            var wrapper = ParseAutoAiWrapper(request);
            Assert.Equal(
                "0123456789abcdef0123456789abcdef",
                wrapper["header"]!["mobileId"]!.GetValue<string>());
            var function = wrapper["header"]!["fn"]!.GetValue<string>();
            switch (function)
            {
                case "GW.M.GET_VEHICLE_STATE":
                    return Json("{\"header\":{\"c\":0},\"body\":" + StatusBody + "}");
                case "GW.M.SEND_COMMON_COMMAND":
                    lockCommandBody = wrapper["body"]!.AsObject();
                    return Json("""
                        {"header":{"c":0},"body":{"transactionId":"transaction-1"}}
                        """);
                case "GW.M.SEND_CHARGE_SETTINGS_WEEKLY":
                    chargeCommandBody = wrapper["body"]!.AsObject();
                    return Json("""
                        {"header":{"c":0},"body":{}}
                        """);
                default:
                    throw new Xunit.Sdk.XunitException($"Unexpected AutoAI function {function}");
            }
        }));

        var client = new ChinaProtocolClient(
            gApp,
            car,
            beanTech,
            autoAi,
            NullLoggerFactory.Instance)
        {
            DeviceId = "0123456789abcdef0123456789abcdef"
        };

        var session = await client.LoginWithSmsAsync("13800138000", "654321", CancellationToken.None);
        Assert.True(session.IsComplete);
        Assert.Equal("654321", smsLoginBody!["code"]!.GetValue<string>());
        Assert.Equal("13800138000", smsLoginBody["phone"]!.GetValue<string>());

        var vehicles = await client.AcquireVehiclesAsync(CancellationToken.None);
        Assert.Single(vehicles);
        var status = await client.GetLastVehicleStatusAsync(Vin, CancellationToken.None);
        Assert.Contains(status.Items, item => item.Code == "2013021" && item.Value.ToString() == "78");

        var lockRequest = RemoteCommandFactory.CreateLockCommand(Vin, String.Empty, lockVehicle: true);
        await client.SendCommandAsync(lockRequest, CancellationToken.None);
        Assert.Equal(2, lockCommandBody!["cmdCode"]!.GetValue<int>());
        Assert.Equal(
            ChinaCrypto.Md5Hex(Vin + "auto-token"),
            lockCommandBody["signStr"]!.GetValue<string>());
        var result = Assert.Single(await client.GetRemoteCommandResultAsync(
            lockRequest.SeqNo,
            Vin,
            CancellationToken.None));
        Assert.Equal("0", result.ResultCode);
        Assert.Equal(lockRequest.SeqNo, result.HwCommandId);

        var start = new DateTimeOffset(2026, 8, 30, 13, 15, 0, TimeSpan.Zero);
        var end = start.AddHours(2);
        await client.SetChargingPlanAsync(
            new SetChargingPlan
            {
                Enable = true,
                Vin = Vin,
                PlanType = 0,
                StartTime = start.ToUnixTimeMilliseconds().ToString(),
                EndTime = end.ToUnixTimeMilliseconds().ToString(),
                Weeks = String.Empty
            },
            CancellationToken.None);
        Assert.Equal("0", chargeCommandBody!["chargeingMode"]!.GetValue<string>());
        Assert.Equal("21:15", chargeCommandBody["chargingStartTime"]!.GetValue<string>());
        Assert.Equal("23:15", chargeCommandBody["chargingEndTime"]!.GetValue<string>());
        Assert.Equal("1000000", chargeCommandBody["repeatTimes"]!.GetValue<string>());
    }

    [Fact]
    public async Task EveryExposedChinaRemoteControlUsesTheAppCommandShape()
    {
        var commands = new List<(string Function, JsonObject Body)>();
        using var unusedGApp = new HttpClient(new DelegateHandler(_ =>
            throw new Xunit.Sdk.XunitException("G-App should not be called")));
        using var car = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            return Json("""
                {"code":"000000","data":{"acquireVehiclesList":[{"vin":"$VIN$","vehicleId":"vehicle-1","belongPlatform":"navinfo","vehicleNetworkType":2}]}}
                """.Replace("$VIN$", Vin, StringComparison.Ordinal));
        }));
        using var unusedBeanTech = new HttpClient(new DelegateHandler(_ =>
            throw new Xunit.Sdk.XunitException("BeanTech should not be called")));
        using var autoAi = new HttpClient(new DelegateHandler(request =>
        {
            AssertOfficialTransport(request);
            var wrapper = ParseAutoAiWrapper(request);
            commands.Add((
                wrapper["header"]!["fn"]!.GetValue<string>(),
                wrapper["body"]!.AsObject()));
            return Json("""
                {"header":{"c":0},"body":{"transactionId":"$TRANSACTION$"}}
                """.Replace(
                    "$TRANSACTION$",
                    $"transaction-{commands.Count}",
                    StringComparison.Ordinal));
        }));
        var client = new ChinaProtocolClient(
            unusedGApp,
            car,
            unusedBeanTech,
            autoAi,
            NullLoggerFactory.Instance)
        {
            DeviceId = "0123456789abcdef0123456789abcdef"
        };
        client.SetSession(CompleteSession());

        await client.SendCommandAsync(
            RemoteCommandFactory.CreateLockCommand(Vin, String.Empty, lockVehicle: false),
            CancellationToken.None);
        await client.SendCommandAsync(
            RemoteCommandFactory.CreateLockCommand(Vin, String.Empty, lockVehicle: true),
            CancellationToken.None);
        await client.SendCommandAsync(
            RemoteCommandFactory.CreateWindowCloseCommand(Vin, String.Empty),
            CancellationToken.None);
        await client.SendCommandAsync(
            RemoteCommandFactory.CreateClimateCommand(Vin, String.Empty, "1", 24, 15),
            CancellationToken.None);
        await client.SendCommandAsync(
            RemoteCommandFactory.CreateClimateCommand(Vin, String.Empty, "0", 24, 15),
            CancellationToken.None);

        Assert.Collection(
            commands,
            command => AssertCommand(command, "GW.M.SEND_COMMON_COMMAND", 1),
            command => AssertCommand(command, "GW.M.SEND_COMMON_COMMAND", 2),
            command => AssertCommand(command, "GW.M.SEND_COMMON_COMMAND", 3),
            command =>
            {
                AssertCommand(command, "GW.M.SET_AND_OPEN_COMMAND", 6);
                Assert.Equal(15, command.Body["airParams"]!["runTime"]!.GetValue<int>());
                Assert.Equal(24, command.Body["airParams"]!["temperature"]!.GetValue<int>());
            },
            command =>
            {
                AssertCommand(command, "GW.M.SEND_COMMON_COMMAND", 7);
                Assert.Null(command.Body["airParams"]);
            });
    }

    private static void AssertCommand(
        (string Function, JsonObject Body) command,
        string expectedFunction,
        int expectedCode)
    {
        Assert.Equal(expectedFunction, command.Function);
        Assert.Equal(expectedCode, command.Body["cmdCode"]!.GetValue<int>());
        Assert.Equal(Vin, command.Body["vin"]!.GetValue<string>());
    }

    private static void AssertOfficialTransport(HttpRequestMessage request)
    {
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, request.VersionPolicy);
        Assert.Equal("gzip", Assert.Single(request.Headers.GetValues("Accept-Encoding")));
        Assert.Equal(
            ChinaProtocolClient.OfficialUserAgent,
            Assert.Single(request.Headers.GetValues("User-Agent")));
        var headerNames = request.Headers.Select(header => header.Key).ToArray();
        Assert.Equal("Accept-Encoding", headerNames[^2]);
        Assert.Equal("User-Agent", headerNames[^1]);
        if (request.Content is not null)
        {
            Assert.Equal(
                "application/json; charset=UTF-8",
                request.Content.Headers.ContentType?.ToString());
            var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Equal(Encoding.UTF8.GetByteCount(body), request.Content.Headers.ContentLength);
        }
    }

    private static libgwmapi.DTO.China.ChinaSession CompleteSession() => new()
    {
        GToken = "g-token",
        GRefreshToken = "g-refresh",
        SsoToken = "sso-token",
        UserId = "g-user",
        BeanId = "g-bean",
        Phone = "13800138000",
        BeanTechAccessToken = "bt-access",
        AutoAiTokenId = "auto-token",
        AutoAiUserId = "auto-user"
    };

    private static JsonObject ParseEncryptedRequest(HttpRequestMessage request)
    {
        var encrypted = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonNode.Parse(ChinaCrypto.DecryptGApp(encrypted))!.AsObject();
    }

    private static JsonObject ParseAutoAiWrapper(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query;
        Assert.StartsWith("?p=", query);
        return JsonNode.Parse(Uri.UnescapeDataString(query[3..]))!.AsObject();
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private const string StatusBody = """
    {
      "vehicleSts": {
        "lastUpdate": 1723456789000,
        "battSts": {
          "battSoc": "78",
          "battSoh": "95",
          "hcuEVContnsDistance": "123",
          "bmsDCChrgConnect": "1",
          "bmsChrgsts": "1",
          "chgSts": 1,
          "chgTime": 45
        },
        "carStatus": {
          "drvDoorLockSts": 2,
          "drvDoorSts": 1,
          "passDoorSts": 0,
          "rlDoorSts": 0,
          "rrDoorSts": 1,
          "trunkSts": 0,
          "drvWinPosnSts": 1,
          "passWinPosnSts": 0,
          "rlWinPosnSts": 0,
          "rrWinPosnSts": 1,
          "vehTotDistance": "45678",
          "remainFuelSts": 1,
          "remainFuel": "320",
          "oilQty": 4,
          "lat": "31.2",
          "lon": "121.5",
          "cdngoffValid": "1",
          "cdngoff": "0",
          "drvTirePress": "240",
          "passTirePress": "241",
          "rlTirePress": "242",
          "rrTirePress": "243"
        }
      }
    }
    """;
}
