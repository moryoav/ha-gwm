using libgwmapi.DTO.Vehicle;

namespace GwmOra.Addon.RemoteCommands;

public static class RemoteCommandFactory
{
    public static ModifyVecicleRemoteCtl CreateClimateDefaults(
        string vin,
        int temperature,
        int operationTimeMinutes)
    {
        return new ModifyVecicleRemoteCtl
        {
            AirConditionerTemperature = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AirConditionerTime = (operationTimeMinutes * 60).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Vin = vin
        };
    }

    public static SendCmd CreateClimateCommand(
        string vin,
        string securityPassword,
        string switchOrder,
        int temperature,
        int operationTimeMinutes,
        bool useRussianProtocol = false)
    {
        return new SendCmd
        {
            Instructions = new SendCmdInstruction
            {
                X04 = new Instruction0x04
                {
                    AirConditioner = new AirConditionerInstruction
                    {
                        OperationTime = operationTimeMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SwitchOrder = switchOrder,
                        Temperature = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                }
            },
            RemoteType = "0",
            SecurityPassword = securityPassword,
            Type = useRussianProtocol ? 3 : 2,
            Vin = vin
        };
    }

    // Overloads without a security password, for callers and regions that do not
    // need one. China BeanTech requires it, so the add-on always passes it through
    // the primary overloads below.
    public static SendCmd CreateChinaClimateCommand(
        string vin,
        string mode,
        int temperature,
        int operationTimeMinutes,
        bool airAlreadyOn) =>
        CreateChinaClimateCommand(
            vin,
            String.Empty,
            mode,
            temperature,
            operationTimeMinutes,
            airAlreadyOn);

    public static SendCmd CreateChinaCommand(
        string vin,
        ChinaRemoteCommandKind kind,
        int commandCode,
        int? runTimeMinutes = null,
        int? temperature = null,
        int? openAngle = null,
        string? climateMode = null,
        bool airAlreadyOn = false) =>
        CreateChinaCommand(
            vin,
            String.Empty,
            kind,
            commandCode,
            runTimeMinutes,
            temperature,
            openAngle,
            climateMode,
            airAlreadyOn);

    public static SendCmd CreateChinaClimateCommand(
        string vin,
        string securityPassword,
        string mode,
        int temperature,
        int operationTimeMinutes,
        bool airAlreadyOn)
    {
        return CreateChinaCommand(
            vin,
            securityPassword,
            ChinaRemoteCommandKind.Climate,
            mode == "off" ? 7 : 6,
            operationTimeMinutes,
            temperature,
            climateMode: mode,
            airAlreadyOn: airAlreadyOn);
    }

    public static SendCmd CreateChinaCommand(
        string vin,
        string securityPassword,
        ChinaRemoteCommandKind kind,
        int commandCode,
        int? runTimeMinutes = null,
        int? temperature = null,
        int? openAngle = null,
        string? climateMode = null,
        bool airAlreadyOn = false)
    {
        return new SendCmd
        {
            ChinaCommand = new ChinaRemoteCommand
            {
                Kind = kind,
                CommandCode = commandCode,
                RunTimeMinutes = runTimeMinutes,
                Temperature = temperature,
                OpenAngle = openAngle,
                ClimateMode = climateMode,
                AirAlreadyOn = airAlreadyOn
            },
            RemoteType = "0",
            SecurityPassword = securityPassword,
            Type = 2,
            Vin = vin
        };
    }

    public static SendCmd CreateLockCommand(
        string vin,
        string securityPassword,
        bool lockVehicle,
        bool useRussianProtocol = false)
    {
        return new SendCmd
        {
            Instructions = new SendCmdInstruction
            {
                X05 = new Instruction0x05
                {
                    OperationTime = "0",
                    SwitchOrder = lockVehicle ? "2" : "1"
                }
            },
            RemoteType = "0",
            SecurityPassword = securityPassword,
            Type = useRussianProtocol ? 3 : 2,
            Vin = vin
        };
    }

    public static SendCmd CreateWindowCloseCommand(
        string vin,
        string securityPassword,
        bool useRussianProtocol = false)
    {
        return new SendCmd
        {
            Instructions = new SendCmdInstruction
            {
                X08 = new Instruction0x08
                {
                    SwitchOrder = useRussianProtocol ? "2" : "0",
                    Window = new WindowInstruction
                    {
                        LeftFront = "0",
                        LeftBack = "0",
                        RightFront = "0",
                        RightBack = "0",
                        SkyLight = useRussianProtocol ? null : String.Empty
                    }
                }
            },
            RemoteType = "0",
            SecurityPassword = securityPassword,
            Type = useRussianProtocol ? 3 : 2,
            Vin = vin
        };
    }
}
